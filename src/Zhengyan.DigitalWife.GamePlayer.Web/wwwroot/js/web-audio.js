let context = null;
const sounds = new Map();

function ensureContext() {
    context ??= new (window.AudioContext || window.webkitAudioContext)();
    return context;
}

export async function unlock() {
    const audio = ensureContext();
    if (audio.state === "suspended") await audio.resume();
    return audio.state;
}

export async function play(key, uri, loop = false, volume = 1) {
    await unlock();
    const audio = sounds.get(key) || new Audio();
    audio.src = uri;
    audio.loop = !!loop;
    audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
    sounds.set(key, audio);
    await audio.play();
}

export function stop(key) {
    const audio = sounds.get(key);
    if (!audio) return;
    audio.pause();
    audio.currentTime = 0;
}

export function stopAll() {
    for (const key of sounds.keys()) stop(key);
}

export function dispose() {
    stopAll();
    sounds.clear();
    if (context) context.close?.();
    context = null;
}
