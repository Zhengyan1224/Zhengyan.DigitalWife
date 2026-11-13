Browser runtimes are shipped locally. Models are project assets; no runtime downloads models from a public service.

* Sherpa ONNX TTS 2.0.1: https://github.com/k2-fsa/sherpa-onnx/releases/tag/v2.0.1 . Apache-2.0; see sherpa/LICENSE. The bundled runtime preload metadata is patched by scripts/prepare-sherpa-browser-runtime.py to load the game's own model files. Original model .data files are excluded.
* Sherpa ONNX streaming ASR runtime: https://www.modelscope.cn/studios/k2-fsa/web-assembly-asr-sherpa-onnx-zh-en . Apache-2.0. The matching JavaScript API and WASM come from this official k2-fsa demo. Original model .data files are excluded.
* Pyodide 314.0.7: https://pyodide.org/en/314.0.7/ . MPL-2.0; see pyodide/LICENSE. Python standard library is included. Optional packages must be shipped locally before scripts import them; no CDN package fallback is configured.
