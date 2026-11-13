// Water equations and underwater compositing follow the desktop renderer.
export const waterVertexUniforms = `
uniform bool u_waterSurface;
uniform vec4 u_waterWaves[4]; // direction, wave number, amplitude
uniform vec4 u_waterSpeeds; uniform float u_waterSteepness;
uniform int u_waterWaveCount; uniform float u_waterElapsed;
void displaceWater(inout vec3 position, inout vec3 normal) {
    vec3 displacement=vec3(0); vec2 gradient=vec2(0);
    for(int i=0;i<4;i++) {
        if(i>=u_waterWaveCount) break;
        vec4 w=u_waterWaves[i];
        float phase=w.z*(dot(w.xy,position.xz)-u_waterSpeeds[i]*u_waterElapsed);
        displacement+=vec3(w.x*u_waterSteepness*w.w*cos(phase),w.w*sin(phase),w.y*u_waterSteepness*w.w*cos(phase));
        gradient+=w.xy*w.w*w.z*cos(phase);
    }
    position+=displacement; normal=normalize(vec3(-gradient.x,1,-gradient.y));
}`;

export const waterFragmentUniforms = `
uniform sampler2D u_waterNormalA; uniform sampler2D u_waterNormalB; uniform sampler2D u_waterSky;
uniform bool u_waterHasNormals; uniform bool u_waterHasSky;
uniform float u_waterTime; uniform float u_waterLerp; uniform float u_waterTiling;
uniform vec3 u_waterDeepColor; uniform vec3 u_waterReflectionTint;
uniform float u_waterSkyStrength; uniform bool u_waterMirror;
uniform int u_rippleCount; uniform vec4 u_rippleCenters[48]; uniform vec2 u_rippleSizes[48];
uniform vec4 u_rippleParameters;
vec4 shadeWater(vec3 surfaceNormal) {
    vec3 normal=surfaceNormal;
    if(u_waterHasNormals) {
        vec2 uv=v_uv*u_waterTiling;
        vec3 a=texture(u_waterNormalA,uv*.1+vec2(u_waterTime)).xyz;
        vec3 b=texture(u_waterNormalB,uv*.1+vec2(u_waterTime)).xyz;
        vec3 c=texture(u_waterNormalA,uv*2.0+vec2(-u_waterTime,-u_waterTime*2.0)).xyz;
        vec3 d=texture(u_waterNormalB,uv*2.0+vec2(-u_waterTime,-u_waterTime*2.0)).xyz;
        vec3 detail=normalize((mix(b,a,u_waterLerp)+mix(d,c,u_waterLerp))-1.0).xzy;
        normal=normalize(normal+(detail-vec3(0,1,0))*.68);
    }
    float ripple=0.0, highlight=0.0;
    for(int i=0;i<48;i++) {
        if(i>=u_rippleCount) break;
        float distanceToCenter=distance(v_worldPosition.xz,u_rippleCenters[i].xz);
        float age=u_rippleCenters[i].w;
        float wave=sin(distanceToCenter*u_rippleParameters.z-age*u_rippleParameters.y);
        float envelope=exp(-age/max(u_rippleParameters.x,.001)*2.4)*exp(-pow(distanceToCenter/max(u_rippleSizes[i].x,.001),2.0));
        ripple+=wave*envelope*u_rippleSizes[i].y;
        highlight+=max(wave,0.0)*envelope*u_rippleSizes[i].y;
    }
    normal=normalize(normal+vec3(cos(v_worldPosition.x*8.0+u_waterTime)*ripple,
        abs(ripple)*1.15,sin(v_worldPosition.z*8.0+u_waterTime)*ripple)*u_rippleParameters.w);
    vec3 incident=normalize(v_worldPosition-u_cameraPosition);
    vec3 reflected=normalize(reflect(incident,normal));
    float horizon=clamp(reflected.y*.5+.5,0.0,1.0);
    float fresnel=pow(1.0-max(dot(-incident,normal),0.0),5.0);
    vec3 reflection=mix(u_waterDeepColor*.72,u_waterReflectionTint,horizon);
    vec2 skyUv=vec2(fract(atan(reflected.z,reflected.x)/6.2831853+.5),.5-asin(clamp(reflected.y,-1.0,1.0))/3.14159265);
    if(u_waterHasSky && u_waterMirror) reflection=mix(reflection,texture(u_waterSky,skyUv).rgb,u_waterSkyStrength);
    vec4 projected=u_reflectionViewProjection*vec4(v_worldPosition,1);
    vec2 uv=projected.xy/max(abs(projected.w),.0001)*.5+.5+normal.xz*.035;
    if(u_reflectionStrength>0.0 && all(greaterThanEqual(uv,vec2(0))) && all(lessThanEqual(uv,vec2(1))))
        reflection=mix(reflection,texture(u_reflectionTexture,clamp(uv,.001,.999)).rgb,.65);
    float weight=u_waterMirror?clamp(.35+fresnel*.65,0.0,1.0):.18;
    vec3 color=mix(u_waterDeepColor,reflection,weight);
    color=mix(color,mix(u_waterDeepColor,u_waterReflectionTint,.30),.42);
    color+=vec3(.22,.25,.28)*clamp(highlight,0.0,1.0);
    return vec4(clamp(color,0.0,1.0),u_diffuse.a);
}`;

const fullScreenVertex = `#version 300 es
precision highp float; out vec2 uv;
void main(){vec2 p=gl_VertexID==0?vec2(0):gl_VertexID==1?vec2(2,0):vec2(0,2);uv=p;gl_Position=vec4(p*2.0-1.0,1,1);}`;

function program(gl, compile, vertex, fragment, label) {
    const vs=compile(gl,gl.VERTEX_SHADER,vertex,label+" vertex"), fs=compile(gl,gl.FRAGMENT_SHADER,fragment,label+" fragment");
    const value=gl.createProgram(); gl.attachShader(value,vs); gl.attachShader(value,fs); gl.linkProgram(value);
    gl.deleteShader(vs); gl.deleteShader(fs);
    if(!gl.getProgramParameter(value,gl.LINK_STATUS)) throw new Error(label+": "+gl.getProgramInfoLog(value));
    return value;
}

export function createSceneEffects(gl, compile, textureCache) {
    const sky=program(gl,compile,fullScreenVertex,`#version 300 es
precision highp float; in vec2 uv; uniform mat4 inverseViewProjection; uniform sampler2D sky;
uniform vec3 tint; uniform float exposure; out vec4 color;
void main(){vec4 p=inverseViewProjection*vec4(uv*2.0-1.0,1,1);vec3 d=normalize(p.xyz/p.w);
vec2 st=vec2(fract(atan(d.z,d.x)/6.2831853+.5),.5-asin(clamp(d.y,-1.0,1.0))/3.14159265);
color=vec4(clamp(texture(sky,vec2(st.x,1.0-st.y)).rgb*tint*exposure,0.0,1.0),1);}`,"Skybox");
    const sprite=program(gl,compile,`#version 300 es
precision highp float; uniform vec4 rect; uniform vec2 viewport; uniform vec4 source; uniform float rotation;
out vec2 uv;
void main(){vec2 p=gl_VertexID==0?vec2(0,0):gl_VertexID==1?vec2(1,0):gl_VertexID==2?vec2(0,1):gl_VertexID==3?vec2(0,1):gl_VertexID==4?vec2(1,0):vec2(1,1);
uv=mix(source.xy,source.zw,p);vec2 q=(p-.5)*rect.zw;
q=mat2(cos(rotation),sin(rotation),-sin(rotation),cos(rotation))*q;
vec2 pixel=rect.xy+rect.zw*.5+q;gl_Position=vec4(pixel.x/viewport.x*2.0-1.0,1.0-pixel.y/viewport.y*2.0,0,1);}`,`#version 300 es
precision highp float; in vec2 uv;uniform sampler2D sprite;uniform float opacity;out vec4 color;
void main(){color=texture(sprite,vec2(uv.x,1.0-uv.y));color.a*=opacity;}`,"Scene Sprite");
    const particle=program(gl,compile,`#version 300 es
precision highp float;
layout(location=0) in vec3 position;layout(location=1) in vec4 tint;layout(location=2) in vec2 size;
layout(location=3) in float rotation;layout(location=4) in vec3 velocity;layout(location=5) in float aligned;
uniform mat4 viewProjection;uniform mat4 inverseSky;out vec2 uv;out vec4 color;out vec3 world;
void main(){vec2 p=gl_VertexID==0?vec2(-.5,-.5):gl_VertexID==1?vec2(.5,-.5):gl_VertexID==2?vec2(-.5,.5):gl_VertexID==3?vec2(-.5,.5):gl_VertexID==4?vec2(.5,-.5):vec2(.5,.5);
uv=vec2(p.x+.5,.5-p.y);vec3 right=normalize(inverseSky[0].xyz),up=normalize(inverseSky[1].xyz);
if(aligned>.5 && length(velocity)>.0001){vec3 eye=normalize(cross(right,up));up=normalize(velocity);vec3 side=cross(up,eye);if(length(side)>.0001)right=normalize(side);}
else p=mat2(cos(rotation),sin(rotation),-sin(rotation),cos(rotation))*p;
world=position+right*p.x*size.x+up*p.y*size.y;gl_Position=viewProjection*vec4(world,1);color=tint;}`,`#version 300 es
precision highp float;in vec2 uv;in vec4 color;in vec3 world;uniform sampler2D image;
uniform bool hasTexture;uniform bool textureColor;uniform int preset;uniform bool clipEnabled;uniform vec4 clipPlane;
out vec4 result;
void main(){if(clipEnabled && dot(vec4(world,1),clipPlane)<0.0)discard;
vec4 t=hasTexture?texture(image,vec2(uv.x,1.0-uv.y)):vec4(1);
if(!hasTexture){float d=length(uv-.5)*2.0;t.a=preset==1?(1.0-smoothstep(.1,1.0,abs(uv.x-.5)*2.0))*(1.0-smoothstep(.7,1.0,abs(uv.y-.5)*2.0)):1.0-smoothstep(.15,1.0,d);}
result=vec4(color.rgb*(textureColor?t.rgb:vec3(1)),color.a*t.a);if(result.a<.001)discard;}`,"Particle quads");
    const line=program(gl,compile,`#version 300 es
precision highp float;layout(location=0) in vec3 position;layout(location=1) in vec4 tint;
uniform mat4 viewProjection;out vec4 color;void main(){gl_Position=viewProjection*vec4(position,1);color=tint;}`,`#version 300 es
precision highp float;in vec4 color;out vec4 result;void main(){result=color;}`,"Debug lines");
    const vao=gl.createVertexArray(), particleVao=gl.createVertexArray(), lineVao=gl.createVertexArray(), lineBuffer=gl.createBuffer(), buffer=gl.createBuffer();
    const location=(p,n)=>gl.getUniformLocation(p,n);
    const bind=(p,n,unit,texture)=>{gl.activeTexture(gl.TEXTURE0+unit);gl.bindSampler(unit,null);gl.bindTexture(gl.TEXTURE_2D,texture);gl.uniform1i(location(p,n),unit);};
    return {
        lines(lines,viewProjection) {
            if(!lines?.length)return;
            const data=new Float32Array(lines.length*14);
            lines.forEach((l,i)=>data.set([...l.start,...l.color,...l.end,...l.color],i*14));
            gl.useProgram(line);gl.bindVertexArray(lineVao);gl.bindBuffer(gl.ARRAY_BUFFER,lineBuffer);gl.bufferData(gl.ARRAY_BUFFER,data,gl.STREAM_DRAW);
            gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,28,0);
            gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,4,gl.FLOAT,false,28,12);
            gl.uniformMatrix4fv(location(line,"viewProjection"),false,viewProjection);
            gl.enable(gl.DEPTH_TEST);gl.depthMask(false);gl.enable(gl.BLEND);gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
            gl.drawArrays(gl.LINES,0,lines.length*2);gl.depthMask(true);gl.disable(gl.BLEND);gl.bindVertexArray(null);
        },
        sky(state,inverse) {
            const texture=textureCache.get(state?.textureUri);
            if(!texture || !inverse) return;
            gl.disable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);gl.disable(gl.BLEND);gl.depthMask(false);
            gl.useProgram(sky);gl.bindVertexArray(vao);bind(sky,"sky",0,texture);
            gl.uniformMatrix4fv(location(sky,"inverseViewProjection"),false,inverse);
            gl.uniform3fv(location(sky,"tint"),state.tint);gl.uniform1f(location(sky,"exposure"),state.exposure);
            gl.drawArrays(gl.TRIANGLES,0,3);gl.depthMask(true);gl.enable(gl.DEPTH_TEST);
        },
        sprites(sprites,width,height,offsetX=0,offsetY=0,foreground=false) {
            gl.disable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);gl.depthMask(false);gl.enable(gl.BLEND);gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
            gl.useProgram(sprite);gl.bindVertexArray(vao);gl.uniform2f(location(sprite,"viewport"),width,height);
            for(const s of sprites.filter(s=>s.visible!==false && (foreground?(s.drawOrder??0)>=0:s.drawOrder<0)).sort((a,b)=>(a.drawOrder??0)-(b.drawOrder??0))) {
                const texture=textureCache.get(s.uri);if(!texture)continue;
                bind(sprite,"sprite",0,texture);gl.uniform4fv(location(sprite,"rect"),[s.x-offsetX,s.y-offsetY,s.width,s.height]);
                const tw=texture.width||1,th=texture.height||1;
                const source=s.sourceWidth>0 && s.sourceHeight>0?[s.sourceX/tw,s.sourceY/th,(s.sourceX+s.sourceWidth)/tw,(s.sourceY+s.sourceHeight)/th]:[0,0,1,1];
                gl.uniform4fv(location(sprite,"source"),source);gl.uniform1f(location(sprite,"rotation"),(s.rotation||0)*Math.PI/180);
                gl.uniform1f(location(sprite,"opacity"),s.opacity??1);gl.drawArrays(gl.TRIANGLES,0,6);
            }
            gl.depthMask(true);gl.enable(gl.DEPTH_TEST);gl.disable(gl.BLEND);
        },
        particles(batches,viewProjection,inverseSky,clipPlane=null,shadow=false) {
            gl.useProgram(particle);gl.bindVertexArray(particleVao);gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
            gl.uniformMatrix4fv(location(particle,"viewProjection"),false,viewProjection);
            gl.uniformMatrix4fv(location(particle,"inverseSky"),false,inverseSky);
            gl.uniform1i(location(particle,"clipEnabled"),clipPlane?1:0);gl.uniform4fv(location(particle,"clipPlane"),clipPlane||[0,0,0,0]);
            gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthMask(shadow);if(shadow)gl.disable(gl.BLEND);else gl.enable(gl.BLEND);
            for(const batch of batches) {
                if(!batch.vertices.length || (shadow && !batch.castShadows))continue;
                const data=new Float32Array(batch.vertices.length*14);
                batch.vertices.forEach((p,i)=>data.set([...p.position,...p.color,...p.size,p.rotation,...p.velocity,p.velocityAligned?1:0],i*14));
                gl.bufferData(gl.ARRAY_BUFFER,data,gl.STREAM_DRAW);
                for(const [index,count,offset] of [[0,3,0],[1,4,3],[2,2,7],[3,1,9],[4,3,10],[5,1,13]]) {
                    gl.enableVertexAttribArray(index);gl.vertexAttribPointer(index,count,gl.FLOAT,false,56,offset*4);gl.vertexAttribDivisor(index,1);
                }
                const texture=textureCache.get(batch.textureUri);bind(particle,"image",0,texture||textureCache.get("default"));
                gl.uniform1i(location(particle,"hasTexture"),texture?1:0);gl.uniform1i(location(particle,"textureColor"),batch.useTextureColor?1:0);
                gl.uniform1i(location(particle,"preset"),batch.texturePreset?.toLowerCase()==="streak"?1:0);
                gl.blendFuncSeparate(gl.SRC_ALPHA,batch.blendMode?.toLowerCase()==="additive"?gl.ONE:gl.ONE_MINUS_SRC_ALPHA,gl.ONE,gl.ONE_MINUS_SRC_ALPHA);
                gl.drawArraysInstanced(gl.TRIANGLES,0,6,batch.vertices.length);
            }
            gl.depthMask(true);gl.disable(gl.BLEND);gl.bindVertexArray(null);
        },
        dispose(){for(const p of [sky,sprite,particle,line])gl.deleteProgram(p);for(const v of [vao,particleVao,lineVao])gl.deleteVertexArray(v);gl.deleteBuffer(buffer);gl.deleteBuffer(lineBuffer);}
    };
}
