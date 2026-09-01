using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RedfurSync.Tests")]

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
            "GS16Data.lua",  "GS17Data.lua",  "RaffleGold.lua", "RaffleGoldRTP.lua", "RaffleGoldRD.lua",
            "PriceTableNA.lua", "ItemLookUpTable_EN.lua"
        };

        private readonly AppConfig      _config;
        private readonly UploadService  _uploader;
        private readonly Action<string> _onStatus;
        private readonly string         _spoolDirectory;
        private readonly Action<AppConfig> _saveConfig;

        public ObservableCollection<UploadJob> Jobs { get; } = new();
        public event Action?               JobsChanged;
        public event Action<bool, string>? ConnectionChecked;

        public Task<(bool ok, string msg)> PingServerAsync()
            => _uploader.PingAsync();

        public Task<(bool ok, string msg)> PairDeviceAsync()
            => _uploader.PairAsync();

        public Task<(bool ok, string message, string model)> AskFissalAsync(string prompt)
            => _uploader.AskFissalAsync(prompt);

        public string GetAssistantContext()
        {
            var esoBase = WatchRootProvider();
            var folders = new[]
            {
                Path.Combine(esoBase, "SavedVariables"),
                Path.Combine(esoBase, "AddOns", "TamrielTradeCentre"),
                Path.Combine(esoBase, "AddOns", "LibEsoHubPrices"),
            };
            var discovered = folders.Where(Directory.Exists).ToArray();
            var files = discovered
                .SelectMany(folder => WatchedFiles.Select(name => Path.Combine(folder, name)))
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(20)
                .ToArray();
            UploadJob[] jobs;
            lock (_jobLock) jobs = Jobs.OrderByDescending(job => job.QueuedAt).Take(8).ToArray();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Machine: {Environment.MachineName}");
            builder.AppendLine($"Paired: {!string.IsNullOrWhiteSpace(_config.DeviceToken)}");
            builder.AppendLine($"Watcher folders detected: {discovered.Length}/{folders.Length}");
            foreach (var folder in folders)
                builder.AppendLine($"- {(Directory.Exists(folder) ? "available" : "missing")}: {folder}");
            builder.AppendLine($"Recognized data files found: {files.Length}");
            foreach (var file in files)
                builder.AppendLine($"- {file.Name}: {file.Length:N0} bytes, modified {file.LastWriteTime:O}");
            builder.AppendLine($"Recent sync jobs: {jobs.Length}");
            foreach (var job in jobs)
                builder.AppendLine($"- {job.FileName}: {job.Status}, queued {job.QueuedAt:O}, error: {(string.IsNullOrWhiteSpace(job.ErrorMessage) ? "none" : job.ErrorMessage)}");
            builder.AppendLine("Harness policy: read-only Relay metadata only; no file contents and no arbitrary file access.");
            return builder.ToString();
        }

        private readonly List<FileSystemWatcher>               _watchers       = new();
        private readonly Dictionary<string, System.Timers.Timer> _debounceTimers = new();
        private readonly Dictionary<string, string>            _lastFileHashes = new();
        private readonly HashSet<string>                        _pendingSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _timerLock = new();
        private readonly object _jobLock   = new();
        private readonly object _hashLock  = new();
        private readonly System.Timers.Timer _updateTimer = new();
        private readonly SemaphoreSlim _updateCheckLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _uploadThrottle = new SemaphoreSlim(3, 3); // Max 3 concurrent uploads
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        /// <summary>Inject the ESO "live" watch root so tests can point watchers at a temp dir.
        /// Default mirrors the original MyDocuments/Elder Scrolls Online/live path.</summary>
        internal Func<string> WatchRootProvider { get; set; } =
            () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Elder Scrolls Online",
                "live");

        /// <summary>Cadence of the self-re-arming update timer. Injectable so tests can keep it
        /// from firing instead of sleeping 60 minutes.</summary>
        internal TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>Delay before the one-shot startup update check. Injectable so tests can
        /// observe the update HTTP call count deterministically.</summary>
        internal TimeSpan StartupUpdateDelay { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>Number of live <see cref="FileSystemWatcher"/> instances. Lets tests assert
        /// a single watcher set survives repeated StartAsync calls.</summary>
        internal int ActiveWatcherCount => _watchers.Count;

        // Guarded by _startLock so only the first StartAsync arms the update machinery.
        private bool _updateCheckScheduled;

        public FileWatcherService(Action<string> onStatus)
            : this(
                onStatus,
                AppConfig.Instance,
                new UploadService(AppConfig.Instance),
                Path.Combine(AppConfig.ConfigDirectory, "spool"),
                config => config.Save())
        {
        }

        internal FileWatcherService(
            Action<string> onStatus,
            AppConfig config,
            UploadService uploader,
            string spoolDirectory,
            Action<AppConfig> saveConfig)
        {
            _onStatus = onStatus;
            _config = config;
            _uploader = uploader;
            _spoolDirectory = spoolDirectory;
            _saveConfig = saveConfig;
            foreach (var entry in _config.SyncedFileHashes)
                _lastFileHashes[entry.Key] = entry.Value;

            _updateTimer.Interval = UpdateInterval.TotalMilliseconds;
            _updateTimer.AutoReset = false;
            _updateTimer.Elapsed += async (_, _) =>
            {
                try { await CheckForUpdatesAsync(); }
                finally { if (!_disposed) _updateTimer.Start(); }
            };
        }

        private async Task CheckForUpdatesAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.UpdateUrl)) return;
            if (!await _updateCheckLock.WaitAsync(0)) return;

            try
            {
                Console.WriteLine("[RedfurSync] Checking for updates...");

                if (Jobs.Any(j => j.Status == UploadStatus.Uploading)) return;

                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                var payload = await _uploader.CheckForUpdateAsync(version);

                if (payload == null)
                {
                    if (!string.IsNullOrWhiteSpace(_uploader.LastError))
                        _onStatus(_uploader.LastError);
                    return;
                }

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
            catch (Exception ex)
            {
                _onStatus($"Update check failed: {ex.Message}");
                Console.WriteLine($"[Update Error] ✖ Error: {ex.Message}");
            }
            finally
            {
                _updateCheckLock.Release();
            }
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
            await _startLock.WaitAsync();
            try
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
                if (ok) _ = ReconcileExistingFilesAsync();

                if (!_updateCheckScheduled)
                {
                    _updateCheckScheduled = true;
                    _updateTimer.Interval = UpdateInterval.TotalMilliseconds;
                    _updateTimer.Stop();
                    _updateTimer.Start();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(StartupUpdateDelay);
                        await CheckForUpdatesAsync();
                    });
                }
            }
            finally
            {
                _startLock.Release();
            }
        }

        private void SetupWatchers()
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();

            int count   = 0;
            var esoBase = WatchRootProvider();

            void TryWatch(string p) { if (Directory.Exists(p)) { AddWatcher(p); count++; } }

            TryWatch(Path.Combine(esoBase, "SavedVariables"));
            TryWatch(Path.Combine(esoBase, "AddOns", "TamrielTradeCentre"));
            TryWatch(Path.Combine(esoBase, "AddOns", "LibEsoHubPrices"));

            _onStatus(count == 0 ? "Cannot find target directories!" : $"Monitoring {count} folder(s)");
        }

        private async Task ReconcileExistingFilesAsync()
        {
            var esoBase = WatchRootProvider();
            var folders = new[]
            {
                Path.Combine(esoBase, "SavedVariables"),
                Path.Combine(esoBase, "AddOns", "TamrielTradeCentre"),
                Path.Combine(esoBase, "AddOns", "LibEsoHubPrices"),
            };

            foreach (var filePath in folders
                .Where(Directory.Exists)
                .SelectMany(folder => WatchedFiles.Select(name => Path.Combine(folder, name)))
                .Where(File.Exists))
            {
                await EnqueueUploadAsync(filePath);
            }
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

        private string CreateSnapshot(string sourcePath)
        {
            Directory.CreateDirectory(_spoolDirectory);
            var snapshotPath = Path.Combine(_spoolDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(sourcePath)}");
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

        private void RecordSuccessfulSync(UploadJob job)
        {
            if (string.IsNullOrWhiteSpace(job.SourcePath)) return;

            var syncedHash = GetFileHash(job.FilePath);
            if (string.IsNullOrEmpty(syncedHash)) return;

            lock (_hashLock)
            {
                _lastFileHashes[job.SourcePath] = syncedHash;
                _config.SyncedFileHashes[job.SourcePath] = syncedHash;
            }
            _saveConfig(_config);
        }

        internal Task EnqueueFileForTestAsync(string filePath) => EnqueueUploadAsync(filePath);

        private async Task EnqueueUploadAsync(string filePath)
        {
            UploadJob job;
            int currentJobCount;

            lock (_jobLock)
            {
                if (Jobs.Any(existing => existing.SourcePath == filePath && existing.Status is UploadStatus.Queued or UploadStatus.Uploading))
                {
                    _pendingSources.Add(filePath);
                    return;
                }
            }

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
                }
            }

            lock (_jobLock)
            {
                foreach (var ex in Jobs)
                {
                    if (ex.SourcePath == filePath && ex.Status is UploadStatus.Queued or UploadStatus.Uploading)
                    {
                        _pendingSources.Add(filePath);
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

        private async Task EnqueuePendingSourceAsync(UploadJob job)
        {
            if (string.IsNullOrWhiteSpace(job.SourcePath)) return;

            lock (_jobLock)
            {
                if (!_pendingSources.Remove(job.SourcePath)) return;
            }

            await EnqueueUploadAsync(job.SourcePath);
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
                            RecordSuccessfulSync(job);
                            CleanupSnapshot(job);
                            NotifyChanged();
                            _onStatus($"{job.FileName} is already synchronized.");
                            await EnqueuePendingSourceAsync(job);
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

                try
                {
                    await _uploadThrottle.WaitAsync(job.Cts.Token);
                }
                catch (OperationCanceledException) when (job.Cts.Token.IsCancellationRequested)
                {
                    break;
                }

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

            // Reclaim the spool file before announcing the terminal status, so no observer
            // ever sees a finished job whose snapshot is still on disk.
            if (success)
            {
                RecordSuccessfulSync(job);
                CleanupSnapshot(job);
            }

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

            await EnqueuePendingSourceAsync(job);
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
            if (_disposed) return;
            _disposed = true;

            _updateTimer.Stop();
            _updateTimer.Dispose();
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            lock (_timerLock)
            {
                foreach (var t in _debounceTimers.Values) { t.Stop(); t.Dispose(); }
                _debounceTimers.Clear();
            }
            lock (_jobLock)
            {
                foreach (var job in Jobs.Where(job => job.Status is UploadStatus.Queued or UploadStatus.Uploading))
                    job.Cts.Cancel();
            }
            _uploader.Dispose();
        }
    }
}