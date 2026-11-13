export async function verifyScriptShader(createRenderer){
    const canvas=document.createElement('canvas');canvas.width=canvas.height=64;
    const errors=[],renderer=createRenderer(canvas,error=>errors.push(error));
    const identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const mesh={positions:[-1,-1,0,1,-1,0,1,1,0,-1,1,0],normals:[0,0,1,0,0,1,0,0,1,0,0,1],uvs:[0,0,1,0,1,1,0,1],indices:[0,1,2,0,2,3],materials:[{startIndex:0,indexCount:6,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true}]};
    const gl=canvas.getContext('webgl2'),pixel=()=>{const value=new Uint8Array(4);gl.readPixels(32,32,1,1,gl.RGBA,gl.UNSIGNED_BYTE,value);return [...value];};
    try{
        await renderer.uploadMeshes([{id:'actor',mesh}]);
        if(!renderer.registerCustomShader('fixture',`#version 300 es
precision highp float;
layout(location=0) in vec3 a_position;
uniform mat4 u_model,u_viewProjection;
void main(){gl_Position=u_viewProjection*u_model*vec4(a_position,1.0);}`,`#version 300 es
precision highp float;
uniform vec4 tint;
out vec4 outputColor;
void main(){outputColor=tint;}`))throw new Error(errors.join('\n'));
        renderer.setFrameState({viewProjection:identity,lightViewProjection:identity,clearColor:[0,0,0,1],lightColor:[0,0,0],ambientStrength:1,ambientColor:[1,1,1],features:{edges:false,shadows:false},meshes:[{id:'actor',model:identity,customShader:{id:'fixture',uniforms:{tint:{values:[1,0,0,1]}}}}]});
        renderer.render();const red=pixel();
        renderer.setFrameState({meshes:[{id:'actor',model:identity,customShader:{id:'fixture',uniforms:{tint:{values:[0,1,0,1]}}}}]});renderer.render();const green=pixel();
        renderer.setFrameState({meshes:[{id:'actor',model:identity,customShader:{id:'fixture',uniforms:{}}}]});renderer.render();const cleared=pixel();
        renderer.setFrameState({meshes:[{id:'actor',model:identity}]});renderer.render();const standard=pixel();
        if(red[0]<250||red[1]>5||green[1]<250||green[0]>5||cleared.slice(0,3).some(v=>v>5)||standard.slice(0,3).some(v=>v<245)||gl.getError()||errors.length)throw new Error(JSON.stringify({red,green,cleared,standard,errors}));
        return {red,green,cleared,standard};
    }finally{renderer.dispose();}
}
