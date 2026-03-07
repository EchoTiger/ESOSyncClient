using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

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
        private readonly Dictionary<string, string>            _lastFileHashes = new();
        private readonly object _timerLock = new();
        private readonly object _jobLock   = new();
        private readonly object _hashLock  = new();
        private readonly System.Timers.Timer _updateTimer = new();

        public FileWatcherService(Action<string> onStatus)
        {
            _onStatus = onStatus;
            _config   = AppConfig.Instance; 
            _uploader = new UploadService(_config);
        }

        private async Task CheckForUpdatesAsync()
        {
            Console.WriteLine("[RedfurSync] Checking for updates...");
            
            if (Jobs.Any(j => j.Status == UploadStatus.Uploading)) return; 
            
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var payload = await _uploader.CheckForUpdateAsync(version);
            
            if (payload == null) return;

            lock (_jobLock)
            {
                if (Jobs.Any(j => j.IsUpdate && j.UpdateVersion == payload.Version)) return;
            }

            string tmpPath = Path.Combine(AppConfig.ConfigDirectory, "Update.tmp");
            
            var job = new UploadJob
            {
                IsUpdate       = true,
                CurrentVersion = version,
                UpdateVersion  = payload.Version,
                Changelog      = payload.Changelog,
                DownloadUrl    = payload.DownloadUrl,
                FilePath       = tmpPath,
                FileName       = $"RedfurSync v{payload.Version}",
                FileSizeBytes  = payload.SizeBytes, 
                QueuedAt       = DateTime.Now,
                Status         = UploadStatus.Queued,
                IsExpanded     = true
            };

            lock (_jobLock)
            {
                PruneOldJobs();
                Jobs.Add(job);
            }
            NotifyChanged();

            job.Status = UploadStatus.Uploading;
            NotifyChanged();

            bool success = await _uploader.DownloadUpdateAsync(job);
            
            job.Status = success ? UploadStatus.UpdateReady : UploadStatus.Failed;
            NotifyChanged();
        }

        public async Task StartAsync()
        {
            if (!_config.IsConfigured())
            {
                _onStatus("Fissal requires calibration!");
                return;
            }

            _onStatus("Establishing connection...");
            var (ok, msg) = await _uploader.PingAsync();

            _onStatus(ok ? "Connection established!" : $"Signal lost: {msg}");
            ConnectionChecked?.Invoke(ok, msg);
            SetupWatchers();
            
            _updateTimer.Interval = TimeSpan.FromMinutes(60).TotalMilliseconds;
            _updateTimer.Elapsed += async (_, _) => await CheckForUpdatesAsync();
            _updateTimer.Start();
            _ = Task.Run(async () => { await Task.Delay(8000); await CheckForUpdatesAsync(); });
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

            _onStatus(count == 0 ? "Cannot find target directories!" : $"Monitoring {count} folder(s)");
        }

        private void AddWatcher(string folder)
        {
            var w = new FileSystemWatcher(folder, "*.lua")
            {
                NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                InternalBufferSize    = 65536, 
                EnableRaisingEvents   = true,
                IncludeSubdirectories = false
            };
            
            w.Changed += OnFileChanged;
            w.Created += OnFileChanged;
            w.Renamed += (sender, e) => OnFileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, e.Name!));
            w.Error += (sender, e) => Console.WriteLine($"[RedfurSync] ⚠ Watcher lost the scent (Buffer Overflow): {e.GetException().Message}");
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

        public void RetryJob(UploadJob original)
        {
            if (original.Status == UploadStatus.Queued || original.Status == UploadStatus.Uploading) return;

            original.Cts.Dispose();
            original.Cts = new CancellationTokenSource();
            original.Status = UploadStatus.Queued;
            original.Progress = 0f;
            original.ErrorMessage = string.Empty;
            original.RetryCount++;
            NotifyChanged();
            
            _ = ProcessUploadAsync(original);
        }

        public void CancelJob(UploadJob job)
        {
            job.Cts.Cancel();
            job.Status = UploadStatus.Cancelled;
            NotifyChanged();
        }

        private string GetFileHash(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private async Task EnqueueUploadAsync(string filePath)
        {
            UploadJob job;
            int currentJobCount;

            int lockWaitRetries = 0;
            while (IsFileLocked(filePath) && lockWaitRetries < 5)
            {
                await Task.Delay(2000); 
                lockWaitRetries++;
            }

            if (IsFileLocked(filePath))
            {
                Console.WriteLine($"[RedfurSync] File {Path.GetFileName(filePath)} is persistently locked. Deferring.");
                return;
            }

            string currentHash = GetFileHash(filePath);
            lock (_hashLock)
            {
                if (!string.IsNullOrEmpty(currentHash))
                {
                    if (_lastFileHashes.TryGetValue(filePath, out var lastHash) && lastHash == currentHash)
                    {
                        Console.WriteLine($"[RedfurSync] {Path.GetFileName(filePath)} contents have not changed. Ignoring.");
                        return; 
                    }
                    _lastFileHashes[filePath] = currentHash;
                }
            }

            lock (_jobLock)
            {
                foreach (var ex in Jobs)
                {
                    if (ex.FilePath == filePath && ex.Status is UploadStatus.Queued or UploadStatus.Uploading)
                    { ex.Progress = 0f; return; }
                }

                long size = 0;
                try { size = new FileInfo(filePath).Length; } catch { }

                job = new UploadJob
                {
                    FilePath       = filePath,
                    FileName       = Path.GetFileName(filePath),
                    FileSizeBytes  = size,
                    QueuedAt       = DateTime.Now,
                    RetryCount     = 0
                };

                PruneOldJobs();
                Jobs.Add(job);
                currentJobCount = Jobs.Count;
            }

            NotifyChanged();
            _ = ProcessUploadAsync(job);
        }

        private async Task ProcessUploadAsync(UploadJob job)
        {
            _onStatus($"Dispatching {job.FileName}...");
            bool success = false;
            int uploadRetries = 0;
            const int maxUploadRetries = 3; 

            while (uploadRetries < maxUploadRetries && !success && !job.Cts.Token.IsCancellationRequested)
            {
                job.Status = UploadStatus.Uploading;
                NotifyChanged();

                success = await _uploader.UploadAsync(job);

                if (!success && !job.Cts.Token.IsCancellationRequested)
                {
                    uploadRetries++;
                    job.Status = UploadStatus.Queued;
                    job.ErrorMessage = $"Failed. Retrying {uploadRetries}/{maxUploadRetries}...";
                    NotifyChanged();
                    
                    try { await Task.Delay(3000 * uploadRetries, job.Cts.Token); }
                    catch (TaskCanceledException) { break; }
                    
                    int lockWait = 0;
                    while (IsFileLocked(job.FilePath) && lockWait < 6 && !job.Cts.Token.IsCancellationRequested)
                    {
                        try { await Task.Delay(2000, job.Cts.Token); }
                        catch (TaskCanceledException) { break; }
                        lockWait++;
                    }
                }
            }

            if (job.Cts.Token.IsCancellationRequested)
                job.Status = UploadStatus.Cancelled;
            else
                job.Status = success ? UploadStatus.Done : UploadStatus.Failed;

            NotifyChanged();

            if (success)
            {
                Console.WriteLine($"[RedfurSync] ✦ {job.FileName} uploaded successfully.");
                _onStatus($"{job.FileName} delivered!");
            }
            else if (job.Status == UploadStatus.Cancelled)
            {
                _onStatus("Transmission aborted.");
            }
            else
            {
                _onStatus($"Transmission failed: {job.FileName}");
            }
        }

        private bool IsFileLocked(string filePath)
        {
            try
            {
                using FileStream stream = new FileInfo(filePath).Open(FileMode.Open, FileAccess.Read, FileShare.None);
                stream.Close();
            }
            catch (IOException) { return true; }
            catch (Exception) { return false; }
            return false;
        }

        private void PruneOldJobs()
        {
            if (Jobs.Count == 0) return;

            var groups = new List<List<UploadJob>>();
            List<UploadJob>? currentGroup = null;
            DateTime? groupStartTime = null;
            bool? lastWasUpdate = null;

            var sortedJobs = Jobs.OrderBy(j => j.QueuedAt).ToList();

            foreach (var job in sortedJobs)
            {
                bool isNewGroup = groupStartTime == null || 
                                  Math.Abs((job.QueuedAt - groupStartTime.Value).TotalSeconds) > 60 || 
                                  (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);

                if (isNewGroup || currentGroup == null)
                {
                    currentGroup = new List<UploadJob>();
                    groups.Add(currentGroup);
                    groupStartTime = job.QueuedAt;
                }
                currentGroup.Add(job);
                lastWasUpdate = job.IsUpdate;
            }

            int maxLogs = AppConfig.Instance.MaxLogsKept;
            
            if (groups.Count > maxLogs)
            {
                int groupsToRemove = groups.Count - maxLogs;
                for (int i = 0; i < groupsToRemove; i++)
                {
                    foreach (var jobToRemove in groups[i])
                    {
                        if (jobToRemove.Status is UploadStatus.Done or UploadStatus.Failed or UploadStatus.Cancelled)
                        {
                            Jobs.Remove(jobToRemove);
                        }
                    }
                }
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