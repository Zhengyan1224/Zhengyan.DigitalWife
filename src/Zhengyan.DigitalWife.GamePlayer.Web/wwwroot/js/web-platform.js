let socket = null;
let recorder = null;
let recorderChunks = [];
let speechGeneration=0,currentSpeech=null;
const offlineUrl=new URL('./web-offline.js',import.meta.url);offlineUrl.search=new URL(import.meta.url).search;
const offline=await import(offlineUrl.href);

export function speak(text, language = "zh-CN", rate = 1, pitch = 1, volume = 1) {
    speakWithCallback(text,language,rate,pitch,volume);
}

export function speakWithCallback(text, language = "zh-CN", rate = 1, pitch = 1, volume = 1, dotNetReference, entityId = "", callbackName = "",speakerId=null) {
    if(offline.isEnabled()){offline.speak(text||'',rate,volume,dotNetReference,entityId,callbackName,speakerId);return;}
    if (!window.speechSynthesis) throw new Error("SpeechSynthesis is not available.");
    stopSpeaking();const generation=++speechGeneration;
    const utterance = new SpeechSynthesisUtterance(text || "");
    utterance.lang = language;
    utterance.rate = Math.max(.1, Math.min(10, rate));
    utterance.pitch = Math.max(0, Math.min(2, pitch));
    utterance.volume = Math.max(0, Math.min(1, volume));
    const notify=(name,...args)=>dotNetReference?.invokeMethodAsync(name,...args).catch(()=>{});
    currentSpeech={reference:dotNetReference,entityId};
    utterance.onstart=()=>{if(generation===speechGeneration)notify('OnSpeechStarted',entityId,text||'',rate);};
    utterance.onboundary=event=>{if(generation===speechGeneration)notify('OnSpeechBoundary',entityId,event.charIndex||0,event.charLength||1);};
    utterance.onend=()=>{if(generation!==speechGeneration)return;currentSpeech=null;notify('OnSpeechFinished',entityId);notify('OnSpeechCompleted',entityId,callbackName);};
    utterance.onerror=event=>{if(generation!==speechGeneration)return;currentSpeech=null;notify('OnSpeechFinished',entityId);notify('OnSpeechFailed',entityId,callbackName,event.error||'Speech synthesis failed.');};
    window.speechSynthesis.speak(utterance);
}

export function stopSpeaking() {
    if(offline.isEnabled()){offline.stopSpeaking();return;}
    speechGeneration++;
    if(currentSpeech){currentSpeech.reference?.invokeMethodAsync('OnSpeechFinished',currentSpeech.entityId).catch(()=>{});currentSpeech=null;}
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

    try {
        const existing=await navigator.serviceWorker.getRegistration(path);
        if(existing?.active){existing.update().catch(()=>{});return true;}
        const registration = await navigator.serviceWorker.register(path, { scope: "./", updateViaCache: "none" });
        return true;
    } catch {
        // Offline cache is an enhancement; it must not prevent the game from starting.
        return false;
    }
}
