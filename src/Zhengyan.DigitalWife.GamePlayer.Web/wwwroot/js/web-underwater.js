// Port of UnderwaterPostProcessRenderer. Web scene depth uses the full 0..1
// range, so linearization uses the WebGL projection instead of PC's half range.
const fragmentSource = `#version 300 es
        precision highp float;

        in vec2 v_TexCoord;
        out vec4 out_Color;

        uniform sampler2D u_ColorTex;
        uniform sampler2D u_DepthTex;
        uniform float u_Time;
        uniform float u_Near;
        uniform float u_Far;
        uniform float u_IsOrthographic;
        uniform vec2 u_ViewportSize;
        uniform vec3 u_Tint;
        uniform vec3 u_FogColor;
        uniform float u_FogDensity;
        uniform float u_VisibilityDistance;
        uniform float u_DistortionStrength;
        uniform float u_CausticsStrength;
        uniform float u_BubbleStrength;
        uniform float u_SurfaceDepth;

        float hash(vec2 p)
        {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float noise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(
                mix(hash(i + vec2(0.0, 0.0)), hash(i + vec2(1.0, 0.0)), u.x),
                mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x),
                u.y);
        }

        float linearDepth(float depth)
        {
            float projectionDepth = depth;
            if (u_IsOrthographic > 0.5)
            {
                return mix(u_Near, u_Far, projectionDepth);
            }

            return (u_Near * u_Far) / max(u_Far - projectionDepth * (u_Far - u_Near), 0.0001);
        }

        float caustics(vec2 uv, float time)
        {
            vec2 p = uv * vec2(18.0, 13.0);
            float a = sin(p.x + sin(p.y * 1.7 + time * 0.85) + time * 0.65);
            float b = sin((p.x * 1.35 - p.y * 0.75) + time * 1.15);
            float c = sin(length(p - vec2(9.0, 6.0)) * 1.35 - time * 1.8);
            float bands = (a + b + c) * 0.333 + 0.5;
            return smoothstep(0.73, 1.0, bands);
        }

        float bubbleLayer(vec2 uv, float time, float scale, float speed, float seed)
        {
            vec2 p = uv * scale;
            p.y -= time * speed;
            vec2 cell = floor(p);
            vec2 f = fract(p);
            float rnd = hash(cell + seed);
            vec2 center = vec2(hash(cell + seed + 17.0), hash(cell + seed + 31.0));
            center.y = fract(center.y + time * speed * 0.13);
            float radius = mix(0.035, 0.085, hash(cell + seed + 47.0));
            float d = length((f - center) * vec2(1.0, 1.25));
            float outer = 1.0 - smoothstep(radius * 0.72, radius, d);
            float inner = smoothstep(radius * 0.35, radius * 0.58, d);
            float ring = outer * inner;
            return ring * smoothstep(0.78, 0.98, rnd);
        }

        float bubbles(vec2 uv, float time)
        {
            float b = 0.0;
            b += bubbleLayer(uv + vec2(0.03, 0.01), time, 8.0, 0.10, 3.0);
            b += bubbleLayer(uv + vec2(0.41, 0.22), time, 13.0, 0.16, 19.0);
            b += bubbleLayer(uv + vec2(0.77, 0.37), time, 21.0, 0.22, 41.0);
            return clamp(b, 0.0, 1.0);
        }

        void main()
        {
            vec2 uv = v_TexCoord;
            float rawDepth = texture(u_DepthTex, uv).r;
            float skyMask = smoothstep(0.9985, 1.0, rawDepth);
            float sceneDepth = linearDepth(rawDepth);
            float skyDistance = min(u_Far * 0.32, max(u_VisibilityDistance, u_Near));
            float depthForWater = mix(sceneDepth, skyDistance, skyMask);
            float entryStrength = smoothstep(0.0, 0.45, u_SurfaceDepth);

            vec2 wave = vec2(
                sin(uv.y * 32.0 + u_Time * 0.9) + sin((uv.x + uv.y) * 22.0 - u_Time * 1.35),
                cos(uv.x * 28.0 - u_Time * 0.75) + sin((uv.x - uv.y) * 18.0 + u_Time * 1.1));
            float shimmer = noise(uv * 14.0 + vec2(u_Time * 0.05, -u_Time * 0.08));
            vec2 distortion = wave * (0.5 + shimmer * 0.5) * u_DistortionStrength * entryStrength;
            distortion *= mix(1.0, 0.35, skyMask);

            vec4 source = texture(u_ColorTex, clamp(uv + distortion, vec2(0.001), vec2(0.999)));
            vec3 color = source.rgb;

            float distanceFog = max(depthForWater - u_Near, 0.0) / max(u_VisibilityDistance, 0.001);
            float density = max(u_FogDensity, 0.0);
            float fogFactor = 1.0 - exp(-distanceFog * density);
            fogFactor = clamp(fogFactor + clamp(u_SurfaceDepth * 0.045, 0.0, 0.38), 0.0, 0.96) * entryStrength;

            vec3 absorption = vec3(
                exp(-depthForWater * 0.018 * density),
                exp(-depthForWater * 0.007 * density),
                exp(-depthForWater * 0.0035 * density));
            color *= mix(vec3(1.0), absorption, 0.55 * entryStrength);
            color *= mix(vec3(1.0), u_Tint, 0.34 * entryStrength);

            vec3 fogged = mix(color, u_FogColor, fogFactor);

            float nearSurface = (1.0 - fogFactor) * (1.0 - skyMask);
            float caustic = caustics(uv + distortion * 4.0, u_Time) * nearSurface * u_CausticsStrength * entryStrength;
            fogged += vec3(0.16, 0.26, 0.23) * caustic;

            float bubble = bubbles(uv, u_Time) * u_BubbleStrength * entryStrength;
            fogged = mix(fogged, vec3(0.72, 0.92, 0.95), bubble * 0.35);

            float vignette = 1.0 - smoothstep(0.18, 0.82, distance(uv, vec2(0.5)));
            fogged *= mix(0.72, 1.0, vignette * entryStrength + (1.0 - entryStrength));

            out_Color = vec4(clamp(fogged, 0.0, 1.0), source.a);
        }
        `;

export function createUnderwaterPresenter(gl, compile) {
    const vertex=compile(gl,gl.VERTEX_SHADER,`#version 300 es
precision highp float;out vec2 v_TexCoord;
void main(){vec2 p=gl_VertexID==0?vec2(0):gl_VertexID==1?vec2(2,0):vec2(0,2);v_TexCoord=p;gl_Position=vec4(p*2.0-1.0,0,1);}`,"Underwater vertex");
    const fragment=compile(gl,gl.FRAGMENT_SHADER,fragmentSource,"Underwater fragment");
    const program=gl.createProgram();gl.attachShader(program,vertex);gl.attachShader(program,fragment);gl.linkProgram(program);
    gl.deleteShader(vertex);gl.deleteShader(fragment);
    if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(program));
    const vao=gl.createVertexArray(), loc=n=>gl.getUniformLocation(program,"u_"+n);
    return {
        draw(target,state,time,width,height) {
            const s=state.settings;
            gl.useProgram(program);gl.bindVertexArray(vao);
            gl.activeTexture(gl.TEXTURE0);gl.bindSampler(0,null);gl.bindTexture(gl.TEXTURE_2D,target.texture);gl.uniform1i(loc("ColorTex"),0);
            gl.activeTexture(gl.TEXTURE1);gl.bindSampler(1,null);gl.bindTexture(gl.TEXTURE_2D,target.depthTexture);gl.uniform1i(loc("DepthTex"),1);
            for(const [name,value] of Object.entries({Time:time,Near:state.near,Far:state.far,IsOrthographic:state.orthographic?1:0,
                FogDensity:s.underwaterFogDensity,VisibilityDistance:s.underwaterVisibilityDistance,DistortionStrength:s.underwaterDistortionStrength,
                CausticsStrength:s.underwaterCausticsStrength,BubbleStrength:s.underwaterBubbleStrength,SurfaceDepth:state.surfaceDepth}))gl.uniform1f(loc(name),value);
            const v=c=>[c.x,c.y,c.z];gl.uniform3fv(loc("Tint"),v(s.underwaterTint));gl.uniform3fv(loc("FogColor"),v(s.underwaterFogColor));
            gl.uniform2f(loc("ViewportSize"),width,height);gl.drawArrays(gl.TRIANGLES,0,3);
            gl.bindTexture(gl.TEXTURE_2D,null);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,null);gl.bindVertexArray(null);
        },
        dispose(){gl.deleteVertexArray(vao);gl.deleteProgram(program);}
    };
}
