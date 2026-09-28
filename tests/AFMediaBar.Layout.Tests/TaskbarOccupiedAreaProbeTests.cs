// 验证真实探测器的线程复用、排队和释放边界；注入扫描函数，避免依赖现场 Explorer/UIA。
// Tests the real worker lifecycle with an injected scan; the probe owns and stops its worker.
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarOccupiedAreaProbeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void SequentialProbesReuseOneMtaThreadAcrossTaskbarsAndIdlePeriods()
    {
        using var completed = new AutoResetEvent(false);
        var threads = new List<Thread>();
        using var probe = new TaskbarOccupiedAreaProbe(_ =>
        {
            threads.Add(Thread.CurrentThread);
            return [];
        });

        for (var handle = 1; handle <= 10; handle++)
        {
            Start(probe, handle, _ => completed.Set());
            Assert.IsTrue(completed.WaitOne(WaitTimeout));
        }

        Assert.AreEqual(1, threads.Distinct().Count());
        Assert.AreEqual(ApartmentState.MTA, threads[0].GetApartmentState());
        Assert.IsTrue(threads[0].IsAlive);
        probe.Dispose();
        Assert.IsTrue(threads[0].Join(WaitTimeout), "The idle worker must exit on disposal.");
    }

    [TestMethod]
    public void ScanFailuresAndCallbackExceptionsDoNotTerminateWorker()
    {
        using var completed = new AutoResetEvent(false);
        var threads = new List<Thread>();
        var scans = 0;
        using var probe = new TaskbarOccupiedAreaProbe(_ =>
        {
            threads.Add(Thread.CurrentThread);
            return ++scans switch
            {
                1 => throw new InvalidOperationException("Simulated Explorer failure"),
                2 => null,
                _ => []
            };
        });

        for (var index = 0; index < 3; index++)
        {
            Action signalAndThrow = () =>
            {
                completed.Set();
                throw new InvalidOperationException("Simulated stale subscriber");
            };
            Start(probe, 1,
                _ => signalAndThrow(),
                signalAndThrow);
            Assert.IsTrue(completed.WaitOne(WaitTimeout));
        }

        Start(probe, 2, _ => completed.Set());
        Assert.IsTrue(completed.WaitOne(WaitTimeout));
        Assert.AreEqual(4, scans);
        Assert.AreEqual(1, threads.Distinct().Count());
        probe.Dispose();
        Assert.IsTrue(threads[0].Join(WaitTimeout));
    }

    [TestMethod]
    public void BlockedScanCoalescesPendingRequestsWithoutStartingAnotherWorker()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var scannedHandles = new List<IntPtr>();
        var threads = new List<Thread>();
        var replaced = 0;
        using var probe = new TaskbarOccupiedAreaProbe(request =>
        {
            threads.Add(Thread.CurrentThread);
            scannedHandles.Add(request.TaskbarHandle);
            if (request.TaskbarHandle == (IntPtr)1)
            {
                entered.Set();
                if (!release.Wait(WaitTimeout))
                    throw new TimeoutException("Test did not release the scan");
            }
            return [];
        });

        try
        {
            Start(probe, 1);
            Assert.IsTrue(entered.Wait(WaitTimeout));
            Start(probe, 2, failed: () => Interlocked.Increment(ref replaced));
            Start(probe, 2, _ => completed.Set());
            Assert.AreEqual(1, replaced);
            Assert.AreEqual(1, scannedHandles.Count);
            release.Set();
            Assert.IsTrue(completed.Wait(WaitTimeout));
            CollectionAssert.AreEqual(new[] { (IntPtr)1, (IntPtr)2 }, scannedHandles.ToArray());
            Assert.AreEqual(1, threads.Distinct().Count());
        }
        finally
        {
            probe.Dispose();
            release.Set();
            if (threads.Count > 0)
                Assert.IsTrue(threads[0].Join(WaitTimeout));
        }
    }

    [TestMethod]
    public void BlockedScanBoundsQueueForStaleTaskbarHandles()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var scannedHandles = new List<IntPtr>();
        Thread? worker = null;
        var dropped = 0;
        using var probe = new TaskbarOccupiedAreaProbe(request =>
        {
            worker = Thread.CurrentThread;
            scannedHandles.Add(request.TaskbarHandle);
            if (request.TaskbarHandle == (IntPtr)1)
            {
                entered.Set();
                if (!release.Wait(WaitTimeout))
                    throw new TimeoutException("Test did not release the scan");
            }
            return [];
        });

        try
        {
            Start(probe, 1);
            Assert.IsTrue(entered.Wait(WaitTimeout));
            for (var handle = 2; handle <= 21; handle++)
            {
                var queuedHandle = handle;
                Start(probe, handle, _ => { if (queuedHandle == 21) completed.Set(); },
                    () => Interlocked.Increment(ref dropped));
            }
            Assert.AreEqual(4, dropped);
            release.Set();
            Assert.IsTrue(completed.Wait(WaitTimeout));
            Assert.AreEqual(17, scannedHandles.Count);
            Assert.IsFalse(scannedHandles.Any(handle => handle.ToInt64() is >= 2 and <= 5));
        }
        finally
        {
            probe.Dispose();
            release.Set();
            if (worker is not null)
                Assert.IsTrue(worker.Join(WaitTimeout));
        }
    }

    [TestMethod]
    public void DisposalDoesNotWaitForBlockedScanOrPublishLateResults()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Thread? worker = null;
        var scans = 0;
        var callbacks = 0;
        using var probe = new TaskbarOccupiedAreaProbe(_ =>
        {
            worker = Thread.CurrentThread;
            Interlocked.Increment(ref scans);
            entered.Set();
            if (!release.Wait(WaitTimeout))
                throw new TimeoutException("Test did not release the scan");
            return [];
        });
        Action finished = () => Interlocked.Increment(ref callbacks);

        try
        {
            Start(probe, 1, _ => finished(), finished);
            Assert.IsTrue(entered.Wait(WaitTimeout));
            Start(probe, 2, _ => finished(), finished);
            probe.Dispose();
            probe.Dispose();
            Assert.IsTrue(worker!.IsAlive, "Disposal must return before the blocked scan finishes.");
            Start(probe, 3, _ => finished(), finished);
            release.Set();
            Assert.IsTrue(worker.Join(WaitTimeout));
            Assert.AreEqual(1, scans);
            Assert.AreEqual(0, callbacks);
        }
        finally
        {
            probe.Dispose();
            release.Set();
            if (worker is not null)
                Assert.IsTrue(worker.Join(WaitTimeout));
        }
    }

    private static void Start(TaskbarOccupiedAreaProbe probe, int handle,
        Action<IReadOnlyList<TaskbarPrimaryRange>>? succeeded = null, Action? failed = null) =>
        probe.Start((IntPtr)handle, default, LayoutOrientation.Horizontal, 1, 0,
            succeeded ?? (_ => { }), failed ?? (() => Assert.Fail("Unexpected probe failure")));
}
