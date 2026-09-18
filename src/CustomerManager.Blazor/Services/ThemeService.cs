using Microsoft.JSInterop;

namespace CustomerManager.Blazor.Services;

/// <summary>App-wide light/dark switch, persisted to localStorage. Singleton —
/// see TokenProvider for why a Scoped service here would silently diverge from
/// any consumer resolved through IHttpClientFactory's internal DI scope; this
/// service isn't used by a DelegatingHandler, but Singleton keeps a single
/// source of truth explicit regardless.</summary>
public class ThemeService
{
    // Must match the key read by index.html's pre-paint inline script.
    private const string StorageKey = "customermanager-theme";
    private readonly IJSRuntime _jsRuntime;
    private bool _initialized;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    /// <summary>Idempotent by design: both AppProviders and MainLayout call
    /// this on OnInitializedAsync (each needs ThemeService ready for its own
    /// render — AppProviders for MudThemeProvider, MainLayout for its
    /// light/dark toggle icon), and navigating between AuthLayout and
    /// MainLayout tears down and recreates whichever of those components was
    /// mounted, re-triggering their OnInitializedAsync. Without this guard,
    /// re-running the full body here would just redundantly reread storage
    /// (harmless on its own) but does redo two JSInterop round trips every
    /// navigation for no benefit — the guard makes it a no-op after the first.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            IsDarkMode = stored switch
            {
                "dark" => true,
                "light" => false,
                // No stored preference yet (first-ever visit) — follow the OS,
                // same as the inline pre-paint script in index.html already did.
                _ => await _jsRuntime.InvokeAsync<bool>("appTheme.prefersDark")
            };

            await _jsRuntime.InvokeVoidAsync("appTheme.apply", IsDarkMode ? "dark" : "light");
        }
        catch (JSException)
        {
            // Storage/matchMedia blocked (e.g. private browsing) — IsDarkMode
            // keeps its false default and the app just runs in light mode for
            // this session instead of failing to load.
        }

        OnChange?.Invoke();
    }

    public async Task ToggleAsync()
    {
        IsDarkMode = !IsDarkMode;

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, IsDarkMode ? "dark" : "light");
            await _jsRuntime.InvokeVoidAsync("appTheme.apply", IsDarkMode ? "dark" : "light");
        }
        catch (JSException)
        {
            // Best-effort persistence/DOM sync — IsDarkMode above is still
            // correct for the rest of this session even if storage is blocked.
        }

        OnChange?.Invoke();
    }
}
