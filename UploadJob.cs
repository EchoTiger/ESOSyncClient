using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RedfurSync
{
    public enum UploadStatus { Queued, Uploading, Done, Failed, Cancelled, UpdateReady }

    public class UpdatePayload
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public long SizeBytes { get; set; } = 0;
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
        public int    RetryCount  { get; set; } = 0; 

        public bool IsUpdate { get; init; } = false;
        public string CurrentVersion { get; init; } = string.Empty;
        public string UpdateVersion { get; init; } = string.Empty;
        public string Changelog { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;

        public CancellationTokenSource Cts { get; set; } = new();

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

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public string FileSizeDisplay
        {
            get
            {
                if (IsUpdate && FileSizeBytes == 0) return "Calculating...";
                if (FileSizeBytes < 1024)           return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024)    return $"{FileSizeBytes / 1024.0:0.0} KB";
                return $"{FileSizeBytes / (1024.0 * 1024):0.0} MB";
            }
        }

        public bool CanCancel => Status is UploadStatus.Queued or UploadStatus.Uploading;
        public bool CanRetry  => Status is UploadStatus.Failed or UploadStatus.Cancelled;

        public static UploadJob CreateUpdateJob(UpdatePayload payload)
        {
            string localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            return new UploadJob 
            {
                FileName = $"Fissal Relay v{payload.Version}",
                IsUpdate = true,
                CurrentVersion = localVersion,           
                UpdateVersion = payload.Version,      
                Changelog = payload.Changelog,        
                FileSizeBytes = payload.SizeBytes,    
                DownloadUrl = payload.DownloadUrl,    
                Status = UploadStatus.UpdateReady,
                QueuedAt = DateTime.Now,
                IsExpanded = true
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}