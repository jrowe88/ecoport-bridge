using System.Text.Json;

namespace EcoPort.Cloud.Contracts;

/// <summary>
/// The one set of JSON options used by the device API, the user API and the dashboard.
/// camelCase names; strict about required members and nullability, so a malformed upload
/// becomes a 400 instead of silently defaulting to zero.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
