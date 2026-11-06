import assert from 'node:assert/strict';
import test from 'node:test';
import {readFile} from 'node:fs/promises';
import { ammoFactory } from './helpers/ammo-fixture.mjs';
import { initialize, createWorld, stepWorld, destroyWorld } from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-physics.js';

await initialize(await ammoFactory());
const matrix = (x=0,y=0,z=0) => [1,0,0,0,0,1,0,0,0,0,1,0,x,y,z,1];
const body = (operation,transform,mass=1) => ({ operation, transform,mass:operation===0?0:mass,
    shape:0,size:[.1,.1,.1],group:0,mask:65535,linearDamping:0,angularDamping:0,restitution:0,friction:.5 });
const joint = { a:0,b:1,frameA:matrix(),frameB:matrix(0,1,0),linearLower:[0,0,0],linearUpper:[0,0,0],
    angularLower:[-1.2,-1.2,-1.2],angularUpper:[1.2,1.2,1.2],springs:[0,0,0,0,0,0] };

test('Bullet gravity is acceleration, independent of rigid-body mass', async () => {
    const transforms = [matrix(-5,100,0),matrix(5,100,0)];
    const id = await createWorld({ gravity:[0,-98,0],bodies:transforms.map((transform,i)=>body(1,transform,i?10:1)),joints:[] });
    try {
        let result;
        for (let frame=0;frame<60;frame++) result=stepWorld(id,transforms.flat(),1/60,frame===0);
        assert.ok(Math.abs(result[2]-result[10]) < .0001, `Mass changed gravity: ${result[2]} / ${result[10]}`);
        assert.ok(result[2] < 60 && result[2] > 40);
    } finally { destroyWorld(id); }
});

test('a constrained dynamic body preserves chain length and swings behind an animated kinematic target', async () => {
    const id = await createWorld({ gravity:[0,-98,0],bodies:[body(0,matrix(0,5,0)),body(1,matrix(0,4,0))],joints:[joint] });
    try {
        let maximumLength=0,maximumRotation=0,maximumLag=0;
        for (let frame=0;frame<360;frame++) {
            const x=.8*Math.sin(frame*.06);
            const values=stepWorld(id,[...matrix(x,5,0),...matrix(0,4,0)],1/60,frame===0);
            maximumLength=Math.max(maximumLength,Math.hypot(values[1]-x,values[2]-5,values[3]));
            maximumRotation=Math.max(maximumRotation,Math.hypot(values[4],values[5],values[6]));
            maximumLag=Math.max(maximumLag,Math.abs(values[1]-x));
        }
        assert.ok(maximumLength<1.2,`The joint chain stretched to ${maximumLength}`);
        assert.ok(maximumRotation>.05 && maximumLag>.1,`Missing inertial swing: ${maximumRotation}, ${maximumLag}`);
        const reset=stepWorld(id,[...matrix(0,5,0),...matrix(0,4,0)],0,true);
        assert.ok(Math.abs(reset[1])<.01 && Math.abs(reset[2]-4)<.01,'Reset retained the old simulated pose or contacts.');
    } finally { destroyWorld(id); }
    assert.throws(()=>stepWorld(id,[],1/60,false),/disposed/);
});

test('PMX masks keep filtering body pairs while the ground remains collidable', async () => {
    const id = await createWorld({gravity:[0,-98,0],bodies:[{...body(1,matrix(0,1,0)),mask:0}],joints:[]});
    try {
        let values;
        for(let frame=0;frame<240;frame++) values=stepWorld(id,matrix(0,1,0),1/60,frame===0);
        assert.ok(values[2]>.08 && values[2]<.15,`The PMX collision mask disabled the floor: ${values[2]}`);
    } finally {destroyWorld(id);}
});

test('real PMX hair/skirt joints stay within their translation limits during Lamb', async () => {
    const definition=JSON.parse(await readFile(new URL('../temp/web-debug/pmx-bullet-definition.json',import.meta.url),'utf8'));
    const raw=await readFile(new URL('../temp/web-debug/pmx-bullet-targets.bin',import.meta.url));
    const targets=new Float32Array(raw.buffer.slice(raw.byteOffset,raw.byteOffset+raw.byteLength));
    const count=definition.bodies.length,stride=count*16;
    const id=await createWorld(definition);
    const point=(transform,p)=>[p[0]*transform[0]+p[1]*transform[4]+p[2]*transform[8]+transform[12],
        p[0]*transform[1]+p[1]*transform[5]+p[2]*transform[9]+transform[13],p[0]*transform[2]+p[1]*transform[6]+p[2]*transform[10]+transform[14]];
    const dynamicPoint=(pose,p)=>{
        const [,, , ,x,y,z,w]=pose;
        const tx=2*(y*p[2]-z*p[1]),ty=2*(z*p[0]-x*p[2]),tz=2*(x*p[1]-y*p[0]);
        return [pose[1]+p[0]+w*tx+y*tz-z*ty,pose[2]+p[1]+w*ty+z*tx-x*tz,pose[3]+p[2]+w*tz+x*ty-y*tx];
    };
    let maximumExcess=0,maximumRotation=0,initialExcess=0,worst;
    try {
        for(let frame=0;frame<targets.length/stride;frame++) {
            const current=targets.subarray(frame*stride,(frame+1)*stride);
            const result=stepWorld(id,new Uint8Array(current.buffer,current.byteOffset,current.byteLength),1/60,frame===0);
            const states=new Map(); for(let i=0;i<result.length;i+=8) states.set(result[i],result.slice(i,i+8));
            for(const [jointIndex,joint] of definition.joints.entries()) {
                const anchor=(index,m)=>states.has(index)?dynamicPoint(states.get(index),m.slice(12,15)):point(current.subarray(index*16,index*16+16),m.slice(12,15));
                const a=anchor(joint.a,joint.frameA),b=anchor(joint.b,joint.frameB);
                const error=Math.hypot(...a.map((v,i)=>v-b[i]));
                const allowance=Math.hypot(...joint.linearLower.map((v,i)=>Math.max(Math.abs(v),Math.abs(joint.linearUpper[i]))));
                if(frame<5) initialExcess=Math.max(initialExcess,error-allowance);
                else if(error-allowance>maximumExcess) { maximumExcess=error-allowance; worst={frame,jointIndex,a:joint.a,b:joint.b,opA:definition.bodies[joint.a].operation,opB:definition.bodies[joint.b].operation,error,allowance}; }
            }
            for(const state of states.values()) maximumRotation=Math.max(maximumRotation,Math.hypot(state[4],state[5],state[6]));
        }
        console.log(JSON.stringify({maximumExcess,initialExcess,worst}));
        assert.ok(initialExcess<1.5,'The initial animated pose did not settle within a bounded joint error.');
        assert.ok(maximumExcess<.5,`PMX spring joints stretched beyond limits by ${maximumExcess} units`);
        assert.ok(maximumRotation>.1,'PMX dynamic bodies did not rotate.');
        console.log(JSON.stringify({bodyCount:count,jointCount:definition.joints.length,maximumJointExcess:maximumExcess}));
    } finally {destroyWorld(id);}
});
