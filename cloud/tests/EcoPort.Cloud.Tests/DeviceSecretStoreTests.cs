using Azure;
using Azure.Security.KeyVault.Secrets;
using EcoPort.Cloud.Core.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace EcoPort.Cloud.Tests;

public class DeviceSecretStoreTests
{
    private const string SecretB64 = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public async Task ConfigStore_ReadsBase64FromDeviceSecretsSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceSecrets:WH001"] = SecretB64 })
            .Build();
        var store = new ConfigDeviceSecretStore(config);

        var secret = await store.GetSecretAsync("WH001", CancellationToken.None);
        Assert.Equal(Convert.FromBase64String(SecretB64), secret);
        Assert.Null(await store.GetSecretAsync("WH002", CancellationToken.None));
    }

    [Fact]
    public async Task ConfigStore_InvalidBase64_ReturnsNull()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DeviceSecrets:WH001"] = "not base64!" })
            .Build();
        Assert.Null(await new ConfigDeviceSecretStore(config).GetSecretAsync("WH001", CancellationToken.None));
    }

    [Fact]
    public async Task KeyVaultStore_ReadsDeviceSecret_AndCachesFor10Minutes()
    {
        var client = new FakeSecretClient { ["device-WH001"] = SecretB64 };
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-09T20:00:00Z"));
        var store = new KeyVaultDeviceSecretStore(client, time);

        Assert.Equal(Convert.FromBase64String(SecretB64), await store.GetSecretAsync("WH001", CancellationToken.None));
        time.Advance(TimeSpan.FromMinutes(9));
        await store.GetSecretAsync("WH001", CancellationToken.None);
        Assert.Equal(1, client.Calls);

        time.Advance(TimeSpan.FromMinutes(2));
        await store.GetSecretAsync("WH001", CancellationToken.None);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task KeyVaultStore_MissingSecret_ReturnsNull()
    {
        var store = new KeyVaultDeviceSecretStore(new FakeSecretClient(), TimeProvider.System);
        Assert.Null(await store.GetSecretAsync("WH001", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WH_001")]
    [InlineData("../x")]
    public async Task KeyVaultStore_InvalidDeviceId_ReturnsNullWithoutCalling(string deviceId)
    {
        var client = new FakeSecretClient();
        var store = new KeyVaultDeviceSecretStore(client, TimeProvider.System);
        Assert.Null(await store.GetSecretAsync(deviceId, CancellationToken.None));
        Assert.Equal(0, client.Calls);
    }

    private sealed class FakeSecretClient : SecretClient
    {
        private readonly Dictionary<string, string> _secrets = new();

        public int Calls { get; private set; }

        public string this[string name] { set => _secrets[name] = value; }

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!_secrets.TryGetValue(name, out var value))
            {
                throw new RequestFailedException(404, "SecretNotFound");
            }

            var secret = SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value);
            return Task.FromResult(Response.FromValue(secret, null!));
        }
    }
}
