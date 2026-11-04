import { createWebGl2Renderer } from "./webgl2-renderer.js";

import { unlock as unlockAudio } from "./web-audio.js";
import { registerServiceWorker as registerSw } from "./web-platform.js";
import { detectWebGpu as detectGpu } from "./web-gpu.js";

let runtime = null;
const overlayImages = new Map();

function reportError(message) {
    if (!runtime?.dotNetReference || isBenignBrowserError(message)) return;
    runtime.dotNetReference.invokeMethodAsync("OnBrowserError", message).catch(() => {});
}

function invoke(method, ...args) {
    if (!runtime?.dotNetReference) return;
    runtime.dotNetReference.invokeMethodAsync(method, ...args).catch(() => {});
}

function updateCanvasMetrics() {
    if (!runtime?.canvas || !runtime.dotNetReference) return;
    const rect = runtime.canvas.getBoundingClientRect();
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
function installInputHandlers() {
    const canvas = runtime.canvas;
    const canvasPoint = event => {
        const rect = canvas.getBoundingClientRect();
        return [
            (event.clientX - rect.left) * (canvas.width / Math.max(rect.width, 1)),
            (event.clientY - rect.top) * (canvas.height / Math.max(rect.height, 1))
        ];
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
        return (runtime.overlayState?.sprites || []).some(sprite =>
            sprite.visible !== false
            && x >= (sprite.x || 0)
            && x < (sprite.x || 0) + (sprite.width || 128)
            && y >= (sprite.y || 0)
            && y < (sprite.y || 0) + (sprite.height || 128));
    };
    runtime.onPointerDown = event => {
        unlockAudio().catch(() => {});
        canvas.setPointerCapture?.(event.pointerId);
        runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        runtime.lastPointerButtons = event.buttons || 1;
        runtime.guiPointerActive = overlayHit(event);
        runtime.spritePointerActive = !runtime.guiPointerActive && spriteHit(event);
        const [downX, downY] = canvasPoint(event);
        if (!runtime.inputCaptured && !runtime.guiPointerActive && !runtime.spritePointerActive) invoke("OnPointerInput", 0, 0, 0, 0, 0, event.pointerType || "mouse", true, downX, downY, event.buttons || 1);
    };
    runtime.onPointerMove = event => {
        const previous = runtime.pointers.get(event.pointerId) || { x: event.clientX, y: event.clientY };
        const dx = event.clientX - previous.x;
        const dy = event.clientY - previous.y;
        runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        const buttons = event.buttons || runtime.lastPointerButtons || 0;
        const look = buttons & 1 ? 1 : 0;
        const pan = buttons & 2 || event.pointerType === "touch" ? 1 : 0;
        const [moveX, moveY] = canvasPoint(event);
        if (!runtime.inputCaptured && !runtime.guiPointerActive && !runtime.spritePointerActive) invoke("OnPointerInput", dx * look, dy * look, dx * pan, dy * pan, 0, event.pointerType || "mouse", false, moveX, moveY, event.buttons || 0);
    };
    runtime.onPointerUp = event => {
        const [x, y] = canvasPoint(event);
        if (runtime.guiPointerActive) invoke("OnGuiPointer", x, y, event.type === "pointercancel" ? "cancel" : "up", event.button ?? 0);
        if (runtime.spritePointerActive) invoke("OnSpritePointer", x, y, event.type === "pointercancel" ? "cancel" : "up", event.button ?? 0);
        runtime.pointers.delete(event.pointerId);
        canvas.releasePointerCapture?.(event.pointerId);
        runtime.lastPointerButtons = 0;
        if (!runtime.inputCaptured && runtime.guiPointerActive === false && runtime.spritePointerActive === false) invoke("OnPointerInput", 0, 0, 0, 0, 0, event.pointerType || "mouse", false, x, y, 0);
        runtime.guiPointerActive = false;
        runtime.spritePointerActive = false;
    };
    runtime.onWheel = event => {
        event.preventDefault();
        const [wheelX, wheelY] = canvasPoint(event);
        if (!runtime.inputCaptured) invoke("OnPointerInput", 0, 0, 0, 0, event.deltaY * 0.01, "wheel", false, wheelX, wheelY, 0);
    };
    runtime.onKeyDown = event => { unlockAudio().catch(() => {}); if (!runtime.inputCaptured) invoke("OnKeyInput", event.code || event.key || "", true); };
    runtime.onKeyUp = event => { if (!runtime.inputCaptured) invoke("OnKeyInput", event.code || event.key || "", false); };
    canvas.addEventListener("pointerdown", runtime.onPointerDown, { passive: true });
    canvas.addEventListener("pointermove", runtime.onPointerMove, { passive: true });
    canvas.addEventListener("pointerup", runtime.onPointerUp, { passive: true });
    canvas.addEventListener("pointercancel", runtime.onPointerUp, { passive: true });
    canvas.addEventListener("wheel", runtime.onWheel, { passive: false });
    window.addEventListener("keydown", runtime.onKeyDown, { passive: true });
    window.addEventListener("keyup", runtime.onKeyUp, { passive: true });
}

function removeInputHandlers() {
    if (!runtime) return;
    const canvas = runtime.canvas;
    canvas.removeEventListener("pointerdown", runtime.onPointerDown);
    canvas.removeEventListener("pointermove", runtime.onPointerMove);
    canvas.removeEventListener("pointerup", runtime.onPointerUp);
    canvas.removeEventListener("pointercancel", runtime.onPointerUp);
    canvas.removeEventListener("wheel", runtime.onWheel);
    window.removeEventListener("keydown", runtime.onKeyDown);
    window.removeEventListener("keyup", runtime.onKeyUp);
}

function frame(timestamp) {
    if (!runtime?.running) return;
    runtime.dotNetReference.invokeMethodAsync("OnAnimationFrame", timestamp)
        .then(() => {
            if (!runtime?.running) return;
            try { runtime.renderer?.render(timestamp); }
            catch (error) {
                const message = error?.message || String(error);
                runtime.renderer?.dispose();
                runtime.renderer = null;
                reportError(`WebGL2 render failed: ${message}`);
            }
            renderOverlay();
            if (runtime?.running) runtime.frameHandle = window.requestAnimationFrame(frame);
        })
        .catch(() => { if (runtime) runtime.running = false; });
}

export function start(canvas, overlayCanvas, dotNetReference) {
    if (!dotNetReference) { dotNetReference = overlayCanvas; overlayCanvas = null; }
    stop();
    runtime = {
        canvas, overlayCanvas, dotNetReference, running: true, renderer: null, frameHandle: 0,
        resizeObserver: null, resizeTarget: null, resizeFrameHandle: 0,
        onWindowResize: null, onError: null, onUnhandledRejection: null,
        pointers: new Map(), lastPointerButtons: 0, guiPointerActive: false, spritePointerActive: false, inputCaptured: false, overlayState: {}
    };
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

export function setFrameState(frameState) {
    runtime?.renderer?.setFrameState(frameState);
}

export function setInputCapture(captured) {
    if (runtime) runtime.inputCaptured = !!captured;
}

export function setOverlayState(state) {
    if (!runtime) return;
    runtime.overlayState = state || {};
    for (const sprite of runtime.overlayState.sprites || []) {
        if (!sprite.uri || overlayImages.has(sprite.uri)) continue;
        const image = new Image();
        image.onload = () => { overlayImages.set(sprite.uri, image); };
        image.onerror = () => { overlayImages.set(sprite.uri, null); };
        image.src = sprite.uri;
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
        if (sprite.visible === false) continue;
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
        context.fillStyle = control.background || "rgba(15,23,42,.78)";
        context.strokeStyle = control.border || "rgba(148,163,184,.8)";
        context.fillRect(control.x || 0, control.y || 0, control.width || 120, control.height || 32);
        context.strokeRect(control.x || 0, control.y || 0, control.width || 120, control.height || 32);
        context.fillStyle = control.color || "#f8fafc";
        context.font = `${control.fontSize || 14}px sans-serif`;
        const text = control.type === "checkbox"
            ? `${control.checkedValue ? "[x]" : "[ ]"} ${control.text || ""}`
            : control.type === "dropdown" && (control.items || []).length > 0
                ? (control.items[control.selectedIndex || 0] || control.text || "")
                : control.type === "progress_bar"
                    ? (control.text || `${Math.round((control.progress || 0) * 100)}%`)
                    : (control.text || "");
        context.fillText(text, (control.x || 0) + 8, (control.y || 0) + (control.height || 32) * .65);
    }
    for (const bubble of state.bubbles || []) {
        context.fillStyle = "rgba(15,23,42,.88)";
        context.strokeStyle = "rgba(226,232,240,.85)";
        context.beginPath();
        context.roundRect(bubble.x || 0, bubble.y || 0, bubble.width || 240, bubble.height || 70, 10);
        context.fill(); context.stroke();
        context.fillStyle = "#f8fafc";
        context.font = "14px sans-serif";
        context.fillText(bubble.text || "", (bubble.x || 0) + 12, (bubble.y || 0) + 25);
    }
}

export function stop() {
    if (!runtime) return;
    runtime.running = false;
    if (runtime.frameHandle) window.cancelAnimationFrame(runtime.frameHandle);
    runtime.renderer?.dispose();
    runtime.renderer = null;
    runtime.resizeObserver?.disconnect();
    if (runtime.resizeFrameHandle) window.cancelAnimationFrame(runtime.resizeFrameHandle);

    if (runtime.onWindowResize) window.removeEventListener("resize", runtime.onWindowResize);
    if (runtime.onError) window.removeEventListener("error", runtime.onError);
    if (runtime.onUnhandledRejection) window.removeEventListener("unhandledrejection", runtime.onUnhandledRejection);
    removeInputHandlers();
    runtime = null;
}

export function log(level, message) {
    const method = console[level] || console.log;
    method.call(console, `[Zhengyan Web] ${message}`);
}
