// Tiny, single-purpose listener: Ctrl+K / Cmd+K toggles the command palette.
// preventDefault is only called for that exact combo, so it never interferes
// with normal typing (form inputs, browser shortcuts, etc.) elsewhere.
window.commandPalette = {
    register: function (dotNetRef) {
        const handler = function (e) {
            const key = e.key.toLowerCase();
            if ((e.ctrlKey || e.metaKey) && key === 'k') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('Toggle');
            }
        };
        document.addEventListener('keydown', handler);
        return handler;
    },
    unregister: function (handler) {
        if (handler) {
            document.removeEventListener('keydown', handler);
        }
    }
};
