using System;
using System.Collections.Generic;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 有界缓存的淘汰规则：条数、总代价，以及"刚写入的那一条永远留下"。
/// The eviction rules of the bounded cache: entry count, total cost, and the rule that the entry just written always stays.
///
/// 这些断言针对的是会真的出问题的分支：缓存不封顶就是稳定的内存增长（歌词缓存是常驻进程里最大的托管对象之一），
/// 而把"刚写入的那一条"也淘汰掉会让缓存变成每次都未命中的抖动。
/// These assertions target branches that really break: an uncapped cache is steady memory growth (the lyric cache is one of the largest managed objects in a
/// resident process), and evicting the entry that was just written turns the cache into a thrash that misses every time.
/// </summary>
[TestClass]
public sealed class LruCacheCostTests
{
    /// <summary>只按条数封顶时淘汰最久未使用的一条。/ With an entry-count cap alone, the least recently used entry is evicted.</summary>
    [TestMethod]
    public void CountCapEvictsTheLeastRecentlyUsedEntry()
    {
        var cache = new LruCache<string, int>(2);

        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        Assert.AreEqual(2, cache.Count);
        Assert.IsFalse(cache.TryGetValue("a", out _));
        Assert.IsTrue(cache.TryGetValue("b", out var b));
        Assert.AreEqual(2, b);
        Assert.IsTrue(cache.TryGetValue("c", out var c));
        Assert.AreEqual(3, c);
    }

    /// <summary>读取会把条目提到最新，因此下一次淘汰落在另一个键上。/ A read promotes an entry, so the next eviction lands on the other key.</summary>
    [TestMethod]
    public void ReadingAnEntryProtectsItFromTheNextEviction()
    {
        var cache = new LruCache<string, int>(2);

        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.IsTrue(cache.TryGetValue("a", out _));
        cache.Set("c", 3);

        Assert.IsTrue(cache.TryGetValue("a", out _));
        Assert.IsFalse(cache.TryGetValue("b", out _));
        Assert.IsTrue(cache.TryGetValue("c", out _));
    }

    /// <summary>总代价封顶时同样从最久未使用的一端淘汰，并保持代价记账准确。/ With a total-cost cap the eviction starts at the least recently used end as well, and the cost accounting stays exact.</summary>
    [TestMethod]
    public void CostCapEvictsUntilTheBudgetFits()
    {
        var cache = new LruCache<string, string>(10, value => value.Length, maximumCost: 10);

        cache.Set("a", "1234");   // 4
        cache.Set("b", "1234");   // 8
        Assert.AreEqual(8, cache.CurrentCost);

        cache.Set("c", "1234");   // 12 → 淘汰最旧的 a，剩 8
        Assert.AreEqual(8, cache.CurrentCost);
        Assert.AreEqual(2, cache.Count);
        Assert.IsFalse(cache.TryGetValue("a", out _));
        Assert.IsTrue(cache.TryGetValue("b", out _));
        Assert.IsTrue(cache.TryGetValue("c", out _));
    }

    /// <summary>
    /// 单条就超过总代价上限时必须留下它自己：把它淘汰掉等于每次读取都未命中并重新取一次，
    /// 那比多留一份内存更糟（这条规则是缓存抖动与内存上限之间的取舍点）。
    /// A single entry larger than the whole budget must be kept: evicting it would mean missing and refetching on every lookup, which is worse than keeping
    /// one entry over budget. This rule is where cache thrash and the memory ceiling trade off.
    /// </summary>
    [TestMethod]
    public void ASingleEntryBiggerThanTheBudgetIsStillKept()
    {
        var cache = new LruCache<string, string>(10, value => value.Length, maximumCost: 4);

        cache.Set("huge", "1234567890");   // 10 > 4

        Assert.AreEqual(1, cache.Count);
        Assert.AreEqual(10, cache.CurrentCost);
        Assert.IsTrue(cache.TryGetValue("huge", out var value));
        Assert.AreEqual("1234567890", value);

        // 之后再写入别的条目时，超预算的旧条目才让位。
        // Only when another entry arrives does the over-budget one make room.
        cache.Set("small", "1");
        Assert.AreEqual(1, cache.Count);
        Assert.IsTrue(cache.TryGetValue("small", out _));
        Assert.IsFalse(cache.TryGetValue("huge", out _));
    }

    /// <summary>覆盖同一个键时代价按差值更新，不会重复累加。/ Overwriting one key updates the cost by its difference instead of accumulating it twice.</summary>
    [TestMethod]
    public void OverwritingAKeyKeepsTheCostConsistent()
    {
        var cache = new LruCache<string, string>(4, value => value.Length, maximumCost: 100);

        cache.Set("a", "1234567890");
        Assert.AreEqual(10, cache.CurrentCost);

        cache.Set("a", "123");
        Assert.AreEqual(3, cache.CurrentCost);
        Assert.AreEqual(1, cache.Count);
    }

    /// <summary>清空之后代价归零：剪枝与设置变化都依赖这一点。/ Clearing resets the cost to zero, which both pruning and a settings change rely on.</summary>
    [TestMethod]
    public void ClearingResetsTheCost()
    {
        var cache = new LruCache<string, string>(4, value => value.Length, maximumCost: 100);

        cache.Set("a", "1234567890");
        cache.Clear();

        Assert.AreEqual(0, cache.Count);
        Assert.AreEqual(0, cache.CurrentCost);
        Assert.IsFalse(cache.TryGetValue("a", out _));
    }

    /// <summary>没有代价函数时总代价恒为 0，条数上限照常生效。/ Without a cost selector the total cost stays zero while the entry cap keeps working.</summary>
    [TestMethod]
    public void WithoutACostSelectorOnlyTheCountMatters()
    {
        var cache = new LruCache<string, string>(1);

        cache.Set("a", "1234567890");
        cache.Set("b", "1");

        Assert.AreEqual(1, cache.Count);
        Assert.AreEqual(0, cache.CurrentCost);
        Assert.IsTrue(cache.TryGetValue("b", out _));
    }

    /// <summary>非法容量与非法预算必须在构造时就拒绝，而不是留到运行期。/ Invalid capacity and budget are rejected at construction rather than at run time.</summary>
    [TestMethod]
    public void InvalidCapacityAndBudgetAreRejected()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new LruCache<string, int>(1, value => value, -1));
    }

    [TestMethod]
    public void NullLookupIsAMissWithoutTouchingTheDictionaryOrExistingEntries()
    {
        var cache = new LruCache<string, string>(2);
        cache.Set("song", "lyrics");
        Assert.IsFalse(cache.TryGetValue(null!, out var value));
        Assert.IsNull(value);
        Assert.AreEqual(1, cache.Count);
        Assert.IsTrue(cache.TryGetValue("song", out var existing));
        Assert.AreEqual("lyrics", existing);
    }

    [TestMethod]
    public void InvalidOrThrowingCostsLeaveValuesAndEvictionOrderUnchanged()
    {
        var cache = new LruCache<string, long>(2, value =>
            value == -2 ? throw new InvalidOperationException() : value, 100);
        cache.Set("a", 10);
        cache.Set("b", 20);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Set("a", -1));
        Assert.ThrowsException<InvalidOperationException>(() => cache.Set("a", -2));
        Assert.AreEqual(30L, cache.CurrentCost);
        cache.Set("c", 30);
        Assert.IsFalse(cache.TryGetValue("a", out _));
        Assert.IsTrue(cache.TryGetValue("b", out var value));
        Assert.AreEqual(20L, value);
    }

    [TestMethod]
    public void CostOverflowRejectsInsertAndOverwriteWithoutCorruptingAccounting()
    {
        var cache = new LruCache<string, long>(4, value => value, 0);
        cache.Set("a", long.MaxValue - 1);
        cache.Set("b", 1);
        Assert.ThrowsException<OverflowException>(() => cache.Set("c", 1));
        Assert.ThrowsException<OverflowException>(() => cache.Set("b", 2));
        Assert.AreEqual(2, cache.Count);
        Assert.AreEqual(long.MaxValue, cache.CurrentCost);
        Assert.IsTrue(cache.TryGetValue("b", out var value));
        Assert.AreEqual(1L, value);
        cache.Set("a", 2);
        Assert.AreEqual(3L, cache.CurrentCost);
    }

    [TestMethod]
    public void RepeatedHitsDoNotExtendExpirationAndRemovalUpdatesCost()
    {
        var clock = new CacheClock();
        var cache = new LruCache<string, long>(4, value => value, 100, clock);
        cache.Set("failure", 10, TimeSpan.FromSeconds(30));
        clock.Advance(29);
        Assert.IsTrue(cache.TryGetValue("failure", out _));
        clock.Advance(1);
        Assert.IsFalse(cache.TryGetValue("failure", out _));
        Assert.AreEqual(0, cache.Count);
        Assert.AreEqual(0L, cache.CurrentCost);
    }

    [TestMethod]
    public void ReplacingAnExpiringFailureWithSuccessRemovesItsDeadline()
    {
        var clock = new CacheClock();
        var cache = new LruCache<string, string?>(2, null, 0, clock);
        cache.Set("song", null, TimeSpan.FromSeconds(30));
        Assert.IsTrue(cache.TryGetValue("song", out var miss));
        Assert.IsNull(miss);
        clock.Advance(10);
        cache.Set("song", "lyrics");
        clock.Advance(60);
        Assert.IsTrue(cache.TryGetValue("song", out var hit));
        Assert.AreEqual("lyrics", hit);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => cache.Set("song", "bad", TimeSpan.Zero));
    }

    [TestMethod]
    public async Task SlowCostEstimationDoesNotBlockCacheReadsOrClear()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var cache = new LruCache<string, int>(2, value =>
        {
            if (value == 2)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return value;
        }, 100);
        cache.Set("ready", 1);
        var write = Task.Run(() => cache.Set("slow", 2));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)));
            var read = Task.Run(() =>
            {
                Assert.IsTrue(cache.TryGetValue("ready", out _));
                cache.Clear();
            });
            await read.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { release.Set(); }
        await write;
        Assert.AreEqual(1, cache.Count);
        Assert.AreEqual(2L, cache.CurrentCost);
    }

    private sealed class CacheClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(int seconds) => _timestamp += TimeSpan.FromSeconds(seconds).Ticks;
    }

    /// <summary>缓存本身要在多线程下可用：歌词缓存由后台取词线程写、UI 线程读。/ The cache has to work across threads: the lyric cache is written by background retrieval and read by the UI thread.</summary>
    [TestMethod]
    public void ConcurrentWritesAndReadsKeepTheCacheBounded()
    {
        var cache = new LruCache<int, int>(16, value => value, maximumCost: 4096);
        var keys = new List<int>();
        for (var index = 0; index < 256; index++)
        {
            keys.Add(index);
        }

        System.Threading.Tasks.Parallel.ForEach(keys, key =>
        {
            cache.Set(key, key);
            cache.TryGetValue(key % 32, out _);
        });

        Assert.IsTrue(cache.Count <= 16);
        Assert.IsTrue(cache.CurrentCost <= 4096);
    }
}
