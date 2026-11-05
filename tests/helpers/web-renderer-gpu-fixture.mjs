// Run in a real WebGL2 browser context. The camera and light face opposite
// directions so the caster is hidden by the receiver in the color pass.
export async function verifyRendererGpu(createRenderer) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 128;
    const renderer = createRenderer(canvas, message => { throw new Error(message); });
    const identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
    const vertices = [-1,-1,0, 1,-1,0, 1,1,0, -1,1,0, -.4,-.4,.5, .4,-.4,.5, .4,.4,.5, -.4,.4,.5];
    const mesh = {
        positions: vertices, normals: Array.from({ length: 8 }, () => [0,0,1]).flat(),
        uvs: Array.from({ length: 2 }, () => [0,0,1,0,1,1,0,1]).flat(),
        indices: [0,1,2, 0,2,3, 4,5,6, 4,6,7],
        materials: [
            { startIndex: 0, indexCount: 6, diffuse: [1,1,1,1], ambient: [.5,.5,.5], doubleSided: true, enableShadow: false, receiveShadow: true },
            { startIndex: 6, indexCount: 6, diffuse: [1,1,1,1], ambient: [1,1,1], doubleSided: true, enableShadow: true, receiveShadow: false }
        ]
    };
    try {
        await renderer.uploadMesh(mesh);
        renderer.setFrameState({
            viewProjection: identity, model: identity, lightViewProjection: identity.map((v,i) => i === 10 ? -1 : v),
            lightDirection: [0,0,-1], lightColor: [.5,.5,.5], ambientColor: [1,1,1], ambientStrength: .5,
            clearColor: [0,0,0,1], shadowEnabled: true, shadowStrength: 1, shadowNormalOffset: 0,
            features: { shadows: true, edges: false }, cameraPosition: [0,0,-2]
        });
        const gl = canvas.getContext('webgl2');
        const pixel = (x,y) => { const data = new Uint8Array(4); gl.readPixels(x,y,1,1,gl.RGBA,gl.UNSIGNED_BYTE,data); return Array.from(data); };
        renderer.render();
        const shadowed = pixel(64,64), lit = pixel(8,8);
        renderer.setFrameState({ viewProjection: [...identity] });
        renderer.render();
        const cached = pixel(64,64);
        const transparent = document.createElement('canvas');
        transparent.width = transparent.height = 1;
        mesh.materials[1].textureUri = transparent.toDataURL();
        await renderer.uploadMesh(mesh);
        renderer.render();
        const alphaHole = pixel(64,64);
        const error = gl.getError();
        const near = (value, expected) => value.slice(0,3).every(channel => Math.abs(channel - expected) <= 3);
        if (!near(shadowed,64) || !near(lit,191) || !near(cached,64) || !near(alphaHole,191) || error)
            throw new Error(JSON.stringify({ shadowed, lit, cached, alphaHole, glError: error }));
        return { shadowed, lit, cached, alphaHole, glError: error };
    } finally { renderer.dispose(); }
}
