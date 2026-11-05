let context = null;
const sounds = new Map();
const oneShots = new Set();
let errorHandler = null;
let generation = 0;

export function setErrorHandler(handler) { errorHandler = handler; }

function reportFailure(error) {
    const message = `Web audio command failed: ${error?.message || String(error)}`;
    try { Promise.resolve(errorHandler?.(message)).catch(() => {}); } catch {}
}

function ensureContext() {
    context ??= new (window.AudioContext || window.webkitAudioContext)();
    return context;
}

export async function unlock() {
    const audio = ensureContext();
    if (audio.state === "suspended") await audio.resume();
    return audio.state;
}

export function prepare(key, uri) {
    ensureContext();
    let entry = sounds.get(key);
    if (!entry) {
        entry = { audio: new Audio(), uri: null, generation: 0, pending: null };
        entry.audio.preload = "auto";
        sounds.set(key, entry);
    }
    if (entry.uri !== uri) {
        entry.generation++;
        entry.uri = uri;
        entry.audio.src = uri;
    }
    return entry;
}

export function play(key, uri, loop = false, volume = 1) {
    const entry = prepare(key, uri);
    const audio = entry.audio;
    audio.loop = !!loop;
    audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
    if (entry.pending?.generation === entry.generation) return entry.pending.promise;
    if (audio.currentTime > 0) audio.currentTime = 0;
    const requestGeneration = entry.generation;
    const promise = (async () => {
        try {
            await unlock();
            if (entry.generation !== requestGeneration) return;
            await audio.play();
        } catch (error) {
            if (entry.generation !== requestGeneration && error?.name === "AbortError") return;
            throw error;
        }
    })();
    entry.pending = { generation: requestGeneration, promise };
    const clear = () => { if (entry.pending?.promise === promise) entry.pending = null; };
    promise.then(clear, clear);
    return promise;
}

// Starting a media element may wait for buffering. The scene loop only waits
// for accepting the command; playback failures arrive through the callback.
export function queuePlay(...args) { play(...args).catch(reportFailure); }
export function queuePlayOneShot(...args) { playOneShot(...args).catch(reportFailure); }

export async function playOneShot(uri, volume = 1) {
    const requestGeneration = generation;
    await unlock();
    if (requestGeneration !== generation) return;
    const audio = new Audio(uri);
    oneShots.add(audio);
    audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
    audio.addEventListener("ended", () => { oneShots.delete(audio); audio.remove(); }, { once: true });
    try { await audio.play(); }
    catch (error) {
        oneShots.delete(audio);
        if (requestGeneration !== generation && error?.name === "AbortError") return;
        throw error;
    }
}

export function pause(key) {
    const entry = sounds.get(key);
    if (entry) { entry.generation++; entry.audio.pause(); }
}

export function stop(key) {
    const entry = sounds.get(key);
    if (!entry) return;
    entry.generation++;
    entry.audio.pause();
    entry.audio.currentTime = 0;
}

export function setVolume(key, volume) {
    const audio = sounds.get(key)?.audio;
    if (audio) audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
}

export function setLoop(key, loop) {
    const audio = sounds.get(key)?.audio;
    if (audio) audio.loop = !!loop;
}

export function stopAll() {
    generation++;
    for (const key of sounds.keys()) stop(key);
    for (const audio of oneShots) { audio.pause(); audio.remove(); }
    oneShots.clear();
}

export function dispose() {
    stopAll();
    sounds.clear();
    if (context) Promise.resolve(context.close?.()).catch(() => {});
    context = null;
    errorHandler = null;
}
