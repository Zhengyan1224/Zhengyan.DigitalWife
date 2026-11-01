let socket = null;
let recorder = null;
let recorderChunks = [];

export function speak(text, language = "zh-CN", rate = 1, pitch = 1, volume = 1) {
    if (!window.speechSynthesis) throw new Error("SpeechSynthesis is not available.");
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text || "");
    utterance.lang = language;
    utterance.rate = Math.max(.1, Math.min(10, rate));
    utterance.pitch = Math.max(0, Math.min(2, pitch));
    utterance.volume = Math.max(0, Math.min(1, volume));
    window.speechSynthesis.speak(utterance);
}

export function stopSpeaking() {
    window.speechSynthesis?.cancel();
}

export async function recordMicrophone(maxMilliseconds = 15000) {
    if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) throw new Error("Microphone recording is not available.");
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    recorderChunks = [];
    recorder = new MediaRecorder(stream);
    const result = new Promise((resolve, reject) => {
        recorder.ondataavailable = event => { if (event.data.size > 0) recorderChunks.push(event.data); };
        recorder.onerror = event => reject(event.error || new Error("Microphone recording failed."));
        recorder.onstop = async () => {
            const blob = new Blob(recorderChunks, { type: recorder.mimeType || "audio/webm" });
            const bytes = new Uint8Array(await blob.arrayBuffer());
            stream.getTracks().forEach(track => track.stop());
            resolve(bytes);
        };
    });
    recorder.start();
    setTimeout(() => { if (recorder?.state === "recording") recorder.stop(); }, Math.max(100, maxMilliseconds));
    return result;
}

export function stopMicrophone() {
    if (recorder?.state === "recording") recorder.stop();
}

export async function fetchJson(url, method = "GET", body = null, headers = {}) {
    const response = await fetch(url, {
        method,
        headers: { "Accept": "application/json", ...(body == null ? {} : { "Content-Type": "application/json" }), ...headers },
        body: body == null ? undefined : JSON.stringify(body)
    });
    const text = await response.text();
    if (!response.ok) throw new Error(`HTTP ${response.status}: ${text.slice(0, 500)}`);
    return text ? JSON.parse(text) : null;
}

export function connectWebSocket(url) {
    socket?.close();
    socket = new WebSocket(url);
    return socket.readyState;
}

export function sendWebSocket(text) {
    if (!socket || socket.readyState !== WebSocket.OPEN) throw new Error("WebSocket is not connected.");
    socket.send(text);
}

export function closeWebSocket() {
    socket?.close();
    socket = null;
}

export async function registerServiceWorker(path = "sw.js") {
    if (!navigator.serviceWorker) return false;
    await navigator.serviceWorker.register(path, { scope: "./" });
    return true;
}
