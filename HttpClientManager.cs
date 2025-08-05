using System;
using System.Net.Http;

namespace TID3
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
    }
}