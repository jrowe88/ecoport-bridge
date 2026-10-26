using System.Net;
using System.Text.Json;
using System.Web;
using EcoPort.Cloud.Contracts;
using EcoPort.Cloud.Core.Read;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace EcoPort.Cloud.Api;

/// <summary>
/// User API under <c>/api/</c> (default route prefix). Static Web Apps route rules restrict
/// <c>/api/*</c> to role <c>bench</c>, so the functions are anonymous at the Functions level.
/// Logic lives in <see cref="DeviceQueries"/>.
/// </summary>
public sealed class DevicesFunctions(DeviceQueries queries)
{
    [Function("ListDevices")]
    public async Task<HttpResponseData> ListDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices")] HttpRequestData req,
        CancellationToken ct) =>
        await Json(req, HttpStatusCode.OK, await queries.ListDevicesAsync(ct), ct);

    [Function("GetDeviceState")]
    public async Task<HttpResponseData> GetState(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/state")] HttpRequestData req,
        string deviceId,
        CancellationToken ct) =>
        await Respond(req, await queries.GetStateAsync(deviceId, ct), ct);

    [Function("GetDeviceTelemetry")]
    public async Task<HttpResponseData> GetTelemetry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/telemetry")] HttpRequestData req,
        string deviceId,
        CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString(req.Url.Query);
        return await Respond(req, await queries.GetTelemetryAsync(deviceId, query["from"], query["to"], ct), ct);
    }

    private static Task<HttpResponseData> Respond<T>(HttpRequestData req, ApiResult<T> result, CancellationToken ct) =>
        result.StatusCode switch
        {
            200 => Json(req, HttpStatusCode.OK, result.Value, ct),
            400 => Json(req, HttpStatusCode.BadRequest, new ErrorResponse(result.Error ?? "Bad request."), ct),
            var status => Task.FromResult(req.CreateResponse((HttpStatusCode)status)),
        };

    private static async Task<HttpResponseData> Json<T>(HttpRequestData req, HttpStatusCode status, T value, CancellationToken ct)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(value, JsonDefaults.Options), ct);
        return response;
    }
}
