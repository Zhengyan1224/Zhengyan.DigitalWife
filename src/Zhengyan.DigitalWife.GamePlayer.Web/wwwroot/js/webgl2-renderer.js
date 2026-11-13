const effectUrl = name => { const url = new URL(name, import.meta.url); url.search = new URL(import.meta.url).search; return url.href; };
const [{ waterVertexUniforms, waterFragmentUniforms, createSceneEffects }, { createUnderwaterPresenter }] = await Promise.all([
    import(effectUrl("./web-scene-effects.js")), import(effectUrl("./web-underwater.js"))
]);

const VERTEX_SHADER_SOURCE = `#version 300 es
layout(location = 0) in vec3 a_position;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
${skinningShaderSource()}
${waterVertexUniforms}
uniform mat4 u_viewProjection;
uniform mat4 u_lightViewProjection;
uniform mat4 u_model;
uniform bool u_edgePass;
uniform float u_edgeSize;
uniform vec3 u_lightDirection;
uniform float u_shadowNormalOffset;
out vec3 v_normal;
out vec2 v_uv;
out vec4 v_shadowPosition;
out vec3 v_worldPosition;

void main() {
    vec3 position, normal;
    skinVertex(position, normal);
    if (u_waterSurface) displaceWater(position, normal);
    if (u_edgePass) position += normalize(normal) * u_edgeSize;
    vec4 worldPosition = u_model * vec4(position, 1.0);
    v_worldPosition = worldPosition.xyz;
    v_normal = transpose(inverse(mat3(u_model))) * normal;
    v_uv = a_uv;
    float ndotl = max(dot(normalize(v_normal), normalize(-u_lightDirection)), 0.0);
    v_shadowPosition = u_lightViewProjection * vec4(worldPosition.xyz + normalize(v_normal) * u_shadowNormalOffset * (2.0 - ndotl), 1.0);
    gl_Position = u_viewProjection * worldPosition;
}`;

function skinningShaderSource() {
    return `
layout(location = 3) in vec4 a_boneIndices;
layout(location = 4) in vec4 a_boneWeights;
uniform bool u_gpuSkinning;
uniform highp sampler2D u_bones;
uniform highp sampler2D u_morphs;
uniform int u_boneCount;
uniform int u_morphWidth;
mat4 boneMatrix(float index) {
    int bone = int(index);
    if (bone < 0 || bone >= u_boneCount) return mat4(1.0);
    return mat4(texelFetch(u_bones,ivec2(0,bone),0), texelFetch(u_bones,ivec2(1,bone),0),
                texelFetch(u_bones,ivec2(2,bone),0), texelFetch(u_bones,ivec2(3,bone),0));
}
void skinVertex(out vec3 position, out vec3 normal) {
    position = a_position; normal = a_normal;
    if (!u_gpuSkinning) return;
    position += texelFetch(u_morphs,ivec2(gl_VertexID % u_morphWidth,gl_VertexID / u_morphWidth),0).xyz;
    mat4 skin = boneMatrix(a_boneIndices.x) * a_boneWeights.x;
    if (a_boneWeights.y != 0.0) skin += boneMatrix(a_boneIndices.y) * a_boneWeights.y;
    if (a_boneWeights.z != 0.0) skin += boneMatrix(a_boneIndices.z) * a_boneWeights.z;
    if (a_boneWeights.w != 0.0) skin += boneMatrix(a_boneIndices.w) * a_boneWeights.w;
    position = (skin * vec4(position,1.0)).xyz;
    vec3 transformedNormal = mat3(skin) * normal;
    normal = dot(transformedNormal,transformedNormal) > 1e-10 ? normalize(transformedNormal) : normal;
}`;
}

const FRAGMENT_SHADER_SOURCE = `#version 300 es
precision highp float;
precision highp sampler2DShadow;
uniform sampler2D u_texture;
uniform sampler2DShadow u_shadowMap;
uniform bool u_hasTexture;
uniform bool u_edgePass;
uniform bool u_shadowEnabled;
uniform bool u_receiveShadow;
uniform vec4 u_diffuse;
uniform vec4 u_textureMultiply; uniform vec4 u_textureAdd;
uniform sampler2D u_toonTexture; uniform sampler2D u_sphereTexture;
uniform bool u_hasToon; uniform int u_sphereMode;
uniform mat4 u_view;
uniform vec4 u_toonMultiply; uniform vec4 u_toonAdd; uniform vec4 u_sphereMultiply; uniform vec4 u_sphereAdd;
uniform vec3 u_materialAmbient;
uniform vec3 u_specular;
uniform float u_specularPower;
uniform vec3 u_cameraPosition;
uniform float u_shadowStrength;
uniform float u_shadowBias;
uniform vec2 u_shadowTexelSize;
uniform vec2 u_shadowFilterParameters;
uniform bool u_shadowToon;
uniform vec4 u_edgeColor;
uniform vec3 u_lightDirection;
uniform vec3 u_lightColor;
uniform vec3 u_ambientColor;
uniform float u_ambientStrength;
uniform int u_pointLightCount;
uniform vec4 u_pointLightPositionRange[8];
uniform vec4 u_pointLightColorIntensity[8];
uniform int u_spotLightCount;
uniform vec4 u_spotLightPositionRange[4];
uniform vec4 u_spotLightDirectionOuterCosine[4];
uniform vec4 u_spotLightColorIntensity[4];
uniform vec4 u_spotLightConeParameters[4];
uniform sampler2DShadow u_localShadowAtlas;
uniform mat4 u_localShadowMatrices[16];
uniform vec4 u_localShadowRects[16];
uniform vec2 u_localShadowRanges[16];
uniform int u_pointShadowBase[8];
uniform int u_spotShadowTile[4];
uniform bool u_texturedPlane;
uniform bool u_waterSurface;
uniform sampler2D u_reflectionTexture;
uniform mat4 u_reflectionViewProjection;
uniform float u_reflectionStrength;
uniform bool u_clipEnabled;
uniform vec4 u_clipPlane;
in vec3 v_normal;
in vec2 v_uv;
in vec4 v_shadowPosition;
in vec3 v_worldPosition;
out vec4 out_color;
${waterFragmentUniforms}

int ShadowFilterSide(vec2 settings, int legacySide) {
    if (settings.x < 1.0) return legacySide;
    if (settings.x <= 1.0) return 1;
    if (settings.x <= 4.0) return 2;
    if (settings.x <= 9.0) return 3;
    if (settings.x <= 16.0) return 4;
    if (settings.x <= 25.0) return 5;
    return 7;
}

vec2 ShadowFilterOffset(vec2 settings, ivec2 cell, int side, float legacyRadius) {
    if (side == 1) return vec2(0.0);
    float radius = settings.x < 1.0 ? legacyRadius : clamp(settings.y, 0.0, 8.0);
    return (vec2(cell) / float(side - 1) * 2.0 - 1.0) * radius;
}

int pointShadowFace(vec3 direction) {
    vec3 a = abs(direction);
    if (a.x >= a.y && a.x >= a.z) return direction.x >= 0.0 ? 0 : 1;
    if (a.y >= a.z) return direction.y >= 0.0 ? 2 : 3;
    return direction.z >= 0.0 ? 4 : 5;
}

float localShadow(int tile, vec3 normal, vec3 lightDirection, float ndotl) {
    if (tile < 0 || !u_shadowEnabled || !u_receiveShadow) return 1.0;
    vec3 position = v_worldPosition + normal * 0.0075 * (2.0 - ndotl);
    vec4 clip = u_localShadowMatrices[tile] * vec4(position, 1.0);
    if (clip.w <= 0.0) return 1.0;
    vec3 ndc = clip.xyz / clip.w;
    if (any(lessThan(ndc, vec3(-1.0))) || any(greaterThan(ndc, vec3(1.0)))) return 1.0;
    vec2 range = u_localShadowRanges[tile];
    float bias = 0.015 * (1.0 + (1.0 - ndotl) * 2.0) * range.x * range.y
        / ((range.y - range.x) * clip.w * clip.w);
    float depth = ndc.z * 0.5 + 0.5 - bias;
    vec4 rect = u_localShadowRects[tile];
    vec2 texel = 1.0 / vec2(textureSize(u_localShadowAtlas, 0));
    vec2 uv = rect.xy + (ndc.xy * 0.5 + 0.5) * rect.zw;
    float visibility = 0.0;
    int side = ShadowFilterSide(u_shadowFilterParameters, 2);
    for (int y = 0; y < 7; y++) {
        if (y >= side) break;
        for (int x = 0; x < 7; x++) {
            if (x >= side) break;
            vec2 offset = ShadowFilterOffset(u_shadowFilterParameters, ivec2(x,y), side, 0.5) * texel;
            vec2 sampleUv = clamp(uv + offset, rect.xy + texel, rect.xy + rect.zw - texel);
            visibility += texture(u_localShadowAtlas, vec3(sampleUv, depth));
        }
    }
    visibility /= float(side * side);
    if (u_shadowToon) visibility = step(0.5, visibility);
    return mix(1.0 - u_shadowStrength, 1.0, visibility);
}

void main() {
    if (u_clipEnabled && dot(vec4(v_worldPosition, 1.0), u_clipPlane) < 0.0) discard;
    if (u_edgePass) {
        out_color = u_edgeColor;
        if (out_color.a <= 0.001) discard;
        return;
    }
    vec4 texel = u_hasTexture ? texture(u_texture, v_uv) : vec4(1.0);
    texel.rgb=mix(vec3(1),texel.rgb*u_textureMultiply.rgb,u_textureMultiply.a);
    texel.rgb=clamp(texel.rgb+(texel.rgb-1.0)*u_textureAdd.a,0.0,1.0)+u_textureAdd.rgb;
    vec3 normal = normalize(v_normal);
    float diffuse = max(dot(normal, normalize(-u_lightDirection)), 0.0);
    float shadowFactor = 1.0;
    if (u_shadowEnabled && u_receiveShadow && v_shadowPosition.w > 0.0) {
        vec3 shadow = v_shadowPosition.xyz / v_shadowPosition.w * 0.5 + 0.5;
        if (all(greaterThanEqual(shadow, vec3(0.0))) && all(lessThanEqual(shadow, vec3(1.0)))) {
            float depth = shadow.z - u_shadowBias * (1.0 + (1.0 - diffuse) * 2.0);
            float visibility = 0.0;
            int side = ShadowFilterSide(u_shadowFilterParameters, 3);
            for (int y = 0; y < 7; ++y) {
                if (y >= side) break;
                for (int x = 0; x < 7; ++x) {
                    if (x >= side) break;
                    vec2 uv = clamp(shadow.xy + ShadowFilterOffset(u_shadowFilterParameters, ivec2(x, y), side, 1.0) * u_shadowTexelSize, u_shadowTexelSize, vec2(1.0) - u_shadowTexelSize);
                    visibility += texture(u_shadowMap, vec3(uv, depth));
                }
            }
            visibility /= float(side * side);
            if (u_shadowToon) visibility = step(0.5, visibility);
            shadowFactor = mix(1.0 - u_shadowStrength, 1.0, visibility);
        }
    }
    if (u_waterSurface) { out_color = shadeWater(normal); if(out_color.a<=.001)discard; return; }
    if (u_texturedPlane) {
        vec4 color = texel * u_diffuse;
        color.rgb *= shadowFactor;
        vec4 reflectionClip = u_reflectionViewProjection * vec4(v_worldPosition, 1.0);
        vec2 uv = reflectionClip.xy / max(abs(reflectionClip.w), 0.0001) * 0.5 + 0.5;
        float reflectionWeight = u_reflectionStrength;
        if (u_waterSurface) {
            float fresnel = pow(1.0 - abs(dot(normalize(u_cameraPosition - v_worldPosition), normal)), 5.0);
            reflectionWeight *= mix(0.35, 1.0, fresnel);
        }
        if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))))
            color.rgb = mix(color.rgb, texture(u_reflectionTexture, clamp(uv, 0.001, 0.999)).rgb, reflectionWeight);
        if (color.a <= 0.001) discard;
        out_color = color;
        return;
    }
    vec3 lighting = u_lightColor * diffuse * shadowFactor;
    if(u_hasToon) {
        float coordinate=clamp(dot(normal,normalize(-u_lightDirection))*.5+.5,0.0,1.0)*shadowFactor;
        vec3 toon=texture(u_toonTexture,vec2(0,coordinate)).rgb;
        toon=mix(vec3(1),toon*u_toonMultiply.rgb,u_toonMultiply.a);
        toon=clamp(toon+(toon-1.0)*u_toonAdd.a,0.0,1.0)+u_toonAdd.rgb;
        lighting*=toon;
    }
    vec3 eyeDirection = normalize(u_cameraPosition - v_worldPosition);
    vec3 specular = vec3(0.0);
    if (u_specularPower > 0.0 && diffuse * shadowFactor > 0.0) {
        vec3 halfDirection = normalize(eyeDirection + normalize(-u_lightDirection));
        specular = pow(max(dot(normal, halfDirection), 0.0), u_specularPower) * u_specular * u_lightColor;
    }
    for (int i = 0; i < 8; i++) {
        if (i >= u_pointLightCount) break;
        vec3 toLight = u_pointLightPositionRange[i].xyz - v_worldPosition;
        float distanceToLight = length(toLight);
        if (distanceToLight <= 0.0001 || distanceToLight >= u_pointLightPositionRange[i].w) continue;
        vec3 lightDirection = toLight / distanceToLight;
        float attenuation = 1.0 - distanceToLight / u_pointLightPositionRange[i].w;
        attenuation *= attenuation;
        float ndotl = max(dot(normal, lightDirection), 0.0);
        int baseTile = u_pointShadowBase[i];
        float visibility = localShadow(baseTile < 0 ? -1 : baseTile + pointShadowFace(-toLight), normal, lightDirection, ndotl);
        lighting += u_pointLightColorIntensity[i].rgb
            * u_pointLightColorIntensity[i].a
            * ndotl * visibility
            * attenuation;
        if(u_specularPower>0.0 && ndotl>0.0)specular+=pow(max(dot(normal,normalize(eyeDirection+lightDirection)),0.0),u_specularPower)
            *u_specular*u_pointLightColorIntensity[i].rgb*u_pointLightColorIntensity[i].a*attenuation*visibility;
    }
    for (int i = 0; i < 4; i++) {
        if (i >= u_spotLightCount) break;
        vec3 toLight = u_spotLightPositionRange[i].xyz - v_worldPosition;
        float distanceToLight = length(toLight);
        if (distanceToLight <= 0.0001 || distanceToLight >= u_spotLightPositionRange[i].w) continue;
        vec3 lightDirection = toLight / distanceToLight;
        float cone = dot(normalize(u_spotLightDirectionOuterCosine[i].xyz), -lightDirection);
        float coneFalloff = smoothstep(
            u_spotLightDirectionOuterCosine[i].w,
            u_spotLightConeParameters[i].x,
            cone);
        float attenuation = 1.0 - distanceToLight / u_spotLightPositionRange[i].w;
        attenuation *= attenuation * coneFalloff;
        float ndotl = max(dot(normal, lightDirection), 0.0);
        float visibility = localShadow(u_spotShadowTile[i], normal, lightDirection, ndotl);
        lighting += u_spotLightColorIntensity[i].rgb
            * u_spotLightColorIntensity[i].a
            * ndotl * visibility
            * attenuation;
        if(u_specularPower>0.0 && ndotl>0.0)specular+=pow(max(dot(normal,normalize(eyeDirection+lightDirection)),0.0),u_specularPower)
            *u_specular*u_spotLightColorIntensity[i].rgb*u_spotLightColorIntensity[i].a*attenuation*visibility;
    }
    vec3 albedo = texel.rgb * u_diffuse.rgb;
    vec3 ambient = albedo * u_materialAmbient * u_ambientColor * u_ambientStrength;
    vec3 baseColor=albedo;
    if(u_sphereMode>0) {
        vec3 viewNormal=normalize(mat3(u_view)*normal);
        vec3 sphere=texture(u_sphereTexture,vec2(viewNormal.x*.5+.5,1.0-(viewNormal.y*.5+.5))).rgb;
        sphere=mix(vec3(1),sphere*u_sphereMultiply.rgb,u_sphereMultiply.a);
        sphere=clamp(sphere+(sphere-1.0)*u_sphereAdd.a,0.0,1.0)+u_sphereAdd.rgb;
        if(u_sphereMode==1)baseColor*=sphere;else if(u_sphereMode==2)baseColor+=sphere;
    }
    vec4 color = vec4(clamp(baseColor * lighting + ambient + specular, 0.0, 1.0), texel.a * u_diffuse.a);
    if (color.a < 0.01) discard;
    out_color = color;
}`;

const SHADOW_VERTEX_SHADER_SOURCE = `#version 300 es
// Both passes share a mesh VAO. Keep UV separate from the normal slot.
layout(location = 0) in vec3 a_position;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
${skinningShaderSource()}
uniform mat4 u_lightViewProjection;
uniform mat4 u_model;
out vec2 v_uv;
void main() { vec3 position, normal; skinVertex(position,normal); v_uv = a_uv; gl_Position = u_lightViewProjection * u_model * vec4(position, 1.0); }`;

const SHADOW_FRAGMENT_SHADER_SOURCE = `#version 300 es
precision highp float;
in vec2 v_uv;
uniform sampler2D u_texture;
uniform bool u_hasTexture;
uniform float u_alpha;
void main() {
    float alpha = u_alpha * (u_hasTexture ? texture(u_texture, v_uv).a : 1.0);
    if (alpha < 0.01) discard;
}`;

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
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, SHADOW_FRAGMENT_SHADER_SOURCE, "WebGL2 shadow fragment shader");
    const program = gl.createProgram();
    if (!program) throw new Error("WebGL2 could not allocate the shadow program.");
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    gl.deleteShader(vertex);
    gl.deleteShader(fragment);
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

function createProgram(gl, rippleCapacity = 48) {
    const vertex = compileShader(gl, gl.VERTEX_SHADER, VERTEX_SHADER_SOURCE, "WebGL2 PMX vertex shader");
    const fragmentSource = FRAGMENT_SHADER_SOURCE.replaceAll('u_rippleCenters[48]',`u_rippleCenters[${rippleCapacity}]`)
        .replaceAll('u_rippleSizes[48]',`u_rippleSizes[${rippleCapacity}]`).replace('for(int i=0;i<48;i++)',`for(int i=0;i<${rippleCapacity};i++)`);
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource, "WebGL2 PMX fragment shader");
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

function createScenePresenter(gl) {
    const vertex = compileShader(gl, gl.VERTEX_SHADER, `#version 300 es
precision highp float; out vec2 uv;
void main(){ vec2 p=gl_VertexID==0?vec2(0,0):gl_VertexID==1?vec2(2,0):vec2(0,2); uv=p; gl_Position=vec4(p*2.0-1.0,0,1); }`, "Scene presenter vertex");
    const fragment = compileShader(gl, gl.FRAGMENT_SHADER, `#version 300 es
precision highp float; in vec2 uv; uniform sampler2D scene; out vec4 color;
void main(){ color=texture(scene,uv); }`, "Scene presenter fragment");
    const program = gl.createProgram();
    gl.attachShader(program,vertex); gl.attachShader(program,fragment); gl.linkProgram(program);
    gl.deleteShader(vertex); gl.deleteShader(fragment);
    if(!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
    return { program, vao: gl.createVertexArray(), texture: gl.getUniformLocation(program,"scene") };
}

function resolveColorTarget(gl,target, depth = false) {
    if(target.sampleCount<=1) return;
    gl.disable(gl.SCISSOR_TEST); gl.bindFramebuffer(gl.READ_FRAMEBUFFER,target.drawFramebuffer);
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER,target.framebuffer);
    gl.blitFramebuffer(0,0,target.width,target.height,0,0,target.width,target.height,gl.COLOR_BUFFER_BIT,gl.NEAREST);
    if(depth) gl.blitFramebuffer(0,0,target.width,target.height,0,0,target.width,target.height,gl.DEPTH_BUFFER_BIT,gl.NEAREST);
}

function createTextureSampler(gl, clamp = false) {
    const sampler = gl.createSampler();
    if (!sampler) throw new Error("WebGL2 could not allocate the texture sampler.");
    gl.samplerParameteri(sampler, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.samplerParameteri(sampler, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    // PMX UVs can tile beyond [0, 1]. Match the desktop material sampler;
    // clamping stretches the classroom atlas edges over floors and walls.
    gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_S, clamp ? gl.CLAMP_TO_EDGE : gl.REPEAT);
    gl.samplerParameteri(sampler, gl.TEXTURE_WRAP_T, clamp ? gl.CLAMP_TO_EDGE : gl.REPEAT);
    return sampler;
}

function readDdsFourCc(view) {
    const value = String.fromCharCode(
        view.getUint8(84),
        view.getUint8(85),
        view.getUint8(86),
        view.getUint8(87));
    return value.split(String.fromCharCode(0)).join("");
}

function decodeRgb565(value) {
    return [
        Math.round(((value >> 11) & 0x1f) * 255 / 31),
        Math.round(((value >> 5) & 0x3f) * 255 / 63),
        Math.round((value & 0x1f) * 255 / 31),
        255
    ];
}

function decodeDdsChannel(pixel, mask, fallback) {
    const unsignedMask = mask >>> 0;
    if (!unsignedMask) return fallback;
    let shift = 0;
    while (shift < 32 && ((unsignedMask >>> shift) & 1) === 0) shift++;
    let bits = 0;
    while (shift + bits < 32 && ((unsignedMask >>> (shift + bits)) & 1) !== 0) bits++;
    if (bits <= 0 || bits > 16) return fallback;
    const value = (pixel >>> shift) & ((1 << bits) - 1);
    return Math.round(value * 255 / ((1 << bits) - 1));
}

function writeDdsPixel(output, width, height, x, y, color) {
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    // DDS rows are conventionally top-to-bottom. Match the image-source path,
    // which uploads with UNPACK_FLIP_Y_WEBGL enabled.
    const flippedY = height - 1 - y;
    const offset = (flippedY * width + x) * 4;
    output[offset] = color[0];
    output[offset + 1] = color[1];
    output[offset + 2] = color[2];
    output[offset + 3] = color[3];
}

function decodeDdsBc1ColorBlock(bytes, offset, output, width, height, blockX, blockY, forceFourColor) {
    const color0 = bytes[offset] | (bytes[offset + 1] << 8);
    const color1 = bytes[offset + 2] | (bytes[offset + 3] << 8);
    const colors = [decodeRgb565(color0), decodeRgb565(color1), [0, 0, 0, 255], [0, 0, 0, 255]];
    if (color0 > color1 || forceFourColor) {
        colors[2] = [
            Math.round((2 * colors[0][0] + colors[1][0]) / 3),
            Math.round((2 * colors[0][1] + colors[1][1]) / 3),
            Math.round((2 * colors[0][2] + colors[1][2]) / 3),
            255
        ];
        colors[3] = [
            Math.round((colors[0][0] + 2 * colors[1][0]) / 3),
            Math.round((colors[0][1] + 2 * colors[1][1]) / 3),
            Math.round((colors[0][2] + 2 * colors[1][2]) / 3),
            255
        ];
    } else {
        colors[2] = [
            Math.round((colors[0][0] + colors[1][0]) / 2),
            Math.round((colors[0][1] + colors[1][1]) / 2),
            Math.round((colors[0][2] + colors[1][2]) / 2),
            255
        ];
        colors[3] = [0, 0, 0, 0];
    }

    const indices = (bytes[offset + 4]
        | (bytes[offset + 5] << 8)
        | (bytes[offset + 6] << 16)
        | (bytes[offset + 7] << 24)) >>> 0;
    for (let y = 0; y < 4; y++) {
        for (let x = 0; x < 4; x++) {
            const index = (indices >>> (2 * (y * 4 + x))) & 3;
            writeDdsPixel(output, width, height, blockX * 4 + x, blockY * 4 + y, colors[index]);
        }
    }
}

function decodeDdsBc2Block(bytes, offset, output, width, height, blockX, blockY) {
    decodeDdsBc1ColorBlock(bytes, offset + 8, output, width, height, blockX, blockY, true);
    for (let y = 0; y < 4; y++) {
        const alphaBits = bytes[offset + y * 2] | (bytes[offset + y * 2 + 1] << 8);
        for (let x = 0; x < 4; x++) {
            const pixelX = blockX * 4 + x;
            const pixelY = blockY * 4 + y;
            if (pixelX >= width || pixelY >= height) continue;
            const targetY = height - 1 - pixelY;
            const target = (targetY * width + pixelX) * 4 + 3;
            output[target] = ((alphaBits >>> (x * 4)) & 0xf) * 17;
        }
    }
}

function decodeDdsBc3Block(bytes, offset, output, width, height, blockX, blockY) {
    const alpha0 = bytes[offset];
    const alpha1 = bytes[offset + 1];
    const alphaPalette = [alpha0, alpha1];
    if (alpha0 > alpha1) {
        alphaPalette.push(
            Math.round((6 * alpha0 + alpha1) / 7),
            Math.round((5 * alpha0 + 2 * alpha1) / 7),
            Math.round((4 * alpha0 + 3 * alpha1) / 7),
            Math.round((3 * alpha0 + 4 * alpha1) / 7),
            Math.round((2 * alpha0 + 5 * alpha1) / 7),
            Math.round((alpha0 + 6 * alpha1) / 7));
    } else {
        alphaPalette.push(
            Math.round((4 * alpha0 + alpha1) / 5),
            Math.round((3 * alpha0 + 2 * alpha1) / 5),
            Math.round((2 * alpha0 + 3 * alpha1) / 5),
            Math.round((alpha0 + 4 * alpha1) / 5),
            0,
            255);
    }
    decodeDdsBc1ColorBlock(bytes, offset + 8, output, width, height, blockX, blockY, true);
    let alphaBits = 0n;
    for (let i = 0; i < 6; i++) alphaBits |= BigInt(bytes[offset + 2 + i]) << BigInt(i * 8);
    for (let y = 0; y < 4; y++) {
        for (let x = 0; x < 4; x++) {
            const pixelX = blockX * 4 + x;
            const pixelY = blockY * 4 + y;
            if (pixelX >= width || pixelY >= height) continue;
            const index = Number((alphaBits >> BigInt(3 * (y * 4 + x))) & 7n);
            const targetY = height - 1 - pixelY;
            output[(targetY * width + pixelX) * 4 + 3] = alphaPalette[index];
        }
    }
}

export function decodeDds(arrayBuffer, { firstCubeFace = false } = {}) {
    const bytes = new Uint8Array(arrayBuffer);
    if (bytes.length < 128 || String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]) !== "DDS ") {
        throw new Error("Invalid DDS header.");
    }
    const view = new DataView(arrayBuffer);
    if (view.getUint32(4, true) !== 124 || view.getUint32(76, true) !== 32) {
        throw new Error("Invalid DDS header size.");
    }
    const width = view.getUint32(16, true);
    const height = view.getUint32(12, true);
    if (!width || !height) throw new Error("Invalid DDS texture dimensions.");
    if ((view.getUint32(112, true) & 0x200000) || (!firstCubeFace && (view.getUint32(112, true) & 0x200))) {
        throw new Error("DDS cube and volume textures are not supported as 2D material textures.");
    }
    const headerFlags = view.getUint32(8, true);
    const pitchOrLinearSize = view.getUint32(20, true);
    const pixelFlags = view.getUint32(80, true);
    const fourCc = readDdsFourCc(view);
    let rgbBits = view.getUint32(88, true);
    let redMask = view.getUint32(92, true);
    let greenMask = view.getUint32(96, true);
    let blueMask = view.getUint32(100, true);
    let alphaMask = view.getUint32(104, true);
    let dataOffset = 128;
    let format = fourCc;
    if (fourCc === "DX10") {
        if (bytes.length < 148) throw new Error("DDS DX10 header is truncated.");
        const dxgiFormat = view.getUint32(128, true);
        if (view.getUint32(132, true) !== 3 || view.getUint32(140, true) !== 1 || (view.getUint32(136, true) & 4)) {
            throw new Error("Only single DDS DX10 2D textures are supported.");
        }
        dataOffset = 148;
        format = {
            28: "RGBA8", 29: "RGBA8",
            87: "BGRA8", 91: "BGRA8",
            88: "BGRX8", 93: "BGRX8",
            71: "DXT1", 72: "DXT1",
            74: "DXT3", 75: "DXT3",
            77: "DXT5", 78: "DXT5"
        }[dxgiFormat];
        if (!format) throw new Error(`Unsupported DDS DXGI format: ${dxgiFormat}.`);
        // DX10 stores the format in its extended header, not in the legacy
        // bit-count and channel masks (which are normally all zero).
        if (format === "RGBA8" || format === "BGRA8" || format === "BGRX8") {
            rgbBits = 32;
            redMask = format === "RGBA8" ? 0x000000ff : 0x00ff0000;
            greenMask = 0x0000ff00;
            blueMask = format === "RGBA8" ? 0x00ff0000 : 0x000000ff;
            alphaMask = format === "BGRX8" ? 0 : 0xff000000;
            format = "RGB";
        }
    }
    if (format === "") {
        format = (pixelFlags & 0x40) !== 0 ? "RGB" : "";
    }

    const uncompressed = format === "RGB";
    const bytesPerPixel = rgbBits / 8;
    const rowSize = width * bytesPerPixel;
    const rowPitch = (headerFlags & 0x8) !== 0 ? pitchOrLinearSize : rowSize;
    const blockWidth = Math.ceil(width / 4);
    const blockHeight = Math.ceil(height / 4);
    const blockBytes = format === "DXT1" ? 8 : format === "DXT3" || format === "DXT5" ? 16 : 0;
    if (uncompressed) {
        if (![16, 24, 32].includes(rgbBits)) throw new Error(`Unsupported DDS RGB bit count: ${rgbBits}.`);
        if (rowPitch < rowSize) throw new Error("Invalid DDS row pitch.");
    } else if (!blockBytes) {
        throw new Error(`Unsupported DDS format: ${fourCc || "unknown"}.`);
    }
    const requiredBytes = uncompressed
        ? (height - 1) * rowPitch + rowSize
        : blockWidth * blockHeight * blockBytes;
    if (dataOffset + requiredBytes > bytes.length) throw new Error("DDS texture data is truncated.");
    const output = new Uint8Array(width * height * 4);

    if (uncompressed) {
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                const offset = dataOffset + y * rowPitch + x * bytesPerPixel;
                let pixel = 0;
                for (let byte = 0; byte < Math.min(bytesPerPixel, 4); byte++) pixel |= bytes[offset + byte] << (byte * 8);
                const color = [
                    decodeDdsChannel(pixel, redMask, bytes[offset + 2] ?? 0),
                    decodeDdsChannel(pixel, greenMask, bytes[offset + 1] ?? 0),
                    decodeDdsChannel(pixel, blueMask, bytes[offset] ?? 0),
                    alphaMask ? decodeDdsChannel(pixel, alphaMask, 255) : 255
                ];
                writeDdsPixel(output, width, height, x, y, color);
            }
        }
    } else {
        for (let blockY = 0; blockY < blockHeight; blockY++) {
            for (let blockX = 0; blockX < blockWidth; blockX++) {
                const offset = dataOffset + (blockY * blockWidth + blockX) * blockBytes;
                if (offset + blockBytes > bytes.length) throw new Error("DDS texture data is truncated.");
                if (format === "DXT1") decodeDdsBc1ColorBlock(bytes, offset, output, width, height, blockX, blockY, false);
                else if (format === "DXT3") decodeDdsBc2Block(bytes, offset, output, width, height, blockX, blockY);
                else decodeDdsBc3Block(bytes, offset, output, width, height, blockX, blockY);
            }
        }
    }
    return { width, height, data: output };
}

async function loadTexture(gl, uri, reportError) {
    if (uri?.startsWith('rt:')) return null;
    const version=new URL(import.meta.url).searchParams.get('v');
    if(version && /^\.?\/?engine\//.test(uri))uri+=(uri.includes('?')?'&':'?')+'v='+encodeURIComponent(version);
    if (!uri) return null;
    let texture = null;
    let bitmap = null;
    try {
        const response = await fetch(uri, { cache: "force-cache" });
        if (!response.ok) throw new Error(`HTTP ${response.status} while loading ${uri}`);
        const blob = await response.blob();
        const signature = new Uint8Array(await blob.slice(0, 4).arrayBuffer());
        const isDds = signature[0] === 0x44 && signature[1] === 0x44 && signature[2] === 0x53 && signature[3] === 0x20;
        const dds = isDds ? decodeDds(await blob.arrayBuffer(), { firstCubeFace: /\/Water\/Sky\.dds(?:$|[?#])/i.test(uri) }) : null;
        if (!dds) {
            // ImageBitmap ignores UNPACK_FLIP_Y_WEBGL. Bake the flip into the
            // bitmap to match the flipped PMX UVs and the DDS upload path.
            bitmap = await createImageBitmap(blob, { imageOrientation: "flipY", premultiplyAlpha: "none" });
        }
        // Decode first. No await may separate binding a texture from uploading
        // it: other loads and render frames share this WebGL context.
        texture = gl.createTexture();
        if (!texture) throw new Error(`Could not allocate texture for ${uri}`);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 0);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
        if (dds) {
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, dds.width, dds.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, dds.data);
        } else {
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, bitmap);
        }
        gl.generateMipmap(gl.TEXTURE_2D);
        texture.width = dds?.width ?? bitmap.width;
        texture.height = dds?.height ?? bitmap.height;
        gl.bindTexture(gl.TEXTURE_2D, null);
        return texture;
    }
    catch (error) {
        if (texture) gl.deleteTexture(texture);
        reportError?.(`Texture load failed for ${uri}: ${error?.message || String(error)}`, false);
        return null;
    }
    finally {
        bitmap?.close();
    }
}

function createMeshResources(gl, payload, locations, reportError) {
    const positions = new Float32Array(toEntryArray(payload.positions ?? payload.Positions));
    const normals = new Float32Array(toEntryArray(payload.normals ?? payload.Normals));
    const uvs = new Float32Array(toEntryArray(payload.uvs ?? payload.Uvs));
    const indices = new Uint32Array(toEntryArray(payload.indices ?? payload.Indices));
    const vertexCount = positions.length / 3;
    const interleaved = new Float32Array(vertexCount * 8);
    for (let i = 0; i < vertexCount; i++) {
        const p = i * 3;
        const v = i * 8;
        interleaved[v] = positions[p];
        interleaved[v + 1] = positions[p + 1];
        interleaved[v + 2] = positions[p + 2];
        interleaved[v + 3] = normals[p] ?? 0;
        interleaved[v + 4] = normals[p + 1] ?? 1;
        interleaved[v + 5] = normals[p + 2] ?? 0;
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

    const mesh = { vao, vertexBuffer, indexBuffer, indexCount: indices.length, vertexCount, groups: [], interleaved, textures: new Set(), disposed: false };
    const skinning = payload.skinning ?? payload.Skinning;
    if (skinning) {
        const bindings = floatBytes(skinning.bindings ?? skinning.Bindings);
        const boneCount = Number(skinning.boneCount ?? skinning.BoneCount);
        const bones = floatBytes(skinning.bones ?? skinning.Bones);
        if (bindings.length !== vertexCount * 8 || bones.length !== boneCount * 16) throw new Error("Invalid PMX GPU skinning data.");
        const bindingBuffer = gl.createBuffer();
        gl.bindVertexArray(vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, bindingBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, bindings, gl.STATIC_DRAW);
        gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 4, gl.FLOAT, false, 32, 0);
        gl.enableVertexAttribArray(4); gl.vertexAttribPointer(4, 4, gl.FLOAT, false, 32, 16);
        gl.bindVertexArray(null);
        gl.bindBuffer(gl.ARRAY_BUFFER, null);
        const morphWidth = 256, morphHeight = Math.max(Math.ceil(vertexCount / morphWidth), 1);
        const morphData = new Float32Array(morphWidth * morphHeight * 4);
        mesh.skinning = { bindingBuffer, boneCount, morphWidth, morphData,
            boneTexture: createFloatTexture(gl, 4, boneCount, bones),
            morphTexture: createFloatTexture(gl, morphWidth, morphHeight, morphData) };
        const morphs = skinning.morphs ?? skinning.Morphs;
        if (morphs) updateMorphTexture(gl, mesh, morphs);
    }
    const texturePromises = new Map();
    mesh.groups = toEntryArray(payload.materials ?? payload.Materials).map(material => {
        const group = { ...material, texture: null };
        const uri = material.textureUri ?? material.TextureUri;
        if (!texturePromises.has(uri)) {
            texturePromises.set(uri, loadTexture(gl, uri, reportError).then(texture => {
                if (texture && mesh.disposed) { gl.deleteTexture(texture); return null; }
                if (texture) mesh.textures.add(texture);
                return texture;
            }));
        }
        group.texturePromise = texturePromises.get(uri).then(texture => {
            group.texture = texture;
            return texture;
        });
        const pending=[group.texturePromise];
        for(const [name,textureUri] of [["toonTexture",material.toonTextureUri],["sphereTexture",material.sphereTextureUri]]) {
            if(!textureUri)continue;
            if(!texturePromises.has(textureUri))texturePromises.set(textureUri,loadTexture(gl,textureUri,reportError).then(texture=>{
                if(texture && mesh.disposed){gl.deleteTexture(texture);return null;}
                if(texture)mesh.textures.add(texture);return texture;
            }));
            pending.push(texturePromises.get(textureUri).then(texture=>group[name]=texture));
        }
        group.texturePromise=Promise.all(pending);
        return group;
    });
    return mesh;
}

function floatBytes(value) {
    const bytes = value instanceof Uint8Array ? value : new Uint8Array(toEntryArray(value));
    if (bytes.byteLength % 4 !== 0) throw new Error("Invalid PMX float buffer length.");
    const aligned = bytes.byteOffset % 4 === 0 ? bytes : bytes.slice();
    return new Float32Array(aligned.buffer, aligned.byteOffset, aligned.byteLength / 4);
}

function createFloatTexture(gl, width, height, data) {
    const texture = gl.createTexture();
    if (!texture) throw new Error("WebGL2 could not allocate a skinning texture.");
    gl.activeTexture(gl.TEXTURE2);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, height, 0, gl.RGBA, gl.FLOAT, data);
    gl.bindTexture(gl.TEXTURE_2D, null);
    gl.activeTexture(gl.TEXTURE0);
    return texture;
}

function updateMorphTexture(gl, mesh, bytes) {
    const skin = mesh.skinning, changes = floatBytes(bytes), rows = new Set();
    if (changes.length % 4 !== 0) throw new Error("Invalid PMX morph update length.");
    for (let i = 0; i < changes.length; i += 4) {
        const vertex = changes[i];
        if (!Number.isInteger(vertex) || vertex < 0 || vertex >= mesh.vertexCount) throw new Error("Invalid PMX morph vertex index.");
        const offset = vertex * 4;
        skin.morphData[offset] = changes[i+1]; skin.morphData[offset+1] = changes[i+2]; skin.morphData[offset+2] = changes[i+3];
        rows.add(Math.floor(vertex / skin.morphWidth));
    }
    gl.activeTexture(gl.TEXTURE3);
    gl.bindTexture(gl.TEXTURE_2D, skin.morphTexture);
    for (const row of rows) {
        const start = row * skin.morphWidth * 4;
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, row, skin.morphWidth, 1, gl.RGBA, gl.FLOAT, skin.morphData.subarray(start,start + skin.morphWidth * 4));
    }
    gl.bindTexture(gl.TEXTURE_2D, null);
    gl.activeTexture(gl.TEXTURE0);
}

function skinningLocations(gl, program) {
    return {
        gpuSkinning: gl.getUniformLocation(program,"u_gpuSkinning"),
        bones: gl.getUniformLocation(program,"u_bones"), morphs: gl.getUniformLocation(program,"u_morphs"),
        boneCount: gl.getUniformLocation(program,"u_boneCount"), morphWidth: gl.getUniformLocation(program,"u_morphWidth")
    };
}

function bindSkinning(gl, locations, mesh, defaultTexture) {
    const skin = mesh.skinning;
    gl.uniform1i(locations.gpuSkinning, skin ? 1 : 0);
    gl.uniform1i(locations.boneCount, skin?.boneCount ?? 0);
    gl.uniform1i(locations.morphWidth, skin?.morphWidth ?? 256);
    gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, skin?.boneTexture ?? defaultTexture);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, skin?.morphTexture ?? defaultTexture);
    gl.uniform1i(locations.bones, 2); gl.uniform1i(locations.morphs, 3);
    gl.activeTexture(gl.TEXTURE0);
}

function updateMeshResources(gl, mesh, payload) {
    const boneBytes = payload.bones ?? payload.Bones;
    if (boneBytes) {
        if (!mesh.skinning) throw new Error("A CPU mesh received a GPU skinning update.");
        const bones = floatBytes(boneBytes);
        if (bones.length !== mesh.skinning.boneCount * 16) throw new Error("Invalid PMX bone update length.");
        gl.activeTexture(gl.TEXTURE2);
        gl.bindTexture(gl.TEXTURE_2D, mesh.skinning.boneTexture);
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 4, mesh.skinning.boneCount, gl.RGBA, gl.FLOAT, bones);
        gl.bindTexture(gl.TEXTURE_2D, null);
        const morphs = payload.morphs ?? payload.Morphs;
        if (morphs) updateMorphTexture(gl, mesh, morphs);
        gl.activeTexture(gl.TEXTURE0);
        return;
    }
    const vertexBytes = payload.vertices ?? payload.Vertices;
    if (vertexBytes) {
        const bytes = vertexBytes instanceof Uint8Array ? vertexBytes : new Uint8Array(toEntryArray(vertexBytes));
        if (bytes.byteLength !== mesh.vertexCount * 32) throw new Error("The PMX vertex update has an invalid byte length.");
        const aligned = bytes.byteOffset % 4 === 0 ? bytes : bytes.slice();
        mesh.interleaved = new Float32Array(aligned.buffer, aligned.byteOffset, aligned.byteLength / 4);
        gl.bindBuffer(gl.ARRAY_BUFFER, mesh.vertexBuffer);
        gl.bufferSubData(gl.ARRAY_BUFFER, 0, aligned);
        gl.bindBuffer(gl.ARRAY_BUFFER, null);
        return;
    }
    const positions = new Float32Array(toEntryArray(payload.positions ?? payload.Positions));
    const normals = new Float32Array(toEntryArray(payload.normals ?? payload.Normals));
    const uvs = new Float32Array(toEntryArray(payload.uvs ?? payload.Uvs));
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
    mesh.disposed = true;
    gl.deleteVertexArray(mesh.vao);
    gl.deleteBuffer(mesh.vertexBuffer);
    gl.deleteBuffer(mesh.indexBuffer);
    if (mesh.skinning) {
        gl.deleteBuffer(mesh.skinning.bindingBuffer);
        gl.deleteTexture(mesh.skinning.boneTexture);
        gl.deleteTexture(mesh.skinning.morphTexture);
    }
    for (const texture of mesh.textures) gl.deleteTexture(texture);
    mesh.textures.clear();
}

function toEntryArray(value) {
    if (Array.isArray(value)) return value;
    if (value && Array.isArray(value.$values)) return value.$values;
    if (value && typeof value[Symbol.iterator] === "function") return Array.from(value);
    if (value && typeof value === "object") {
        if ("mesh" in value || "Mesh" in value) return [value];
        const keys = Object.keys(value);
        if (keys.every(key => /^\d+$/.test(key))) {
            return keys.sort((left, right) => Number(left) - Number(right)).map(key => value[key]);
        }
        return Object.values(value);
    }
    return [];
}

// .NET's System.Numerics.Vector3/Vector4 values cross the Blazor JS boundary
// as objects ({ x, y, z[, w] }) while float[] values arrive as arrays. WebGL
// uniform methods only accept an iterable sequence, so normalize both forms
// before passing data to the graphics API.
function toNumericVector(value, length, fallback) {
    const defaults = Array.isArray(fallback) ? fallback : [];
    let values = null;
    if (Array.isArray(value) || ArrayBuffer.isView(value)) {
        values = Array.from(value);
    } else if (value && Array.isArray(value.$values)) {
        values = value.$values;
    } else if (value && typeof value === "object") {
        const names = length === 4 ? ["x", "y", "z", "w"] : ["x", "y", "z"];
        const upperNames = length === 4 ? ["X", "Y", "Z", "W"] : ["X", "Y", "Z"];
        if (names.some(name => name in value) || upperNames.some(name => name in value)) {
            values = names.map((name, index) => value[name] ?? value[upperNames[index]]);
        }
    }
    const normalized = new Float32Array(length);
    for (let i = 0; i < length; i++) {
        const numeric = Number(values?.[i]);
        normalized[i] = Number.isFinite(numeric)
            ? numeric
            : (Number.isFinite(Number(defaults[i])) ? Number(defaults[i]) : 0);
    }
    return normalized;
}

const sampleChoices = [1, 2, 4, 8, 16];

export function resolveShadowResolution(requested, maximum = 8192) {
    return [256,512,1024,2048,4096,8192].findLast(value => value <= (Number(requested) || 1024) && value <= maximum) || 256;
}

export function resolveShadowFilterSamples(requested) {
    if (!(Number(requested) > 0)) return 0; // Preserve the legacy per-light filters.
    return [1,4,9,16,25,49].findLast(value => value <= Number(requested)) || 1;
}

export function resolveReflectionSamples(requested, sceneSamples, colorSamples, depthSamples) {
    const normalized = sampleChoices.find(value => value >= (Number(requested) || 1)) || 16;
    return sampleChoices.findLast(value => value <= normalized && value <= Math.max(1, Number(sceneSamples) || 1)
        && (value === 1 || (colorSamples.includes(value) && depthSamples.includes(value)))) || 1;
}

function createRenderTarget(gl, width, height, depthOnly, samples = 1, captureDepth = false) {
    const texture = gl.createTexture(), framebuffer = gl.createFramebuffer();
    const depth = depthOnly ? null : gl.createRenderbuffer();
    if (!texture || !framebuffer || (!depthOnly && !depth)) throw new Error("WebGL2 could not allocate a scene render target.");
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, depthOnly ? gl.DEPTH_COMPONENT24 : gl.RGBA8, width, height,
        0, depthOnly ? gl.DEPTH_COMPONENT : gl.RGBA, depthOnly ? gl.UNSIGNED_INT : gl.UNSIGNED_BYTE, null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, depthOnly ? gl.DEPTH_ATTACHMENT : gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
    if (depthOnly) {
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_MODE, gl.COMPARE_REF_TO_TEXTURE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_COMPARE_FUNC, gl.LEQUAL);
        gl.drawBuffers([gl.NONE]);
        gl.readBuffer(gl.NONE);
    } else {
        gl.bindRenderbuffer(gl.RENDERBUFFER, depth);
        gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
        gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
        gl.bindRenderbuffer(gl.RENDERBUFFER, null);
    }
    const target = { texture, framebuffer, depth, width, height, sampleCount: 1, requestedSamples: samples };
    if(captureDepth && !depthOnly) {
        target.depthTexture=gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D,target.depthTexture);
        gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
        gl.texImage2D(gl.TEXTURE_2D,0,gl.DEPTH_COMPONENT24,width,height,0,gl.DEPTH_COMPONENT,gl.UNSIGNED_INT,null);
        gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.DEPTH_ATTACHMENT,gl.TEXTURE_2D,target.depthTexture,0);
    }
    if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
        deleteRenderTarget(gl, target);
        throw new Error("WebGL2 scene framebuffer is incomplete.");
    }
    if (!depthOnly && samples > 1) {
        const colors = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER, gl.RGBA8, gl.SAMPLES) || []);
        const depths = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, gl.SAMPLES) || []);
        for (const candidate of sampleChoices.slice().reverse()) {
            if (candidate <= 1 || candidate > samples || !colors.includes(candidate) || !depths.includes(candidate)) continue;
            const drawFramebuffer = gl.createFramebuffer(), color = gl.createRenderbuffer(), multisampleDepth = gl.createRenderbuffer();
            if (drawFramebuffer && color && multisampleDepth) {
                gl.bindFramebuffer(gl.FRAMEBUFFER, drawFramebuffer);
                gl.bindRenderbuffer(gl.RENDERBUFFER, color);
                gl.renderbufferStorageMultisample(gl.RENDERBUFFER, candidate, gl.RGBA8, width, height);
                gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, color);
                gl.bindRenderbuffer(gl.RENDERBUFFER, multisampleDepth);
                gl.renderbufferStorageMultisample(gl.RENDERBUFFER, candidate, gl.DEPTH_COMPONENT24, width, height);
                gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, multisampleDepth);
                if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE) {
                    Object.assign(target, { drawFramebuffer, multisampleColor: color, multisampleDepth, sampleCount: candidate });
                    break;
                }
            }
            if (drawFramebuffer) gl.deleteFramebuffer(drawFramebuffer);
            if (color) gl.deleteRenderbuffer(color);
            if (multisampleDepth) gl.deleteRenderbuffer(multisampleDepth);
        }
        gl.bindRenderbuffer(gl.RENDERBUFFER, null);
    }
    gl.bindTexture(gl.TEXTURE_2D, null);
    return target;
}

function deleteRenderTarget(gl, target) {
    gl.deleteTexture(target.texture);
    gl.deleteFramebuffer(target.framebuffer);
    if (target.depth) gl.deleteRenderbuffer(target.depth);
    if (target.depthTexture) gl.deleteTexture(target.depthTexture);
    if (target.drawFramebuffer) gl.deleteFramebuffer(target.drawFramebuffer);
    if (target.multisampleColor) gl.deleteRenderbuffer(target.multisampleColor);
    if (target.multisampleDepth) gl.deleteRenderbuffer(target.multisampleDepth);
}

export function createWebGl2Renderer(canvas, reportError) {
    const gl = canvas.getContext("webgl2", { alpha: true, premultipliedAlpha: true, antialias: true, depth: true });
    if (!gl) throw new Error("This browser does not provide WebGL2.");
    const sceneSamples = Math.max(1, Number(gl.getParameter(gl.SAMPLES)) || 1);
    let actualSceneSamples = sceneSamples;
    const reflectionColorSamples = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER, gl.RGBA8, gl.SAMPLES) || []);
    const reflectionDepthSamples = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, gl.SAMPLES) || []);
    const uniformBudget = Number(gl.getParameter(gl.MAX_FRAGMENT_UNIFORM_VECTORS)) || 1024;
    const rippleCapacity = uniformBudget < 256 ? 8 : uniformBudget < 384 ? 16 : 48;
    const program = createProgram(gl, rippleCapacity);
    const shadowProgram = createShadowProgram(gl);
    const particleProgram = createParticleProgram(gl);
    const defaultTexture = createDefaultTexture(gl);
    const textures = new Map([["default", defaultTexture]]), textureLoads = new Map();
    const effects = createSceneEffects(gl, compileShader, textures);
    let underwaterPresenter = null, backgroundSprites = [];
    const ensureTexture = uri => {
        if(!uri || uri.startsWith('rt:') || textures.has(uri))return Promise.resolve();
        if(!textureLoads.has(uri))textureLoads.set(uri,loadTexture(gl,uri,reportError).then(texture=>{
            if(texture) { if(disposed)gl.deleteTexture(texture);else textures.set(uri,texture); }
        }));
        return textureLoads.get(uri);
    };
    const maxShadowSize = Math.max(256, Math.min(Number(gl.getParameter(gl.MAX_TEXTURE_SIZE)) || 8192,
        Number(gl.getParameter(gl.MAX_RENDERBUFFER_SIZE)) || 8192));
    const createShadowTarget = requested => {
        for (let size = requested; size >= 256; size /= 2) {
            try { return createRenderTarget(gl, size, size, true); }
            catch (error) { if (size <= 256) throw error; }
        }
    };
    let directionalShadowTarget = createShadowTarget(Math.min(1024, maxShadowSize));
    let shadowTexture = directionalShadowTarget.texture;
    let shadowFramebuffer = directionalShadowTarget.framebuffer;
    let shadowSize = directionalShadowTarget.width;
    let shadowRequestedSize = Math.min(1024, maxShadowSize);
    const sampler = createTextureSampler(gl);
    const toonSampler = createTextureSampler(gl, true);
    const vao = gl.createVertexArray();
    if (!vao) throw new Error("WebGL2 could not allocate the empty mesh VAO.");
    let locations = {
        ...skinningLocations(gl, program),
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
        materialAmbient: gl.getUniformLocation(program, "u_materialAmbient"),
        specular: gl.getUniformLocation(program, "u_specular"),
        specularPower: gl.getUniformLocation(program, "u_specularPower"),
        cameraPosition: gl.getUniformLocation(program, "u_cameraPosition"),
        shadowStrength: gl.getUniformLocation(program, "u_shadowStrength"),
        shadowBias: gl.getUniformLocation(program, "u_shadowBias"),
        shadowTexelSize: gl.getUniformLocation(program, "u_shadowTexelSize"),
        shadowNormalOffset: gl.getUniformLocation(program, "u_shadowNormalOffset"),
        shadowToon: gl.getUniformLocation(program, "u_shadowToon"),
        edgeColor: gl.getUniformLocation(program, "u_edgeColor"),
        lightDirection: gl.getUniformLocation(program, "u_lightDirection"),
        lightColor: gl.getUniformLocation(program, "u_lightColor"),
        ambientColor: gl.getUniformLocation(program, "u_ambientColor"),
        ambientStrength: gl.getUniformLocation(program, "u_ambientStrength"),
        pointLightCount: gl.getUniformLocation(program, "u_pointLightCount"),
        pointLightPositionRange: gl.getUniformLocation(program, "u_pointLightPositionRange"),
        pointLightColorIntensity: gl.getUniformLocation(program, "u_pointLightColorIntensity"),
        spotLightCount: gl.getUniformLocation(program, "u_spotLightCount"),
        spotLightPositionRange: gl.getUniformLocation(program, "u_spotLightPositionRange"),
        spotLightDirectionOuterCosine: gl.getUniformLocation(program, "u_spotLightDirectionOuterCosine"),
        spotLightColorIntensity: gl.getUniformLocation(program, "u_spotLightColorIntensity"),
        spotLightConeParameters: gl.getUniformLocation(program, "u_spotLightConeParameters"),
        lightViewProjection: gl.getUniformLocation(program, "u_lightViewProjection"),
        shadowMap: gl.getUniformLocation(program, "u_shadowMap"),
        shadowEnabled: gl.getUniformLocation(program, "u_shadowEnabled"),
        receiveShadow: gl.getUniformLocation(program, "u_receiveShadow")
    };
    for (const name of ["localShadowAtlas", "localShadowMatrices", "localShadowRects", "localShadowRanges",
        "pointShadowBase", "spotShadowTile", "texturedPlane", "reflectionTexture", "reflectionViewProjection",
        "reflectionStrength", "waterSurface", "shadowFilterParameters", "clipEnabled", "clipPlane",
        "waterWaves", "waterSpeeds", "waterSteepness", "waterWaveCount", "waterElapsed", "waterNormalA", "waterNormalB", "waterSky", "waterHasNormals", "waterHasSky",
        "waterTime", "waterLerp", "waterTiling", "waterDeepColor", "waterReflectionTint", "waterSkyStrength", "waterMirror", "rippleCount", "rippleCenters", "rippleSizes", "rippleParameters", "textureMultiply", "textureAdd",
        "view", "toonTexture", "sphereTexture", "hasToon", "sphereMode", "toonMultiply", "toonAdd", "sphereMultiply", "sphereAdd"]) locations[name] = gl.getUniformLocation(program, `u_${name}`);
    const shadowLocations = {
        ...skinningLocations(gl, shadowProgram),
        position: gl.getAttribLocation(shadowProgram, "a_position"),
        uv: gl.getAttribLocation(shadowProgram, "a_uv"),
        texture: gl.getUniformLocation(shadowProgram, "u_texture"),
        hasTexture: gl.getUniformLocation(shadowProgram, "u_hasTexture"),
        alpha: gl.getUniformLocation(shadowProgram, "u_alpha"),
        lightViewProjection: gl.getUniformLocation(shadowProgram, "u_lightViewProjection"),
        model: gl.getUniformLocation(shadowProgram, "u_model")
    };
    const defaultLocations=locations,customShaders=new Map();
    function registerCustomShader(id,vertex,fragment){
        if(customShaders.has(id))return true;
        let custom;
        try{
            const vs=compileShader(gl,gl.VERTEX_SHADER,vertex,'Custom vertex'),fs=compileShader(gl,gl.FRAGMENT_SHADER,fragment,'Custom fragment');
            custom=gl.createProgram();gl.attachShader(custom,vs);gl.attachShader(custom,fs);
            gl.bindAttribLocation(custom,0,'a_position');gl.bindAttribLocation(custom,1,'a_normal');gl.bindAttribLocation(custom,2,'a_uv');
            gl.linkProgram(custom);gl.deleteShader(vs);gl.deleteShader(fs);
            if(!gl.getProgramParameter(custom,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(custom));
            const refs={};for(const key of Object.keys(defaultLocations))refs[key]=gl.getUniformLocation(custom,'u_'+key);
            customShaders.set(id,{program:custom,locations:refs,uniforms:new Map()});return true;
        }catch(error){if(custom)gl.deleteProgram(custom);reportError('WebGL2 custom shader failed: '+error.message);return false;}
    }
    function customFrame(shader,viewProjection,cameraPosition,clipPlane){
        const l=shader.locations;
        gl.uniformMatrix4fv(l.viewProjection,false,viewProjection);gl.uniformMatrix4fv(l.lightViewProjection,false,frameState.lightViewProjection);
        gl.uniform3fv(l.cameraPosition,cameraPosition||[0,0,0]);gl.uniform3fv(l.lightDirection,frameState.lightDirection);gl.uniform3fv(l.lightColor,frameState.lightColor);
        gl.uniform3fv(l.ambientColor,frameState.ambientColor);gl.uniform1f(l.ambientStrength,frameState.ambientStrength);
        gl.uniform1i(l.clipEnabled,clipPlane?1:0);gl.uniform4fv(l.clipPlane,clipPlane||[0,0,0,0]);
        gl.uniform1i(l.shadowMap,1);gl.uniform1i(l.localShadowAtlas,2);
    }
    function customUniforms(shader,values){
        const current=values||{};
        for(const [name,value]of Object.entries(shader.lastValues||{}))if(!(name in current)){
            const location=shader.uniforms.get(name),v=value.values||[];
            if(value.integer)gl.uniform1i(location,0);else if(v.length===1)gl.uniform1f(location,0);else if(v.length===2)gl.uniform2fv(location,[0,0]);else if(v.length===3)gl.uniform3fv(location,[0,0,0]);else if(v.length===4)gl.uniform4fv(location,[0,0,0,0]);
        }
        for(const [name,value]of Object.entries(values||{})){
            if(!shader.uniforms.has(name))shader.uniforms.set(name,gl.getUniformLocation(shader.program,name));
            const location=shader.uniforms.get(name),v=value.values||[];
            if(value.integer)gl.uniform1i(location,v[0]||0);
            else if(v.length===1)gl.uniform1f(location,v[0]);else if(v.length===2)gl.uniform2fv(location,v);else if(v.length===3)gl.uniform3fv(location,v);else if(v.length===4)gl.uniform4fv(location,v);
        }
        shader.lastValues=current;
    }
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
        ambientStrength: 0.25, clearColor: [0.035, 0.055, 0.1, 1], meshes: undefined
    };
    let shadowDirty = true;
    let shadowStateKey = "";
    let localShadowTarget = null;
    let localShadowRequestedSize = 0;
    let reflectionTargets = new Map();
    const cameraTargets = new Map();
    const renderTextureTargets = new Map();
    let sceneTarget = null, scenePresenter = null;
    let graphicsDirty = true, reportedRevision = -1, reportedScenePath = "";
    let reportedDirectional = -1, reportedLocal = -1, reportedSamples = -1;
    const particleBuffer = gl.createBuffer();
    if (!particleBuffer) throw new Error("WebGL2 could not allocate the particle buffer.");
    const contextLost = event => { event.preventDefault(); disposed = true; reportError?.("WebGL2 context was lost."); };
    canvas.addEventListener("webglcontextlost", contextLost, false);

    const info = Object.freeze({
        version: String(gl.getParameter(gl.VERSION) || "WebGL 2"),
        renderer: String(gl.getParameter(gl.RENDERER) || "WebGL2"),
        shadingLanguageVersion: String(gl.getParameter(gl.SHADING_LANGUAGE_VERSION) || "GLSL ES 3.00")
    });

    const bindWater = water => {
        const s=water.settings, waves=new Float32Array(16), speeds=new Float32Array(4);
        for(let i=0;i<4;i++) {
            const angle=s.gerstnerDirectionDegrees*Math.PI/180+(i-1.5)*.75;
            waves.set([Math.cos(angle),Math.sin(angle),2*Math.PI/Math.max(.1,s.gerstnerWavelength/(1+i*.55)),Math.max(0,s.gerstnerAmplitude)*.55**i],i*4);
            speeds[i]=Math.max(0,s.gerstnerSpeed)*(1+i*.18);
        }
        gl.uniform4fv(locations.waterWaves,waves);gl.uniform4fv(locations.waterSpeeds,speeds);
        gl.uniform1f(locations.waterSteepness,Math.min(1,Math.max(0,s.gerstnerSteepness)));
        gl.uniform1i(locations.waterWaveCount,s.gerstnerWavesEnabled?Math.min(4,Math.max(1,s.gerstnerWaveCount)):0);
        gl.uniform1f(locations.waterElapsed,water.time);gl.uniform1f(locations.waterTime,water.time*Math.max(0,s.animationSpeed));
        const phase=water.time*Math.max(0,s.animationSpeed), step=Math.floor(phase), blend=phase-step;
        const normalA=textures.get(`./engine/Water/Ocean${step%4}_N.dds`), normalB=textures.get(`./engine/Water/Ocean${(step+1)%4}_N.dds`);
        for(const [unit,name,texture] of [[6,"waterNormalA",normalA],[7,"waterNormalB",normalB],[8,"waterSky",textures.get("./engine/Water/Sky.dds")]]) {
            gl.activeTexture(gl.TEXTURE0+unit);gl.bindSampler(unit,null);gl.bindTexture(gl.TEXTURE_2D,texture||defaultTexture);gl.uniform1i(locations[name],unit);
        }
        gl.uniform1i(locations.waterHasNormals,normalA && normalB?1:0);gl.uniform1i(locations.waterHasSky,textures.has("./engine/Water/Sky.dds")?1:0);
        gl.uniform1f(locations.waterLerp,1-blend);gl.uniform1f(locations.waterTiling,s.normalTiling);
        gl.uniform3fv(locations.waterDeepColor,toNumericVector(s.deepColor,3,[.02,.1,.22]));
        gl.uniform3fv(locations.waterReflectionTint,toNumericVector(s.reflectionTint,3,[.56,.7,.9]));
        gl.uniform1f(locations.waterSkyStrength,s.skyReflectionStrength);gl.uniform1i(locations.waterMirror,s.mirrorReflectionEnabled?1:0);
        const ripples=water.ripples.slice(-rippleCapacity), centers=new Float32Array(rippleCapacity*4), sizes=new Float32Array(rippleCapacity*2);
        ripples.forEach((r,i)=>{centers.set([...r.center,r.age],i*4);sizes.set([r.radius,r.strength],i*2);});
        gl.uniform1i(locations.rippleCount,ripples.length);gl.uniform4fv(locations.rippleCenters,centers);gl.uniform2fv(locations.rippleSizes,sizes);
        gl.uniform4fv(locations.rippleParameters,[Math.max(.05,s.rippleLifetimeSeconds),s.rippleWaveSpeed,s.rippleFrequency,s.rippleNormalStrength]);
        gl.activeTexture(gl.TEXTURE0);
    };

    function renderView(outputViewport = null, outputTarget = null) {
            if (disposed) return;
            gl.enable(gl.DEPTH_TEST);
            gl.depthMask(true);
            gl.enable(gl.CULL_FACE);
            gl.cullFace(gl.BACK);
            const frameMeshes = Array.isArray(frameState.meshes)
                ? frameState.meshes
                : [{ id: "default", model: frameState.model, shadowEnabled: frameState.shadowEnabled, receiveShadow: true, edgeEnabled: true }];
            const features = frameState.features || {};
            const shadowsEnabled = (features.shadows ?? frameState.shadowEnabled) && (frameState.shadowStrength ?? 0.7) > 0;
            const frameById = new Map(frameMeshes.map(item => [String(item.id ?? item.Id ?? "default"), item]));
            const renderEntries = [...meshes.entries()]
                .map(([key, mesh]) => ({ key, mesh, frame: frameById.get(key) || frameById.get("default") }))
                .filter(entry => entry.frame);
            const localShadows = Array.isArray(frameState.localShadows) ? frameState.localShadows.slice(0, 16) : [];
            const desiredSceneSamples = resolveReflectionSamples(frameState.sceneAntiAliasingSamples ?? sceneSamples,
                16,reflectionColorSamples,reflectionDepthSamples);
            const useSceneTarget = desiredSceneSamples !== sceneSamples || !!outputViewport || !!frameState.underwater;
            if(useSceneTarget) {
                if(!sceneTarget || sceneTarget.width!==width || sceneTarget.height!==height || sceneTarget.requestedSamples!==desiredSceneSamples || !!sceneTarget.depthTexture!==!!frameState.underwater) {
                    if(sceneTarget) deleteRenderTarget(gl,sceneTarget);
                    sceneTarget=createRenderTarget(gl,width,height,false,desiredSceneSamples,!!frameState.underwater);
                    graphicsDirty=true;
                }
                scenePresenter ??= createScenePresenter(gl);
                actualSceneSamples=sceneTarget.sampleCount;
            } else actualSceneSamples=sceneSamples;
            const requestedSize = resolveShadowResolution(frameState.shadowResolution, maxShadowSize);
            if (requestedSize !== shadowRequestedSize) {
                const replacement = createShadowTarget(requestedSize);
                deleteRenderTarget(gl, directionalShadowTarget);
                directionalShadowTarget = replacement;
                shadowTexture = replacement.texture; shadowFramebuffer = replacement.framebuffer; shadowSize = replacement.width;
                shadowRequestedSize = requestedSize;
                shadowDirty = true;
            }
            const requestedLocalSize = resolveShadowResolution(frameState.localShadowResolution, Math.min(2048, maxShadowSize / 4)) * 4;
            if (localShadowTarget && localShadowRequestedSize !== requestedLocalSize) {
                deleteRenderTarget(gl, localShadowTarget); localShadowTarget = null; shadowDirty = true;
            }
            if (localShadows.length && shadowsEnabled && !localShadowTarget) {
                localShadowTarget = createShadowTarget(requestedLocalSize);
                localShadowRequestedSize = requestedLocalSize;
                shadowDirty = true;
            }
            const atlasSize = localShadowTarget?.width || requestedLocalSize;
            // Keep two pixels of padding even when the device uses a smaller atlas.
            const atlasRects = localShadows.map((shadow, tile) => {
                const rect = shadow.atlasRect;
                const authoredPadding = 2 / ((frameState.localShadowResolution || 1024) * 4);
                const padding = 2 / atlasSize;
                return [rect[0] - authoredPadding + padding, rect[1] - authoredPadding + padding,
                    rect[2] + 2 * authoredPadding - 2 * padding, rect[3] + 2 * authoredPadding - 2 * padding];
            });
            const reflections = Array.isArray(frameState.reflections) ? frameState.reflections : [];
            const drawCasters = matrix => {
                gl.useProgram(shadowProgram);
                gl.uniformMatrix4fv(shadowLocations.lightViewProjection, false, matrix);
                gl.cullFace(gl.BACK);
                for (const entry of renderEntries) {
                    if (!entry.frame.shadowEnabled) continue;
                    gl.uniformMatrix4fv(shadowLocations.model, false, entry.frame.model || identity);
                    bindSkinning(gl, shadowLocations, entry.mesh, defaultTexture);
                    gl.bindVertexArray(entry.mesh.vao);
                    for (const group of entry.mesh.groups) {
                        if (!group.enableShadow) continue;
                        if (group.doubleSided) gl.disable(gl.CULL_FACE); else gl.enable(gl.CULL_FACE);
                        gl.activeTexture(gl.TEXTURE0);
                        gl.bindTexture(gl.TEXTURE_2D, group.texture || defaultTexture);
                        gl.bindSampler(0, sampler);
                        gl.uniform1i(shadowLocations.texture, 0);
                        gl.uniform1i(shadowLocations.hasTexture, group.texture ? 1 : 0);
                        gl.uniform1f(shadowLocations.alpha, toNumericVector(group.diffuse, 4, [1, 1, 1, 1])[3]);
                        gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                    }
                }
                if(frameState.particleBatches?.some(b=>b.castShadows))effects.particles(frameState.particleBatches,matrix,frameState.skyInverseViewProjection||identity,null,true);
            };
            if (shadowDirty && renderEntries.some(entry => entry.frame.shadowEnabled) && shadowsEnabled) {
                gl.bindFramebuffer(gl.FRAMEBUFFER, shadowFramebuffer);
                gl.viewport(0, 0, shadowSize, shadowSize);
                gl.colorMask(false, false, false, false);
                gl.disable(gl.BLEND);
                gl.clearDepth(1.0);
                gl.clear(gl.DEPTH_BUFFER_BIT);
                drawCasters(frameState.lightViewProjection);
                if (localShadows.length > 0) {
                    gl.bindFramebuffer(gl.FRAMEBUFFER, localShadowTarget.framebuffer);
                    gl.clear(gl.DEPTH_BUFFER_BIT);
                    for (let tile = 0; tile < localShadows.length; tile++) {
                        const shadow = localShadows[tile];
                        const rect = atlasRects[tile];
                        gl.viewport(...rect.map(value => Math.round(value * atlasSize)));
                        drawCasters(shadow.viewProjection);
                    }
                }
                gl.bindVertexArray(null);
                gl.colorMask(true, true, true, true);
                shadowDirty = false;
            }
            const drawColor = (viewProjection, cameraPosition, clipPlane = null, skyInverse = frameState.skyInverseViewProjection, view = frameState.view) => {
                locations=defaultLocations;
                gl.enable(gl.DEPTH_TEST);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.BLEND);
                gl.colorMask(true, true, true, true);
                gl.depthMask(true);
                gl.clearColor(...frameState.clearColor);
                gl.clearDepth(1.0);
                gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
                effects.sky(frameState.sky, skyInverse);
                if(!clipPlane)effects.sprites(backgroundSprites,width,height,outputViewport?.[0]||0,
                    outputViewport ? canvas.height-outputViewport[1]-outputViewport[3] : 0);
                gl.useProgram(program);
                gl.uniformMatrix4fv(locations.viewProjection, false, viewProjection);
                gl.uniformMatrix4fv(locations.view, false, view?.length===16?view:identity);
                gl.uniform1i(locations.clipEnabled, clipPlane ? 1 : 0);
                gl.uniform4fv(locations.clipPlane, clipPlane || [0, 0, 0, 0]);
                gl.uniformMatrix4fv(locations.lightViewProjection, false, frameState.lightViewProjection);
                gl.uniform1i(locations.shadowEnabled, shadowsEnabled ? 1 : 0);
                gl.activeTexture(gl.TEXTURE1);
                gl.bindTexture(gl.TEXTURE_2D, shadowTexture);
                gl.bindSampler(1, null);
                gl.uniform1i(locations.shadowMap, 1);
                gl.uniform1f(locations.shadowStrength, frameState.shadowStrength ?? 0.7);
                gl.uniform1f(locations.shadowBias, frameState.shadowBias ?? 0.0018);
                gl.uniform2f(locations.shadowTexelSize, 1 / shadowSize, 1 / shadowSize);
                gl.uniform2f(locations.shadowFilterParameters, resolveShadowFilterSamples(frameState.shadowFilterSamples),
                    Math.min(8, Math.max(0, Number.isFinite(frameState.shadowFilterRadius) ? frameState.shadowFilterRadius : 1)));
                gl.uniform1f(locations.shadowNormalOffset, (frameState.shadowNormalOffset ?? 0)
                    * (frameState.shadowResolution || 1024) / shadowSize);
                gl.uniform3fv(locations.cameraPosition, cameraPosition || [0, 0, 0]);
                const pointShadowBase = new Int32Array(8).fill(-1), spotShadowTile = new Int32Array(4).fill(-1);
                const shadowMatrices = new Float32Array(16 * 16), shadowRects = new Float32Array(16 * 4), shadowRanges = new Float32Array(16 * 2);
                for (let tile = 0; tile < localShadows.length; tile++) {
                    const shadow = localShadows[tile];
                    if (shadow.pointLightIndex >= 0 && shadow.face === 0) pointShadowBase[shadow.pointLightIndex] = tile;
                    if (shadow.spotLightIndex >= 0) spotShadowTile[shadow.spotLightIndex] = tile;
                    shadowMatrices.set(shadow.viewProjection, tile * 16);
                    shadowRects.set(atlasRects[tile], tile * 4);
                    shadowRanges.set([shadow.nearPlane, shadow.farPlane], tile * 2);
                }
                gl.uniform1iv(locations.pointShadowBase, pointShadowBase);
                gl.uniform1iv(locations.spotShadowTile, spotShadowTile);
                gl.uniformMatrix4fv(locations.localShadowMatrices, false, shadowMatrices);
                gl.uniform4fv(locations.localShadowRects, shadowRects);
                gl.uniform2fv(locations.localShadowRanges, shadowRanges);
                gl.activeTexture(gl.TEXTURE4);
                gl.bindTexture(gl.TEXTURE_2D, localShadowTarget?.texture || shadowTexture);
                gl.bindSampler(4, null);
                gl.uniform1i(locations.localShadowAtlas, 4);
                gl.uniform1i(locations.reflectionTexture, 5);
                gl.uniform3fv(locations.lightDirection, toNumericVector(frameState.lightDirection, 3, [0, -1, 0]));
                gl.uniform3fv(locations.lightColor, toNumericVector(frameState.lightColor, 3, [1, 1, 1]));
                gl.uniform3fv(locations.ambientColor, toNumericVector(frameState.ambientColor, 3, [0.15, 0.15, 0.15]));
                gl.uniform1f(locations.ambientStrength, frameState.ambientStrength);
                const pointLights = Array.isArray(frameState.pointLights) ? frameState.pointLights.slice(0, 8) : [];
                const pointPositionRange = new Float32Array(8 * 4);
                const pointColorIntensity = new Float32Array(8 * 4);
                for (let i = 0; i < pointLights.length; i++) {
                    const light = pointLights[i];
                    pointPositionRange.set([
                        ...toNumericVector(light?.position, 3, [0, 0, 0]),
                        Number(light?.range) || 1
                    ], i * 4);
                    pointColorIntensity.set([
                        ...toNumericVector(light?.color, 3, [1, 1, 1]),
                        Number(light?.intensity) || 0
                    ], i * 4);
                }
                gl.uniform1i(locations.pointLightCount, pointLights.length);
                gl.uniform4fv(locations.pointLightPositionRange, pointPositionRange);
                gl.uniform4fv(locations.pointLightColorIntensity, pointColorIntensity);
                const spotLights = Array.isArray(frameState.spotLights) ? frameState.spotLights.slice(0, 4) : [];
                const spotPositionRange = new Float32Array(4 * 4);
                const spotDirectionOuterCosine = new Float32Array(4 * 4);
                const spotColorIntensity = new Float32Array(4 * 4);
                const spotConeParameters = new Float32Array(4 * 4);
                for (let i = 0; i < spotLights.length; i++) {
                    const light = spotLights[i];
                    spotPositionRange.set([
                        ...toNumericVector(light?.position, 3, [0, 0, 0]),
                        Number(light?.range) || 1
                    ], i * 4);
                    spotDirectionOuterCosine.set([
                        ...toNumericVector(light?.direction, 3, [0, -1, 0]),
                        Number(light?.outerConeCosine) || 0
                    ], i * 4);
                    spotColorIntensity.set([
                        ...toNumericVector(light?.color, 3, [1, 1, 1]),
                        Number(light?.intensity) || 0
                    ], i * 4);
                    spotConeParameters.set([Number(light?.innerConeCosine) || 1, 0, 0, 0], i * 4);
                }
                gl.uniform1i(locations.spotLightCount, spotLights.length);
                gl.uniform4fv(locations.spotLightPositionRange, spotPositionRange);
                gl.uniform4fv(locations.spotLightDirectionOuterCosine, spotDirectionOuterCosine);
                gl.uniform4fv(locations.spotLightColorIntensity, spotColorIntensity);
                gl.uniform4fv(locations.spotLightConeParameters, spotConeParameters);
                for (const entry of renderEntries.slice().sort((a,b)=>(a.frame.waterSurface?120:a.frame.texturedPlane?115:100)-(b.frame.waterSurface?120:b.frame.texturedPlane?115:100))) {
                    const mesh = entry.mesh;
                    const meshFrame = entry.frame;
                    const custom=customShaders.get(meshFrame.customShader?.id);
                    locations=custom?.locations||defaultLocations;
                    gl.useProgram(custom?.program||program);
                    if(custom){customFrame(custom,viewProjection,cameraPosition,clipPlane);customUniforms(custom,meshFrame.customShader.uniforms);}
                    // Desktop excludes all mirror planes from reflection passes.
                    if (clipPlane && (meshFrame.texturedPlane || meshFrame.waterSurface) && meshFrame.mirrorReflectionStrength > 0) continue;
                    const reflection = !clipPlane ? reflectionTargets.get(entry.key) : null;
                    const water=(frameState.waters||[]).find(w=>w.id===entry.key);
                    gl.uniform1i(locations.texturedPlane, meshFrame.texturedPlane || (meshFrame.waterSurface && !water) ? 1 : 0);
                    gl.uniform1i(locations.waterSurface, meshFrame.waterSurface && water ? 1 : 0);
                    if(water)bindWater(water);
                    gl.uniformMatrix4fv(locations.reflectionViewProjection, false, reflection?.viewProjection || identity);
                    gl.uniform1f(locations.reflectionStrength, reflection ? meshFrame.mirrorReflectionStrength || 0 : 0);
                    gl.activeTexture(gl.TEXTURE5);
                    gl.bindTexture(gl.TEXTURE_2D, reflection?.texture || defaultTexture);
                    gl.bindSampler(5, null);
                    gl.uniformMatrix4fv(locations.model, false, meshFrame.model || identity);
                    bindSkinning(gl, locations, mesh, defaultTexture);
                    gl.bindVertexArray(mesh.vao);
                    for (const group of mesh.groups) {
                        const material = meshFrame.materials?.[mesh.groups.indexOf(group)] || group;
                        const diffuse = toNumericVector(material.diffuse ?? material.Diffuse, 4, [1, 1, 1, 1]);
                        if(water)diffuse[3]=Math.min(1,Math.max(0,water.settings.alpha));
                        const edge = toNumericVector(material.edgeColor ?? material.EdgeColor, 4, [0, 0, 0, 1]);
                        gl.uniform4fv(locations.textureMultiply,material.textureMultiply||[1,1,1,1]);
                        gl.uniform4fv(locations.textureAdd,material.textureAdd||[0,0,0,0]);
                        gl.uniform1i(locations.hasToon,group.toonTexture?1:0);gl.uniform1i(locations.sphereMode,group.sphereTexture?(material.sphereMode||0):0);
                        for(const [unit,name,texture] of [[9,"toonTexture",group.toonTexture],[10,"sphereTexture",group.sphereTexture]]) {
                            gl.activeTexture(gl.TEXTURE0+unit);gl.bindSampler(unit,toonSampler);gl.bindTexture(gl.TEXTURE_2D,texture||defaultTexture);gl.uniform1i(locations[name],unit);
                        }
                        gl.uniform4fv(locations.toonMultiply,material.toonMultiply||[1,1,1,1]);gl.uniform4fv(locations.toonAdd,material.toonAdd||[0,0,0,0]);
                        gl.uniform4fv(locations.sphereMultiply,material.sphereMultiply||[1,1,1,1]);gl.uniform4fv(locations.sphereAdd,material.sphereAdd||[0,0,0,0]);
                        gl.uniform4fv(locations.diffuse, diffuse);
                        gl.uniform3fv(locations.materialAmbient, toNumericVector(material.ambient ?? material.Ambient, 3, [1, 1, 1]));
                        gl.uniform3fv(locations.specular, toNumericVector(material.specular ?? material.Specular, 3, [0, 0, 0]));
                        gl.uniform1f(locations.specularPower, material.specularPower ?? material.SpecularPower ?? 0);
                        gl.uniform1i(locations.shadowToon, meshFrame.shadowToon ? 1 : 0);
                        gl.uniform4fv(locations.edgeColor, edge);
                        // PC applies the entity receive-shadow setting to every
                        // PMX material, including classroom floors whose PMX
                        // self-shadow flag is unset.
                        const receiveShadow = meshFrame.receiveShadow ?? group.receiveShadow;
                        gl.uniform1i(locations.receiveShadow, receiveShadow ? 1 : 0);
                        if (group.doubleSided) gl.disable(gl.CULL_FACE); else gl.enable(gl.CULL_FACE);
                        if ((features.edges ?? true) && meshFrame.edgeEnabled !== false && group.enableEdge && group.edgeSize > 0) {
                            // The expanded outline must draw only back faces even
                            // when the material itself is double-sided. Otherwise
                            // its black front shell occludes the textured surface.
                            gl.enable(gl.CULL_FACE);
                            gl.cullFace(gl.FRONT);
                            gl.uniform1i(locations.edgePass, 1);
                            gl.uniform1f(locations.edgeSize, (material.edgeSize??group.edgeSize) * 0.01);
                            gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                            gl.cullFace(gl.BACK);
                            if (group.doubleSided) gl.disable(gl.CULL_FACE);
                        }
                        gl.uniform1i(locations.edgePass, 0);
                        gl.uniform1f(locations.edgeSize, 0);
                        const resolvedTexture = textures.get(material.textureUri || group.textureUri) || group.texture;
                        const texture = resolvedTexture || defaultTexture;
                        gl.activeTexture(gl.TEXTURE0);
                        gl.bindTexture(gl.TEXTURE_2D, texture);
                        gl.bindSampler(0, sampler);
                        gl.uniform1i(locations.texture, 0);
                        gl.uniform1i(locations.hasTexture, resolvedTexture ? 1 : 0);
                        // PMX skin/hair overlays can have an opaque material color
                        // and translucent texture pixels. Match the desktop pass
                        // by blending textured materials as well.
                        if (group.texture || diffuse[3] < 0.999) gl.enable(gl.BLEND); else gl.disable(gl.BLEND);
                        gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
                        if(custom)customUniforms(custom,meshFrame.customShader.uniforms);
                        gl.drawElements(gl.TRIANGLES, group.indexCount, gl.UNSIGNED_INT, group.startIndex * 4);
                    }
                }
                const particles = Array.isArray(frameState.particles) && frameState.particles.length > 0 ? frameState.particles : [];
                if ((features.particles ?? false) && particles.length > 0 && !frameState.particleBatches?.length) {
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
                    gl.uniformMatrix4fv(particleLocations.viewProjection, false, viewProjection);
                    gl.bindVertexArray(vao);
                    gl.bindBuffer(gl.ARRAY_BUFFER, particleBuffer);
                    gl.bufferData(gl.ARRAY_BUFFER, data, gl.STREAM_DRAW);
                    gl.enableVertexAttribArray(particleLocations.position);
                    gl.vertexAttribPointer(particleLocations.position, 3, gl.FLOAT, false, 32, 0);
                    gl.enableVertexAttribArray(particleLocations.color);
                    gl.vertexAttribPointer(particleLocations.color, 4, gl.FLOAT, false, 32, 12);
                    gl.enableVertexAttribArray(particleLocations.size);
                    gl.vertexAttribPointer(particleLocations.size, 1, gl.FLOAT, false, 32, 28);
                    gl.enable(gl.BLEND);
                    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
                    gl.depthMask(false);
                    gl.drawArrays(gl.POINTS, 0, particles.length);
                    gl.depthMask(true);
                    gl.bindBuffer(gl.ARRAY_BUFFER, null);
                }
                if(frameState.particleBatches?.length)effects.particles(frameState.particleBatches,viewProjection,skyInverse||identity,clipPlane);
                if(!clipPlane)effects.lines(frameState.debugLines,viewProjection);
                gl.disable(gl.BLEND);
                gl.bindSampler(0, null);
                gl.activeTexture(gl.TEXTURE1);
                gl.bindTexture(gl.TEXTURE_2D, null);
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, null);
                gl.bindVertexArray(null);
            };
            const activeReflections = new Set(reflections.map(item => item.id));
            for (const [id, target] of reflectionTargets) {
                if (!activeReflections.has(id) || target.width !== width || target.height !== height) {
                    deleteRenderTarget(gl, target);
                    reflectionTargets.delete(id);
                    graphicsDirty=true;
                }
            }
            for (const reflection of reflections) {
                let target = reflectionTargets.get(reflection.id);
                const samples = resolveReflectionSamples(reflection.samples,
                    actualSceneSamples, reflectionColorSamples, reflectionDepthSamples);
                if (target && target.requestedSamples !== samples) {
                    deleteRenderTarget(gl, target);
                    reflectionTargets.delete(reflection.id);
                    target = null;
                }
                if (!target) {
                    target = createRenderTarget(gl, width, height, false, samples);
                    reflectionTargets.set(reflection.id, target);
                    graphicsDirty=true;
                }
                target.viewProjection = reflection.viewProjection;
                // Unbind sampled color targets before attaching them for drawing.
                gl.activeTexture(gl.TEXTURE5);
                gl.bindTexture(gl.TEXTURE_2D, null);
                gl.bindFramebuffer(gl.FRAMEBUFFER, target.drawFramebuffer || target.framebuffer);
                gl.viewport(0, 0, width, height);
                drawColor(reflection.viewProjection, reflection.cameraPosition, reflection.clipPlane, reflection.skyInverseViewProjection, reflection.view);
                resolveColorTarget(gl,target);
            }
            gl.bindFramebuffer(gl.FRAMEBUFFER, useSceneTarget ? sceneTarget.drawFramebuffer || sceneTarget.framebuffer : null);
            gl.viewport(0, 0, width, height);
            drawColor(frameState.viewProjection, frameState.cameraPosition);
            if(useSceneTarget) {
                resolveColorTarget(gl,sceneTarget,!!frameState.underwater); gl.bindFramebuffer(gl.FRAMEBUFFER,outputTarget?.framebuffer || null);
                if(outputViewport)gl.viewport(...outputViewport);
                gl.disable(gl.DEPTH_TEST); gl.disable(gl.STENCIL_TEST); gl.disable(gl.SCISSOR_TEST); gl.disable(gl.CULL_FACE); gl.disable(gl.BLEND);
                gl.depthMask(false); gl.colorMask(true,true,true,true); gl.useProgram(scenePresenter.program); gl.bindVertexArray(scenePresenter.vao);
                gl.activeTexture(gl.TEXTURE0); gl.bindSampler(0,null); gl.bindTexture(gl.TEXTURE_2D,sceneTarget.texture);
                if(frameState.underwater) {
                    underwaterPresenter??=createUnderwaterPresenter(gl,compileShader);
                    underwaterPresenter.draw(sceneTarget,frameState.underwater,frameState.time||0,width,height);
                } else { gl.uniform1i(scenePresenter.texture,0); gl.drawArrays(gl.TRIANGLES,0,3); }
                gl.bindTexture(gl.TEXTURE_2D,null); gl.bindVertexArray(null); gl.depthMask(true);
            }
            const directionalActual=shadowsEnabled && renderEntries.some(entry=>entry.frame.shadowEnabled) ? shadowSize : 0;
            const localActual=directionalActual && localShadows.length ? atlasSize/4 : 0;
            if(graphicsDirty || reportedSamples!==actualSceneSamples || reportedDirectional!==directionalActual || reportedLocal!==localActual) {
                graphicsDirty=false; reportedRevision=frameState.graphicsRevision??0; reportedScenePath=frameState.graphicsScenePath??"";
                reportedSamples=actualSceneSamples; reportedDirectional=directionalActual; reportedLocal=localActual;
                return { revision:reportedRevision,scenePath:reportedScenePath,sceneSamples:actualSceneSamples,
                    directionalResolution:directionalActual,localLightResolution:localActual,
                    reflectionSamples:Object.fromEntries([...reflectionTargets].map(([id,target])=>[id,target.sampleCount])) };
            }
    }

    return {
        info,
        registerCustomShader,
        getDiagnostics() { return { width, height,rippleCapacity,canvasSize:frameState.canvasSize,time:frameState.time,cameras: frameState.cameras?.map(c=>({id:c.id,viewport:c.viewport})),
            renderTextures:frameState.renderTextures?.map(r=>({id:r.id,viewport:r.camera.viewport})),meshCount:meshes.size }; },
        async loadSceneTextures(uris) { await Promise.all(toEntryArray(uris).map(ensureTexture)); },
        setSprites(sprites) { backgroundSprites=toEntryArray(sprites); for(const s of backgroundSprites)ensureTexture(s.uri); },
        resize(nextWidth, nextHeight) {
            if (disposed) return;
            width = Math.max(Math.floor(nextWidth), 1);
            height = Math.max(Math.floor(nextHeight), 1);
            gl.viewport(0, 0, width, height);
        },
        setFrameState(nextState) {
            if (disposed || !nextState) return;
            frameState = { ...frameState, ...nextState };
            ensureTexture(frameState.sky?.textureUri);
            for(const mesh of frameState.meshes||[])for(const material of mesh.materials||[])ensureTexture(material.textureUri);
            if(frameState.waters?.length) {
                for(let i=0;i<4;i++)ensureTexture(`./engine/Water/Ocean${i}_N.dds`);
                ensureTexture("./engine/Water/Sky.dds");
            }
            for(const batch of frameState.particleBatches||[])ensureTexture(batch.textureUri);
            if ((frameState.graphicsRevision ?? 0) !== reportedRevision || (frameState.graphicsScenePath ?? "") !== reportedScenePath) graphicsDirty = true;
            // Camera drags and ambient edits do not change caster depth.
            const key = JSON.stringify([frameState.lightViewProjection, frameState.localShadows, frameState.model, frameState.meshes, frameState.shadowEnabled, frameState.features?.shadows]);
            if (key !== shadowStateKey) { shadowDirty = true; shadowStateKey = key; }
            if(frameState.particleBatches?.some(b=>b.castShadows))shadowDirty=true;
        },
        async uploadMesh(payload) {
            if (disposed) return;
            shadowDirty = true;
            const nextMesh = createMeshResources(gl, payload, locations, reportError);
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            meshes.set("default", nextMesh);
            await Promise.allSettled(nextMesh.groups.map(group => group.texturePromise));
            shadowDirty = true;
        },
        async uploadMeshes(entries) {
            if (disposed) return;
            shadowDirty = true;
            const nextMeshes = new Map();
            const pending = [];
            for (const entry of toEntryArray(entries)) {
                const key = String(entry?.id ?? entry?.Id ?? "").trim();
                const payload = entry?.mesh ?? entry?.Mesh;
                if (!key || !payload) continue;
                const nextMesh = createMeshResources(gl, payload, locations, reportError);
                nextMeshes.set(key, nextMesh);
                pending.push(...nextMesh.groups.map(group => group.texturePromise));
            }
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            for (const [key, value] of nextMeshes) meshes.set(key, value);
            await Promise.allSettled(pending);
            shadowDirty = true;
        },
        updateMesh(payload) {
            if (disposed || !payload) return;
            const mesh = meshes.get("default");
            if (!mesh) return;
            updateMeshResources(gl, mesh, payload);
            shadowDirty = true;
        },
        async upsertMeshes(entries) {
            if(disposed)return;
            const pending=[];
            for(const entry of toEntryArray(entries)) {
                const mesh=createMeshResources(gl,entry.mesh,locations,reportError),previous=meshes.get(entry.id);
                if(previous)deleteMeshResources(gl,previous);
                meshes.set(entry.id,mesh);pending.push(...mesh.groups.map(g=>g.texturePromise));
            }
            await Promise.allSettled(pending);shadowDirty=true;
        },
        removeMeshes(ids) {
            for(const id of ids) { const mesh=meshes.get(id);if(mesh)deleteMeshResources(gl,mesh);meshes.delete(id); }
            shadowDirty=true;
        },
        updateMeshes(entries) {
            if (disposed) return;
            for (const entry of toEntryArray(entries)) {
                const key = String(entry?.id ?? entry?.Id ?? "").trim();
                const payload = entry?.mesh ?? entry?.Mesh;
                const mesh = meshes.get(key);
                if (mesh && payload) { updateMeshResources(gl, mesh, payload); shadowDirty = true; }
            }
        },
        render() {
            const base=frameState, fullWidth=width, fullHeight=height;
            if(!base.cameras?.length && !base.renderTextures?.length) {
                const applied=renderView();gl.viewport(0,0,fullWidth,fullHeight);effects.sprites(backgroundSprites,fullWidth,fullHeight,0,0,true);return applied;
            }
            let applied;
            const active=new Set([...(base.cameras||[]).map(c=>c.id),...(base.renderTextures||[]).map(r=>'rt:'+r.id)]);
            for(const [id,resources] of cameraTargets) {
                if(active.has(id))continue;
                if(resources.scene)deleteRenderTarget(gl,resources.scene);
                for(const target of resources.reflections.values())deleteRenderTarget(gl,target);
                cameraTargets.delete(id);
            }
            try {
                const activeTextures=new Set((base.renderTextures||[]).map(r=>r.id));
                for(const [id,target] of renderTextureTargets) {
                    if(activeTextures.has(id))continue;
                    for(const alias of target.aliases)textures.delete(alias);
                    deleteRenderTarget(gl,target);renderTextureTargets.delete(id);
                }
                for(const definition of base.renderTextures||[]) {
                    const camera=definition.camera, limit=Number(gl.getParameter(gl.MAX_TEXTURE_SIZE))||8192;
                    const w=Math.min(camera.viewport[2],limit),h=Math.min(camera.viewport[3],limit);
                    let target=renderTextureTargets.get(definition.id);
                    if(target && (target.width!==w || target.height!==h || target.name!==definition.name)) {
                        for(const alias of target.aliases)textures.delete(alias);
                        deleteRenderTarget(gl,target);renderTextureTargets.delete(definition.id);target=null;
                    }
                    if(!target) {
                        target=createRenderTarget(gl,w,h,false);target.updated=-Infinity;target.name=definition.name;
                        target.aliases=['rt:'+definition.id,'rt:'+definition.name];renderTextureTargets.set(definition.id,target);
                    }
                    const mode=definition.refreshMode?.toLowerCase(),time=base.time||0;
                    if(Number.isFinite(target.updated) && (mode==='once' || mode==='on_demand' ||
                        (mode==='interval' || mode==='fixed_rate') && time-target.updated<definition.refreshIntervalSeconds))continue;
                    const id='rt:'+definition.id,resources=cameraTargets.get(id)||{scene:null,reflections:new Map()};
                    sceneTarget=resources.scene;reflectionTargets=resources.reflections;width=w;height=h;
                    frameState={...base,...camera,clearColor:definition.clearColor};
                    renderView([0,0,w,h],target);
                    resources.scene=sceneTarget;cameraTargets.set(id,resources);target.updated=time;
                    for(const alias of target.aliases)textures.set(alias,target.texture);
                }
                for(const camera of base.cameras||[]) {
                    const resources=cameraTargets.get(camera.id)||{scene:null,reflections:new Map()};
                    sceneTarget=resources.scene;reflectionTargets=resources.reflections;
                    width=camera.viewport[2];height=camera.viewport[3];
                    frameState={...base,...camera};
                    const result=renderView(camera.viewport);
                    if(result)applied=result;
                    resources.scene=sceneTarget;cameraTargets.set(camera.id,resources);
                }
                gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,fullWidth,fullHeight);
                effects.sprites(backgroundSprites,fullWidth,fullHeight,0,0,true);
            } finally {
                frameState=base;width=fullWidth;height=fullHeight;gl.viewport(0,0,width,height);
            }
            return applied;
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            canvas.removeEventListener("webglcontextlost", contextLost, false);
            gl.bindVertexArray(null);
            for (const existing of meshes.values()) deleteMeshResources(gl, existing);
            meshes.clear();
            gl.deleteVertexArray(vao);
            const scenes = new Set([sceneTarget,...[...cameraTargets.values()].map(r=>r.scene)].filter(Boolean));
            for(const target of scenes)deleteRenderTarget(gl,target);
            if(scenePresenter) { gl.deleteProgram(scenePresenter.program); gl.deleteVertexArray(scenePresenter.vao); }
            for(const shader of customShaders.values())gl.deleteProgram(shader.program);customShaders.clear();
            for(const target of renderTextureTargets.values()) {
                for(const alias of target.aliases)textures.delete(alias);
                deleteRenderTarget(gl,target);
            }
            renderTextureTargets.clear();
            for(const texture of new Set(textures.values()))gl.deleteTexture(texture);
            textures.clear();effects.dispose();underwaterPresenter?.dispose();
            deleteRenderTarget(gl, directionalShadowTarget);
            if (localShadowTarget) deleteRenderTarget(gl, localShadowTarget);
            const allReflections=new Set([...reflectionTargets.values(),...[...cameraTargets.values()].flatMap(r=>[...r.reflections.values()])]);
            for (const target of allReflections) deleteRenderTarget(gl, target);
            reflectionTargets.clear();
            cameraTargets.clear();
            gl.deleteSampler(sampler);
            gl.deleteSampler(toonSampler);
            gl.deleteProgram(shadowProgram);
            gl.deleteBuffer(particleBuffer);
            gl.deleteProgram(particleProgram);
            gl.deleteProgram(program);
        }
    };
}





