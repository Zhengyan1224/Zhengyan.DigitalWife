const VERTEX_SHADER_SOURCE = `#version 300 es
in vec3 a_position;
in vec3 a_normal;
in vec2 a_uv;
uniform mat4 u_viewProjection;
uniform mat4 u_lightViewProjection;
uniform mat4 u_model;
uniform bool u_edgePass;
uniform float u_edgeSize;
out vec3 v_normal;
out vec2 v_uv;
out vec4 v_shadowPosition;

void main() {
    vec3 position = a_position;
    if (u_edgePass) position += normalize(a_normal) * u_edgeSize;
    vec4 worldPosition = u_model * vec4(position, 1.0);
    v_normal = mat3(u_model) * a_normal;
    v_uv = a_uv;
    v_shadowPosition = u_lightViewProjection * worldPosition;
    gl_Position = u_viewProjection * worldPosition;
}`;

const FRAGMENT_SHADER_SOURCE = `#version 300 es
precision highp float;
uniform sampler2D u_texture;
uniform sampler2D u_shadowMap;
uniform bool u_hasTexture;
uniform bool u_edgePass;
uniform bool u_shadowEnabled;
uniform bool u_receiveShadow;
uniform vec4 u_diffuse;
uniform vec4 u_edgeColor;
uniform vec3 u_lightDirection;
uniform vec3 u_lightColor;
uniform vec3 u_ambientColor;
uniform float u_ambientStrength;
in vec3 v_normal;
in vec2 v_uv;
in vec4 v_shadowPosition;
out vec4 out_color;

void main() {
    if (u_edgePass) {
        out_color = u_edgeColor;
        if (out_color.a <= 0.001) discard;
        return;
    }
    vec4 texel = u_hasTexture ? texture(u_texture, v_uv) : vec4(1.0);
    vec3 normal = normalize(v_normal);
    float diffuse = max(dot(normal, normalize(-u_lightDirection)), 0.0);
    vec3 lighting = u_ambientColor * u_ambientStrength + u_lightColor * diffuse;
    if (u_shadowEnabled && u_receiveShadow && v_shadowPosition.w > 0.0) {
        vec3 shadow = v_shadowPosition.xyz / v_shadowPosition.w;
        shadow = shadow * 0.5 + 0.5;
        if (shadow.x >= 0.0 && shadow.x <= 1.0 && shadow.y >= 0.0 && shadow.y <= 1.0 && shadow.z <= 1.0) {
            float closest = texture(u_shadowMap, shadow.xy).r;
            if (closest + 0.002 < shadow.z) lighting *= 0.45;
        }
    }
    vec4 color = vec4(texel.rgb * u_diffuse.rgb * lighting, texel.a * u_diffuse.a);
    if (color.a <= 0.001) discard;
    out_color = color;
}`;

const SHADOW_VERTEX_SHADER_SOURCE = `#version 300 es
in vec3 a_position;
uniform mat4 u_lightViewProjection;
uniform mat4 u_model;
void main() { gl_Position = u_lightViewProjection * u_model * vec4(a_position, 1.0); }`;

const PARTICLE_VERTEX_SHADER_SOURCE = `#version 300 es
in vec3 a_position;
in vec4 a_color;
in float a_size;
uniform mat4 u_viewProjection;
out vec4 v_color;
void main() {
    gl_Position = u_viewProjection * vec4(a_position, 1.0);
    gl_PointSize = a_size * 80.0 / max(gl_Position.w, 0.1);
    v_color = a_color;
}`;

const PARTICLE_FRAGMENT_SHADER_SOURCE = `#version 300 es
precision highp float;
in vec4 v_color;
out vec4 out_color;
void main() {
    vec2 delta = gl_PointCoord - vec2(0.5);
    if (dot(delta, delta) > 0.25) discard;
    out_color = v_color;
}`;

function numberedSource(source) {
    return source.split("\n").map((line, index) => `${String(index + 1).padStart(3, " ")} | ${line}`).join("\n");
}

function compileShader(gl, type, source, label) {
    const shader = gl.createShader(type);
    if (!shader) throw new Error(`WebGL2 could not allocate ${label}.`);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        const infoLog = gl.getShaderInfoLog(shader) || "no driver log";
        gl.deleteShader(shader);
        throw new Error(`${label} compile failed in webgl2-renderer.js.\n${infoLog}\nSource:\n${numberedSource(source)}`);
    }
    return shader;
}

function createShadowProgram(gl) {
    const vertex = compileShader(gl, gl.VERTEX_SHADER, SHADOW_VERTEX_SHADER_SOURCE, "WebGL2 shadow vertex shader");
    const program = gl.createProgram();
    if (!program) throw new Error("WebGL2 could not allocate the shadow program.");
    gl.attachShader(program, vertex);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const infoLog = gl.getProgramInfoLog(program) || "no driver log";
        gl.deleteProgram(program);
        throw new Error(`WebGL2 shadow program link failed.\n${infoLog}`);
    }
    return program;
}

function createParticleProgram(gl) {
    const vertex = compileShader(gl, gl.VERTEX_SHADER, PARTICLE_VERTEX_SHADER_SOURCE, "WebGL2 particle vertex shader");
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, PARTICLE_FRAGMENT_SHADER_SOURCE, "WebGL2 particle fragment shader");
    const program = gl.createProgram();
    if (!program) throw new Error("WebGL2 could not allocate the particle program.");
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    gl.deleteShader(fragment);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const infoLog = gl.getProgramInfoLog(program) || "no driver log";
        gl.deleteProgram(program);
        throw new Error(`WebGL2 particle program link failed.\n${infoLog}`);
    }
    return program;
}

function createProgram(gl) {
    const vertex = compileShader(gl, gl.VERTEX_SHADER, VERTEX_SHADER_SOURCE, "WebGL2 PMX vertex shader");
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE, "WebGL2 PMX fragment shader");
    const program = gl.createProgram();
    if (!program) throw new Error("WebGL2 could not allocate the PMX program.");
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    gl.deleteShader(fragment);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const infoLog = gl.getProgramInfoLog(program) || "no driver log";
        gl.deleteProgram(program);
        throw new Error(`WebGL2 PMX program link failed.\n${infoLog}`);
    }
    return program;
}

function createDefaultTexture(gl) {
    const texture = gl.createTexture();
    if (!texture) throw new Error("WebGL2 could not allocate the default texture.");
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE,
        new Uint8Array([255, 255, 255, 255]));
    gl.bindTexture(gl.TEXTURE_2D, null);
    return texture;
}

function createTextureSampler(gl) {
    const sampler = gl.createSampler();
    if (!sampler) throw new Error("WebGL2 could not allocate the texture sampler.");
    gl.samplerParameteri(sampler, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.samplerParameteri(sampler, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return sampler;
}

async function loadTexture(gl, uri, reportError) {
    if (!uri) return null;
    try {
        const response = await fetch(uri, { cache: "force-cache" });
        if (!response.ok) throw new Error(`HTTP ${response.status} while loading ${uri}`);
        const blob = await response.blob();
        const bitmap = await createImageBitmap(blob);
        const texture = gl.createTexture();
        if (!texture) throw new Error(`Could not allocate texture for ${uri}`);
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 1);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, bitmap);
        gl.generateMipmap(gl.TEXTURE_2D);
        gl.bindTexture(gl.TEXTURE_2D, null);
        bitmap.close?.();
        return texture;
    }
    catch (error) {
        reportError?.(`Texture load failed for ${uri}: ${error?.message || String(error)}`);
        return null;
    }
}

function createMeshResources(gl, payload, locations, defaultTexture, reportError) {
    const positions = new Float32Array(payload.positions || []);
    const normals = new Float32Array(payload.normals || []);
    const uvs = new Float32Array(payload.uvs || []);
    const indices = new Uint32Array(payload.indices || []);
    const vertexCount = positions.length / 3;
    const interleaved = new Float32Array(vertexCount * 8);
    for (let i = 0; i < vertexCount; i++) {
        const p = i * 3;
        const v = i * 8;
        interleaved[v] = positions[p];
        interleaved[v + 1] = positions[p + 1];
        interleaved[v + 2] = positions[p + 2];
        interleaved[v + 3] = normals[p] || 0;
        interleaved[v + 4] = normals[p + 1] || 1;
        interleaved[v + 5] = normals[p + 2] || 0;
        const uv = i * 2;
        interleaved[v + 6] = uvs[uv] || 0;
        interleaved[v + 7] = uvs[uv + 1] || 0;
    }
    const vao = gl.createVertexArray();
    const vertexBuffer = gl.createBuffer();
    const indexBuffer = gl.createBuffer();
    if (!vao || !vertexBuffer || !indexBuffer) throw new Error("WebGL2 could not allocate PMX mesh buffers.");
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, vertexBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, interleaved, gl.DYNAMIC_DRAW);
    gl.enableVertexAttribArray(locations.position);
    gl.vertexAttribPointer(locations.position, 3, gl.FLOAT, false, 32, 0);
    gl.enableVertexAttribArray(locations.normal);
    gl.vertexAttribPointer(locations.normal, 3, gl.FLOAT, false, 32, 12);
    gl.enableVertexAttribArray(locations.uv);
    gl.vertexAttribPointer(locations.uv, 2, gl.FLOAT, false, 32, 24);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
    gl.bindVertexArray(null);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);

    const groups = (payload.materials || []).map(material => ({
        ...material,
        texture: null,
        texturePromise: loadTexture(gl, material.textureUri, reportError)
            .then(texture => { if (texture) material.texture = texture; return texture; })
    }));
    return { vao, vertexBuffer, indexBuffer, indexCount: indices.length, vertexCount, groups, interleaved };
}

function updateMeshResources(gl, mesh, payload) {
    const positions = new Float32Array(payload.positions || []);
    const normals = new Float32Array(payload.normals || []);
    const uvs = new Float32Array(payload.uvs || []);
    for (let i = 0; i < mesh.vertexCount; i++) {
        const p = i * 3, v = i * 8, uv = i * 2;
        mesh.interleaved[v] = positions[p] ?? mesh.interleaved[v];
        mesh.interleaved[v + 1] = positions[p + 1] ?? mesh.interleaved[v + 1];
        mesh.interleaved[v + 2] = positions[p + 2] ?? mesh.interleaved[v + 2];
        mesh.interleaved[v + 3] = normals[p] ?? mesh.interleaved[v + 3];
        mesh.interleaved[v + 4] = normals[p + 1] ?? mesh.interleaved[v + 4];
        mesh.interleaved[v + 5] = normals[p + 2] ?? mesh.interleaved[v + 5];
        mesh.interleaved[v + 6] = uvs[uv] ?? mesh.interleaved[v + 6];
        mesh.interleaved[v + 7] = uvs[uv + 1] ?? mesh.interleaved[v + 7];
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, mesh.vertexBuffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, mesh.interleaved);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
}

function deleteMeshResources(gl, mesh) {
    if (!mesh) return;
    gl.deleteVertexArray(mesh.vao);
    gl.deleteBuffer(mesh.vertexBuffer);
    gl.deleteBuffer(mesh.indexBuffer);
    for (const group of mesh.groups || []) if (group.texture) gl.deleteTexture(group.texture);
}

export function createWebGl2Renderer(canvas, reportError) {
    const gl = canvas.getContext("webgl2", { alpha: false, antialias: true, depth: true });
    if (!gl) throw new Error("This browser does not provide WebGL2.");
    const program = createProgram(gl);
    const shadowProgram = createShadowProgram(gl);
    const particleProgram = createParticleProgram(gl);
    const defaultTexture = createDefaultTexture(gl);
    const shadowTexture = gl.createTexture();
    const shadowFramebuffer = gl.createFramebuffer();
    const shadowSize = 1024;
    if (!shadowTexture || !shadowFramebuffer) throw new Error("WebGL2 could not allocate the shadow map.");
    gl.bindTexture(gl.TEXTURE_2D, shadowTexture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.DEPTH_COMPONENT24, shadowSize, shadowSize, 0, gl.DEPTH_COMPONENT, gl.UNSIGNED_INT, null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, shadowFramebuffer);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, shadowTexture, 0);
    gl.drawBuffers([gl.NONE]);
    gl.readBuffer(gl.NONE);
    if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) throw new Error("WebGL2 shadow framebuffer is incomplete.");
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.bindTexture(gl.TEXTURE_2D, null);
    const sampler = createTextureSampler(gl);
    const vao = gl.createVertexArray();
    if (!vao) throw new Error("WebGL2 could not allocate the empty mesh VAO.");
    const locations = {
        position: gl.getAttribLocation(program, "a_position"),
        normal: gl.getAttribLocation(program, "a_normal"),
        uv: gl.getAttribLocation(program, "a_uv"),
        viewProjection: gl.getUniformLocation(program, "u_viewProjection"),
        model: gl.getUniformLocation(program, "u_model"),
        edgePass: gl.getUniformLocation(program, "u_edgePass"),
        edgeSize: gl.getUniformLocation(program, "u_edgeSize"),
        texture: gl.getUniformLocation(program, "u_texture"),
        hasTexture: gl.getUniformLocation(program, "u_hasTexture"),
        diffuse: gl.getUniformLocation(program, "u_diffuse"),
        edgeColor: gl.getUniformLocation(program, "u_edgeColor"),
        lightDirection: gl.getUniformLocation(program, "u_lightDirection"),
        lightColor: gl.getUniformLocation(program, "u_lightColor"),
        ambientColor: gl.getUniformLocation(program, "u_ambientColor"),
        ambientStrength: gl.getUniformLocation(program, "u_ambientStrength"),
        lightViewProjection: gl.getUniformLocation(program, "u_lightViewProjection"),
        shadowMap: gl.getUniformLocation(program, "u_shadowMap"),
        shadowEnabled: gl.getUniformLocation(program, "u_shadowEnabled"),
        receiveShadow: gl.getUniformLocation(program, "u_receiveShadow")
    };
    const shadowLocations = {
        position: gl.getAttribLocation(shadowProgram, "a_position"),
        lightViewProjection: gl.getUniformLocation(shadowProgram, "u_lightViewProjection"),
        model: gl.getUniformLocation(shadowProgram, "u_model")
    };
    const particleLocations = {
        position: gl.getAttribLocation(particleProgram, "a_position"),
        color: gl.getAttribLocation(particleProgram, "a_color"),
        size: gl.getAttribLocation(particleProgram, "a_size"),
        viewProjection: gl.getUniformLocation(particleProgram, "u_viewProjection")
    };
    if (locations.position < 0 || locations.normal < 0 || locations.uv < 0) {
        throw new Error("WebGL2 PMX shader attributes are incomplete.");
    }
    if (shadowLocations.position < 0 || !shadowLocations.lightViewProjection || !shadowLocations.model) {
        throw new Error("WebGL2 shadow shader bindings are incomplete.");
    }
    if (particleLocations.position < 0 || particleLocations.color < 0 || particleLocations.size < 0 || !particleLocations.viewProjection) {
        throw new Error("WebGL2 particle shader bindings are incomplete.");
    }

    let disposed = false;
    let width = Math.max(canvas.width, 1);
    let height = Math.max(canvas.height, 1);
    const meshes = new Map();
    const identity = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    let frameState = {
        viewProjection: identity,
        model: identity,
        lightViewProjection: identity,
        lightDirection: [0.5, -1, 0.5], lightColor: [1, 1, 1], ambientColor: [0.65, 0.65, 0.65],
        ambientStrength: 0.25, clearColor: [0.035, 0.055, 0.1, 1], meshes: []
    };
    const particleBuffer = gl.createBuffer();
    if (!particleBuffer) throw new Error("WebGL2 could not allocate the particle buffer.");
    const contextLost = event => { event.preventDefault(); disposed = true; reportError?.("WebGL2 context was lost."); };
    canvas.addEventListener("webglcontextlost", contextLost, false);

    const info = Object.freeze({
        version: String(gl.getParameter(gl.VERSION) || "WebGL 2"),
        renderer: String(gl.getParameter(gl.RENDERER) || "WebGL2"),
        shadingLanguageVersion: String(gl.getParameter(gl.SHADING_LANGUAGE_VERSION) || "GLSL ES 3.00")
    });

    return {
        info,
        resize(nextWidth, nextHeight) {
            if (disposed) return;
            width = Math.max(Math.floor(nextWidth), 1);
            height = Math.max(Math.floor(nextHeight), 1);
            gl.viewport(0, 0, width, height);
        },
        setFrameState(nextState) {
            if (disposed || !nextState) return;
            frameState = { ...frameState, ...nextState };
        },
        async uploadMesh(payload) {
            if (disposed) return;
            const nextMesh = createMeshResources(gl, payload, locations, defaultTexture, reportError);
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            meshes.set("default", nextMesh);
            await Promise.allSettled(nextMesh.groups.map(group => group.texturePromise));
        },
        async uploadMeshes(entries) {
            if (disposed) return;
            const nextMeshes = new Map();
            const pending = [];
            for (const entry of entries || []) {
                const key = String(entry?.id ?? entry?.Id ?? "").trim();
                const payload = entry?.mesh ?? entry?.Mesh;
                if (!key || !payload) continue;
                const nextMesh = createMeshResources(gl, payload, locations, defaultTexture, reportError);
                nextMeshes.set(key, nextMesh);
                pending.push(...nextMesh.groups.map(group => group.texturePromise));
            }
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            for (const [key, value] of nextMeshes) meshes.set(key, value);
            await Promise.allSettled(pending);
        },
        updateMesh(payload) {
            if (disposed || !payload) return;
            const mesh = meshes.get("default");
            if (!mesh) return;
            updateMeshResources(gl, mesh, payload);
        },
        updateMeshes(entries) {
            if (disposed) return;
            for (const entry of entries || []) {
                const key = String(entry?.id ?? entry?.Id ?? "").trim();
                const payload = entry?.mesh ?? entry?.Mesh;
                const mesh = meshes.get(key);
                if (mesh && payload) updateMeshResources(gl, mesh, payload);
            }
        },
        render() {
            if (disposed) return;
            gl.enable(gl.DEPTH_TEST);
            gl.enable(gl.CULL_FACE);
            const frameMeshes = Array.isArray(frameState.meshes) && frameState.meshes.length > 0
                ? frameState.meshes
                : [{ id: "default", model: frameState.model, shadowEnabled: frameState.shadowEnabled, receiveShadow: true, edgeEnabled: true }];
            const features = frameState.features || {};
            const shadowsEnabled = features.shadows ?? frameState.shadowEnabled;
            const frameById = new Map(frameMeshes.map(item => [String(item.id ?? item.Id ?? "default"), item]));
            const renderEntries = [...meshes.entries()]
                .map(([key, mesh]) => ({ key, mesh, frame: frameById.get(key) || frameById.get("default") }))
                .filter(entry => entry.frame);
            if (renderEntries.some(entry => entry.frame.shadowEnabled) && shadowsEnabled) {
                gl.bindFramebuffer(gl.FRAMEBUFFER, shadowFramebuffer);
                gl.viewport(0, 0, shadowSize, shadowSize);
                gl.colorMask(false, false, false, false);
                gl.clear(gl.DEPTH_BUFFER_BIT);
                gl.useProgram(shadowProgram);
                gl.uniformMatrix4fv(shadowLocations.lightViewProjection, false, frameState.lightViewProjection);
                for (const entry of renderEntries) {
                    if (!entry.frame.shadowEnabled) continue;
                    gl.uniformMatrix4fv(shadowLocations.model, false, entry.frame.model || identity);
                    gl.bindVertexArray(entry.mesh.vao);
                    gl.bindBuffer(gl.ARRAY_BUFFER, entry.mesh.vertexBuffer);
                    gl.enableVertexAttribArray(shadowLocations.position);
                    gl.vertexAttribPointer(shadowLocations.position, 3, gl.FLOAT, false, 32, 0);
                    for (const group of entry.mesh.groups) gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                }
                gl.bindVertexArray(null);
                gl.colorMask(true, true, true, true);
            }
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.viewport(0, 0, width, height);
            gl.clearColor(...frameState.clearColor);
            gl.clearDepth(1.0);
            gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
            gl.useProgram(program);
            gl.uniformMatrix4fv(locations.viewProjection, false, frameState.viewProjection);
            gl.uniformMatrix4fv(locations.lightViewProjection, false, frameState.lightViewProjection);
            gl.uniform1i(locations.shadowEnabled, shadowsEnabled ? 1 : 0);
            gl.activeTexture(gl.TEXTURE1);
            gl.bindTexture(gl.TEXTURE_2D, shadowTexture);
            gl.uniform1i(locations.shadowMap, 1);
            gl.uniform3fv(locations.lightDirection, frameState.lightDirection);
            gl.uniform3fv(locations.lightColor, frameState.lightColor);
            gl.uniform3fv(locations.ambientColor, frameState.ambientColor);
            gl.uniform1f(locations.ambientStrength, frameState.ambientStrength);
            for (const entry of renderEntries) {
                const mesh = entry.mesh;
                const meshFrame = entry.frame;
                gl.uniformMatrix4fv(locations.model, false, meshFrame.model || identity);
                gl.bindVertexArray(mesh.vao);
                for (const group of mesh.groups) {
                    const diffuse = group.diffuse || [1, 1, 1, 1];
                    const edge = group.edgeColor || [0, 0, 0, 1];
                    gl.uniform4fv(locations.diffuse, diffuse);
                    gl.uniform4fv(locations.edgeColor, edge);
                    gl.uniform1i(locations.receiveShadow, group.receiveShadow && meshFrame.receiveShadow !== false ? 1 : 0);
                    if (group.doubleSided) gl.disable(gl.CULL_FACE); else gl.enable(gl.CULL_FACE);
                    if ((features.edges ?? true) && meshFrame.edgeEnabled !== false && group.enableEdge && group.edgeSize > 0) {
                        gl.cullFace(gl.FRONT);
                        gl.uniform1i(locations.edgePass, 1);
                        gl.uniform1f(locations.edgeSize, group.edgeSize * 0.01);
                        gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                        gl.cullFace(gl.BACK);
                    }
                    gl.uniform1i(locations.edgePass, 0);
                    gl.uniform1f(locations.edgeSize, 0);
                    const texture = group.texture || defaultTexture;
                    gl.activeTexture(gl.TEXTURE0);
                    gl.bindTexture(gl.TEXTURE_2D, texture);
                    gl.bindSampler(0, sampler);
                    gl.uniform1i(locations.texture, 0);
                    gl.uniform1i(locations.hasTexture, group.texture ? 1 : 0);
                    if (diffuse[3] < 0.999) gl.enable(gl.BLEND); else gl.disable(gl.BLEND);
                    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
                    gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                }
            }
            const particles = Array.isArray(frameState.particles) && frameState.particles.length > 0 ? frameState.particles : [];
            if ((features.particles ?? false) && particles.length > 0) {
                const data = new Float32Array(particles.length * 8);
                for (let i = 0; i < particles.length; i++) {
                    const particle = particles[i];
                    const offset = i * 8;
                    const position = particle.position || [0, 0, 0];
                    const color = particle.color || [1, 1, 1, 1];
                    data[offset] = position[0]; data[offset + 1] = position[1]; data[offset + 2] = position[2];
                    data[offset + 3] = color[0]; data[offset + 4] = color[1]; data[offset + 5] = color[2]; data[offset + 6] = color[3];
                    data[offset + 7] = particle.size || 0.1;
                }
                gl.useProgram(particleProgram);
                gl.uniformMatrix4fv(particleLocations.viewProjection, false, frameState.viewProjection);
                gl.bindBuffer(gl.ARRAY_BUFFER, particleBuffer);
                gl.bufferData(gl.ARRAY_BUFFER, data, gl.STREAM_DRAW);
                gl.enableVertexAttribArray(particleLocations.position);
                gl.vertexAttribPointer(particleLocations.position, 3, gl.FLOAT, false, 32, 0);
                gl.enableVertexAttribArray(particleLocations.color);
                gl.vertexAttribPointer(particleLocations.color, 4, gl.FLOAT, false, 32, 12);
                gl.enableVertexAttribArray(particleLocations.size);
                gl.vertexAttribPointer(particleLocations.size, 1, gl.FLOAT, false, 32, 28);
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
                gl.depthMask(false);
                gl.drawArrays(gl.POINTS, 0, particles.length);
                gl.depthMask(true);
                gl.bindBuffer(gl.ARRAY_BUFFER, null);
            }
            gl.disable(gl.BLEND);
            gl.bindSampler(0, null);
            gl.activeTexture(gl.TEXTURE1);
            gl.bindTexture(gl.TEXTURE_2D, null);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, null);
            gl.bindVertexArray(null);
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            canvas.removeEventListener("webglcontextlost", contextLost, false);
            gl.bindVertexArray(null);
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            gl.deleteVertexArray(vao);
            gl.deleteTexture(defaultTexture);
            gl.deleteTexture(shadowTexture);
            gl.deleteFramebuffer(shadowFramebuffer);
            gl.deleteSampler(sampler);
            gl.deleteProgram(shadowProgram);
            gl.deleteBuffer(particleBuffer);
            gl.deleteProgram(particleProgram);
            gl.deleteProgram(program);
        }
    };
}
