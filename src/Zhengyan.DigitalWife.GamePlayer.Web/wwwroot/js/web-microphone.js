let authorized = false, blocked = '', prompt = null, button = null, explanation = null, checking = null;
let preparedStream = null;
let deviceSelect = null;
let preferredDevice = '';
const waiting = new Set();

async function openMicrophone() {
    const deviceId=deviceSelect?.value??preferredDevice;
    const media=globalThis.navigator?.mediaDevices;
    if(!media?.getUserMedia)throw new Error('此浏览器或页面安全策略不支持麦克风采集');
    try { return await media.getUserMedia({audio:deviceId?{deviceId:{exact:deviceId}}:true}); }
    catch(error){
        if(deviceId || !['NotReadableError','NotFoundError'].includes(error.name))throw error;
        // Default/communications aliases can point at an unavailable endpoint.
        const devices=await media.enumerateDevices?.().catch(()=>[])||[];
        for(const device of devices.filter(d=>d.kind==='audioinput'&&!['','default','communications'].includes(d.deviceId))){
            try{return await media.getUserMedia({audio:{deviceId:{exact:device.deviceId}}});}catch{}
        }
        throw error;
    }
}

async function refreshDevices() {
    if(!deviceSelect)return;
    const select=deviceSelect,selected=select.value;
    const devices=await navigator.mediaDevices?.enumerateDevices?.().catch(()=>[])||[];
    if(select!==deviceSelect)return;
    select.replaceChildren(new Option('自动选择麦克风',''));
    for(const device of devices.filter(d=>d.kind==='audioinput'&&!['default','communications'].includes(d.deviceId)))
        select.add(new Option(device.label||`麦克风 ${select.options.length}`,device.deviceId));
    select.value=selected;select.hidden=select.options.length<2;
}

export function microphoneError(error) {
    const name = error?.name || error?.error || String(error || '');
    if (['NotAllowedError', 'SecurityError', 'not-allowed', 'service-not-allowed'].includes(name))
        return '麦克风或语音识别未获允许。请在浏览器的网站权限中允许麦克风，再点击重试。';
    if (name === 'NotFoundError') return '浏览器未找到麦克风。请连接或启用录音设备，检查系统的默认输入设备后重试。';
    if (['NotReadableError', 'AbortError'].includes(name)) return `麦克风采集启动失败（${name}）。请检查系统麦克风权限、默认输入设备、驱动和独占占用后重试。`;
    if (name === 'audio-capture') return '浏览器语音识别服务无法采集音频。请检查其麦克风权限与输入设备；本地 ASR 可使用浏览器麦克风采集。';
    if (globalThis.isSecureContext === false) return '当前页面无法使用麦克风。请使用 HTTPS，或本机的 http://127.0.0.1 / http://localhost 地址。';
    return `麦克风暂时不可用（${name}）。请检查设备和浏览器权限后重试。`;
}

function removePrompt() { prompt?.remove(); prompt = button = explanation = deviceSelect = null; }
function showPrompt() {
    if (typeof document === 'undefined' || (!waiting.size && !blocked)) return;
    if (!prompt) {
        const parent = document.querySelector('.canvas-frame') || document.body;
        if (!parent) return;
        prompt = document.createElement('div'); prompt.className = 'game-microphone-prompt';
        button = document.createElement('button'); button.type = 'button';
        explanation = document.createElement('p'); explanation.setAttribute('role', 'status');
        deviceSelect=document.createElement('select');deviceSelect.setAttribute('aria-label','麦克风输入设备');deviceSelect.hidden=true;
        button.addEventListener('click', enableMicrophone);
        prompt.append(button, deviceSelect, explanation); parent.append(prompt);refreshDevices();
    }
    button.textContent = blocked ? '重试麦克风' : '启用麦克风';
    button.disabled = !!checking;
    if(deviceSelect)deviceSelect.disabled=!!checking;
    explanation.textContent = checking ? '正在启动麦克风，请稍候…' : blocked || '启用后可使用语音识别和唤醒词。';
}

async function enableMicrophone() {
    if (checking) return;
    const attempt = checking = (async () => {
        try {
            // The offline recognizer takes ownership of this stream. Opening and
            // immediately reopening capture can fail on exclusive audio drivers.
            const stream = await openMicrophone();
            if ([...waiting].some(waiter => waiter.capture)) preparedStream = stream;
            else stream.getTracks().forEach(track => track.stop());
            preferredDevice=deviceSelect?.value??preferredDevice;
            authorized = true; blocked = ''; removePrompt();
            for (const waiter of [...waiting]) waiter.resolve();
        } catch (error) { authorized = false; blocked = microphoneError(error);refreshDevices(); }
    })();
    showPrompt();
    await attempt;
    if (checking === attempt) checking = null;
    showPrompt();
}

export function blockMicrophone(error) {
    authorized = false; blocked = microphoneError(error); showPrompt();
}

export async function waitForMicrophone(signal, capture = false) {
    if (signal?.aborted) throw new DOMException('Microphone request cancelled.', 'AbortError');
    if (typeof document === 'undefined') return;
    if (!authorized && !blocked) {
        try { authorized = (await navigator.permissions?.query({ name: 'microphone' }))?.state === 'granted'; }
        catch { /* Browsers without microphone permission queries use the button. */ }
    }
    if (signal?.aborted) throw new DOMException('Microphone request cancelled.', 'AbortError');
    if (authorized && !blocked) return;
    await new Promise((resolve, reject) => {
        const cleanup = () => { waiting.delete(waiter); signal?.removeEventListener('abort', cancel); if (!waiting.size) removePrompt(); };
        const waiter = { capture, resolve: () => { cleanup(); resolve(); } };
        const cancel = () => { cleanup(); reject(new DOMException('Microphone request cancelled.', 'AbortError')); };
        waiting.add(waiter); signal?.addEventListener('abort', cancel, { once: true }); showPrompt();
    });
}

export async function requestMicrophone(signal) {
    while (!signal?.aborted) {
        await waitForMicrophone(signal, true);
        try {
            const stream = preparedStream || await openMicrophone();
            preparedStream = null;
            if (signal?.aborted) { stream.getTracks().forEach(track => track.stop()); throw new DOMException('Microphone request cancelled.', 'AbortError'); }
            return stream;
        } catch (error) { if (signal?.aborted || typeof document==='undefined') throw error; blockMicrophone(error); }
    }
    throw new DOMException('Microphone request cancelled.', 'AbortError');
}
