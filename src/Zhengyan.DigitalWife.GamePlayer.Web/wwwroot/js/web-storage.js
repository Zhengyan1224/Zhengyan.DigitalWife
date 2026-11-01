const DATABASE = "zhengyan-digitalwife-web";
const VERSION = 1;
const STORE = "assets";

function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(STORE)) database.createObjectStore(STORE);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error || new Error("IndexedDB open failed."));
    });
}

function transaction(key, mode, action) {
    return openDatabase().then(database => new Promise((resolve, reject) => {
        const request = database.transaction(STORE, mode).objectStore(STORE);
        const operation = action(request, key);
        operation.onsuccess = () => { database.close(); resolve(operation.result); };
        operation.onerror = () => { database.close(); reject(operation.error || new Error("IndexedDB operation failed.")); };
    }));
}

export function putAsset(key, bytes) {
    const value = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || []);
    return transaction(key, "readwrite", (store, name) => store.put(value, name));
}

export function getAsset(key) {
    return transaction(key, "readonly", (store, name) => store.get(name));
}

export function removeAsset(key) {
    return transaction(key, "readwrite", (store, name) => store.delete(name));
}

export function clearAssets() {
    return transaction("__all__", "readwrite", store => store.clear());
}

export function putJson(key, value) {
    return transaction(`json:${key}`, "readwrite", (store, name) => store.put(JSON.stringify(value ?? null), name));
}

export function getJson(key) {
    return transaction(`json:${key}`, "readonly", (store, name) => store.get(name))
        .then(value => value == null ? null : JSON.parse(value));
}

export function removeJson(key) {
    return transaction(`json:${key}`, "readwrite", (store, name) => store.delete(name));
}
