using System;

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
    }

    /// <summary>Real implementation backed by <see cref="System.IO.File"/>.</summary>
    public sealed class PhysicalUpdateFileSystem : IUpdateFileSystem
    {
        public bool FileExists(string path) => System.IO.File.Exists(path);
        public void DeleteFile(string path) => System.IO.File.Delete(path);
        public void MoveFile(string sourcePath, string destinationPath) => System.IO.File.Move(sourcePath, destinationPath);
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
    /// Extracted from TrayApp.ApplyUpdate so the executable backup → replacement →
    /// launch sequence is testable on Linux. On any failed step the original
    /// executable is restored from the ".old" backup before the failure is
    /// reported; a restoration failure is appended to the message.
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
    }
}
