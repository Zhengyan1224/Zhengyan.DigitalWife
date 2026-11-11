// src/wasm-from-cdn.ts
var WasmFromCDN = {
  default: "https://cdn.jsdelivr.net/npm/@wllama/wllama@3.13.0/esm/wasm/wllama.wasm"
};
var WasmCompatFromCDN = {
  worker: "https://cdn.jsdelivr.net/npm/@wllama/wllama-compat@3.13.0/wasm/wllama.js",
  wasm: "https://cdn.jsdelivr.net/npm/@wllama/wllama-compat@3.13.0/wasm/wllama.wasm"
};
var wasm_from_cdn_default = WasmFromCDN;
export {
  WasmCompatFromCDN,
  wasm_from_cdn_default as default
};
