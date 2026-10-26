using EcoPort.Web;
using EcoPort.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The User API is served from the same origin under /api (SWA managed functions, or the SWA CLI locally).
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IEcoPortApi, EcoPortApiClient>();
builder.Services.AddScoped<IAuthStatus, SwaAuthStatus>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMudServices();

await builder.Build().RunAsync();
