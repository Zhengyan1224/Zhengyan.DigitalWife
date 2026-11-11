if(typeof importScripts==='function')importScripts('./service-worker-assets.js');
const CACHE_NAME = "zhengyan-web-runtime-v6-"+(self.assetsManifest?.version||'development');
const CORE_ASSETS = ["./", "./index.html", "./css/app.css", "./js/web-runtime.js", "./js/webgl2-renderer.js", "./js/web-storage.js", "./js/web-audio.js", "./js/web-platform.js"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith('zhengyan-web-runtime-') && key !== CACHE_NAME).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});

self.addEventListener('message',event=>{
    if(event.data?.type!=='cache-offline-game')return;
    const port=event.ports[0];
    event.waitUntil((async()=>{
        const cache=await caches.open(CACHE_NAME),urls=new Set(CORE_ASSETS);
        for(const asset of self.assetsManifest?.assets||[])if(!asset.url.endsWith('.map'))urls.add(asset.url);
        const manifestUrl=new URL(event.data.manifest||'./web-game/web.manifest.json',self.location.href);
        if(manifestUrl.origin!==self.location.origin)throw new Error('Offline game must use the same origin.');
        const response=await fetch(manifestUrl,{cache:'no-store'});if(!response.ok)throw new Error('Game manifest HTTP '+response.status);
        const manifest=await response.clone().json();await cache.put(manifestUrl,response);
        for(const asset of manifest.assets||[]){const url=new URL(asset.path,manifestUrl);if(url.origin!==manifestUrl.origin)throw new Error('Invalid game asset origin.');if(asset.sha256)url.searchParams.set('v',asset.sha256.toLowerCase());urls.add(url.href);}
        let count=0;
        for(const path of urls){const url=new URL(path,self.location.href);if(!(await cache.match(url))){const response=await fetch(url);if(!response.ok)throw new Error('Offline asset HTTP '+response.status+': '+path);await cache.put(url,response);}port.postMessage({progress:++count/urls.size,count,total:urls.size});}
        port.postMessage({done:true,count});
    })().catch(error=>port.postMessage({error:error.message})));
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

    const publishedAsset=(self.assetsManifest?.assets||[]).find(asset=>new URL(asset.url,self.location.href).pathname===url.pathname);
    event.respondWith(caches.match(event.request).then(async cached => cached || (publishedAsset && await caches.match(new URL(publishedAsset.url,self.location.href))) || fetch(event.request).then(response => {
        if (response.ok && new URL(event.request.url).origin === self.location.origin) {
            const clone = response.clone();
            caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
    })));
});
