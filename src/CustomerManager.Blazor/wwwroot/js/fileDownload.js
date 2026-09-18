// Triggers a browser download from bytes already in memory on the Blazor
// side. Needed because template/error-report files are fetched through
// HttpClient (so the Bearer token gets attached by AuthorizationMessageHandler)
// rather than a plain <a href> pointing at the API, which cannot carry an
// Authorization header.
window.fileDownload = {
    save: function (fileName, base64, contentType) {
        const bytes = atob(base64);
        const buffer = new Uint8Array(bytes.length);
        for (let i = 0; i < bytes.length; i++) {
            buffer[i] = bytes.charCodeAt(i);
        }

        const blob = new Blob([buffer], { type: contentType });
        const url = URL.createObjectURL(blob);

        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);

        URL.revokeObjectURL(url);
    }
};
