using System.Net;
using System.Text.Json;
using Marketplaces.Credentials;

namespace Marketplaces.Api.IntegrationTests.Helpers;

/// <summary>
/// Shared test doubles for marketplace adapter tests.
/// Extracted from AmazonMarketplaceAdapterTests (S3-0) so that Walmart and eBay
/// adapter tests can reuse the same fakes without duplication.
/// </summary>
public static class MarketplaceAdapterTestHelpers
{
    /// <summary>
    /// Fake HTTP handler that queues responses and records sent requests.
    /// Supports both queue-based (FIFO) responses and URL-keyed responses
    /// for multi-step flows (e.g., eBay create-offer → publish-offer).
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        private readonly Dictionary<string, Queue<HttpResponseMessage>> _urlResponses = new(StringComparer.OrdinalIgnoreCase);
        public List<HttpRequestMessage> SentRequests { get; } = [];

        /// <summary>
        /// Enqueue a response that will be dequeued in FIFO order regardless of URL.
        /// </summary>
        public void EnqueueResponse(HttpStatusCode statusCode, object? body = null)
        {
            var response = new HttpResponseMessage(statusCode);
            if (body is not null)
            {
                response.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json");
            }
            _responses.Enqueue(response);
        }

        /// <summary>
        /// Enqueue a response keyed to a specific URL prefix. When a request matches,
        /// this response is returned instead of the generic FIFO queue.
        /// Useful for multi-step flows where different endpoints return different responses.
        /// </summary>
        public void EnqueueResponseForUrl(string urlPrefix, HttpStatusCode statusCode, object? body = null)
        {
            var response = new HttpResponseMessage(statusCode);
            if (body is not null)
            {
                response.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json");
            }

            if (!_urlResponses.TryGetValue(urlPrefix, out var queue))
            {
                queue = new Queue<HttpResponseMessage>();
                _urlResponses[urlPrefix] = queue;
            }
            queue.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentRequests.Add(request);

            // Check URL-keyed responses first
            var requestUrl = request.RequestUri?.ToString() ?? string.Empty;
            foreach (var (urlPrefix, queue) in _urlResponses)
            {
                if (requestUrl.Contains(urlPrefix, StringComparison.OrdinalIgnoreCase) && queue.Count > 0)
                {
                    return Task.FromResult(queue.Dequeue());
                }
            }

            // Fall back to generic FIFO queue
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No more queued responses in FakeHttpMessageHandler")
                });
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    /// <summary>
    /// Fake vault client for adapter tests — returns pre-configured secrets.
    /// </summary>
    internal sealed class FakeVaultClient : IVaultClient
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public void SetSecret(string path, string value) => _secrets[path] = value;

        public Task<string> GetSecretAsync(string path, CancellationToken ct = default)
        {
            if (!_secrets.TryGetValue(path, out var value))
                throw new InvalidOperationException($"Test vault: secret not found: {path}");
            return Task.FromResult(value);
        }
    }

    /// <summary>
    /// Fake IHttpClientFactory that returns a pre-configured HttpClient.
    /// </summary>
    internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
