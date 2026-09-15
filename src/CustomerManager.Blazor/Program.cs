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
// Singleton, not Scoped: IHttpClientFactory builds AuthorizationMessageHandler's
// pipeline in its own internal DI scope (documented HttpClientFactory behavior),
// separate from the app's normal @inject scope. A Scoped TokenProvider would
// resolve to a second, never-populated instance inside that handler pipeline,
// silently dropping the Authorization header from every request. Singleton
// guarantees the handler and the rest of the app always share the same instance.
builder.Services.AddSingleton<TokenProvider>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<CommandPaletteService>();

// Singleton (not Scoped): SilentRefreshScheduler is a Singleton that needs to
// call NotifyAuthenticationStateChanged() when a background token refresh
// fails — a Singleton can't safely depend on a Scoped service (same class of
// bug as the AuthorizationMessageHandler/TokenProvider issue above). WASM has
// exactly one real scope for the app's lifetime anyway, so this changes
// nothing observable, it just makes the DI graph consistent.
builder.Services.AddSingleton<CustomAuthStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddSingleton<SilentRefreshScheduler>();
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
