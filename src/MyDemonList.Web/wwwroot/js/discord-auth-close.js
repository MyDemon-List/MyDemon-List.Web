const origineApplication = document.body.dataset.authOrigin;

if (window.opener && origineApplication) {
    try {
        window.opener.postMessage("auth:done", origineApplication);
    } catch {
    }

    window.close();
} else {
    location.href = "/";
}
