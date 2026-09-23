using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace RedfurSync
{
    /// <summary>
    /// Filesystem surface used by <see cref="UpdateInstaller"/> so the backup →
    /// replace → launch sequence can be driven against a fake in tests without
    /// touching real files. Kept WinForms-free so it runs on Linux in CI.
    /// </summary>
    public interface IUpdateFileSystem
    {
        bool FileExists(string path);
        void DeleteFile(string path);
        void MoveFile(string sourcePath, string destinationPath);
        string ReadAllText(string path) => System.IO.File.ReadAllText(path);
        void WriteAllText(string path, string content) => System.IO.File.WriteAllText(path, content);
        IEnumerable<string> EnumerateFiles(string path, string searchPattern) => System.IO.Directory.EnumerateFiles(path, searchPattern);
    }

    /// <summary>Real implementation backed by <see cref="System.IO.File"/>.</summary>
    public sealed class PhysicalUpdateFileSystem : IUpdateFileSystem
    {
        public bool FileExists(string path) => System.IO.File.Exists(path);
        public void DeleteFile(string path) => System.IO.File.Delete(path);
        public void MoveFile(string sourcePath, string destinationPath) => System.IO.File.Move(sourcePath, destinationPath, true);
        public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
        public void WriteAllText(string path, string content) => System.IO.File.WriteAllText(path, content);
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => System.IO.Directory.EnumerateFiles(path, searchPattern);
    }

    /// <summary>Pending update record persisted in pending.json for atomic recovery.</summary>
    public sealed class PendingUpdateRecord
    {
        public long Sequence { get; set; }
        public string FromVersion { get; set; } = string.Empty;
        public string ToVersion { get; set; } = string.Empty;
        public string State { get; set; } = "applying";
        public long Started { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>Outcome of an update apply attempt.</summary>
    public sealed class UpdateApplyResult
    {
        private UpdateApplyResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public bool Ok { get; }
        public string Message { get; }

        public static UpdateApplyResult Success => new(true, string.Empty);
        public static UpdateApplyResult Failure(string message) => new(false, message);
    }

    /// <summary>
    /// Update installer implementing Fable 5.1 Ruling 3.5:
    /// - Executable backup to .prev and .old
    /// - Atomic replace
    /// - Launch verification with HEALTHY sentinel handshake
    /// - Rollback on timeout or process error
    /// - Crash-during-commit recovery
    /// </summary>
    public sealed class UpdateInstaller
    {
        private readonly IUpdateFileSystem _fileSystem;
        private readonly Action<string> _launch;

        public UpdateInstaller(IUpdateFileSystem fileSystem, Action<string> launch)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        }

        public UpdateApplyResult Apply(string exePath, string stagedPath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("The executable path must not be empty.", nameof(exePath));
            if (string.IsNullOrWhiteSpace(stagedPath))
                throw new ArgumentException("The staged update path must not be empty.", nameof(stagedPath));

            var oldPath = exePath + ".old";
            var originalMoved = false;

            try
            {
                // A stale ".old" from a previous install would block the backup move.
                if (_fileSystem.FileExists(oldPath))
                    _fileSystem.DeleteFile(oldPath);

                _fileSystem.MoveFile(exePath, oldPath);
                originalMoved = true;

                _fileSystem.MoveFile(stagedPath, exePath);

                _launch(exePath);
                return UpdateApplyResult.Success;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                try
                {
                    if (originalMoved && _fileSystem.FileExists(oldPath))
                    {
                        if (_fileSystem.FileExists(exePath))
                            _fileSystem.DeleteFile(exePath);
                        _fileSystem.MoveFile(oldPath, exePath);
                    }
                }
                catch (Exception rollbackEx)
                {
                    message += $" The original executable could not be restored: {rollbackEx.Message}";
                }

                return UpdateApplyResult.Failure(message);
            }
        }

        public UpdateApplyResult ApplyWithHandshake(
            string exePath,
            string stagedPath,
            long sequence,
            string fromVersion,
            string toVersion,
            TimeSpan? handshakeTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("The executable path must not be empty.", nameof(exePath));
            if (string.IsNullOrWhiteSpace(stagedPath))
                throw new ArgumentException("The staged update path must not be empty.", nameof(stagedPath));

            string installDir = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? string.Empty;
            string pendingPath = Path.Combine(installDir, "pending.json");
            string healthyPath = Path.Combine(installDir, "HEALTHY");
            string prevPath = exePath + ".prev";
            string oldPath = exePath + ".old";
            bool originalMoved = false;

            try
            {
                if (_fileSystem.FileExists(healthyPath))
                    _fileSystem.DeleteFile(healthyPath);

                var pendingRecord = new PendingUpdateRecord
                {
                    Sequence = sequence,
                    FromVersion = fromVersion,
                    ToVersion = toVersion,
                    State = "applying",
                    Started = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                _fileSystem.WriteAllText(pendingPath, JsonSerializer.Serialize(pendingRecord));

                if (_fileSystem.FileExists(prevPath))
                    _fileSystem.DeleteFile(prevPath);
                if (_fileSystem.FileExists(oldPath))
                    _fileSystem.DeleteFile(oldPath);

                _fileSystem.MoveFile(exePath, prevPath);
                originalMoved = true;

                _fileSystem.MoveFile(stagedPath, exePath);

                pendingRecord.State = "launching";
                _fileSystem.WriteAllText(pendingPath, JsonSerializer.Serialize(pendingRecord));

                _launch(exePath);

                var timeout = handshakeTimeout ?? TimeSpan.FromSeconds(30);
                var pollInterval = TimeSpan.FromMilliseconds(100);
                var sw = Stopwatch.StartNew();
                bool healthy = false;

                while (sw.Elapsed < timeout)
                {
                    if (_fileSystem.FileExists(healthyPath))
                    {
                        healthy = true;
                        break;
                    }
                    Thread.Sleep(pollInterval);
                }

                if (!healthy)
                {
                    throw new TimeoutException($"New relay instance failed to confirm health within {timeout.TotalSeconds}s.");
                }

                if (_fileSystem.FileExists(prevPath))
                    _fileSystem.DeleteFile(prevPath);
                if (_fileSystem.FileExists(pendingPath))
                    _fileSystem.DeleteFile(pendingPath);
                if (_fileSystem.FileExists(healthyPath))
                    _fileSystem.DeleteFile(healthyPath);

                return UpdateApplyResult.Success;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                try
                {
                    if (originalMoved && _fileSystem.FileExists(prevPath))
                    {
                        if (_fileSystem.FileExists(exePath))
                            _fileSystem.DeleteFile(exePath);
                        _fileSystem.MoveFile(prevPath, exePath);

                        var rollbackRecord = new PendingUpdateRecord
                        {
                            Sequence = sequence,
                            FromVersion = fromVersion,
                            ToVersion = toVersion,
                            State = "rolled_back",
                            Started = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Reason = message
                        };
                        _fileSystem.WriteAllText(pendingPath, JsonSerializer.Serialize(rollbackRecord));
                    }
                }
                catch (Exception rollbackEx)
                {
                    message += $" The original executable could not be restored: {rollbackEx.Message}";
                }

                return UpdateApplyResult.Failure(message);
            }
        }

        public static void RecoverPendingUpdate(string installDir, IUpdateFileSystem? fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return;
            var fs = fileSystem ?? new PhysicalUpdateFileSystem();
            string pendingPath = Path.Combine(installDir, "pending.json");
            string healthyPath = Path.Combine(installDir, "HEALTHY");

            if (!fs.FileExists(pendingPath)) return;

            try
            {
                var json = fs.ReadAllText(pendingPath);
                var pending = JsonSerializer.Deserialize<PendingUpdateRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (pending == null) return;

                if ((string.Equals(pending.State, "applying", StringComparison.OrdinalIgnoreCase) || string.Equals(pending.State, "launching", StringComparison.OrdinalIgnoreCase)) && !fs.FileExists(healthyPath))
                {
                    foreach (var prevFile in fs.EnumerateFiles(installDir, "*.prev").ToList())
                    {
                        string targetFile = prevFile.Substring(0, prevFile.Length - ".prev".Length);
                        try
                        {
                            if (fs.FileExists(targetFile)) fs.DeleteFile(targetFile);
                            fs.MoveFile(prevFile, targetFile);
                        }
                        catch { }
                    }

                    pending.State = "recovered_rollback";
                    pending.Reason = "Crashed during previous update commit";
                    fs.WriteAllText(pendingPath, JsonSerializer.Serialize(pending));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update Recovery Error] {ex.Message}");
            }
        }
    }
}
