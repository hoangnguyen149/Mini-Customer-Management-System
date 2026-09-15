using Microsoft.JSInterop;

namespace CustomerManager.Blazor.Services;

/// <summary>App-wide light/dark switch, persisted to localStorage. Singleton —
/// see TokenProvider for why a Scoped service here would silently diverge from
/// any consumer resolved through IHttpClientFactory's internal DI scope; this
/// service isn't used by a DelegatingHandler, but Singleton keeps a single
/// source of truth explicit regardless.</summary>
public class ThemeService
{
    private const string StorageKey = "customermanager-theme";
    private readonly IJSRuntime _jsRuntime;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        IsDarkMode = stored == "dark";
        OnChange?.Invoke();
    }

    public async Task ToggleAsync()
    {
        IsDarkMode = !IsDarkMode;
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, IsDarkMode ? "dark" : "light");
        OnChange?.Invoke();
    }
}
