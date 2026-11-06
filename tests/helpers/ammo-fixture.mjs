import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

export async function ammoFactory() {
    const directory = new URL('../../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/vendor/ammo/',import.meta.url);
    const bundle = {exports:{}};
    vm.runInNewContext(await readFile(new URL('ammo.wasm.js',directory),'utf8'),{
        module:bundle,exports:bundle.exports,require:createRequire(import.meta.url),process,
        __dirname:fileURLToPath(directory),console,setTimeout,clearTimeout,WebAssembly,Buffer
    });
    const binary = await readFile(new URL('ammo.wasm.wasm',directory));
    return () => bundle.exports({wasmBinary:binary});
}
