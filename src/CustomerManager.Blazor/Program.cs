using CustomerManager.Blazor;
using CustomerManager.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// Auth state: JWT lives only in TokenProvider (in-memory) — see its XML doc for why.
builder.Services.AddScoped<TokenProvider>();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddAuthorizationCore();

builder.Services.AddTransient<AuthorizationMessageHandler>();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services
    .AddHttpClient("CustomerManagerApi", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthorizationMessageHandler>();

// Every *ApiService gets the same named, authorization-handler-wrapped HttpClient.
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("CustomerManagerApi"));

builder.Services.AddScoped<IAuthApiService, AuthApiService>();
builder.Services.AddScoped<ICustomerApiService, CustomerApiService>();

await builder.Build().RunAsync();
