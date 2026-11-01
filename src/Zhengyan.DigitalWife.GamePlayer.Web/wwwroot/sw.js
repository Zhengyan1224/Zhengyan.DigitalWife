const CACHE_NAME = "zhengyan-web-runtime-v1";
const CORE_ASSETS = ["./", "./index.html", "./css/app.css", "./js/web-runtime.js", "./js/webgl2-renderer.js", "./js/web-storage.js", "./js/web-audio.js", "./js/web-platform.js"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    if (event.request.method !== "GET") return;
    event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request).then(response => {
        if (response.ok && new URL(event.request.url).origin === self.location.origin) {
            const clone = response.clone();
            caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
    })));
});
