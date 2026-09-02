using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

/// Guards the P1 guarantee: an update-apply failure between executable backup,
/// replacement, and launch must leave the original executable recoverable, and
/// a rollback failure must be surfaced in the failure message. The installer is
/// driven entirely through a fake filesystem, so nothing touches real files on
/// Linux and every step can be fault-injected.
public sealed class UpdateInstallerTests
{
    private static readonly byte[] Original = Bytes(64, 7);
    private static readonly byte[] Staged = Bytes(64, 9);
    private static readonly byte[] Stale = Bytes(32, 1);

    [Fact]
    public void Success_BacksUpReplacesAndLaunchesOnce()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;
        var launches = new List<string>();

        var result = new UpdateInstaller(fs, path => launches.Add(path))
            .Apply("app.exe", "staged.exe");

        Assert.True(result.Ok, result.Message);
        Assert.Equal(Staged, fs["app.exe"]);
        Assert.Equal(Original, fs["app.exe.old"]);
        Assert.False(fs.Has("staged.exe"));
        Assert.Single(launches);
        Assert.Equal("app.exe", launches[0]);
        Assert.True(fs.Ops.SequenceEqual(new[]
        {
            "move app.exe -> app.exe.old",
            "move staged.exe -> app.exe"
        }));
    }

    [Fact]
    public void StaleOldBackup_IsDeletedBeforeBackupMove()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["app.exe.old"] = Stale;
        fs["staged.exe"] = Staged;

        var result = new UpdateInstaller(fs, _ => { }).Apply("app.exe", "staged.exe");

        Assert.True(result.Ok, result.Message);
        // The stale backup is deleted first, so .old now holds the fresh backup of the current exe.
        Assert.Equal(Original, fs["app.exe.old"]);
        Assert.Equal("delete app.exe.old", fs.Ops[0]);
    }

    [Fact]
    public void BackupFails_OriginalUntouched_NoLaunch()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;
        fs.MoveExceptionFactory = (_, destination) =>
            destination.EndsWith(".old", StringComparison.Ordinal)
                ? new IOException("backup boom")
                : null;

        var launches = new List<string>();
        var result = new UpdateInstaller(fs, path => launches.Add(path)).Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("backup boom", result.Message, StringComparison.Ordinal);
        Assert.Equal(Original, fs["app.exe"]);
        Assert.False(fs.Has("app.exe.old"));
        Assert.Empty(launches);
    }

    [Fact]
    public void ReplacementFails_OriginalRestored()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;
        fs.MoveExceptionFactory = (source, destination) =>
            destination == "app.exe" && source == "staged.exe"
                ? new IOException("replace boom")
                : null;

        var launches = new List<string>();
        var result = new UpdateInstaller(fs, path => launches.Add(path)).Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("replace boom", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be restored", result.Message, StringComparison.Ordinal);
        Assert.Equal(Original, fs["app.exe"]);            // original recovered
        Assert.False(fs.Has("app.exe.old"));              // backup consumed by the rollback
        Assert.True(fs.Has("staged.exe"));                // staged file not consumed
        Assert.Empty(launches);
    }

    [Fact]
    public void LaunchFails_OriginalRestored()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;

        var result = new UpdateInstaller(fs, _ => throw new IOException("launch boom"))
            .Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("launch boom", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be restored", result.Message, StringComparison.Ordinal);
        Assert.Equal(Original, fs["app.exe"]);            // the failed launch is rolled back too
        Assert.False(fs.Has("app.exe.old"));
        Assert.False(fs.Has("staged.exe"));               // replacement already consumed the staged file
    }

    [Fact]
    public void Rollback_RestoreMoveFails_MessageSurfacesUnrestoredOriginal()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;
        // Fault only the rollback restore (old -> app); backup and replacement both succeed.
        fs.MoveExceptionFactory = (source, destination) =>
            source == "app.exe.old" && destination == "app.exe"
                ? new IOException("rollback boom")
                : null;

        var result = new UpdateInstaller(fs, _ => throw new IOException("launch boom"))
            .Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("could not be restored", result.Message, StringComparison.Ordinal);
        Assert.False(fs.Has("app.exe"));                  // new exe was deleted before the restore failed
        Assert.True(fs.Has("app.exe.old"));               // original still on disk, recoverable manually
    }

    [Fact]
    public void MissingStagedFile_OriginalRestored()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;

        var result = new UpdateInstaller(fs, _ => { }).Apply("app.exe", "missing.exe");

        Assert.False(result.Ok);
        Assert.False(result.Message.Contains("could not be restored", StringComparison.Ordinal));
        Assert.Equal(Original, fs["app.exe"]);
        Assert.False(fs.Has("app.exe.old"));
    }

    [Fact]
    public void EmptyPaths_AreRejected()
    {
        var installer = new UpdateInstaller(FakeFileSystem(), _ => { });
        Assert.Throws<ArgumentException>(() => installer.Apply("", "staged.exe"));
        Assert.Throws<ArgumentException>(() => installer.Apply("app.exe", ""));
    }

    [Fact]
    public void StaleOldDeleteFails_OriginalUntouched_NoLaunch()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["app.exe.old"] = Stale;
        fs["staged.exe"] = Staged;
        fs.DeleteExceptionFactory = path =>
            path.EndsWith(".old", StringComparison.Ordinal)
                ? new IOException("delete boom")
                : null;

        var launches = new List<string>();
        var result = new UpdateInstaller(fs, path => launches.Add(path)).Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("delete boom", result.Message, StringComparison.Ordinal);
        Assert.Equal(Original, fs["app.exe"]);
        Assert.Equal(Stale, fs["app.exe.old"]);
        Assert.Empty(fs.Ops);                             // nothing was moved or deleted beyond the failed delete
        Assert.Empty(launches);
    }

    [Fact]
    public void Rollback_DeleteNewExeFails_ReportsUnrestored()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;
        fs.DeleteExceptionFactory = path =>
            path == "app.exe"
                ? new IOException("delete new exe boom")
                : null;

        var result = new UpdateInstaller(fs, _ => throw new IOException("launch boom"))
            .Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("could not be restored", result.Message, StringComparison.Ordinal);
        Assert.Equal(Staged, fs["app.exe"]);              // new exe survived the failed delete
        Assert.Equal(Original, fs["app.exe.old"]);        // original backup retained
    }

    [Fact]
    public void Rollback_BackupMissing_LeavesNewExeInPlace()
    {
        var fs = FakeFileSystem();
        fs["app.exe"] = Original;
        fs["staged.exe"] = Staged;

        // The backup vanishes during launch (AV, user cleanup, …); rollback must not
        // delete the only remaining executable just because the restore is impossible.
        var result = new UpdateInstaller(fs, _ =>
        {
            fs.DeleteFile("app.exe.old");
            throw new IOException("launch boom");
        }).Apply("app.exe", "staged.exe");

        Assert.False(result.Ok);
        Assert.Contains("launch boom", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be restored", result.Message, StringComparison.Ordinal);
        Assert.Equal(Staged, fs["app.exe"]);              // the validated new exe is left in place
        Assert.False(fs.Has("app.exe.old"));
    }

    [Fact]
    public void PhysicalFileSystem_CrossDirectoryApplySucceeds()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var exePath = Path.Combine(temporaryDirectory.CreateDirectory("app"), "RedfurSync.exe");
        var stagedPath = Path.Combine(temporaryDirectory.CreateDirectory("stage"), "Update.tmp");
        File.WriteAllBytes(exePath, Original);
        File.WriteAllBytes(stagedPath, Staged);
        var launches = new List<string>();

        var result = new UpdateInstaller(new PhysicalUpdateFileSystem(), path => launches.Add(path))
            .Apply(exePath, stagedPath);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(Staged, File.ReadAllBytes(exePath));
        Assert.Equal(Original, File.ReadAllBytes(exePath + ".old"));
        Assert.Single(launches);
        Assert.Equal(exePath, launches[0]);
    }

    private static FakeUpdateFileSystem FakeFileSystem() => new();

    private static byte[] Bytes(int length, byte seed)
    {
        var buffer = new byte[length];
        for (var i = 0; i < length; i++) buffer[i] = (byte)(seed + i);
        return buffer;
    }

    /// <summary>
    /// In-memory stand-in for <see cref="IUpdateFileSystem"/> that mirrors
    /// System.IO.File semantics (destination-exists throws, missing source throws,
    /// missing-path delete is a silent no-op) while logging every operation and
    /// allowing per-call fault injection.
    /// </summary>
    private sealed class FakeUpdateFileSystem : IUpdateFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new();
        private readonly List<string> _ops = new();

        public Func<string, string, Exception?>? MoveExceptionFactory { get; set; }
        public Func<string, Exception?>? DeleteExceptionFactory { get; set; }

        public byte[] this[string path]
        {
            get => _files[path];
            set => _files[path] = value;
        }

        public bool Has(string path) => _files.ContainsKey(path);
        public IReadOnlyList<string> Ops => _ops;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public void DeleteFile(string path)
        {
            var fault = DeleteExceptionFactory?.Invoke(path);
            if (fault is not null) throw fault;
            // System.IO.File.Delete is a silent no-op for a missing file.
            if (_files.Remove(path))
                _ops.Add($"delete {path}");
        }

        public void MoveFile(string source, string destination)
        {
            var fault = MoveExceptionFactory?.Invoke(source, destination);
            if (fault is not null) throw fault;
            if (!_files.TryGetValue(source, out var content))
                throw new FileNotFoundException($"Source file not found: {source}");
            if (_files.ContainsKey(destination))
                throw new IOException($"Destination already exists: {destination}");
            _files.Remove(source);
            _files[destination] = content;
            _ops.Add($"move {source} -> {destination}");
        }
    }
}
