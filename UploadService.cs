using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RedfurSync
{
    public class UploadService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly AppConfig  _config;

        public string? LastError { get; private set; }

        public async Task<UpdatePayload?> CheckForUpdateAsync(string currentVersion)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _http.GetAsync(_config.UpdateUrl, cts.Token);
                
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<UpdatePayload>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                if (payload != null && !string.IsNullOrWhiteSpace(payload.Version) && payload.Version != currentVersion)
                {                    
                    return payload;
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[Update Error] ✖ Error: {ex.Message}"); 
            }
            return null;
        }

        public async Task<bool> DownloadUpdateAsync(UploadJob job)
        {
            try
            {
                LastError = null;
                using var response = await _http.GetAsync(job.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, job.Cts.Token);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync(job.Cts.Token);
                
                await using var fileStream = new FileStream(job.FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, job.Cts.Token)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, job.Cts.Token);
                    totalRead += bytesRead;
                    if (totalBytes.HasValue)
                    {
                        job.Progress = (float)totalRead / totalBytes.Value;
                    }
                }

                job.Progress = 1f;
                return true;
            }
            catch (Exception ex)
            {
                LastError = job.ErrorMessage = $"Download failed: {ex.Message}";
                return false;
            }
        }

        public UploadService(AppConfig config)
        {
            _config = config;
            _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _http.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        }

        public async Task<(bool ok, string message)> PingAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _http.GetAsync(_config.ServerUrl, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return (false, "Server reached but API key was rejected (401)");

                return (true, $"Connected — server responded {(int)response.StatusCode}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Connection timed out — server did not respond in 8 s");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot reach server: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<bool> UploadAsync(UploadJob job)
        {
            try
            {
                LastError = null;

                var fileInfo = new FileInfo(job.FilePath);
                if (!fileInfo.Exists)
                {
                    LastError = job.ErrorMessage = "File not found on disk";
                    return false;
                }

                // FileShare.ReadWrite is used because the game client may still have a handle open.
                // However, we've already ensured it isn't strictly locked via IsFileLocked in FileWatcherService.
                await using var fileStream = new FileStream(
                    job.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                await using var progressStream = new ProgressStream(
                    fileStream, fileInfo.Length, p => job.Progress = p);

                using var form = new MultipartFormDataContent();

                var streamContent = new StreamContent(progressStream);
                streamContent.Headers.ContentType      = new MediaTypeHeaderValue("application/octet-stream");
                streamContent.Headers.ContentLength    = fileInfo.Length;

                form.Add(streamContent,                         "file",        job.FileName);
                form.Add(new StringContent(_config.DisplayName),"displayName"           );

                var response = await _http.PostAsync(_config.ServerUrl, form, job.Cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    LastError = job.ErrorMessage =
                        $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}" +
                        (string.IsNullOrWhiteSpace(body) ? "" : $": {body.Trim()}");
                    return false;
                }

                job.Progress = 1f;
                return true;
            }
            catch (OperationCanceledException)
            {
                LastError = job.ErrorMessage = "Cancelled by user";
                return false;
            }
            catch (IOException ex)
            {
                LastError = job.ErrorMessage = $"File IO error (possibly locked): {ex.Message}";
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastError = job.ErrorMessage =
                    $"Connection failed: {ex.Message}" +
                    (ex.InnerException != null ? $" ({ex.InnerException.Message})" : "");
                return false;
            }
            catch (Exception ex)
            {
                LastError = job.ErrorMessage = ex.Message;
                return false;
            }
        }

        public void Dispose() => _http.Dispose();
    }
}