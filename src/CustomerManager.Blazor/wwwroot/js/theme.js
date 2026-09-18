// Runtime half of the dark/light mechanism (the pre-paint half is the inline
// script in index.html's <head>, which cannot depend on this file — it has to
// run before any external script has loaded). ThemeService calls apply()
// after every resolve/toggle so document.documentElement's data-theme
// attribute — the hook app.css uses for anything MudThemeProvider's own
// runtime-injected <style> doesn't cover — always matches ThemeService.IsDarkMode.
window.appTheme = {
    prefersDark: function () {
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    },
    apply: function (mode) {
        document.documentElement.dataset.theme = mode;
    }
};
