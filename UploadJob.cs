using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RedfurSync
{
    public enum UploadStatus
    {
        Queued,
        Uploading,
        Done,
        Failed,
        Cancelled
    }

    public class UploadJob : INotifyPropertyChanged
    {
        private UploadStatus _status = UploadStatus.Queued;
        private float _progress = 0f;
        private string _errorMessage = string.Empty;
        private bool _isExpanded = false;

        public string FilePath    { get; init; } = string.Empty;
        public string FileName    { get; init; } = string.Empty;
        public long   FileSizeBytes { get; init; } = 0;
        public DateTime QueuedAt  { get; init; } = DateTime.Now;

        /// <summary>Cancel this upload. Works for both Queued and Uploading states.</summary>
        public CancellationTokenSource Cts { get; } = new();

        public UploadStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public float Progress
        {
            get => _progress;
            set { _progress = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Whether the error detail panel is expanded in the UI.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes < 1024)           return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024)    return $"{FileSizeBytes / 1024.0:0.0} KB";
                return $"{FileSizeBytes / (1024.0 * 1024):0.0} MB";
            }
        }

        public bool CanCancel => Status is UploadStatus.Queued or UploadStatus.Uploading;
        public bool CanRetry  => Status is UploadStatus.Failed or UploadStatus.Cancelled;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
