// Propagate the WASM build ID to dependencies as well as the entry module.
// Sharing the audio URL with C# also keeps one audio context and clip registry.
export function moduleUrl(fileName, baseUrl = import.meta.url) {
    const base = new URL(baseUrl), url = new URL(fileName, base);
    const version = base.searchParams.get("v");
    if (version) url.searchParams.set("v", version);
    return url.href;
}

const [{ createWebGl2Renderer }, { unlock: unlockAudio, setErrorHandler: setAudioErrorHandler },
    { registerServiceWorker: registerSw }, { detectWebGpu: detectGpu }] = await Promise.all([
    import(moduleUrl("./webgl2-renderer.js")), import(moduleUrl("./web-audio.js")),
    import(moduleUrl("./web-platform.js")), import(moduleUrl("./web-gpu.js"))
]);
const offline=await import(moduleUrl('./web-offline.js'));

let runtime = null;
const overlayImages = new Map();

function reportError(message, fatal = true) {
    if (!runtime?.dotNetReference || isBenignBrowserError(message)) return;
    runtime.dotNetReference.invokeMethodAsync(fatal ? "OnBrowserError" : "OnBrowserWarning", message).catch(() => {});
}

function invoke(method, ...args) {
    if (!runtime?.dotNetReference) return;
    runtime.dotNetReference.invokeMethodAsync(method, ...args)
        .catch(error => reportError(`Web runtime callback ${method} failed: ${error?.message || String(error)}`, false));
}

function updateCanvasMetrics() {
    if (!runtime?.canvas || !runtime.dotNetReference) return;
    const rect = runtime.canvas.getBoundingClientRect();
    if(rect.width<=0 || rect.height<=0)return;
    const devicePixelRatio = Math.max(window.devicePixelRatio || 1, 1);
    const width = Math.max(Math.round(rect.width * devicePixelRatio), 1);
    const height = Math.max(Math.round(rect.height * devicePixelRatio), 1);
    const changed = runtime.canvas.width !== width
        || runtime.canvas.height !== height
        || runtime.devicePixelRatio !== devicePixelRatio;
    if (!changed && runtime.hasMeasuredCanvas) return;
    runtime.hasMeasuredCanvas = true;
    runtime.devicePixelRatio = devicePixelRatio;
    if (runtime.canvas.width !== width) runtime.canvas.width = width;
    if (runtime.canvas.height !== height) runtime.canvas.height = height;
    runtime.renderer?.resize(width, height);
    if (runtime.overlayCanvas) {
        runtime.overlayCanvas.width = width;
        runtime.overlayCanvas.height = height;
    }
    invoke("OnCanvasResized", width, height, devicePixelRatio);
    invoke("OnWindowStateChanged", Math.round(rect.width), Math.round(rect.height), globalThis.document?.fullscreenElement === runtime.canvas.parentElement);
}

function scheduleCanvasMetrics() {
    if (!runtime || runtime.resizeFrameHandle) return;
    runtime.resizeFrameHandle = window.requestAnimationFrame(() => {
        if (!runtime) return;
        runtime.resizeFrameHandle = 0;
        updateCanvasMetrics();
    });
}

function isBenignBrowserError(message) {
    const normalized = String(message || "").toLowerCase();
    return normalized.includes("resizeobserver loop completed")
        || normalized.includes("resizeobserver loop limit exceeded");
}

function queuePointerInput(lookX, lookY, panX, panY, zoom, pointerX, pointerY, buttons) {
    if (!runtime || runtime.inputCaptured) return;
    const input = runtime.pendingPointerInput ??= { lookX: 0, lookY: 0, panX: 0, panY: 0, zoom: 0, pointerX: 0, pointerY: 0, buttons: 0 };
    input.lookX += lookX;
    input.lookY += lookY;
    input.panX += panX;
    input.panY += panY;
    input.zoom += zoom;
    input.pointerX = pointerX;
    input.pointerY = pointerY;
    input.buttons = buttons;
}

function resetInput() {
    if (!runtime) return;
    const input = runtime.pendingPointerInput;
    runtime.pendingPointerInput = { lookX: 0, lookY: 0, panX: 0, panY: 0, zoom: 0, pointerX: input?.pointerX || 0, pointerY: input?.pointerY || 0, buttons: 0 };
    runtime.lastPointerButtons = 0;
    runtime.pointers.clear();
    for(const touch of runtime.touches.values())runtime.touchTransitions.push({...touch,phase:4,deltaX:0,deltaY:0});
    runtime.touches.clear();
    for (const key of runtime.pressedKeys) invoke("OnKeyInput", key, false);
    runtime.pressedKeys.clear();
}

function installInputHandlers() {
    const canvas = runtime.canvas;
    const canvasPoint = event => {
        const rect = canvas.getBoundingClientRect();
        return [
            (event.clientX - rect.left) * (canvas.width / Math.max(rect.width, 1)),
            (event.clientY - rect.top) * (canvas.height / Math.max(rect.height, 1))
        ];
    };
    const updateTouch=(event,phase)=>{
        if(event.pointerType!=='touch' && event.pointerType!=='pen')return;
        runtime.touchAvailable=true;
        const [x,y]=canvasPoint(event),old=runtime.touches.get(event.pointerId);
        const touch={id:event.pointerId,x,y,deltaX:old?x-old.x:0,deltaY:old?y-old.y:0,phase,kind:event.pointerType==='pen'?2:1,pressure:event.pressure??0};
        runtime.touchTransitions.push(touch);
        if(phase>=3)runtime.touches.delete(touch.id);else runtime.touches.set(touch.id,touch);
    };
    const overlayHit = event => {
        const [x, y] = canvasPoint(event);
        return (runtime.overlayState?.controls || []).some(control =>
            control.visible !== false
            && x >= (control.x || 0)
            && x < (control.x || 0) + (control.width || 120)
            && y >= (control.y || 0)
            && y < (control.y || 0) + (control.height || 32));
    };
    const spriteHit = event => {
        const [x, y] = canvasPoint(event);
        return (runtime.overlayState?.sprites || []).some(sprite => {
            if(sprite.visible===false || sprite.interactive===false)return false;
            const w=sprite.width||128,h=sprite.height||128,angle=-(sprite.rotation||0)*Math.PI/180;
            const dx=x-(sprite.x||0)-w*.5,dy=y-(sprite.y||0)-h*.5;
            return Math.abs(dx*Math.cos(angle)-dy*Math.sin(angle))<=w*.5 && Math.abs(dx*Math.sin(angle)+dy*Math.cos(angle))<=h*.5;
        });
    };
    runtime.onPointerDown = event => {
        updateTouch(event,0);
        unlockAudio().catch(() => {});
        offline.unlockAudio().catch(() => {});
        canvas.setPointerCapture?.(event.pointerId);
        runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        runtime.lastPointerButtons = event.buttons || 1;
        runtime.guiPointerActive = overlayHit(event);
        runtime.spritePointerActive = !runtime.guiPointerActive && spriteHit(event);
        const [downX, downY] = canvasPoint(event);
        if(event.button===2){runtime.rightDown=[downX,downY];runtime.rightDragged=false;}
        if(runtime.guiPointerActive)invoke("OnGuiPointer",downX,downY,"down",event.button??0);
        if(runtime.spritePointerActive)invoke("OnSpritePointer",downX,downY,"down",event.button??0);
        if (!runtime.guiPointerActive && !runtime.spritePointerActive) queuePointerInput(0, 0, 0, 0, 0, downX, downY, event.buttons || 1);
    };
    runtime.onPointerMove = event => {
        const oldTouches=[...runtime.touches.values()];
        if(runtime.touches.has(event.pointerId))updateTouch(event,1);
        const active = runtime.pointers.has(event.pointerId);
        const previous = runtime.pointers.get(event.pointerId) || { x: event.clientX, y: event.clientY };
        const locked=globalThis.document?.pointerLockElement===canvas;
        const dx = locked?(event.movementX||0):event.clientX - previous.x;
        const dy = locked?(event.movementY||0):event.clientY - previous.y;
        if (active || event.buttons) runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        const buttons = event.pointerType === "touch" && active
            ? (event.buttons || 1)
            : (event.buttons || 0);
        runtime.lastPointerButtons = buttons;
        const look = locked || buttons & 1 ? 1 : 0;
        const pan = !locked&&(buttons & 2 || event.pointerType === "touch") ? 1 : 0;
        const [moveX, moveY] = canvasPoint(event);
        if((buttons&2) && runtime.rightDown && Math.hypot(moveX-runtime.rightDown[0],moveY-runtime.rightDown[1])>12*(runtime.devicePixelRatio||1))runtime.rightDragged=true;
        if (!runtime.guiPointerActive && !runtime.spritePointerActive) {
            if(event.pointerType==='touch' && runtime.touches.size>=2 && oldTouches.length>=2){
                const next=[...runtime.touches.values()],ratio=runtime.devicePixelRatio||1;
                const distance=p=>Math.hypot(p[0].x-p[1].x,p[0].y-p[1].y);
                queuePointerInput(0,0,((next[0].x+next[1].x)-(oldTouches[0].x+oldTouches[1].x))/2/ratio,
                    ((next[0].y+next[1].y)-(oldTouches[0].y+oldTouches[1].y))/2/ratio,-(distance(next)-distance(oldTouches))/100/ratio,moveX,moveY,buttons);
            }else queuePointerInput(dx*look,dy*look,event.pointerType==='touch'?0:dx*pan,event.pointerType==='touch'?0:dy*pan,0,moveX,moveY,buttons);
        }
    };
    runtime.onPointerUp = event => {
        updateTouch(event,event.type==='pointercancel'?4:3);
        const [x, y] = canvasPoint(event);
        if (runtime.guiPointerActive) invoke("OnGuiPointer", x, y, event.type === "pointercancel" ? "cancel" : "up", event.button ?? 0);
        if (runtime.spritePointerActive) invoke("OnSpritePointer", x, y, event.type === "pointercancel" ? "cancel" : "up", event.button ?? 0);
        const buttons = event.type === "pointercancel" || event.pointerType === "touch" ? 0 : (event.buttons || 0);
        if (buttons) runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        else { runtime.pointers.delete(event.pointerId); canvas.releasePointerCapture?.(event.pointerId); }
        runtime.lastPointerButtons = buttons;
        if (!runtime.guiPointerActive && !runtime.spritePointerActive) queuePointerInput(0, 0, 0, 0, 0, x, y, buttons);
        runtime.guiPointerActive = false;
        runtime.spritePointerActive = false;
    };
    runtime.onWheel = event => {
        event.preventDefault();
        const [wheelX, wheelY] = canvasPoint(event);
        queuePointerInput(0, 0, 0, 0, event.deltaY * 0.01, wheelX, wheelY, runtime.lastPointerButtons);
        if(runtime.pendingPointerInput)runtime.pendingPointerInput.scrollX=(runtime.pendingPointerInput.scrollX||0)+(event.deltaX||0)*.01;
    };
    runtime.onContextMenu = event => {event.preventDefault();if(!runtime.rightDragged){const [x,y]=canvasPoint(event);invoke("OnContextMenuRequest",x,y);}runtime.rightDragged=false;};
    runtime.onKeyDown = event => {
        unlockAudio().catch(() => {});
        offline.unlockAudio().catch(() => {});
        const key = event.code || event.key || "";
        if (!runtime.inputCaptured && !runtime.pressedKeys.has(key)) {
            runtime.pressedKeys.add(key);
            invoke("OnKeyInput", key, true);
        }
    };
    runtime.onKeyUp = event => {
        const key = event.code || event.key || "";
        if (runtime.pressedKeys.delete(key)) invoke("OnKeyInput", key, false);
    };
    runtime.onBlur = resetInput;
    canvas.addEventListener("pointerdown", runtime.onPointerDown, { passive: true });
    canvas.addEventListener("pointermove", runtime.onPointerMove, { passive: true });
    canvas.addEventListener("pointerup", runtime.onPointerUp, { passive: true });
    canvas.addEventListener("pointercancel", runtime.onPointerUp, { passive: true });
    canvas.addEventListener("wheel", runtime.onWheel, { passive: false });
    canvas.addEventListener("contextmenu", runtime.onContextMenu);
    window.addEventListener("keydown", runtime.onKeyDown, { passive: true });
    window.addEventListener("keyup", runtime.onKeyUp, { passive: true });
    window.addEventListener("blur", runtime.onBlur);
}

function removeInputHandlers() {
    if (!runtime) return;
    const canvas = runtime.canvas;
    canvas.removeEventListener("pointerdown", runtime.onPointerDown);
    canvas.removeEventListener("pointermove", runtime.onPointerMove);
    canvas.removeEventListener("pointerup", runtime.onPointerUp);
    canvas.removeEventListener("pointercancel", runtime.onPointerUp);
    canvas.removeEventListener("wheel", runtime.onWheel);
    canvas.removeEventListener("contextmenu", runtime.onContextMenu);
    window.removeEventListener("keydown", runtime.onKeyDown);
    window.removeEventListener("keyup", runtime.onKeyUp);
    window.removeEventListener("blur", runtime.onBlur);
}

function frame(timestamp) {
    if (!runtime?.running) return;
    const current = runtime;
    let input = current.pendingPointerInput;
    if(current.touches.size || current.touchTransitions.length || current.touchAvailable || navigator.maxTouchPoints>0) {
        input??={lookX:0,lookY:0,panX:0,panY:0,zoom:0,pointerX:0,pointerY:0,buttons:0};
        input.touches=[...current.touches.values()];input.touchTransitions=current.touchTransitions.splice(0);input.touchAvailable=true;
        for(const [id,touch] of current.touches)current.touches.set(id,{...touch,phase:2,deltaX:0,deltaY:0});
    }
    current.pendingPointerInput = null;
    const gamepad=globalThis.navigator?.getGamepads?.()?.find(p=>p?.connected);
    if(gamepad||current.hadGamepad){input??={lookX:0,lookY:0,panX:0,panY:0,zoom:0,pointerX:0,pointerY:0,buttons:0};input.gamepad=gamepad?{connected:true,name:gamepad.id,index:gamepad.index,axes:Array.from(gamepad.axes),buttons:gamepad.buttons.map(b=>b.value)}:{connected:false};}
    current.hadGamepad=!!gamepad;
    // Sample the accumulated gesture once per frame, in the same call that
    // advances the scene. High-rate mouse events never queue .NET callbacks.
    current.dotNetReference.invokeMethodAsync("OnAnimationFrameWithInput", timestamp, input)
        .then(() => {
            if (runtime !== current || !current.running) return;
            try {
                const quality=runtime.renderer?.render(timestamp);
                if(quality) current.dotNetReference.invokeMethodAsync("OnGraphicsQualityApplied",quality).catch(()=>{});
            }
            catch (error) {
                const message = error?.message || String(error);
                runtime.renderer?.dispose();
                runtime.renderer = null;
                reportError(`WebGL2 render failed: ${message}`);
            }
            renderOverlay();
            if (runtime?.running) runtime.frameHandle = window.requestAnimationFrame(frame);
        })
        .catch(error => {
            if (runtime !== current) return;
            current.running = false;
            reportError(`Web runtime frame failed: ${error?.message || String(error)}`);
        });
}

export function start(canvas, overlayCanvas, dotNetReference) {
    if (!dotNetReference) { dotNetReference = overlayCanvas; overlayCanvas = null; }
    stop();
    runtime = {
        canvas, overlayCanvas, dotNetReference, running: true, renderer: null, frameHandle: 0,
        resizeObserver: null, resizeTarget: null, resizeFrameHandle: 0,
        onWindowResize: null, onError: null, onUnhandledRejection: null,
        pointers: new Map(), touches:new Map(), touchTransitions:[], pressedKeys: new Set(), lastPointerButtons: 0, pendingPointerInput: null, guiPointerActive: false, spritePointerActive: false, inputCaptured: false, overlayState: {}
    };
    setAudioErrorHandler(message => dotNetReference.invokeMethodAsync("OnBrowserWarning", message));
    updateCanvasMetrics();
    installInputHandlers();
    if (typeof ResizeObserver !== "undefined") {
        runtime.resizeObserver = new ResizeObserver(scheduleCanvasMetrics);
        // Observe the layout container instead of the canvas itself. Changing
        // canvas.width/height updates its backing buffer and can otherwise
        // feed the observer with a self-induced size change.
        runtime.resizeTarget = canvas.parentElement || canvas;
        runtime.resizeObserver.observe(runtime.resizeTarget);
    }
    runtime.onWindowResize = scheduleCanvasMetrics;
    window.addEventListener("resize", runtime.onWindowResize, { passive: true });
    globalThis.document?.addEventListener('fullscreenchange', reportWindowState);
    runtime.onPointerLockChange=()=>{const locked=document.pointerLockElement===runtime?.canvas;if(locked){runtime.cursorLockButton?.remove();runtime.cursorLockButton=null;}invoke('OnCursorLockStateChanged',locked,locked&&!!runtime.rawPointerInput);};
    globalThis.document?.addEventListener('pointerlockchange',runtime.onPointerLockChange);
    runtime.onClipboardPaste=event=>invoke('OnClipboardChanged',event.clipboardData?.getData('text/plain')||'');
    window.addEventListener('paste',runtime.onClipboardPaste);
    runtime.onError = event => {
        const message = event?.error?.message || event?.message || "Unhandled browser exception";
        if (isBenignBrowserError(message)) return;
        reportError(message);
    };
    runtime.onUnhandledRejection = event => {
        const message = event?.reason?.message || String(event?.reason || "Unhandled Promise rejection");
        if (!isBenignBrowserError(message)) reportError(message);
    };
    window.addEventListener("error", runtime.onError);
    window.addEventListener("unhandledrejection", runtime.onUnhandledRejection);
    runtime.frameHandle = window.requestAnimationFrame(frame);
}

export function enableWebGl2() {
    if (!runtime?.canvas) throw new Error("Web runtime has not been started.");
    runtime.renderer?.dispose();
    runtime.renderer = createWebGl2Renderer(runtime.canvas, reportError);
    runtime.renderer.resize(runtime.canvas.width, runtime.canvas.height);
    return runtime.renderer.info;
}

export async function loadPmxMesh(mesh) {
    if (!runtime?.renderer) throw new Error("WebGL2 renderer has not been initialized.");
    await runtime.renderer.uploadMesh(mesh);
    return { vertexCount: mesh.positions.length / 3, indexCount: mesh.indices.length, modelName: mesh.modelName || "PMX mesh" };
}

export async function loadPmxMeshes(meshes) {
    if (!runtime?.renderer) throw new Error("WebGL2 renderer has not been initialized.");
    await runtime.renderer.uploadMeshes(meshes || []);
    return (meshes || []).length;
}

export function updatePmxMesh(mesh) {
    runtime?.renderer?.updateMesh(mesh);
}

export function updatePmxMeshes(meshes) {
    runtime?.renderer?.updateMeshes(meshes || []);
}

export async function upsertMeshes(meshes) { await runtime?.renderer?.upsertMeshes(meshes); }
export function removeMeshes(ids) { runtime?.renderer?.removeMeshes(ids); }

export function setFrameState(frameState) {
    runtime?.renderer?.setFrameState(frameState);
}
export function getRenderDiagnostics() { return runtime?.renderer?.getDiagnostics(); }
export function registerCustomShader(id,vertex,fragment){return runtime?.renderer?.registerCustomShader(id,vertex,fragment)??false;}
export function getOverlayDiagnostics(){return {controls:(runtime?.overlayState.controls||[]).map(c=>({id:c.id,name:c.name,type:c.type,x:c.x,y:c.y,width:c.width,height:c.height})),
    bubbles:(runtime?.overlayState.bubbles||[]).map(b=>({text:b.text,header:b.header,visible:b.visible}))};}
export function configureOffline(project,assetUris,identity){return offline.configure(project,assetUris,identity);}
export async function resolveLlmEndpoint(baseUrl,chatCompletionsPath){
    try{
        const response=await fetch('./__web_api/capabilities',{cache:'no-store'});if(!response.ok)return null;
        const value=await response.json();
        return value.baseUrl===baseUrl&&value.chatCompletionsPath===chatCompletionsPath?new URL(value.endpoint,location.href).href:null;
    }catch{return null;}
}
export const preloadModels=offline.preload;
export function nextPaint(){return new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));}
export function presentScene(){runtime?.renderer?.render(performance.now());renderOverlay();}
export function clearOfflineAssetUrls(){offline.clearAssetUrls();}
export function createOfflineAssetUrl(bytes){return offline.createAssetUrl(bytes);}
export const disposeOffline=offline.dispose;
export async function cacheOfflineGame(){
    if(!navigator.serviceWorker)throw new Error('当前浏览器不支持离线缓存。');
    if(!await registerSw('./sw.js'))throw new Error('离线缓存初始化失败。请使用 HTTPS 或 localhost 打开播放器。');
    let timeout;const registration=await Promise.race([navigator.serviceWorker.ready,new Promise((_,reject)=>{timeout=setTimeout(()=>reject(new Error('离线缓存初始化超时。')),30000);})]).finally(()=>clearTimeout(timeout));
    const worker=navigator.serviceWorker.controller||registration.active;
    return new Promise((resolve,reject)=>{const channel=new MessageChannel();channel.port1.onmessage=event=>{
        if(event.data.done){channel.port1.close();resolve(event.data.count);}
        if(event.data.error){channel.port1.close();reject(new Error(event.data.error));}
    };worker.postMessage({type:'cache-offline-game'},[channel.port2]);});
}
export async function decryptGamePackage(bytes,password){return await(await import(moduleUrl('./web-package-crypto.js'))).decryptGamePackage(bytes,password);}

export function configureGame(width, height, desktopSpriteMode = false) {
    const frame=runtime?.canvas?.parentElement;
    if(frame)frame.style.aspectRatio=`${Math.max(1,width)} / ${Math.max(1,height)}`;
    document.documentElement.classList.toggle('game-embedded', new URLSearchParams(location.search).get('embed') === '1');
    document.documentElement.classList.toggle('game-transparent', !!desktopSpriteMode);
    reportWindowState();
}

export function setGameSize(width, height) {
    const frame=runtime?.canvas?.parentElement;
    if(!frame)return;
    frame.style.width=`${Math.max(320,width)}px`;
    frame.style.height=`${Math.max(240,height)}px`;
    frame.style.aspectRatio='auto';
    scheduleCanvasMetrics();
}

export function setGameTitle(title){document.title=title||'Zhengyan Web GamePlayer';}
export function setGameVisible(visible){const frame=runtime?.canvas?.parentElement;if(frame){if(!visible)setGameFullscreen(false);frame.hidden=!visible;scheduleCanvasMetrics();}}
export function setGameResizable(resizable){const frame=runtime?.canvas?.parentElement;if(frame)frame.style.resize=resizable?'both':'none';}

export function trySetClipboardText(text){
    if(typeof document==='undefined' || !document.execCommand)return false;
    const previous=document.activeElement,field=document.createElement('textarea');field.value=text||'';
    Object.assign(field.style,{position:'fixed',opacity:'0',pointerEvents:'none'});document.body.append(field);field.select();
    let copied=false;try{copied=document.execCommand('copy');}catch{}finally{field.remove();previous?.focus?.();}
    if(copied)invoke('OnClipboardChanged',text||'');return copied;
}
export async function readClipboardText(){if(!navigator.clipboard?.readText)throw new Error('Clipboard reading requires a supported secure browser context.');const text=await navigator.clipboard.readText();invoke('OnClipboardChanged',text);return text;}
export function trySetCursorLocked(locked,rawInput=false){
    const current=runtime,canvas=current?.canvas;if(!canvas)return false;
    if(!locked){current.cursorLockButton?.remove();current.cursorLockButton=null;if(document.pointerLockElement===canvas)document.exitPointerLock();return true;}
    if(document.pointerLockElement===canvas)return true;
    if(!canvas.requestPointerLock){reportError('此浏览器不支持鼠标捕获。',false);return false;}
    const request=()=>{
        current.rawPointerInput=rawInput;
        const result=canvas.requestPointerLock(rawInput?{unadjustedMovement:true}:undefined);
        if(!result?.then)current.rawPointerInput=false;
        Promise.resolve(result).catch(error=>{
            if(rawInput&&error.name==='NotSupportedError'){current.rawPointerInput=false;return canvas.requestPointerLock();}
            throw error;
        }).catch(error=>reportError('鼠标捕获失败：'+error.message,false));
    };
    if(navigator.userActivation?.isActive)request();
    else if(!current.cursorLockButton){const button=document.createElement('button');button.className='game-pointer-lock-prompt';button.textContent='点击捕获鼠标（Esc 释放）';button.addEventListener('click',request);canvas.parentElement.append(button);current.cursorLockButton=button;}
    return true;
}

function reportWindowState() {
    scheduleCanvasMetrics();
    const frame=runtime?.canvas?.parentElement;
    if(frame){const rect=frame.getBoundingClientRect();invoke('OnWindowStateChanged',Math.round(rect.width),Math.round(rect.height),document.fullscreenElement===frame);}
}

export function setGameFullscreen(fullscreen) {
    const current=runtime,frame=current?.canvas?.parentElement;
    if(!frame)return;
    current.fullscreenButton?.remove();current.fullscreenButton=null;
    if(!fullscreen){if(document.fullscreenElement===frame)document.exitFullscreen().catch(e=>reportError('退出全屏失败：'+e.message,false));return;}
    if(document.fullscreenElement===frame)return;
    const request=()=>frame.requestFullscreen().then(()=>{current.fullscreenButton?.remove();current.fullscreenButton=null;reportWindowState();})
        .catch(e=>reportError('无法进入全屏：'+e.message,false));
    if(!frame.requestFullscreen || document.fullscreenEnabled===false){reportError('当前浏览器或嵌入页面未允许全屏。',false);return;}
    if(navigator.userActivation?.isActive){void request();return;}
    const button=document.createElement('button');button.className='game-fullscreen-prompt';button.textContent='点击进入全屏';
    button.addEventListener('click',request);frame.append(button);current.fullscreenButton=button;
}

export function setCursorVisible(visible) {
    if(runtime?.canvas?.style)runtime.canvas.style.cursor=visible?"default":"none";
}

export async function loadSceneTextures(uris) {
    await runtime?.renderer?.loadSceneTextures(uris);
}

export function setInputCapture(captured) {
    if (!runtime) return;
    runtime.inputCaptured = !!captured;
    if (captured) resetInput();
}

export function setOverlayState(state) {
    if (!runtime) return;
    runtime.overlayState = state || {};
    runtime.renderer?.setSprites(runtime.overlayState.sprites || []);
    updateGuiEditors();
    for (const sprite of runtime.overlayState.sprites || []) {
        if (!sprite.uri || sprite.uri.startsWith('rt:') || overlayImages.has(sprite.uri)) continue;
        const image = new Image();
        image.onload = () => { overlayImages.set(sprite.uri, image); };
        image.onerror = () => { overlayImages.set(sprite.uri, null); };
        image.src = sprite.uri;
    }
}

export function showContextMenu(menu) {
    if(!runtime || typeof document==='undefined')return;
    closeContextMenu();if(!menu)return;
    const parent=runtime.canvas.parentElement;if(!parent)return;
    const ratio=runtime.devicePixelRatio||1,element=document.createElement('div');runtime.contextMenu=element;
    Object.assign(element.style,{position:'absolute',left:`${menu.x/ratio}px`,top:`${menu.y/ratio}px`,width:`${menu.width/ratio}px`,
        background:menu.background,color:menu.color,border:`1px solid ${menu.border}`,borderRadius:`${menu.rounding/ratio}px`,padding:`${menu.paddingY/ratio}px ${menu.paddingX/ratio}px`,zIndex:'10'});
    element.setAttribute('role','menu');
    for(const item of menu.items){const button=document.createElement('button');button.textContent=item.text;button.disabled=!item.enabled;button.setAttribute('role','menuitem');
        Object.assign(button.style,{display:'block',width:'100%',height:`${menu.itemHeight/ratio}px`,textAlign:'left',color:menu.color,background:'transparent',border:'0',font:`${menu.fontSize/ratio}px sans-serif`,cursor:item.enabled?'pointer':'default'});
        button.addEventListener('pointerenter',()=>{if(item.enabled)button.style.background=menu.hover;});button.addEventListener('pointerleave',()=>button.style.background='transparent');
        button.addEventListener('click',()=>{if(item.enabled){closeContextMenu();invoke('OnContextMenuItem',menu.id,item.id);}});element.append(button);}
    parent.append(element);setInputCapture(true);
    runtime.menuDismiss=event=>{if(!element.contains(event.target))closeContextMenu();};
    runtime.menuEscape=event=>{if(event.key==='Escape')closeContextMenu();};
    window.addEventListener('pointerdown',runtime.menuDismiss,true);window.addEventListener('keydown',runtime.menuEscape,true);
}
export function closeContextMenu(){
    if(!runtime)return;runtime.contextMenu?.remove();runtime.contextMenu=null;
    if(runtime.menuDismiss)window.removeEventListener('pointerdown',runtime.menuDismiss,true);
    if(runtime.menuEscape)window.removeEventListener('keydown',runtime.menuEscape,true);
    runtime.menuDismiss=runtime.menuEscape=null;setInputCapture(false);
}

function updateGuiEditors() {
    const parent=runtime?.canvas?.parentElement;
    if(!parent || typeof document==="undefined")return;
    runtime.editors??=new Map();
    const controls=(runtime.overlayState.controls||[]).filter(c=>["textbox","input","input_text","textarea"].includes(c.type) && c.visible!==false);
    const active=new Set(controls.map(c=>c.id));
    for(const [id,editor] of runtime.editors)if(!active.has(id)){editor.remove();runtime.editors.delete(id);}
    const ratio=runtime.devicePixelRatio||1;
    for(const control of controls) {
        const multiline=control.multiline===true||control.type==="textarea";
        let editor=runtime.editors.get(control.id);
        if(editor&&editor.tagName!==(multiline?"TEXTAREA":"INPUT")){editor.remove();runtime.editors.delete(control.id);editor=null;}
        if(!editor){editor=document.createElement(multiline?"textarea":"input");editor.setAttribute("aria-label",control.name||control.id);
            const selection=()=>invoke('OnGuiSelectionChanged',control.id,editor.selectionStart||0,editor.selectionEnd||0,editor.compositionStart??-1,editor.compositionStart>=0?editor.selectionEnd:-1);
            editor.addEventListener("input",()=>{invoke("OnGuiValueChanged",control.id,editor.value);selection();});
            editor.addEventListener('select',selection);
            editor.addEventListener('compositionstart',()=>{editor.compositionStart=editor.selectionStart;selection();});
            editor.addEventListener('compositionend',()=>{editor.compositionStart=-1;selection();});
            editor.addEventListener("focus",()=>setInputCapture(true));editor.addEventListener("blur",()=>setInputCapture(false));
            parent.append(editor);runtime.editors.set(control.id,editor);}
        const remoteText=control.text||'';
        if(editor.remoteText!==remoteText){if(editor.value!==remoteText)editor.value=remoteText;editor.remoteText=remoteText;}
        const range=[control.selectionStart||0,control.selectionEnd||0];
        if(editor.remoteRange?.[0]!==range[0]||editor.remoteRange?.[1]!==range[1]){editor.setSelectionRange(...range);editor.remoteRange=range;}
        editor.wrap=control.wordWrap===false?'off':'soft';
        Object.assign(editor.style,{position:"absolute",left:`${control.x/ratio}px`,top:`${control.y/ratio}px`,width:`${control.width/ratio}px`,height:`${control.height/ratio}px`,
            background:control.background,color:control.color,border:`${(control.borderThickness||1)/ratio}px solid ${control.border}`,
            borderRadius:`${(control.rounding||0)/ratio}px`,font:`${control.fontSize/ratio}px sans-serif`,padding:`${4/ratio}px`,resize:"none",boxSizing:"border-box",zIndex:"2"});
    }
}

export async function registerServiceWorker(path = "sw.js") {
    return await registerSw(path);
}

export async function detectWebGpu() {
    return await detectGpu();
}

function renderOverlay() {
    if (!runtime?.overlayCanvas) return;
    const canvas = runtime.overlayCanvas;
    const context = canvas.getContext("2d");
    if (!context) return;
    context.clearRect(0, 0, canvas.width, canvas.height);
    const state = runtime.overlayState || {};
    for (const sprite of (state.sprites || []).slice().sort((a, b) => (a.drawOrder || 0) - (b.drawOrder || 0))) {
        if (runtime.renderer || sprite.visible === false || sprite.drawOrder < 0 || sprite.uri?.startsWith('rt:')) continue;
        context.save();
        context.globalAlpha = sprite.opacity ?? 1;
        const x = sprite.x || 0;
        const y = sprite.y || 0;
        const width = sprite.width || 16;
        const height = sprite.height || 16;
        context.translate(x + width * 0.5, y + height * 0.5);
        context.rotate((sprite.rotation || 0) * Math.PI / 180);
        const image = sprite.uri ? overlayImages.get(sprite.uri) : null;
        if (image) {
            const sourceWidth = sprite.sourceWidth > 0 ? sprite.sourceWidth : image.naturalWidth;
            const sourceHeight = sprite.sourceHeight > 0 ? sprite.sourceHeight : image.naturalHeight;
            context.drawImage(image, sprite.sourceX || 0, sprite.sourceY || 0, sourceWidth, sourceHeight, -width * 0.5, -height * 0.5, width, height);
        } else {
            context.fillStyle = sprite.color || "rgba(255,255,255,.75)";
            context.fillRect(-width * 0.5, -height * 0.5, width, height);
        }
        context.restore();
    }
    context.globalAlpha = 1;
    for (const control of state.controls || []) {
        if (control.visible === false) continue;
        if(["textbox","input","input_text","textarea"].includes(control.type) && runtime.editors?.has(control.id))continue;
        context.fillStyle = control.background || "rgba(15,23,42,.78)";
        context.strokeStyle = control.border || "rgba(148,163,184,.8)";
        context.lineWidth=control.borderThickness??1;
        context.beginPath();context.roundRect(control.x||0,control.y||0,control.width||120,control.height||32,control.rounding||0);
        context.fill();context.stroke();
        context.fillStyle = control.color || "#f8fafc";
        context.font = `${control.fontSize || 14}px sans-serif`;
        const text = control.type === "checkbox"
            ? `${control.checkedValue ? "[x]" : "[ ]"} ${control.text || ""}`
            : control.type === "dropdown" && (control.items || []).length > 0
                ? (control.items[control.selectedIndex || 0] || control.text || "")
                : control.type === "progress_bar"
                    ? (control.text || `${Math.round((control.progress || 0) * 100)}%`)
                    : (control.text || "");
        context.textAlign=control.alignment||"center";
        context.textBaseline=control.verticalAlignment==="top"?"top":control.verticalAlignment==="bottom"?"bottom":"middle";
        const x=(control.x||0)+(context.textAlign==="center"?(control.width||120)*.5:context.textAlign==="right"?(control.width||120)-8:8);
        const y=(control.y||0)+(context.textBaseline==="top"?4:context.textBaseline==="bottom"?(control.height||32)-4:(control.height||32)*.5);
        context.fillText(text,x,y,Math.max(1,(control.width||120)-16));
    }
    for (const bubble of state.bubbles || []) {
        if(bubble.visible===false)continue;
        const padding=bubble.padding||[12,10],lines=[],width=bubble.width||240,contentWidth=Math.max(1,width-padding[0]*2);
        let contentHeight=0;
        const blocks=[[bubble.header,bubble.headerFontSize||bubble.fontSize,bubble.headerColor||bubble.color],[bubble.text,bubble.fontSize,bubble.color],[bubble.footer,bubble.footerFontSize||bubble.fontSize,bubble.footerColor||bubble.color]];
        for(const [text,size,color]of blocks){
            if(!text)continue;
            if(lines.length)contentHeight+=6;
            const font=size||14;context.font=`${font}px sans-serif`;
            const add=line=>{lines.push({text:line,font,color:color||'#f8fafc',y:contentHeight});contentHeight+=font*1.3;};
            for(const paragraph of text.split('\n')){
                if(bubble.wordWrap===false){add(paragraph);continue;}
                const chars=globalThis.Intl?.Segmenter?[...new Intl.Segmenter(undefined,{granularity:'grapheme'}).segment(paragraph)].map(s=>s.segment):[...paragraph];
                let line='';for(const character of chars){if(line&&context.measureText(line+character).width>contentWidth){add(line);line='';}line+=character;}add(line);
            }
        }
        const height=contentHeight+padding[1]*2,pivot=bubble.pivot||[0,0];
        const x=(bubble.x||0)-width*pivot[0],y=(bubble.y||0)-height*pivot[1];
        context.save();context.fillStyle=bubble.background||'rgba(15,23,42,.88)';context.strokeStyle=bubble.border||'rgba(226,232,240,.85)';
        context.lineWidth=bubble.borderThickness??1;context.beginPath();context.roundRect(x,y,width,height,bubble.rounding??10);context.fill();if(context.lineWidth>0)context.stroke();
        context.beginPath();context.rect(x+padding[0],y+padding[1],contentWidth,contentHeight);context.clip();
        const alignment=['left','center','right'].includes(bubble.textAlignment)?bubble.textAlignment:'left';context.textAlign=alignment;context.textBaseline='top';
        const textX=x+padding[0]+(alignment==='center'?contentWidth*.5:alignment==='right'?contentWidth:0);
        for(const line of lines){context.font=`${line.font}px sans-serif`;context.fillStyle=line.color;context.fillText(line.text,textX,y+padding[1]+line.y);}context.restore();
    }
}

export function stop() {
    if (!runtime) return;
    closeContextMenu();
    for(const editor of runtime.editors?.values()||[])editor.remove();
    setAudioErrorHandler(null);
    runtime.running = false;
    if (runtime.frameHandle) window.cancelAnimationFrame(runtime.frameHandle);
    runtime.renderer?.dispose();
    runtime.renderer = null;
    runtime.resizeObserver?.disconnect();
    if (runtime.resizeFrameHandle) window.cancelAnimationFrame(runtime.resizeFrameHandle);

    if (runtime.onWindowResize) window.removeEventListener("resize", runtime.onWindowResize);
    globalThis.document?.removeEventListener('fullscreenchange', reportWindowState);
    globalThis.document?.removeEventListener('pointerlockchange',runtime.onPointerLockChange);
    window.removeEventListener('paste',runtime.onClipboardPaste);
    if(globalThis.document?.pointerLockElement===runtime.canvas)document.exitPointerLock();
    runtime.cursorLockButton?.remove();
    runtime.fullscreenButton?.remove();
    if (runtime.onError) window.removeEventListener("error", runtime.onError);
    if (runtime.onUnhandledRejection) window.removeEventListener("unhandledrejection", runtime.onUnhandledRejection);
    removeInputHandlers();
    runtime = null;
}

export function log(level, message) {
    const method = console[level] || console.log;
    method.call(console, `[Zhengyan Web] ${message}`);
}
