import { createWebGl2Renderer } from "./webgl2-renderer.js";

import { unlock as unlockAudio } from "./web-audio.js";
import { registerServiceWorker as registerSw } from "./web-platform.js";
import { detectWebGpu as detectGpu } from "./web-gpu.js";

let runtime = null;

function reportError(message) {
    if (!runtime?.dotNetReference) return;
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
    if (runtime.canvas.width !== width) runtime.canvas.width = width;
    if (runtime.canvas.height !== height) runtime.canvas.height = height;
    runtime.renderer?.resize(width, height);
    if (runtime.overlayCanvas) {
        runtime.overlayCanvas.width = width;
        runtime.overlayCanvas.height = height;
    }
    invoke("OnCanvasResized", width, height, devicePixelRatio);
}

function installInputHandlers() {
    const canvas = runtime.canvas;
    runtime.onPointerDown = event => {
        unlockAudio().catch(() => {});
        canvas.setPointerCapture?.(event.pointerId);
        runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        runtime.lastPointerButtons = event.buttons || 1;
        if (!runtime.inputCaptured) invoke("OnPointerInput", 0, 0, 0, 0, 0, event.pointerType || "mouse", true);
    };
    runtime.onPointerMove = event => {
        const previous = runtime.pointers.get(event.pointerId) || { x: event.clientX, y: event.clientY };
        const dx = event.clientX - previous.x;
        const dy = event.clientY - previous.y;
        runtime.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
        const buttons = event.buttons || runtime.lastPointerButtons || 0;
        const look = buttons & 1 ? 1 : 0;
        const pan = buttons & 2 || event.pointerType === "touch" ? 1 : 0;
        if (!runtime.inputCaptured) invoke("OnPointerInput", dx * look, dy * look, dx * pan, dy * pan, 0, event.pointerType || "mouse", false);
    };
    runtime.onPointerUp = event => {
        runtime.pointers.delete(event.pointerId);
        canvas.releasePointerCapture?.(event.pointerId);
        runtime.lastPointerButtons = 0;
        if (!runtime.inputCaptured) invoke("OnPointerInput", 0, 0, 0, 0, 0, event.pointerType || "mouse", false);
    };
    runtime.onWheel = event => {
        event.preventDefault();
        if (!runtime.inputCaptured) invoke("OnPointerInput", 0, 0, 0, 0, event.deltaY * 0.01, "wheel", false);
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
        resizeObserver: null, onWindowResize: null, onError: null, onUnhandledRejection: null,
        pointers: new Map(), lastPointerButtons: 0, inputCaptured: false, overlayState: {}
    };
    updateCanvasMetrics();
    installInputHandlers();
    if (typeof ResizeObserver !== "undefined") {
        runtime.resizeObserver = new ResizeObserver(updateCanvasMetrics);
        runtime.resizeObserver.observe(canvas);
    }
    runtime.onWindowResize = updateCanvasMetrics;
    window.addEventListener("resize", runtime.onWindowResize, { passive: true });
    runtime.onError = event => reportError(event?.error?.message || event?.message || "Unhandled browser exception");
    runtime.onUnhandledRejection = event => reportError(event?.reason?.message || String(event?.reason || "Unhandled Promise rejection"));
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
    if (runtime) runtime.overlayState = state || {};
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
    for (const sprite of state.sprites || []) {
        if (sprite.visible === false) continue;
        context.globalAlpha = sprite.opacity ?? 1;
        context.fillStyle = sprite.color || "rgba(255,255,255,.75)";
        context.fillRect(sprite.x || 0, sprite.y || 0, sprite.width || 16, sprite.height || 16);
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
        context.fillText(control.text || "", (control.x || 0) + 8, (control.y || 0) + (control.height || 32) * .65);
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
