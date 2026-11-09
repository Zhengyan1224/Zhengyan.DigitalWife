// The host deliberately retains the old bare JS URLs across an application
// upgrade. A working build must request its own module versions to escape them.
export async function inspectSceneEffects(page, screenshot) {
    await page.waitForFunction(()=>Number(document.body.innerText.match(/帧数\s*(\d+)/)?.[1])>10,{},{timeout:60000});
    await page.evaluate(async()=>{
        const bridge=await import(globalThis.__testRuntimeUrl);
        bridge.__testFreeze();
        await bridge.__testMove({lookX:900,lookY:-150,panX:0,panY:0,zoom:5,pointerX:0,pointerY:0,buttons:1});
        __testRenderer.setFrameState(__testFrame);__testRenderer.render();
    });
    await page.locator('canvas').first().screenshot({path:screenshot});
    return await page.evaluate(()=>{
        const frame=structuredClone(__testFrame),renderer=__testRenderer;
        const gl=document.querySelector('canvas').getContext('webgl2');
        const read=state=>{
            renderer.setFrameState(state);renderer.render();
            const data=new Uint8Array(gl.drawingBufferWidth*gl.drawingBufferHeight*4);
            gl.readPixels(0,0,gl.drawingBufferWidth,gl.drawingBufferHeight,gl.RGBA,gl.UNSIGNED_BYTE,data);return data;
        };
        const enabled=read(frame);
        const compare=data=>{
            let changed=0,darkened=0;
            for(let i=0;i<data.length;i+=4){let difference=0;for(let c=0;c<3;c++)difference+=Math.abs(enabled[i+c]-data[i+c]);
                if(difference>20)changed++;
                if(enabled[i]+enabled[i+1]+enabled[i+2]<data[i]+data[i+1]+data[i+2]-20)darkened++;}
            return {changed,darkened};
        };
        const point=compare(read({...frame,localShadows:frame.localShadows.filter(s=>s.pointLightIndex<0)}));
        const spot=compare(read({...frame,localShadows:frame.localShadows.filter(s=>s.spotLightIndex<0)}));
        const mirror=compare(read({...frame,reflections:[]}));
        renderer.setFrameState(frame);renderer.render();
        return {point,spot,mirror,localShadowFaces:frame.localShadows.length,reflections:frame.reflections.length,
            moduleUrl:__testRuntimeUrl,glError:gl.getError()};
    });
}
