using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RedfurSync
{
    public class UploadService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly AppConfig  _config;

        public string? LastError { get; private set; }

        public UploadService(AppConfig config)
        {
            _config = config;
            _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _http.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        }

        // ── Connection check ─────────────────────────────────────────────────
        /// <summary>
        /// Pings the server with a GET request. Returns (reachable, message).
        /// No server changes required — a 4xx back means it's alive and auth is working.
        /// </summary>
        public async Task<(bool ok, string message)> PingAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _http.GetAsync(_config.ServerUrl, cts.Token);

                // 401 = reached server but key is wrong
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return (false, "Server reached but API key was rejected (401)");

                // Any other response (404, 405, 200…) means server is up and key was accepted
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

        // ── Upload ───────────────────────────────────────────────────────────
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
