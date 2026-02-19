using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Timers;

namespace RedfurSync
{
    public class FileWatcherService : IDisposable
    {
        private static readonly HashSet<string> WatchedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "GS00Data.lua",  "GS01Data.lua",  "GS02Data.lua",  "GS03Data.lua",
            "GS04Data.lua",  "GS05Data.lua",  "GS06Data.lua",  "GS07Data.lua",
            "GS08Data.lua",  "GS09Data.lua",  "GS10Data.lua",  "GS11Data.lua",
            "GS12Data.lua",  "GS13Data.lua",  "GS14Data.lua",  "GS15Data.lua",
            "GS16Data.lua",  "GS17Data.lua",  "RaffleGold.lua",
            "PriceTableNA.lua", "ItemLookUpTable_EN.lua"
        };

        private readonly AppConfig      _config;
        private readonly UploadService  _uploader;
        private readonly Action<string> _onStatus;

        public ObservableCollection<UploadJob> Jobs { get; } = new();
        public event Action?               JobsChanged;
        public event Action<bool, string>? ConnectionChecked;

        private readonly List<FileSystemWatcher>               _watchers       = new();
        private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = new();
        private readonly object _timerLock = new();
        private readonly object _jobLock   = new();

        public FileWatcherService(Action<string> onStatus)
        {
            _onStatus = onStatus;
            _config   = AppConfig.Load();
            _uploader = new UploadService(_config);
        }

        public async Task StartAsync()
        {
            if (!_config.IsConfigured())
            {
                _onStatus("Fissal requires calibration!");
                return;
            }

            _onStatus("Fissal is establishing the aetheric link…");
            var (ok, msg) = await _uploader.PingAsync();

            _onStatus(ok
                ? "Aetheric link established!"
                : $"Signal lost: {msg}");

            ConnectionChecked?.Invoke(ok, msg);
            SetupWatchers();
        }

        private void SetupWatchers()
        {
            int count   = 0;
            var docs    = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var esoBase = Path.Combine(docs, "Elder Scrolls Online", "live");

            void TryWatch(string p) { if (Directory.Exists(p)) { AddWatcher(p); count++; } }

            TryWatch(Path.Combine(esoBase, "SavedVariables"));
            TryWatch(Path.Combine(esoBase, "AddOns", "TamrielTradeCentre"));
            TryWatch(Path.Combine(esoBase, "AddOns", "LibEsoHubPrices"));

            _onStatus(count == 0
                ? "Fissal cannot find your data scrolls!"
                : $"Fissal is monitoring {count} folder(s)");
        }

        private void AddWatcher(string folder)
        {
            var w = new FileSystemWatcher(folder, "*.lua")
            {
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents   = true,
                IncludeSubdirectories = false
            };
            w.Changed += OnFileChanged;
            _watchers.Add(w);
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);
            if (!WatchedFiles.Contains(fileName)) return;

            lock (_timerLock)
            {
                if (_debounceTimers.TryGetValue(e.FullPath, out var existing))
                { existing.Stop(); existing.Dispose(); }

                var t = new System.Timers.Timer(_config.DebounceMs) { AutoReset = false };
                t.Elapsed += (_, _) => _ = EnqueueUploadAsync(e.FullPath);
                t.Start();
                _debounceTimers[e.FullPath] = t;
            }
        }

        public void RetryJob(UploadJob original) =>
            _ = EnqueueUploadAsync(original.FilePath, isRetry: true);

        public void CancelJob(UploadJob job) =>
            job.Cts.Cancel();

        private async Task EnqueueUploadAsync(string filePath, bool isRetry = false)
        {
            UploadJob job;
            lock (_jobLock)
            {
                if (!isRetry)
                {
                    foreach (var ex in Jobs)
                    {
                        if (ex.FilePath == filePath &&
                            ex.Status is UploadStatus.Queued or UploadStatus.Uploading)
                        { ex.Progress = 0f; return; }
                    }
                }

                long size = 0;
                try { size = new FileInfo(filePath).Length; } catch { }

                job = new UploadJob
                {
                    FilePath       = filePath,
                    FileName       = Path.GetFileName(filePath),
                    FileSizeBytes  = size,
                    QueuedAt       = DateTime.Now
                };

                PruneOldJobs();
                Jobs.Add(job);
            }

            NotifyChanged();
            _onStatus($"Dispatching {job.FileName} to the vault…");

            job.Status = UploadStatus.Uploading;
            NotifyChanged();

            var success = await _uploader.UploadAsync(job);

            if (job.Cts.Token.IsCancellationRequested)
                job.Status = UploadStatus.Cancelled;
            else
                job.Status = success ? UploadStatus.Done : UploadStatus.Failed;

            NotifyChanged();

            if (success)
            {
                Console.WriteLine($"[Fissal] ✦ {job.FileName} delivered to the vault.");
                _onStatus($"{job.FileName} delivered!");
            }
            else if (job.Status == UploadStatus.Cancelled)
            {
                Console.WriteLine($"[Fissal] — Transmission of {job.FileName} aborted.");
                _onStatus("Transmission aborted.");
            }
            else
            {
                Console.WriteLine($"[Fissal] ✖ Transmission of {job.FileName} failed: {job.ErrorMessage}");
                _onStatus($"Transmission failed: {job.FileName}");
            }
        }

        private void PruneOldJobs()
        {
            var cutoff = DateTime.Now.AddMinutes(-5);
            for (int i = Jobs.Count - 1; i >= 0; i--)
            {
                var j = Jobs[i];
                if (j.Status is UploadStatus.Done or UploadStatus.Failed or UploadStatus.Cancelled
                    && j.QueuedAt < cutoff)
                    Jobs.RemoveAt(i);
            }
        }

        private void NotifyChanged() => JobsChanged?.Invoke();

        public void Dispose()
        {
            foreach (var w in _watchers) w.Dispose();
            lock (_timerLock)
            {
                foreach (var t in _debounceTimers.Values) { t.Stop(); t.Dispose(); }
                _debounceTimers.Clear();
            }
            _uploader.Dispose();
        }
    }
}
