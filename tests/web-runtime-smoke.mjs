import * as runtime from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js";
import { createWebGl2Renderer } from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js";
import * as storage from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-storage.js";
import * as audio from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-audio.js";
import * as platform from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-platform.js";
import * as gpu from "../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-gpu.js";

const requiredRuntimeExports = ["start", "enableWebGl2", "detectWebGpu", "loadPmxMesh", "loadPmxMeshes", "updatePmxMesh", "updatePmxMeshes", "setFrameState", "setInputCapture", "setOverlayState", "stop"];
for (const name of requiredRuntimeExports) {
    if (typeof runtime[name] !== "function") throw new Error(`Missing Web runtime export: ${name}`);
}
if (typeof createWebGl2Renderer !== "function") throw new Error("Missing WebGL2 renderer factory.");
for (const name of ["putAsset", "getAsset", "removeAsset", "clearAssets"]) {
    if (typeof storage[name] !== "function") throw new Error(`Missing IndexedDB export: ${name}`);
}
for (const name of ["unlock", "play", "playOneShot", "pause", "stop", "setVolume", "setLoop", "stopAll", "dispose"]) {
    if (typeof audio[name] !== "function") throw new Error(`Missing audio export: ${name}`);
}
for (const name of ["speak", "speakWithCallback", "stopSpeaking", "recordMicrophone", "stopMicrophone", "fetchJson", "connectWebSocket", "sendWebSocket", "closeWebSocket", "registerServiceWorker"]) {
    if (typeof platform[name] !== "function") throw new Error(`Missing platform export: ${name}`);
}
if (typeof gpu.detectWebGpu !== "function") throw new Error("Missing WebGPU capability export.");
console.log("Web runtime module smoke test passed.");
