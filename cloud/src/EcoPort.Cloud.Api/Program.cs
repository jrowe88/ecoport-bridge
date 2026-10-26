using EcoPort.Cloud.Core.Read;
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

// SWA managed functions cannot use managed identity: Storage__ConnectionString is required there.
// In Azure it carries a read + list SAS for the Table service only, so never try to create tables.
builder.Services.AddSingleton<ITableStore>(sp =>
    new AzureTableStore(AzureTableStore.CreateServiceClient(sp.GetRequiredService<IConfiguration>()), createTables: false));
builder.Services.AddSingleton<DeviceQueries>();

builder.Build().Run();
