using System.Net;
using EcoPort.Cloud.Core.Ingest;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace EcoPort.Cloud.Device;

/// <summary><c>POST /device/v1/records</c> (contracts/device-v1). Logic lives in <see cref="DeviceUploadHandler"/>.</summary>
public sealed class RecordsFunction(DeviceUploadHandler handler)
{
    [Function("Records")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device/v1/records")] HttpRequestData req,
        CancellationToken ct)
    {
        if (req.Headers.TryGetValues("Content-Length", out var lengths)
            && long.TryParse(lengths.FirstOrDefault(), out var declared)
            && declared > DeviceUploadHandler.MaxBodyBytes)
        {
            return req.CreateResponse(HttpStatusCode.RequestEntityTooLarge);
        }

        // Read at most one byte past the limit so an oversized chunked body is caught without
        // buffering all of it.
        var body = await ReadBodyAsync(req.Body, DeviceUploadHandler.MaxBodyBytes + 1, ct);

        var result = await handler.HandleAsync(
            req.Method,
            req.Url.AbsolutePath,
            Header(req, "X-Device-Id"),
            Header(req, "X-Timestamp"),
            Header(req, "X-Signature"),
            body,
            ct);

        var response = req.CreateResponse((HttpStatusCode)result.StatusCode);
        if (result.Json is not null)
        {
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(result.Json, ct);
        }

        return response;
    }

    private static string? Header(HttpRequestData req, string name) =>
        req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(Stream stream, int limit, CancellationToken ct)
    {
        var buffer = new byte[limit];
        var total = 0;
        while (total < limit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, limit - total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer.AsMemory(0, total);
    }
}
