import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import test from 'node:test';
import { createWebGl2Renderer, decodeDds } from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/webgl2-renderer.js';
import { colorBlock, createFakeGl, deferred, makeDds, settle, triangle } from './helpers/web-renderer-fixtures.mjs';

const pixels = data => Array.from(data);
const bufferOf = bytes => bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);

test('GPU skinning uploads a 237-bone palette and sparse morph rows without replacing vertex buffers', async () => {
    const { canvas,state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas);
    const bytes = values => new Uint8Array(new Float32Array(values).buffer);
    const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const bones = bytes(Array.from({ length: 237 }, () => identity).flat());
    const mesh = triangle([null]);
    mesh.skinning = { boneCount: 237, bones, bindings: bytes(Array.from({ length: 3 }, () => [0,1,2,3,1,0,0,0]).flat()) };
    await renderer.uploadMesh(mesh);
    renderer.updateMesh({ vertices: new Uint8Array(), bones, morphs: bytes([1,.1,.2,.3]) });
    renderer.render();
    assert.equal(state.bufferUpdates.length,0);
    assert.equal(state.textureUpdates[0].width,4); assert.equal(state.textureUpdates[0].height,237);
    assert.equal(state.textureUpdates[1].height,1);
    assert.deepEqual(state.textureUpdates[1].data.slice(4,7), Array.from(new Float32Array([.1,.2,.3])));
    assert.equal(state.draws.at(-1).gpuSkinning,1); assert.equal(state.draws.at(-1).boneCount,237);
    renderer.dispose();
    assert.equal(new Set(state.deletedTextures).size,state.deletedTextures.length);
});

test('shadow passes honor material flags, alpha and double-sided state, and cache static caster depth', async () => {
    const { canvas, gl, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas);
    const mesh = triangle([null, null]);
    Object.assign(mesh.materials[0], { enableShadow: true, diffuse: [1, 1, 1, .6], ambient: { x: .2, y: .3, z: .4 } });
    Object.assign(mesh.materials[1], { enableShadow: false, receiveShadow: false, doubleSided: false });
    await renderer.uploadMesh(mesh);
    const identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
    renderer.setFrameState({ shadowEnabled: true, shadowStrength: .7, lightViewProjection: identity });
    renderer.render();
    assert.equal(state.shadowDraws.length, 1);
    assert.equal(state.shadowDraws[0].cullEnabled, false);
    assert.equal(state.shadowDraws[0].blendEnabled, false);
    assert.equal(state.shadowDraws[0].alpha, Math.fround(.6));
    assert.deepEqual(state.draws[0].ambient, Array.from(new Float32Array([.2, .3, .4])));
    assert.equal(state.draws[0].shadowStrength, .7);
    assert.equal(state.draws[1].receiveShadow, 1, 'The PC entity receive-shadow override did not reach a material with self-shadow disabled.');
    assert.ok(state.textureParameters.some(item => item.parameter === gl.TEXTURE_COMPARE_MODE && item.value === gl.COMPARE_REF_TO_TEXTURE));
    renderer.setFrameState({ viewProjection: identity.map((value, i) => i === 12 ? 1 : value) });
    renderer.render();
    assert.equal(state.shadowDraws.length, 1, 'A camera drag rebuilt unchanged shadow depth.');
    renderer.updateMesh({ positions: mesh.positions, normals: mesh.normals, uvs: mesh.uvs });
    renderer.render();
    assert.equal(state.shadowDraws.length, 2, 'Animated vertices did not invalidate shadow depth.');
    renderer.setFrameState({ shadowStrength: 0 });
    renderer.render();
    assert.equal(state.shadowDraws.length, 2);
    renderer.dispose();
});

test('DDS BGRA channels, alpha and row padding match the flipped PMX UVs', () => {
    const dds = makeDds({ width: 1, height: 2, pitch: 8, data: [3, 2, 1, 4, 99, 99, 99, 99, 30, 20, 10, 40] });
    assert.deepEqual(pixels(decodeDds(dds).data), [10, 20, 30, 40, 1, 2, 3, 4]);
});

test('DDS RGB565 and 24-bit RGB do not invent transparency', () => {
    const rgb565 = makeDds({ width: 1, height: 1, bits: 16, masks: [0xf800, 0x07e0, 0x001f, 0], data: [0, 0xf8] });
    assert.deepEqual(pixels(decodeDds(rgb565).data), [255, 0, 0, 255]);
    const rgb24 = makeDds({ width: 1, height: 1, bits: 24, masks: [0xff0000, 0xff00, 0xff, 0], data: [3, 2, 1] });
    assert.deepEqual(pixels(decodeDds(rgb24).data), [1, 2, 3, 255]);
});

test('DX10 RGBA and BGRA use their DXGI layout with zero legacy masks', () => {
    for (const [dxgiFormat, bytes, expected] of [
        [28, [1, 2, 3, 4], [1, 2, 3, 4]],
        [29, [1, 2, 3, 4], [1, 2, 3, 4]],
        [87, [3, 2, 1, 4], [1, 2, 3, 4]],
        [88, [3, 2, 1, 0], [1, 2, 3, 255]]
    ]) {
        const dds = makeDds({ width: 1, height: 1, dxgiFormat, data: bytes });
        assert.deepEqual(pixels(decodeDds(dds).data), expected);
    }
});

test('BC1 decodes color selectors, vertical orientation and transparent blocks', () => {
    const selectors = [...new Array(8).fill(0), ...new Array(8).fill(1)];
    const dds = makeDds({ width: 4, height: 4, fourCc: 'DXT1', data: colorBlock(0xf800, 0x001f, selectors) });
    const decoded = decodeDds(dds).data;
    assert.deepEqual(pixels(decoded.slice(0, 4)), [0, 0, 255, 255]);
    assert.deepEqual(pixels(decoded.slice(48, 52)), [255, 0, 0, 255]);
    const transparent = makeDds({ width: 3, height: 2, fourCc: 'DXT1', data: colorBlock(0, 0xffff, new Array(16).fill(3)) });
    assert.equal(decodeDds(transparent).data.length, 24);
    assert.ok(decodeDds(transparent).data.every(value => value === 0));
});

test('BC2 decodes four-color blocks and all explicit alpha nibbles', () => {
    const data = new Uint8Array(16);
    for (let i = 0; i < 8; i++) data[i] = i * 2 | ((i * 2 + 1) << 4);
    data.set(colorBlock(0, 0xffff, new Array(16).fill(3)), 8);
    const decoded = decodeDds(makeDds({ width: 4, height: 4, fourCc: 'DXT3', data })).data;
    assert.deepEqual(pixels(decoded.slice(0, 4)), [170, 170, 170, 204]);
    assert.deepEqual(pixels(decoded.slice(60, 64)), [170, 170, 170, 51]);
});

test('BC3 decodes both alpha palettes including the upper selector bits', () => {
    for (const [alpha0, alpha1, palette] of [
        [255, 0, [255, 0, 219, 182, 146, 109, 73, 36]],
        [0, 255, [0, 255, 51, 102, 153, 204, 0, 255]]
    ]) {
        const data = new Uint8Array(16);
        data[0] = alpha0;
        data[1] = alpha1;
        let alphaSelectors = 0n;
        for (let i = 0; i < 16; i++) alphaSelectors |= BigInt(i % 8) << BigInt(i * 3);
        for (let i = 0; i < 6; i++) data[i + 2] = Number((alphaSelectors >> BigInt(i * 8)) & 255n);
        data.set(colorBlock(0xf800, 0x001f), 8);
        const decoded = decodeDds(makeDds({ width: 4, height: 4, fourCc: 'DXT5', data })).data;
        for (let y = 0; y < 4; y++) for (let x = 0; x < 4; x++) {
            assert.equal(decoded[((3 - y) * 4 + x) * 4 + 3], palette[(y * 4 + x) % 8]);
        }
    }
});

test('invalid and truncated DDS files fail before allocating or reading pixels', () => {
    assert.throws(() => decodeDds(new ArrayBuffer(4)), /Invalid DDS header/);
    assert.throws(() => decodeDds(makeDds({ width: 0, data: [] })), /dimensions/);
    assert.throws(() => decodeDds(makeDds({ data: [1, 2, 3] })), /truncated/);
    assert.throws(() => decodeDds(makeDds({ width: 4, height: 4, fourCc: 'DXT5', data: [1] })), /truncated/);
    assert.throws(() => decodeDds(makeDds({ dxgiFormat: 99, data: [] })), /Unsupported DDS DXGI format: 99/);
    assert.throws(() => decodeDds(makeDds({ pitch: 1, data: new Uint8Array(16) })), /row pitch/);
});

test('all 24 music classroom DDS assets decode, including 75430BCC.DDS', async () => {
    const directory = new URL('../assets/mmd/samples/GameData/Scene/MusicClassroom/', import.meta.url);
    const files = (await readdir(directory)).filter(file => /\.dds$/i.test(file));
    assert.equal(files.length, 24);
    for (const file of files) {
        const bytes = await readFile(new URL(file, directory));
        const decoded = decodeDds(bufferOf(bytes));
        assert.equal(decoded.data.length, decoded.width * decoded.height * 4, file);
        if (file === '75430BCC.DDS') {
            assert.equal(decoded.width, 256);
            assert.ok(decoded.data.some((value, index) => index % 4 !== 3 && value < 200));
        }
    }
});

function mockBitmap(t, implementation) {
    const original = Object.getOwnPropertyDescriptor(globalThis, 'createImageBitmap');
    globalThis.createImageBitmap = implementation;
    t.after(() => {
        if (original) Object.defineProperty(globalThis, 'createImageBitmap', original);
        else delete globalThis.createImageBitmap;
    });
}

test('concurrent texture decoding cannot change the upload binding or lose material textures', async t => {
    const first = deferred();
    const second = deferred();
    const closed = [];
    const orientations = [];
    t.mock.method(globalThis, 'fetch', async uri => new Response(uri, { headers: { 'content-type': 'image/png' } }));
    mockBitmap(t, async (blob, options) => {
        const uri = await blob.text();
        orientations.push(options.imageOrientation);
        await (uri === 'first.png' ? first.promise : second.promise);
        return { uri, width: 2, height: 2, close: () => closed.push(uri) };
    });
    const { canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    const upload = renderer.uploadMesh(triangle(['first.png', 'second.png', 'first.png']));
    await settle();
    renderer.render(); // Leaves no texture bound while decoding is pending.
    second.resolve();
    await settle();
    renderer.render();
    first.resolve();
    await upload;
    state.draws.length = 0;
    renderer.render();
    assert.deepEqual(state.draws.map(draw => draw.texture.source.uri), ['first.png', 'second.png', 'first.png']);
    assert.ok(state.draws.every(draw => draw.hasTexture === 1));
    assert.equal(state.uploads.filter(upload => upload.width === 2).length, 2);
    assert.deepEqual(orientations, ['flipY', 'flipY']);
    assert.equal(closed.length, 2);
    // Zero normal Y components must survive upload.
    assert.equal(new Float32Array(state.buffers[0].buffer)[4], 0);
    renderer.dispose();
    assert.equal(new Set(state.deletedTextures).size, state.deletedTextures.length);
});

test('DDS in a memory-package data URI is detected by its header', async () => {
    const data = makeDds({ width: 1, height: 1, data: [3, 2, 1, 255] });
    const uri = `data:application/octet-stream;base64,${Buffer.from(data).toString('base64')}`;
    const { canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    await renderer.uploadMesh(triangle([uri]));
    renderer.render();
    assert.equal(state.draws.at(-1).hasTexture, 1);
    assert.deepEqual(pixels(state.draws.at(-1).texture.source), [1, 2, 3, 255]);
    renderer.dispose();
});

test('textures finishing after disposal are released', async t => {
    const gate = deferred();
    t.mock.method(globalThis, 'fetch', async () => new Response('pending.png'));
    mockBitmap(t, async () => { await gate.promise; return { width: 2, height: 2, close() {} }; });
    const { canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    const upload = renderer.uploadMesh(triangle(['pending.png']));
    await settle();
    renderer.dispose();
    gate.resolve();
    await upload;
    assert.ok(state.deletedTextures.includes(state.uploads.at(-1).texture));
});

test('a bad texture reports a warning and leaves the mesh drawable', async () => {
    const { canvas, state } = createFakeGl();
    const warnings = [];
    const renderer = createWebGl2Renderer(canvas, (...args) => warnings.push(args));
    await renderer.uploadMesh(triangle(['data:application/octet-stream;base64,RERTIA==']));
    renderer.render();
    assert.equal(warnings.length, 1);
    assert.equal(warnings[0][1], false);
    assert.match(warnings[0][0], /Invalid DDS header/);
    assert.equal(state.draws.at(-1).hasTexture, 0);
    renderer.dispose();
});

test('binary PMX updates upload the original bytes with the expected vertex stride', async () => {
    const { canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    await renderer.uploadMesh(triangle([null]));
    const vertices = new Float32Array(24).map((value, index) => index * .25 - 2);
    const bytes = new Uint8Array(vertices.buffer);
    renderer.updateMesh({ vertices: bytes });
    assert.deepEqual(state.bufferUpdates.at(-1), bytes);
    const unaligned = new Uint8Array(bytes.length + 1);
    unaligned.set(bytes, 1);
    renderer.updateMesh({ vertices: unaligned.subarray(1) });
    assert.deepEqual(state.bufferUpdates.at(-1), bytes);
    assert.throws(() => renderer.updateMesh({ vertices: new Uint8Array(4) }), /invalid byte length/);
    renderer.dispose();
});

test('PMX materials preserve tiled UVs and use repeat sampling on both axes', async () => {
    const { gl, canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    const dds = makeDds({ width: 1, height: 1, data: [3, 2, 1, 255] });
    const mesh = triangle([`data:application/octet-stream;base64,${Buffer.from(dds).toString('base64')}`]);
    mesh.uvs = [-.75, -6.5, 13.25, 0, 0, 4.75];
    await renderer.uploadMesh(mesh);
    const uploadedVertices = new Float32Array(state.buffers[0].buffer);
    assert.equal(uploadedVertices[6], -.75);
    assert.equal(uploadedVertices[7], -6.5);
    assert.equal(uploadedVertices[14], 13.25);
    renderer.render();
    const texture = state.draws.at(-1).texture;
    for (const parameter of [gl.TEXTURE_WRAP_S, gl.TEXTURE_WRAP_T]) {
        assert.equal(state.samplerParameters.find(item => item.parameter === parameter).value, gl.REPEAT);
        assert.equal(state.textureParameters.find(item => item.texture === texture && item.parameter === parameter).value, gl.REPEAT);
    }
    renderer.dispose();
});

test('double-sided PMX outlines cull front faces and restore the material state', async () => {
    const { gl, canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    const mesh = triangle([null]);
    mesh.materials[0].enableEdge = true;
    mesh.materials[0].edgeSize = 1;
    await renderer.uploadMesh(mesh);
    renderer.setFrameState({ features: { edges: true, shadows: false } });
    renderer.render();
    assert.equal(state.draws.length, 2);
    assert.equal(state.draws[0].edgePass, 1);
    assert.equal(state.draws[0].cullEnabled, true);
    assert.equal(state.draws[0].cullMode, gl.FRONT);
    assert.equal(state.draws[1].edgePass, 0);
    assert.equal(state.draws[1].cullEnabled, false);
    renderer.dispose();
});

test('texture alpha is blended even when the PMX diffuse alpha is opaque', async () => {
    const { canvas, state } = createFakeGl();
    const renderer = createWebGl2Renderer(canvas, message => assert.fail(message));
    const dds = makeDds({ width: 1, height: 1, data: [3, 2, 1, 128] });
    await renderer.uploadMesh(triangle([`data:application/octet-stream;base64,${Buffer.from(dds).toString('base64')}`]));
    renderer.render();
    assert.equal(state.draws.at(-1).blendEnabled, true);
    renderer.dispose();
});
