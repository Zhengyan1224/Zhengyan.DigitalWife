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
            shadowResolution:frame.shadowResolution,localShadowResolution:frame.localShadowResolution,
            shadowFilterSamples:frame.shadowFilterSamples,shadowFilterRadius:frame.shadowFilterRadius,
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
            for (const requestedSamples of [1,16]) {
                renderer.setFrameState({...base,viewProjection:view,lightColor:[0,0,0],ambientStrength:1,pointLights:[],spotLights:[],
                    features:{shadows:false,edges:false},
                    meshes:[{id:'mirror',model:identity,texturedPlane:true,mirrorReflectionStrength:1},
                        {id:'red',model:identity},{id:'blue',model:identity}],
                    reflections:[{id:'mirror',samples:requestedSamples,viewProjection:reflectionView,cameraPosition:[0,0,-side*2],clipPlane:[0,0,side,-.01]}]
                });
                // Both objects are outside the source camera's depth range. In
                // the reflected camera the blue object would cover the red one
                // if the mirror's clipping plane were ignored.
                renderer.render();
                const reflected = pixel(64,64);
                require(reflected[0]>240 && reflected[1]<10 && reflected[2]<10, 'Double-sided mirror reflection failed', {side,reflected});
                samples['mirror'+side+'-'+requestedSamples+'x'] = reflected;
            }
        }
        require(gl.getError() === gl.NO_ERROR,'WebGL error',{});
        return samples;
    } finally { renderer.dispose(); }
}

export async function verifyReflectionSamplingGpu(createRenderer) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 128;
    const renderer = createRenderer(canvas, message => { throw new Error(message); });
    const gl = canvas.getContext('webgl2');
    const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const plane = {
        positions: [-1,-1,0,1,-1,0,1,1,0,-1,1,0], normals: Array.from({ length: 4 }, () => [0,0,1]).flat(),
        uvs: [0,0,1,0,1,1,0,1], indices: [0,1,2,0,2,3],
        materials: [{ startIndex: 0, indexCount: 6, diffuse: [1,1,1,1], ambient: [1,1,1], doubleSided: true }]
    };
    const actor = {
        positions: [-.81,-.77,.5,.86,-.31,.5,-.37,.91,.5], normals: [0,0,1,0,0,1,0,0,1],
        uvs: [0,0,0,0,0,0], indices: [0,1,2],
        materials: [{ startIndex: 0, indexCount: 3, diffuse: [1,0,0,1], ambient: [1,1,1], doubleSided: true }]
    };
    try {
        await renderer.uploadMeshes([{ id: 'surface', mesh: plane }, { id: 'actor', mesh: actor }]);
        const base = { viewProjection: identity.map((v,i) => i === 10 ? 4 : v), lightViewProjection: identity,
            lightDirection: [0,0,-1], lightColor: [0,0,0], ambientColor: [1,1,1], ambientStrength: 1,
            clearColor: [0,0,0,1], cameraPosition: [0,0,2], features: { shadows: false, edges: false },
            meshes: [{ id: 'surface', texturedPlane: true, mirrorReflectionStrength: 1 }, { id: 'actor' }] };
        const coverage = {};
        for (const samples of [1,16]) {
            renderer.setFrameState({ ...base, reflections: [{ id: 'surface', samples, viewProjection: identity,
                cameraPosition: [0,0,-2], clipPlane: [0,0,1,-.01] }] });
            renderer.render();
            const pixels = new Uint8Array(128 * 128 * 4);
            gl.readPixels(0,0,128,128,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
            let solid = 0, partial = 0;
            for (let i = 0; i < pixels.length; i += 4) {
                if (pixels[i] > 252) solid++; else if (pixels[i] > 3) partial++;
            }
            if (solid < 100) throw new Error('Reflection resolve lost the red triangle.');
            coverage[samples] = { solid, partial };
        }
        const colors = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER,gl.RGBA8,gl.SAMPLES));
        const depths = Array.from(gl.getInternalformatParameter(gl.RENDERBUFFER,gl.DEPTH_COMPONENT24,gl.SAMPLES));
        const available = colors.some(n => n > 1 && n <= gl.getParameter(gl.SAMPLES) && depths.includes(n));
        if (available && coverage[16].partial <= coverage[1].partial + 10)
            throw new Error('MSAA reflection did not improve triangle edge coverage: ' + JSON.stringify(coverage));
        // Exercise the water shader's reflected color path with the same target.
        renderer.setFrameState({ meshes: [{ id: 'surface', waterSurface: true, mirrorReflectionStrength: 1 }, { id: 'actor' }] });
        renderer.render();
        const water = new Uint8Array(4);
        gl.readPixels(64,64,1,1,gl.RGBA,gl.UNSIGNED_BYTE,water);
        if (!(water[0] > 240 && water[1] < 240 && water[2] < 240))
            throw new Error('Water shader did not blend the reflected red geometry: ' + Array.from(water));
        if (gl.getError() !== gl.NO_ERROR) throw new Error('WebGL reflection MSAA raised a GL error.');
        return { coverage, water: Array.from(water), sceneSamples: gl.getParameter(gl.SAMPLES), colors, depths };
    } finally { renderer.dispose(); }
}

export async function verifyShadowQualityGpu(createRenderer) {
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 128;
    const renderer = createRenderer(canvas, message => { throw new Error(message); });
    const gl = canvas.getContext('webgl2');
    const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const mesh = {
        positions: [-1,-1,0,1,-1,0,1,1,0,-1,1,0, -.81,-.77,-.5,.86,-.31,-.5,-.37,.91,-.5],
        normals: Array.from({length:7},()=>[0,0,1]).flat(), uvs: Array.from({length:7},()=>[0,0]).flat(),
        indices: [0,1,2,0,2,3,4,5,6],
        materials: [
            {startIndex:0,indexCount:6,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true,enableShadow:false},
            {startIndex:6,indexCount:3,diffuse:[1,1,1,1],ambient:[1,1,1],doubleSided:true,enableShadow:true}
        ]
    };
    const results = {}, snapshots = {};
    try {
        await renderer.uploadMesh(mesh);
        const base = { viewProjection:identity.map((v,i)=>i===10?4:v), model:identity,lightViewProjection:identity,
            lightDirection:[0,0,-1],lightColor:[1,1,1],ambientColor:[0,0,0],ambientStrength:0,
            clearColor:[0,0,0,1],shadowEnabled:true,shadowStrength:1,shadowBias:0,shadowNormalOffset:0,
            features:{shadows:true,edges:false},cameraPosition:[0,0,2],reflections:[],localShadows:[],
            meshes:[{id:'default',model:identity,shadowEnabled:true,receiveShadow:true,texturedPlane:true}] };
        for (const [samples,radius,resolution] of [[1,0,256],[1,8,256],[9,0,256],[9,8,256],[49,8,256],[49,8,512]]) {
            renderer.setFrameState({...base,shadowFilterSamples:samples,shadowFilterRadius:radius,shadowResolution:resolution,localShadowResolution:256});
            renderer.render();
            const pixels = new Uint8Array(128*128*4); gl.readPixels(0,0,128,128,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
            let shadow=0,lit=0,partial=0;
            for(let i=0;i<pixels.length;i+=4) {
                if(pixels[i]<3) shadow++; else if(pixels[i]>252) lit++; else partial++;
            }
            const key=`${samples}-${radius}-${resolution}`;
            results[key]={shadow,lit,partial}; snapshots[key]=pixels;
            if(shadow<100||lit<100) throw new Error('Shadow quality lost shadow or illuminated pixels: '+key);
        }
        if(results['1-0-256'].partial!==results['1-8-256'].partial || results['1-0-256'].partial!==results['9-0-256'].partial)
            throw new Error('Radius zero or a single filter position did not preserve the base lookup.');
        if(results['9-8-256'].partial<=results['1-0-256'].partial*4+10)
            throw new Error('Increasing filter radius did not soften the shadow: '+JSON.stringify(results));
        let changed=0;
        for(let i=0;i<128*128*4;i+=4) changed+=Math.abs(snapshots['9-8-256'][i]-snapshots['49-8-256'][i]);
        if(changed<100) throw new Error('Increasing filter samples did not change shadow coverage.');
        if(results['49-8-512'].partial>=results['49-8-256'].partial)
            throw new Error('Shadow resolution did not change texel-sized filter coverage.');
        if(gl.getError()!==gl.NO_ERROR) throw new Error('WebGL shadow quality raised a GL error.');
        return results;
    } finally { renderer.dispose(); }
}

export async function verifyRuntimeGraphicsGpu(createRenderer) {
    const canvas=document.createElement('canvas');canvas.width=canvas.height=128;
    const renderer=createRenderer(canvas,message=>{throw new Error(message);});
    const gl=canvas.getContext('webgl2');
    const identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
    const mesh={positions:[-.81,-.77,0,.86,-.31,0,-.37,.91,0],normals:[0,0,1,0,0,1,0,0,1],uvs:[0,0,0,0,0,0],indices:[0,1,2],
        materials:[{startIndex:0,indexCount:3,diffuse:[1,0,0,1],ambient:[1,1,1],doubleSided:true}]};
    const samples=[];
    try {
        await renderer.uploadMesh(mesh);
        const base={viewProjection:identity,model:identity,lightViewProjection:identity,lightDirection:[0,0,-1],lightColor:[0,0,0],
            ambientColor:[1,1,1],ambientStrength:1,clearColor:[0,0,0,1],features:{shadows:false,edges:false},reflections:[],localShadows:[]};
        let revision=1;
        for(const requested of [4,1,2,4,8,16,1]) {
            renderer.setFrameState({...base,sceneAntiAliasingSamples:requested,graphicsRevision:revision++,overrideSceneSamples:true,graphicsScenePath:'main'});
            const report=renderer.render();
            if(!report || report.sceneSamples>requested)throw new Error('Runtime scene sampling report is inconsistent.');
            const pixels=new Uint8Array(128*128*4);gl.readPixels(0,0,128,128,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
            let partial=0,solid=0;
            for(let i=0;i<pixels.length;i+=4){if(pixels[i]===255)solid++;else if(pixels[i]>0)partial++;}
            if(solid<100 || (report.sceneSamples===1?partial!==0:partial===0))throw new Error(`Runtime scene coverage failed: ${requested}/${report.sceneSamples}, ${partial}`);
            if(renderer.render()!==undefined)throw new Error('Stable graphics re-issued telemetry.');
            samples.push({requested,actual:report.sceneSamples,partial});
        }
        if(gl.getError()!==gl.NO_ERROR)throw new Error('Dynamic Web scene sampling raised a GL error.');
        return samples;
    } finally {renderer.dispose();}
}
