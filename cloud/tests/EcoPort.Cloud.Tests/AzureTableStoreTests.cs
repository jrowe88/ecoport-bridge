using System.Net;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using EcoPort.Cloud.Core.Storage;

namespace EcoPort.Cloud.Tests;

/// <summary>
/// Checks which HTTP calls <see cref="AzureTableStore"/> makes, using a fake transport instead of
/// real storage.
/// </summary>
public class AzureTableStoreTests
{
    [Fact]
    public async Task ReadOnlyStore_NeverCreatesTables()
    {
        var handler = new RecordingHandler();
        var store = new AzureTableStore(ServiceClient(handler), createTables: false);

        await foreach (var _ in store.ListDevicesAsync(CancellationToken.None))
        {
        }

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains(handler.Requests, r => r.Path.StartsWith("/Devices()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultStore_CreatesTablesOnFirstUse()
    {
        var handler = new RecordingHandler();
        var store = new AzureTableStore(ServiceClient(handler));

        await foreach (var _ in store.ListDevicesAsync(CancellationToken.None))
        {
        }

        Assert.Equal(TableNames.All.Count, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/Tables"));
    }

    private static TableServiceClient ServiceClient(HttpMessageHandler handler) =>
        new(
            new Uri("https://example.table.core.windows.net"),
            new AzureSasCredential("sv=2024-01-01&sig=test"),
            new TableClientOptions
            {
                Transport = new HttpClientTransport(new HttpClient(handler)),
                Retry = { MaxRetries = 0 },
            });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));

            // Table creation: report that it already exists. Entity queries: no rows.
            var response = request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.Conflict, """{"odata.error":{"code":"TableAlreadyExists","message":{"lang":"en-US","value":"exists"}}}""")
                : Json(HttpStatusCode.OK, """{"value":[]}""");
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
