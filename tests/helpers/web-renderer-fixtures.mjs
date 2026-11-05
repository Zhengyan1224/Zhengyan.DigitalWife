export function makeDds({ width = 2, height = 2, data, fourCc = '', dxgiFormat, bits = 32, pitch, masks = [0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000] }) {
    if (dxgiFormat !== undefined) fourCc = 'DX10';
    const headerSize = fourCc === 'DX10' ? 148 : 128;
    const buffer = new ArrayBuffer(headerSize + data.length);
    const bytes = new Uint8Array(buffer);
    const view = new DataView(buffer);
    bytes.set([0x44, 0x44, 0x53, 0x20]);
    view.setUint32(4, 124, true);
    view.setUint32(8, fourCc && fourCc !== 'DX10' ? 0x81007 : 0x100f, true);
    view.setUint32(12, height, true);
    view.setUint32(16, width, true);
    view.setUint32(20, pitch ?? (fourCc && fourCc !== 'DX10' ? data.length : width * bits / 8), true);
    view.setUint32(76, 32, true);
    view.setUint32(80, fourCc ? 4 : 0x41, true);
    for (let i = 0; i < fourCc.length; i++) bytes[84 + i] = fourCc.charCodeAt(i);
    if (fourCc !== 'DX10') {
        view.setUint32(88, bits, true);
        for (let i = 0; i < 4; i++) view.setUint32(92 + i * 4, masks[i], true);
    } else {
        view.setUint32(128, dxgiFormat, true);
        view.setUint32(132, 3, true);
        view.setUint32(140, 1, true);
    }
    view.setUint32(108, 0x1000, true);
    bytes.set(data, headerSize);
    return buffer;
}

export function colorBlock(color0, color1, selectors = new Array(16).fill(0)) {
    const bytes = new Uint8Array(8);
    const view = new DataView(bytes.buffer);
    view.setUint16(0, color0, true);
    view.setUint16(2, color1, true);
    let indices = 0;
    for (let i = 0; i < selectors.length; i++) indices |= selectors[i] << (2 * i);
    view.setUint32(4, indices >>> 0, true);
    return bytes;
}

export function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

export async function settle() {
    for (let i = 0; i < 20; i++) await Promise.resolve();
}

export function createFakeGl() {
    let nextId = 0;
    const constants = new Map();
    const uniforms = new Map();
    const bindings = new Map();
    let activeUnit;
    let currentProgram;
    let cullEnabled = false;
    let cullMode;
    let blendEnabled = false;
    const state = { uploads: [], textureUpdates: [], draws: [], shadowDraws: [], deletedTextures: [], bufferUpdates: [], buffers: [], samplerParameters: [], textureParameters: [] };
    const resource = type => ({ type, id: ++nextId });
    const methods = {
        createShader: () => resource('shader'),
        createProgram: () => resource('program'),
        createTexture: () => resource('texture'),
        createFramebuffer: () => resource('framebuffer'),
        createSampler: () => resource('sampler'),
        createVertexArray: () => resource('vao'),
        createBuffer: () => resource('buffer'),
        getShaderParameter: () => true,
        getProgramParameter: () => true,
        getParameter: () => 'Fake WebGL2',
        checkFramebufferStatus: () => gl.FRAMEBUFFER_COMPLETE,
        getAttribLocation: (program, name) => ({ a_position: 0, a_normal: 1, a_uv: 2, a_color: 1, a_size: 2 })[name] ?? -1,
        getUniformLocation: (program, name) => ({ program, name }),
        useProgram: program => { currentProgram = program; },
        enable: capability => { if (capability === gl.CULL_FACE) cullEnabled = true; if (capability === gl.BLEND) blendEnabled = true; },
        disable: capability => { if (capability === gl.CULL_FACE) cullEnabled = false; if (capability === gl.BLEND) blendEnabled = false; },
        cullFace: mode => { cullMode = mode; },
        uniform1i: (location, value) => { uniforms.set(`${location.program.id}:${location.name}`, value); },
        uniform1f: (location, value) => { uniforms.set(`${location.program.id}:${location.name}`, value); },
        uniform3fv: (location, value) => { uniforms.set(`${location.program.id}:${location.name}`, Array.from(value)); },
        activeTexture: unit => { activeUnit = unit; },
        samplerParameteri: (sampler, parameter, value) => state.samplerParameters.push({ sampler, parameter, value }),
        texParameteri: (target, parameter, value) => state.textureParameters.push({ texture: bindings.get(activeUnit ?? gl.TEXTURE0), parameter, value }),
        bindTexture: (target, texture) => { bindings.set(activeUnit ?? gl.TEXTURE0, texture); },
        texImage2D: (...args) => {
            const texture = bindings.get(activeUnit ?? gl.TEXTURE0);
            if (!texture) throw new Error('Texture upload has no bound texture.');
            texture.source = args.at(-1);
            state.uploads.push({ texture, width: args.length === 9 ? args[3] : args.at(-1).width, source: args.at(-1) });
        },
        texSubImage2D: (...args) => state.textureUpdates.push({ texture: bindings.get(activeUnit), x: args[2], y: args[3], width: args[4], height: args[5], data: Array.from(args.at(-1)) }),
        generateMipmap: () => {
            const texture = bindings.get(activeUnit ?? gl.TEXTURE0);
            if (!texture?.source) throw new Error('Mipmap generation has no uploaded texture.');
        },
        deleteTexture: texture => { state.deletedTextures.push(texture); },
        bufferData: (target, data) => { state.buffers.push(new Uint8Array(data.buffer, data.byteOffset, data.byteLength).slice()); },
        bufferSubData: (target, offset, data) => { state.bufferUpdates.push(new Uint8Array(data.buffer, data.byteOffset, data.byteLength).slice()); },
        drawElements: () => {
            if (uniforms.has(`${currentProgram.id}:u_edgePass`)) {
                state.draws.push({ hasTexture: uniforms.get(`${currentProgram.id}:u_hasTexture`) || 0, texture: bindings.get(gl.TEXTURE0), edgePass: uniforms.get(`${currentProgram.id}:u_edgePass`), ambient: uniforms.get(`${currentProgram.id}:u_materialAmbient`), shadowStrength: uniforms.get(`${currentProgram.id}:u_shadowStrength`), receiveShadow: uniforms.get(`${currentProgram.id}:u_receiveShadow`), gpuSkinning: uniforms.get(`${currentProgram.id}:u_gpuSkinning`), boneCount: uniforms.get(`${currentProgram.id}:u_boneCount`), cullEnabled, cullMode, blendEnabled });
            } else {
                state.shadowDraws.push({ hasTexture: uniforms.get(`${currentProgram.id}:u_hasTexture`) || 0, texture: bindings.get(gl.TEXTURE0), alpha: uniforms.get(`${currentProgram.id}:u_alpha`), cullEnabled, cullMode, blendEnabled });
            }
        }
    };
    const gl = new Proxy(methods, {
        get(target, key) {
            if (key in target) return target[key];
            if (/^[A-Z][A-Z0-9_]*$/.test(key)) {
                if (!constants.has(key)) constants.set(key, ++nextId);
                return constants.get(key);
            }
            return () => {};
        }
    });
    const listeners = new Map();
    const canvas = {
        width: 320, height: 240,
        getContext: () => gl,
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: name => listeners.delete(name)
    };
    return { gl, canvas, state };
}

export function triangle(materials) {
    return {
        positions: [0, 0, 0, 1, 0, 0, 0, 1, 0],
        normals: [0, 0, 1, 0, 0, 1, 0, 0, 1],
        uvs: [0, 0, 1, 0, 0, 1],
        indices: materials.flatMap(() => [0, 1, 2]),
        materials: materials.map((textureUri, index) => ({
            textureUri, startIndex: index * 3, indexCount: 3,
            diffuse: [1, 1, 1, 1], doubleSided: true, enableEdge: false
        }))
    };
}
