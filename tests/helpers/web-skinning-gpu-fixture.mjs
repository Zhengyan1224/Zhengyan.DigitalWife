// Capture the real vertex shader outputs before rasterization and compare
// them with the shared CPU solver's BDEF1/2/4 and morph results.
export async function verifySkinningGpu(createRenderer, fixture) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 32;
    const gl = canvas.getContext('webgl2');
    const link = gl.linkProgram.bind(gl), draw = gl.drawElements.bind(gl);
    let program = null, capture = null;
    gl.linkProgram = candidate => {
        if (gl.getAttachedShaders(candidate).some(shader => gl.getShaderSource(shader).includes('out vec3 v_worldPosition'))) {
            gl.transformFeedbackVaryings(candidate,['v_worldPosition','v_normal'],gl.INTERLEAVED_ATTRIBS);
            program = candidate;
        }
        link(candidate);
    };
    const buffer = gl.createBuffer(), feedback = gl.createTransformFeedback();
    const count = fixture.positions.length / 3;
    gl.drawElements = (...args) => {
        if (gl.getParameter(gl.CURRENT_PROGRAM) !== program) return draw(...args);
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK,feedback);
        gl.bindBuffer(gl.TRANSFORM_FEEDBACK_BUFFER,buffer);
        gl.bufferData(gl.TRANSFORM_FEEDBACK_BUFFER,count*6*4,gl.DYNAMIC_READ);
        gl.bindBufferBase(gl.TRANSFORM_FEEDBACK_BUFFER,0,buffer);
        gl.enable(gl.RASTERIZER_DISCARD);
        gl.beginTransformFeedback(gl.POINTS);
        gl.drawArrays(gl.POINTS,0,count);
        gl.endTransformFeedback();
        gl.disable(gl.RASTERIZER_DISCARD);
        capture = new Float32Array(count*6);
        gl.getBufferSubData(gl.TRANSFORM_FEEDBACK_BUFFER,0,capture);
        gl.bindBufferBase(gl.TRANSFORM_FEEDBACK_BUFFER,0,null);
        gl.bindTransformFeedback(gl.TRANSFORM_FEEDBACK,null);
    };
    const bytes = encoded => Uint8Array.from(atob(encoded),c=>c.charCodeAt(0));
    const renderer = createRenderer(canvas,message => { throw new Error(message); });
    try {
        const mesh = { ...fixture, materials: [{ startIndex:0,indexCount:3,diffuse:[1,1,1,1],doubleSided:true }],
            skinning: { ...fixture.skinning, bindings: bytes(fixture.skinning.bindings), bones: bytes(fixture.skinning.bones) } };
        const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        await renderer.uploadMesh(mesh);
        renderer.setFrameState({ viewProjection:identity, model:identity, lightViewProjection:identity,
            lightDirection:[0,-1,0],shadowEnabled:false,features:{ shadows:false,edges:false } });
        let maximumError = 0;
        for (const frame of fixture.frames) {
            renderer.updateMesh({ bones:bytes(frame.bones), morphs:bytes(frame.morphs) });
            capture = null;
            renderer.render();
            if (!capture) throw new Error('GPU skinning transform feedback did not run.');
            for (let i=0;i<capture.length;i++) {
                const error = Math.abs(capture[i]-frame.expected[i]);
                if (!Number.isFinite(error)) throw new Error('GPU skinning produced a non-finite vertex.');
                maximumError = Math.max(maximumError,error);
            }
        }
        const error = gl.getError();
        if (maximumError > .0002 || error) throw new Error(JSON.stringify({ maximumError,glError:error }));
        return { vertices:count,bones:fixture.skinning.boneCount,frames:fixture.frames.length,maximumError,glError:error };
    } finally {
        renderer.dispose(); gl.deleteBuffer(buffer); gl.deleteTransformFeedback(feedback);
    }
}
