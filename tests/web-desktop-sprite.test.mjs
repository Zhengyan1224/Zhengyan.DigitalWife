import assert from 'node:assert/strict';
import test from 'node:test';
import {alphaRegions} from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-desktop-sprite.js';

test('alpha hit regions leave transparent holes and merge adjacent occupied rows', () => {
    const width=8,height=4,pixels=new Uint8Array(width*height*4);
    for(let y=0;y<height;y++)for(let x=0;x<width;x++)if(x<2||x>=6)pixels[(y*width+x)*4+3]=255;
    assert.deepEqual(alphaRegions(pixels,width,height),[[0,0,.25,1],[.75,0,.25,1]]);
    pixels.fill(0);assert.deepEqual(alphaRegions(pixels,width,height),[]);
    for(let i=3;i<pixels.length;i+=4)pixels[i]=255;
    assert.deepEqual(alphaRegions(pixels,width,height),[[0,0,1,1]]);
});

test('alpha masks retain visible translucent pixels while excluding near-zero alpha', () => {
    const pixels=new Uint8Array([0,0,0,0,0,0,0,7,0,0,0,8,0,0,0,128]);
    assert.deepEqual(alphaRegions(pixels,4,1),[[.5,0,.5,1]]);
});
