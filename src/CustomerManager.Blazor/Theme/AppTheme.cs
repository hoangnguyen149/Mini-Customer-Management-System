using MudBlazor;

namespace CustomerManager.Blazor.Theme;

/// <summary>Single source of truth for the app's visual identity. Colors are
/// CEP-inspired (not copied) — sampled from real usage on cep.org.vn: #003399
/// is the deep navy blue used there to emphasize brand copy ("hơn 35 năm"),
/// #3199B1/#00AFC8 is a recurring teal accent. Financial semantic colors
/// (Success/Warning/Error/Info) stay conventional rather than brand-matched —
/// a red brand accent would collide with "Error"/delete affordances.</summary>
public static class AppTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#003399",
            Secondary = "#0E8FA6",
            AppbarBackground = "#00266E",
            AppbarText = "#FFFFFF",
            Background = "#F5FAF9",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            TextPrimary = "#14213D",
            TextSecondary = "#5A6B7A",
            Success = "#15803D",
            Error = "#DC2626",
            Warning = "#B45309",
            Info = "#0E7FA6",
            TableLines = "#E3ECEA",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6E9BE8",
            Secondary = "#3FC4DB",
            AppbarBackground = "#0B1B33",
            AppbarText = "#F9FAFB",
            Background = "#0A1420",
            Surface = "#101E30",
            DrawerBackground = "#0B1B33",
            TextPrimary = "#F3F4F6",
            TextSecondary = "#9CAAB8",
            Success = "#4ADE80",
            Error = "#F87171",
            Warning = "#FBBF24",
            Info = "#4FC3DE",
            TableLines = "#1E2E42",
            LinesDefault = "#1E2E42",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
