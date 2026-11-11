export async function verifyParityGpu(createRenderer) {
    const canvas=document.createElement('canvas');canvas.width=128;canvas.height=96;
    const errors=[],renderer=createRenderer(canvas,error=>errors.push(error)),gl=canvas.getContext('webgl2');
    const identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const pixel=(x,y)=>{const p=new Uint8Array(4);gl.readPixels(x,y,1,1,gl.RGBA,gl.UNSIGNED_BYTE,p);return [...p];};
    const check=(value,message,data)=>{if(!value)throw new Error(message+': '+JSON.stringify(data));};
    const texture=color=>{const c=document.createElement('canvas');c.width=c.height=2;c.getContext('2d').fillStyle=color;c.getContext('2d').fillRect(0,0,2,2);return c.toDataURL();};
    const red=texture('red'),blue=texture('blue');
    const quad={positions:[-.35,-.35,0,.35,-.35,0,.35,.35,0,-.35,.35,0],normals:[0,0,1,0,0,1,0,0,1,0,0,1],uvs:[0,0,1,0,1,1,0,1],indices:[0,1,2,0,2,3],
        materials:[{startIndex:0,indexCount:6,diffuse:[0,1,0,1],ambient:[1,1,1],doubleSided:true}]};
    const base={viewProjection:identity,model:identity,lightViewProjection:identity,cameraPosition:[0,0,2],lightDirection:[0,0,-1],lightColor:[0,0,0],ambientColor:[1,1,1],ambientStrength:1,
        clearColor:[0,0,0,1],features:{shadows:false,edges:false,particles:false},meshes:[{id:'actor',model:identity}],reflections:[],waters:[],particleBatches:[]};
    const camera=(id,viewport,materials)=>({id,viewport,viewProjection:identity,skyInverseViewProjection:identity,cameraPosition:[0,0,2],reflections:[],underwater:null,meshes:[{id:'actor',model:identity,materials}]});
    try {
        await renderer.uploadMeshes([{id:'actor',mesh:quad}]);await renderer.loadSceneTextures([red,blue]);
        renderer.setSprites([{uri:blue,x:20,y:15,width:90,height:65,visible:true,opacity:1,drawOrder:-1}]);
        renderer.setFrameState({...base,sky:{textureUri:red,tint:[1,1,1],exposure:1},cameras:[camera('main',[0,0,128,96])]});renderer.render();
        const sky=pixel(5,5),background=pixel(25,25),actor=pixel(64,48);
        check(sky[0]>240 && sky[1]<10,'Skybox did not draw',sky);
        check(background[2]>240 && background[0]<10,'Background sprite did not draw',background);
        check(actor[1]>240 && actor[2]<10,'Background sprite covered 3D geometry',actor);
        renderer.setSprites([]);
        renderer.setFrameState({sky:null,cameras:[camera('main',[0,0,128,96]),camera('second',[80,8,40,30],[{...quad.materials[0],diffuse:[1,0,0,1]}])]});renderer.render();
        const second=pixel(100,23),main=pixel(64,48);
        check(second[0]>240 && second[1]<10 && main[1]>240,'Second camera placement or first camera preservation failed',{second,main});
        const settings={underwaterTint:{x:.58,y:.88,z:.95},underwaterFogColor:{x:.02,y:.2,z:.28},underwaterFogDensity:1,underwaterVisibilityDistance:5,
            underwaterDistortionStrength:.01,underwaterCausticsStrength:.28,underwaterBubbleStrength:.18};
        const submerged={...camera('main',[0,0,128,96]),underwater:{settings,surfaceDepth:1,near:.1,far:10,orthographic:false}};
        renderer.setFrameState({cameras:[submerged],time:1});renderer.render();const wet=pixel(64,48);
        check(wet[1]<main[1]-10 && wet[2]>=main[2]+3,'Underwater tint/fog did not composite',{wet,main});
        renderer.setFrameState({cameras:[camera('main',[0,0,128,96])]});renderer.render();const exited=pixel(64,48);
        check(exited[1]>240 && exited[2]<10,'Underwater state leaked after leaving water',exited);
        const gradient=document.createElement('canvas');gradient.width=gradient.height=4;
        const ctx=gradient.getContext('2d');ctx.fillStyle='red';ctx.fillRect(0,0,4,2);ctx.fillStyle='blue';ctx.fillRect(0,2,4,2);
        const gradientUri=gradient.toDataURL();await renderer.loadSceneTextures([gradientUri]);
        renderer.setSprites([{uri:gradientUri,x:0,y:0,width:32,height:96,visible:true,opacity:1,drawOrder:-1}]);
        renderer.render();const top=pixel(16,80),bottom=pixel(16,16);
        check(top[0]>230 && bottom[2]>230,'Background sprite image is vertically inverted',{top,bottom});
        renderer.setSprites([{uri:'rt:preview',x:0,y:0,width:32,height:32,visible:true,opacity:1,drawOrder:1}]);
        const rt={id:'preview-id',name:'preview',refreshMode:'fixed_rate',refreshIntervalSeconds:1,clearColor:[0,0,0,1],camera:camera('rt',[0,0,32,32],[{...quad.materials[0],diffuse:[1,0,0,1]}])};
        renderer.setFrameState({renderTextures:[rt],time:1});renderer.render();const firstRt=pixel(16,80);
        check(firstRt[0]>240 && firstRt[1]<10,'RenderTexture sprite did not sample its camera',firstRt);
        rt.camera=camera('rt',[0,0,32,32]);renderer.setFrameState({renderTextures:[rt],time:1.2});renderer.render();
        check(pixel(16,80)[0]>240,'RenderTexture ignored its fixed refresh interval',pixel(16,80));
        renderer.setFrameState({time:2.1});renderer.render();const refreshedRt=pixel(16,80);
        check(refreshedRt[1]>240 && refreshedRt[0]<10,'RenderTexture failed to refresh',refreshedRt);
        renderer.setFrameState({renderTextures:[]});renderer.render();
        check(pixel(16,80)[1]<10,'A removed RenderTexture retained its texture alias',pixel(16,80));
        check(gl.getError()===gl.NO_ERROR,'WebGL errors after sky, background, cameras and underwater',errors);
        check(!errors.length,'Renderer reported errors',errors);
        return {sky,background,actor,second,main,wet,exited,top,bottom,firstRt,refreshedRt};
    } finally {renderer.dispose();}
}
