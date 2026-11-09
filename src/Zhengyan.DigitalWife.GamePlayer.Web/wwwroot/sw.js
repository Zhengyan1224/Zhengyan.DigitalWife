const CACHE_NAME = "zhengyan-web-runtime-v5";
const CORE_ASSETS = ["./", "./index.html", "./css/app.css", "./js/web-runtime.js", "./js/webgl2-renderer.js", "./js/web-storage.js", "./js/web-audio.js", "./js/web-platform.js"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    if (event.request.method !== "GET") return;
    const url = new URL(event.request.url);
    const projectManifest = url.pathname.endsWith("/web.manifest.json");
    if (url.origin !== self.location.origin || (event.request.cache === "no-store" && !projectManifest) || url.pathname.includes("/_framework/debug/")) return;

    // These modules keep their URL across builds. Revalidate them so a cached
    // decoder or input bridge cannot survive a runtime update indefinitely.
    const runtimeAsset = CORE_ASSETS.some(asset => new URL(asset, self.location.href).href === url.href);

    // Always check the network for the HTML entry point so a new deployment
    // can publish a new fingerprinted WebAssembly/JavaScript file immediately.
    // Project manifests are mutable entry points. Refresh them from the network
    // while retaining an offline fallback; their assets use content-hash URLs.
    if (event.request.mode === "navigate" || runtimeAsset || projectManifest) {
        event.respondWith(fetch(event.request).then(response => {
            if (response.ok) {
                const clone = response.clone();
                const cacheKey = event.request.mode === "navigate" ? "./index.html" : event.request;
                caches.open(CACHE_NAME).then(cache => cache.put(cacheKey, clone));
            }
            return response;
        }).catch(() => caches.match(event.request.mode === "navigate" ? "./index.html" : event.request)));
        return;
    }

    event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request).then(response => {
        if (response.ok && new URL(event.request.url).origin === self.location.origin) {
            const clone = response.clone();
            caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
    })));
});
