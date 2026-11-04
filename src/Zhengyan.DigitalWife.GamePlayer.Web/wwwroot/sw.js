const CACHE_NAME = "zhengyan-web-runtime-v4";
const CORE_ASSETS = ["./", "./index.html", "./css/app.css", "./js/web-runtime.js", "./js/webgl2-renderer.js", "./js/web-storage.js", "./js/web-audio.js", "./js/web-platform.js"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    if (event.request.method !== "GET") return;

    // Always check the network for the HTML entry point so a new deployment
    // can publish a new fingerprinted WebAssembly/JavaScript file immediately.
    if (event.request.mode === "navigate") {
        event.respondWith(fetch(event.request).then(response => {
            if (response.ok) {
                const clone = response.clone();
                caches.open(CACHE_NAME).then(cache => cache.put("./index.html", clone));
            }
            return response;
        }).catch(() => caches.match("./index.html")));
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
