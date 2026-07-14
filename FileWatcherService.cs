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

        public Task<(bool ok, string message, string model)> AskFissalAsync(string prompt)
            => _uploader.AskFissalAsync(prompt);

        private readonly List<FileSystemWatcher>               _watchers       = new();
        private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = new();
        private readonly Dictionary<string, string>            _lastFileHashes = new();
        private readonly object _timerLock = new();
        private readonly object _jobLock   = new();
        private readonly object _hashLock  = new();
        private readonly System.Timers.Timer _updateTimer = new();
        private readonly SemaphoreSlim _uploadThrottle = new SemaphoreSlim(3, 3); // Max 3 concurrent uploads

        public FileWatcherService(Action<string> onStatus)
        {
            _onStatus = onStatus;
            _config   = AppConfig.Instance; 
            _uploader = new UploadService(_config);
        }

        private async Task CheckForUpdatesAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.UpdateUrl)) return;

            Console.WriteLine("[RedfurSync] Checking for updates...");
            
            if (Jobs.Any(j => j.Status == UploadStatus.Uploading)) return; 
            
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var payload = await _uploader.CheckForUpdateAsync(version);
            
            if (payload == null) return;

            UploadJob? existingUpdate;
            lock (_jobLock)
            {
                existingUpdate = Jobs.LastOrDefault(j => j.IsUpdate && j.UpdateVersion == payload.Version);
            }

            if (existingUpdate != null)
            {
                if (existingUpdate.Status is not (UploadStatus.Failed or UploadStatus.Cancelled)) return;
                RetryJob(existingUpdate);
                return;
            }

            string tmpPath = Path.Combine(AppConfig.ConfigDirectory, "Update.tmp");
            
            var job = new UploadJob
            {
                IsUpdate       = true,
                CurrentVersion = version,
                UpdateVersion  = payload.Version,
                Changelog      = payload.Changelog,
                DownloadUrl    = payload.DownloadUrl,
                UpdateSha256   = payload.Sha256,
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

            await ProcessUpdateDownloadAsync(job);
        }

        private async Task ProcessUpdateDownloadAsync(UploadJob job)
        {
            job.Status = UploadStatus.Uploading;
            NotifyChanged();

            bool success = await _uploader.DownloadUpdateAsync(job);

            job.Status = job.Cts.Token.IsCancellationRequested
                ? UploadStatus.Cancelled
                : success ? UploadStatus.UpdateReady : UploadStatus.Failed;
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
            var (paired, pairingMessage) = await _uploader.PairAsync();
            if (!paired)
            {
                _onStatus(pairingMessage);
                ConnectionChecked?.Invoke(false, pairingMessage);
                return;
            }
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

            if (original.IsUpdate)
                _ = ProcessUpdateDownloadAsync(original);
            else
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
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return string.Empty; }
        }

        private static string CreateSnapshot(string sourcePath)
        {
            var spoolDirectory = Path.Combine(AppConfig.ConfigDirectory, "spool");
            Directory.CreateDirectory(spoolDirectory);
            var snapshotPath = Path.Combine(spoolDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(sourcePath)}");
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destination = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
            destination.Flush(true);
            return snapshotPath;
        }

        private static void CleanupSnapshot(UploadJob job)
        {
            if (!job.IsSnapshot || string.IsNullOrWhiteSpace(job.FilePath)) return;
            try { File.Delete(job.FilePath); } catch { }
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

            string snapshotPath;
            try
            {
                snapshotPath = CreateSnapshot(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RedfurSync] Could not snapshot {Path.GetFileName(filePath)}: {ex.Message}");
                return;
            }

            string currentHash = GetFileHash(snapshotPath);
            lock (_hashLock)
            {
                if (!string.IsNullOrEmpty(currentHash))
                {
                    if (_lastFileHashes.TryGetValue(filePath, out var lastHash) && lastHash == currentHash)
                    {
                        Console.WriteLine($"[RedfurSync] {Path.GetFileName(filePath)} contents have not changed. Ignoring.");
                        try { File.Delete(snapshotPath); } catch { }
                        return; 
                    }
                    _lastFileHashes[filePath] = currentHash;
                }
            }

            lock (_jobLock)
            {
                foreach (var ex in Jobs)
                {
                    if (ex.SourcePath == filePath && ex.Status is UploadStatus.Queued or UploadStatus.Uploading)
                    {
                        ex.Progress = 0f;
                        try { File.Delete(snapshotPath); } catch { }
                        return;
                    }
                }

                long size = 0;
                try { size = new FileInfo(snapshotPath).Length; } catch { }

                job = new UploadJob
                {
                    FilePath       = snapshotPath,
                    SourcePath     = filePath,
                    IsSnapshot     = true,
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

            if (MasterMerchantSaleScanner.IsSalesFile(job.FileName))
            {
                try
                {
                    var saleIds = MasterMerchantSaleScanner.ReadSaleIds(job.FilePath);
                    if (saleIds.Count > 0)
                    {
                        _onStatus($"Comparing {saleIds.Count:N0} sales in {job.FileName}...");
                        var missing = await _uploader.GetMissingSaleIdsAsync(saleIds, job.Cts.Token);
                        if (missing is { Count: 0 })
                        {
                            job.Progress = 1f;
                            job.Status = UploadStatus.Done;
                            job.ErrorMessage = "No upload needed; every sale is already stored by Redfur.";
                            NotifyChanged();
                            _onStatus($"{job.FileName} is already synchronized.");
                            CleanupSnapshot(job);
                            return;
                        }
                        if (missing != null)
                            _onStatus($"{missing.Count:N0} new sale(s) found; sending the source file safely...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RedfurSync] MM comparison skipped for {job.FileName}: {ex.Message}");
                }
            }

            while (uploadRetries < maxUploadRetries && !success && !job.Cts.Token.IsCancellationRequested)
            {
                job.Status = UploadStatus.Queued;
                NotifyChanged();

                await _uploadThrottle.WaitAsync(job.Cts.Token);
                try
                {
                    if (job.Cts.Token.IsCancellationRequested) break;

                    job.Status = UploadStatus.Uploading;
                    NotifyChanged();

                    success = await _uploader.UploadAsync(job);
                }
                finally
                {
                    _uploadThrottle.Release();
                }

                if (!success && !job.Cts.Token.IsCancellationRequested)
                {
                    uploadRetries++;
                    job.Status = UploadStatus.Queued;
                    // [Req 2] Do not overwrite the actual error message
                    if (string.IsNullOrWhiteSpace(job.ErrorMessage))
                        job.ErrorMessage = _uploader.LastError ?? "Transmission failed.";
                    
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
                CleanupSnapshot(job);
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
                // [Req 4] Group by Day
                bool isNewGroup = groupStartTime == null || 
                                  job.QueuedAt.Date != groupStartTime.Value.Date || 
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
                            CleanupSnapshot(jobToRemove);
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