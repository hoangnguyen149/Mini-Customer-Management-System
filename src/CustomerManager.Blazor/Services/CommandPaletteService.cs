using Microsoft.JSInterop;

namespace CustomerManager.Blazor.Services;

/// <summary>Bridges the Ctrl+K JS keydown listener (wwwroot/js/commandPalette.js)
/// back into Blazor. Singleton — one global shortcut, one global listener.</summary>
public class CommandPaletteService : IDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<CommandPaletteService>? _selfReference;
    private bool _registered;

    public CommandPaletteService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public event Action? OnToggleRequested;

    public async Task EnsureRegisteredAsync()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        _selfReference = DotNetObjectReference.Create(this);
        await _jsRuntime.InvokeVoidAsync("commandPalette.register", _selfReference);
    }

    [JSInvokable]
    public void Toggle() => OnToggleRequested?.Invoke();

    public void Dispose() => _selfReference?.Dispose();
}
