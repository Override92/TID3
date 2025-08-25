using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using System.Threading;

namespace TID3.Utils
{
    public static class HttpClientManager
    {
        private static readonly Lazy<HttpClient> _lazyClient = new Lazy<HttpClient>(() => 
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        });

        public static HttpClient Instance => _lazyClient.Value;

        public static HttpClient CreateClientWithUserAgent(string userAgent)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        public static async Task<HttpResponseMessage?> SafeGetAsync(this HttpClient client, string url, int maxRetries = 3, int delayMs = 1000)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    return response;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries - 1)
                {
                    System.Diagnostics.Debug.WriteLine($"HTTP request failed (attempt {attempt + 1}/{maxRetries}): {ex.Message}");
                    await Task.Delay(delayMs * (attempt + 1)); // Exponential backoff
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ex.CancellationToken.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"HTTP request timed out (attempt {attempt + 1}/{maxRetries}): {ex.Message}");
                    if (attempt < maxRetries - 1)
                        await Task.Delay(delayMs * (attempt + 1));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unexpected error in HTTP request (attempt {attempt + 1}/{maxRetries}): {ex.Message}");
                    if (attempt < maxRetries - 1)
                        await Task.Delay(delayMs);
                }
            }
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