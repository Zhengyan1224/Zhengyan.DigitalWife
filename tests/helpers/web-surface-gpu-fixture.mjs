// Pixel checks use the C# point/spot matrices, not a separate JS implementation.
export async function verifySurfaceGpu(createRenderer, frame) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 128;
    const renderer = createRenderer(canvas, message => { throw new Error(message); });
    const gl = canvas.getContext('webgl2');
    const identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
    const mesh = {
        positions: [-1,-1,0, 1,-1,0, 1,1,0, -1,1,0, -.4,-.4,.5, .4,-.4,.5, .4,.4,.5, -.4,.4,.5],
        normals: Array.from({length:8}, () => [0,0,1]).flat(), uvs: Array.from({length:8}, () => [0,0]).flat(),
        indices: [0,1,2,0,2,3,4,5,6,4,6,7],
        materials: [
            {startIndex:0,indexCount:6,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true,enableShadow:false},
            {startIndex:6,indexCount:6,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true,enableShadow:true}
        ]
    };
    const pixel = (x,y) => { const data = new Uint8Array(4); gl.readPixels(x,y,1,1,gl.RGBA,gl.UNSIGNED_BYTE,data); return Array.from(data); };
    const require = (condition, message, details) => { if (!condition) throw new Error(message + ': ' + JSON.stringify(details)); };
    try {
        await renderer.uploadMesh(mesh);
        const base = {
            viewProjection:identity, model:identity, lightViewProjection:identity,
            lightDirection:[0,0,-1],lightColor:[0,0,0],ambientColor:[1,1,1],ambientStrength:.1,
            clearColor:[0,0,0,1],shadowEnabled:true,shadowStrength:1,shadowNormalOffset:0,
            features:{shadows:true,edges:false},cameraPosition:[0,0,-2],localShadows:frame.localShadows,reflections:[]
        };
        const samples = {};
        for (const type of ['point','spot']) {
            renderer.setFrameState({...base,
                pointLights:type === 'point' ? [{...frame.pointLights[0],intensity:.8,color:[1,1,1]}] : [],
                spotLights:type === 'spot' ? [{...frame.spotLights[0],intensity:.8,color:[1,1,1]}] : []
            });
            renderer.render();
            const shadow = pixel(64,64), lit = pixel(112,64);
            renderer.setFrameState({viewProjection:[...identity]}); renderer.render();
            const cached = pixel(64,64);
            renderer.setFrameState({features:{shadows:false,edges:false}}); renderer.render();
            const disabled = pixel(64,64);
            samples[type] = {shadow,lit,cached,disabled};
            require(shadow[0] < lit[0] - 80 && disabled[0] > shadow[0] + 80 && Math.abs(cached[0] - shadow[0]) <= 2,
                'Local light shadow, disable flag or cache failed', samples[type]);
        }
        // A mirror plane is viewed from both sides. The red caster is
        // kept in the reflection, while a blue object behind the mirror is clipped.
        const plane = {
            positions:[-1,-1,0,1,-1,0,1,1,0,-1,1,0],normals:Array.from({length:4},()=>[0,0,1]).flat(),
            uvs:[0,0,1,0,1,1,0,1],indices:[0,1,2,0,2,3],
            materials:[{startIndex:0,indexCount:6,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true}]
        };
        const object = (z,color) => ({...plane, positions:plane.positions.map((v,i)=>i%3===2?z:v),
            materials:[{...plane.materials[0],diffuse:[...color,1]}]});
        for (const side of [1,-1]) {
            await renderer.uploadMeshes([{id:'mirror',mesh:plane},{id:'red',mesh:object(side*2,[1,0,0])},{id:'blue',mesh:object(-side*2,[0,0,1])}]);
            const view = identity.map((v,i)=>i===10?side:v);
            const reflectionView = identity.map((v,i)=>i===10?side*.25:v);
            renderer.setFrameState({...base,viewProjection:view,lightColor:[0,0,0],ambientStrength:1,pointLights:[],spotLights:[],
                features:{shadows:false,edges:false},
                meshes:[{id:'mirror',model:identity,texturedPlane:true,mirrorReflectionStrength:1},
                    {id:'red',model:identity},{id:'blue',model:identity}],
                reflections:[{id:'mirror',viewProjection:reflectionView,cameraPosition:[0,0,-side*2],clipPlane:[0,0,side,-.01]}]
            });
            // Both objects are outside the source camera's depth range. In
            // the reflected camera the blue object would cover the red one
            // if the mirror's clipping plane were ignored.
            renderer.render();
            const reflected = pixel(64,64);
            require(reflected[0]>240 && reflected[1]<10 && reflected[2]<10, 'Double-sided mirror reflection failed', {side,reflected});
            samples['mirror'+side] = reflected;
        }
        require(gl.getError() === gl.NO_ERROR,'WebGL error',{});
        return samples;
    } finally { renderer.dispose(); }
}
