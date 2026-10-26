using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using EcoPort.Cloud.Core.Auth;
using EcoPort.Cloud.Core.Ingest;
using EcoPort.Cloud.Core.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITableStore>(sp =>
    new AzureTableStore(AzureTableStore.CreateServiceClient(sp.GetRequiredService<IConfiguration>())));

// Device secrets: Key Vault in Azure (KeyVault__Uri), local settings (DeviceSecrets__{id}) otherwise.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Services.AddSingleton<IDeviceSecretStore, ConfigDeviceSecretStore>();
}
else
{
    builder.Services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
    builder.Services.AddSingleton<IDeviceSecretStore, KeyVaultDeviceSecretStore>();
}

builder.Services.AddSingleton<RecordIngestor>();
builder.Services.AddSingleton<DeviceUploadHandler>();

builder.Build().Run();
