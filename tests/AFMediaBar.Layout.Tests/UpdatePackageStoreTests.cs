using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 更新目录所有者的测试：待安装记录的路径边界、整体替换式写入，以及靠哈希确认文件本身而不是相信它的长度。
/// Tests for the owner of the update directory: the path boundary of the pending record, its replace-in-one-step
/// write, and confirming the file itself by hash instead of trusting its length.
/// </summary>
[TestClass]
public sealed class UpdatePackageStoreTests
{
    private const string Payload = "installer payload";

    private readonly List<string> _roots = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var root in _roots)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响结论，交给系统清理。
                // A temporary directory that cannot be deleted changes no conclusion; leave it to the system.
            }
        }
    }

    [TestMethod]
    public void IsTrustedInstallerPath_AcceptsOnlyExeFilesInsideTheUpdateDirectory()
    {
        var root = CreateTemporaryRoot();
        var updates = Path.Combine(root, "updates");
        Directory.CreateDirectory(updates);
        var store = new UpdatePackageStore(updates);

        Assert.IsTrue(
            store.IsTrustedInstallerPath(Path.Combine(updates, "AFMediaBar-Setup-v1.3.1-win-x64.exe")),
            "更新目录里的安装包本来就是自己下载的。");

        Assert.IsFalse(store.IsTrustedInstallerPath(Path.Combine(updates, "notes.txt")), "只信任 .exe。");
        Assert.IsFalse(store.IsTrustedInstallerPath(Path.Combine(updates, "payload.scr")));
        Assert.IsFalse(store.IsTrustedInstallerPath(Path.Combine(root, "payload.exe")), "更新目录之外一律不信任。");
        Assert.IsFalse(store.IsTrustedInstallerPath(@"\\server\share\payload.exe"), "UNC 一律不信任。");
        Assert.IsFalse(store.IsTrustedInstallerPath("payload.exe"), "相对路径一律不信任。");
        Assert.IsFalse(store.IsTrustedInstallerPath(Path.Combine(updates, "..", "payload.exe")), "向上穿越一律不信任。");
        Assert.IsFalse(store.IsTrustedInstallerPath(null));
        Assert.IsFalse(store.IsTrustedInstallerPath(string.Empty));
    }

    [TestMethod]
    public void WritePendingRecord_WritesTheWholeRecordAndLeavesNoTemporaryFile()
    {
        var updates = CreateUpdateDirectory();
        var store = new UpdatePackageStore(updates);

        var record = WriteInstaller(Path.Combine(updates, "AFMediaBar-Setup-v1.3.1-win-x64.exe"));
        store.WritePendingRecord(record);

        var readBack = store.ReadPendingRecord();
        Assert.IsNotNull(readBack);
        Assert.AreEqual(record.Path, readBack!.Path);
        Assert.AreEqual(record.Version, readBack.Version);
        Assert.AreEqual(record.Sha256, readBack.Sha256);
        Assert.AreEqual(record.Size, readBack.Size);
        Assert.IsFalse(File.Exists(store.PendingRecordPath + ".tmp"), "替换完成后不能留下临时文件。");
    }

    [TestMethod]
    public void MatchesRecordedHash_ReadsTheFileInsteadOfTrustingItsLength()
    {
        var updates = CreateUpdateDirectory();
        var store = new UpdatePackageStore(updates);

        var record = WriteInstaller(Path.Combine(updates, "AFMediaBar-Setup-v1.3.1-win-x64.exe"));
        Assert.IsTrue(store.MatchesRecordedHash(record));

        // 换掉内容但长度保持不变：只有真的把字节读一遍才能发现。
        // The contents change while the length stays the same: only reading the bytes back reveals it.
        File.WriteAllBytes(record.Path, Encoding.UTF8.GetBytes("installer payload!"));
        var sameLength = new UpdatePendingFileRecord(
            record.Path,
            record.Version,
            record.Sha256,
            File.ReadAllBytes(record.Path).Length,
            record.ModifiedUtc);
        Assert.IsFalse(store.MatchesRecordedHash(sameLength), "长度相同的替换也必须被识破。");

        File.WriteAllBytes(record.Path, Encoding.UTF8.GetBytes("tampered"));
        Assert.IsFalse(store.MatchesRecordedHash(record), "长度改变时同样不能通过。");
    }

    [TestMethod]
    public void Evaluate_ReusesAnInstallerSittingInsideTheUpdateDirectory()
    {
        var updates = CreateUpdateDirectory();
        var store = new UpdatePackageStore(updates);

        var record = WriteInstaller(Path.Combine(updates, "AFMediaBar-Setup-v99.0.0-win-x64.exe"));
        store.WritePendingRecord(record);

        Assert.AreEqual(
            UpdatePendingFileAction.Reuse,
            store.Evaluate(SelfReferencingAsset(record)),
            "自己下载的、没有被动过的安装包应当可以直接复用。");
    }

    [TestMethod]
    public void Evaluate_DiscardsARecordThatPointsOutsideTheUpdateDirectory()
    {
        var root = CreateTemporaryRoot();
        var updates = Path.Combine(root, "updates");
        Directory.CreateDirectory(updates);
        var store = new UpdatePackageStore(updates);

        // 安装包落在别处，而记录的哈希与调用方的 asset 完全一致：这种情况下"哈希对得上"证明不了任何东西，
        // 唯一拦得住它的是路径边界。
        // The installer sits elsewhere while the record's hash matches the caller's asset exactly: a matching hash
        // proves nothing in that situation, and only the path boundary stops it.
        var record = WriteInstaller(Path.Combine(root, "payload.exe"));
        store.WritePendingRecord(record);

        Assert.AreEqual(
            UpdatePendingFileAction.Discard,
            store.Evaluate(SelfReferencingAsset(record)),
            "指向更新目录之外的记录必须丢弃，而不是复用。");
    }

    private static UpdatePackageAsset SelfReferencingAsset(UpdatePendingFileRecord record) =>
        new(string.Empty, record.Size, record.Sha256);

    private static UpdatePendingFileRecord WriteInstaller(string path)
    {
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Payload));
        return new UpdatePendingFileRecord(
            path,
            "99.0.0",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload))).ToLowerInvariant(),
            File.ReadAllBytes(path).Length,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
    }

    private string CreateUpdateDirectory()
    {
        var root = CreateTemporaryRoot();
        var updates = Path.Combine(root, "updates");
        Directory.CreateDirectory(updates);
        return updates;
    }

    private string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "AFMediaBar.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }
}
