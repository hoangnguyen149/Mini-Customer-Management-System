using MudBlazor;

namespace CustomerManager.Blazor.Theme;

/// <summary>Single source of truth for the app's visual identity. Colors are
/// CEP-inspired (not copied) — sampled from real usage on cep.org.vn: #003399
/// is the deep navy blue used there to emphasize brand copy ("hơn 35 năm"),
/// #3199B1/#00AFC8 is a recurring teal accent. Financial semantic colors
/// (Success/Warning/Error/Info) stay conventional rather than brand-matched —
/// a red brand accent would collide with "Error"/delete affordances.
///
/// Every palette property below is set explicitly for both light and dark —
/// letting any of them fall back to MudBlazor's Material defaults is how a
/// theme ends up with one stray default-blue chip or a drawer icon that
/// doesn't match the drawer text. app.css only ever styles things MudBlazor
/// has no C# API for (radius/shadow/focus-ring scale, page-specific layout) —
/// colors and typography live here, not there.</summary>
public static class AppTheme
{
    private static readonly string[] AppFontFamily =
        { "Inter", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif" };

    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#003399",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#0E8FA6",
            SecondaryContrastText = "#FFFFFF",

            // A solid navy app bar/drawer competes with the data for
            // attention on every single screen. A light surface + a hairline
            // border (see app.css) reads as chrome, not content — the navy
            // stays as the accent color used for the active nav item, links,
            // and primary actions instead.
            AppbarBackground = "#FFFFFF",
            AppbarText = "#14213D",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#14213D",
            DrawerIcon = "#4B5B6E",

            Background = "#F3F6FB",
            BackgroundGray = "#EEF2F8",
            Surface = "#FFFFFF",
            TextPrimary = "#14213D",
            TextSecondary = "#5A6B7A",
            TextDisabled = "#9AA6B2",

            Success = "#15803D",
            SuccessContrastText = "#FFFFFF",
            Error = "#DC2626",
            ErrorContrastText = "#FFFFFF",
            Warning = "#B45309",
            WarningContrastText = "#FFFFFF",
            Info = "#0E7FA6",
            InfoContrastText = "#FFFFFF",

            Divider = "#E3E8EF",
            LinesDefault = "#E3E8EF",
            LinesInputs = "#CBD5E1",
            TableLines = "#E3ECEA",
            TableStriped = "rgba(20, 33, 61, 0.02)",
            TableHover = "rgba(20, 33, 61, 0.04)",
            ActionDefault = "#5A6B7A",
        },
        PaletteDark = new PaletteDark
        {
            // Same brand hue, lifted in lightness — the literal navy (#003399)
            // fails contrast against a near-black surface, so dark mode uses a
            // tint of it rather than swapping to an unrelated color.
            Primary = "#8AB0F5",
            PrimaryContrastText = "#0B1B33",
            Secondary = "#3FC4DB",
            SecondaryContrastText = "#0B1B33",

            AppbarBackground = "#0B1B33",
            AppbarText = "#F9FAFB",
            DrawerBackground = "#0B1B33",
            DrawerText = "#F3F4F6",
            DrawerIcon = "#9CAAB8",

            Background = "#0A1420",
            BackgroundGray = "#0F1A2A",
            Surface = "#101E30",
            TextPrimary = "#F3F4F6",
            TextSecondary = "#9CAAB8",
            TextDisabled = "#5B6B7D",

            // Semantic colors are all bright/light in dark mode for contrast
            // against a near-black surface — which means dark ink, not white,
            // reads correctly as their ContrastText.
            Success = "#4ADE80",
            SuccessContrastText = "#0A1420",
            Error = "#F87171",
            ErrorContrastText = "#0A1420",
            Warning = "#FBBF24",
            WarningContrastText = "#0A1420",
            Info = "#4FC3DE",
            InfoContrastText = "#0A1420",

            Divider = "#1E2E42",
            LinesDefault = "#1E2E42",
            LinesInputs = "#2A3B52",
            TableLines = "#1E2E42",
            TableStriped = "rgba(255, 255, 255, 0.03)",
            TableHover = "rgba(255, 255, 255, 0.06)",
            ActionDefault = "#9CAAB8",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "264px",
        },
        Typography = new Typography
        {
            // No webfont/CDN (see index.html) — an all-system stack that still
            // covers Vietnamese diacritics correctly on every OS this app is
            // likely to run on. Every variant is its own BaseTypography
            // instance in MudBlazor (none of them inherit Default's
            // FontFamily automatically), so each one is set explicitly.
            Default = new Default { FontFamily = AppFontFamily },
            H1 = new H1 { FontFamily = AppFontFamily },
            H2 = new H2 { FontFamily = AppFontFamily },
            H3 = new H3 { FontFamily = AppFontFamily },
            H4 = new H4 { FontFamily = AppFontFamily },
            H5 = new H5 { FontFamily = AppFontFamily },
            H6 = new H6 { FontFamily = AppFontFamily },
            Subtitle1 = new Subtitle1 { FontFamily = AppFontFamily },
            Subtitle2 = new Subtitle2 { FontFamily = AppFontFamily },
            Body1 = new Body1 { FontFamily = AppFontFamily },
            Body2 = new Body2 { FontFamily = AppFontFamily },
            Caption = new Caption { FontFamily = AppFontFamily },
            Overline = new Overline { FontFamily = AppFontFamily },
            // Material's default is UPPERCASE button text — for Vietnamese
            // that strips every diacritic's visual weight ("ĐĂNG NHẬP" reads
            // far worse than "Đăng nhập") and is slower to scan either way.
            Button = new Button { FontFamily = AppFontFamily, TextTransform = "none" },
        },
    };
}
