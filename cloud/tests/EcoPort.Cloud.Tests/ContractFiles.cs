using System.Text.Json;

namespace EcoPort.Cloud.Tests;

/// <summary>Reads the shared files in <c>contracts/device-v1/</c> (copied next to the test assembly).</summary>
internal static class ContractFiles
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "contracts", name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    public static JsonDocument Json(string name) => JsonDocument.Parse(Bytes(name));
}
