using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using System.Threading;

namespace TID3.Utils
{
    public static class HttpClientManager
    {
        private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> _clients = new();
        private static readonly object _lock = new object();

        public static HttpClient GetOrCreateClient(string name, Action<HttpClient>? configure = null)
        {
            return _clients.GetOrAdd(name, _ => new Lazy<HttpClient>(() =>
            {
                TID3Logger.Debug("HTTP", "Creating new HTTP client", new { ClientName = name }, "HttpClientManager");
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                configure?.Invoke(client);
                return client;
            })).Value;
        }

        public static HttpClient Default => GetOrCreateClient("default");

        public static HttpClient MusicBrainz => GetOrCreateClient("musicbrainz", client =>
        {
            var settings = SettingsManager.LoadSettings();
            client.DefaultRequestHeaders.Add("User-Agent", settings.GetUserAgent());
        });

        public static HttpClient CoverArt => GetOrCreateClient("coverart", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TID3 Cover Art Fetcher/1.0");
        });

        public static HttpClient Update => GetOrCreateClient("update", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TID3/1.0");
        });

        public static HttpClient AcoustId => GetOrCreateClient("acoustid", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TID3/1.0 (contact@example.com)");
        });

        public static HttpClient General => GetOrCreateClient("general", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TID3/1.0");
        });

        [Obsolete("Use specific named clients like MusicBrainz, CoverArt, etc. instead")]
        public static HttpClient Instance => Default;

        [Obsolete("Use GetOrCreateClient with configuration instead")]
        public static HttpClient CreateClientWithUserAgent(string userAgent)
        {
            return GetOrCreateClient($"legacy_{userAgent}", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            });
        }

        public static async Task<HttpResponseMessage?> SafeGetAsync(this HttpClient client, string url, int maxRetries = 3, int delayMs = 1000)
        {
            var startTime = DateTime.Now;
            TID3Logger.Http.LogRequest(url, "Unknown", "HttpClientManager");
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    var duration = DateTime.Now - startTime;
                    TID3Logger.Http.LogResponse(url, (int)response.StatusCode, response.Content.Headers.ContentLength, duration, "HttpClientManager");
                    return response;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries - 1)
                {
                    TID3Logger.Warning("HTTP", "HTTP request failed, retrying", new { 
                        Url = url,
                        Attempt = attempt + 1,
                        MaxRetries = maxRetries,
                        ErrorMessage = ex.Message
                    }, "HttpClientManager");
                    await Task.Delay(delayMs * (attempt + 1)); // Exponential backoff
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ex.CancellationToken.IsCancellationRequested)
                {
                    TID3Logger.Warning("HTTP", "HTTP request timed out, retrying", new {
                        Url = url,
                        Attempt = attempt + 1,
                        MaxRetries = maxRetries,
                        ErrorMessage = ex.Message
                    }, "HttpClientManager");
                    if (attempt < maxRetries - 1)
                        await Task.Delay(delayMs * (attempt + 1));
                }
                catch (Exception ex)
                {
                    TID3Logger.Error("HTTP", "Unexpected error in HTTP request", ex, new {
                        Url = url,
                        Attempt = attempt + 1,
                        MaxRetries = maxRetries
                    }, "HttpClientManager");
                    if (attempt < maxRetries - 1)
                        await Task.Delay(delayMs);
                }
            }
            
            var finalDuration = DateTime.Now - startTime;
            TID3Logger.Error("HTTP", "All retry attempts failed", null, new {
                Url = url,
                MaxRetries = maxRetries,
                TotalDurationMs = finalDuration.TotalMilliseconds
            }, "HttpClientManager");
            
            return null;
        }

        public static string GetFriendlyErrorMessage(Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx when httpEx.Message.Contains("Name or service not known") => "No internet connection available. Please check your network connection.",
                HttpRequestException httpEx when httpEx.Message.Contains("timeout") => "Request timed out. The service may be temporarily unavailable.",
                HttpRequestException httpEx when httpEx.Message.Contains("SSL") => "Secure connection failed. Please check your internet security settings.",
                TaskCanceledException when ex.InnerException is TimeoutException => "Request timed out. Please try again later.",
                TaskCanceledException => "Request was cancelled.",
                _ => "Network error occurred. Please check your internet connection and try again."
            };
        }
    }
}