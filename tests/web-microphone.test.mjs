import assert from 'node:assert/strict';
import test from 'node:test';
import {setTimeout as tick} from 'node:timers/promises';

async function fixture(t, acquire, devices=[]) {
    const previous=new Map(['document','navigator','Option'].map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
    const element=tag=>({tag,children:[],handlers:new Map(),value:'',setAttribute(){},
        append(...children){for(const child of children){child.parent=this;this.children.push(child);}},
        remove(){if(this.parent)this.parent.children=this.parent.children.filter(c=>c!==this);},
        addEventListener(type,handler){this.handlers.set(type,handler);},
        replaceChildren(...children){this.children=children;},add(child){this.children.push(child);},
        get options(){return this.children;}});
    const frame=element('frame');
    Object.defineProperty(globalThis,'document',{configurable:true,value:{body:frame,querySelector:()=>frame,createElement:element}});
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{permissions:{query:async()=>({state:'prompt'})},
        mediaDevices:{getUserMedia:acquire,enumerateDevices:async()=>devices}}});
    globalThis.Option=class{constructor(label,value){this.label=label;this.value=value;}};
    const mic=await import(`../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-microphone.js?test=${encodeURIComponent(t.name)}`);
    t.after(()=>{for(const [key,value] of previous){if(value)Object.defineProperty(globalThis,key,value);else delete globalThis[key];}});
    return {mic,frame,click:async()=>{frame.children[0].children.find(c=>c.tag==='button').handlers.get('click')();await tick();}};
}

test('consent passes the opened stream directly to capture without reopening or stopping it',async t=>{
    let calls=0,stops=0;
    const stream={getTracks:()=>[{stop(){stops++;}}]};
    const app=await fixture(t,async()=>{calls++;return stream;});
    const request=app.mic.requestMicrophone(new AbortController().signal);await tick();
    assert.equal(calls,0);
    await app.click();
    assert.equal(await request,stream);assert.equal(calls,1);assert.equal(stops,0);assert.equal(app.frame.children.length,0);
});

test('a rejected capture waits for one explicit retry and retains the original request',async t=>{
    let calls=0,fail=true;
    const stream={getTracks:()=>[{stop(){}}]};
    const app=await fixture(t,async()=>{calls++;if(fail)throw new DOMException('denied','NotAllowedError');return stream;});
    const request=app.mic.requestMicrophone(new AbortController().signal);await tick();
    await app.click();await tick();assert.equal(calls,1);
    assert.match(app.frame.children[0].children.at(-1).textContent,/允许/);
    fail=false;await app.click();assert.equal(await request,stream);assert.equal(calls,2);
});

test('an unreadable default endpoint falls back to a physical input device',async t=>{
    const requests=[],stream={getTracks:()=>[{stop(){}}]};
    const app=await fixture(t,async constraints=>{requests.push(constraints);if(constraints.audio===true)throw new DOMException('cannot start','NotReadableError');return stream;},
        [{kind:'audioinput',deviceId:'default'},{kind:'audioinput',deviceId:'communications'},{kind:'audioinput',deviceId:'physical',label:'Microphone'}]);
    const request=app.mic.requestMicrophone(new AbortController().signal);await tick();await app.click();
    assert.equal(await request,stream);
    assert.deepEqual(requests,[{audio:true},{audio:{deviceId:{exact:'physical'}}}]);
});

test('cancelling during an outstanding consent request stops the returned stream',async t=>{
    let finish,stops=0;
    const app=await fixture(t,()=>new Promise(resolve=>{finish=resolve;}));
    const abort=new AbortController(),request=app.mic.requestMicrophone(abort.signal);
    const rejected=assert.rejects(request,{name:'AbortError'});await tick();await app.click();
    abort.abort();await rejected;finish({getTracks:()=>[{stop(){stops++;}}]});await tick();
    assert.equal(stops,1);assert.equal(app.frame.children.length,0);
});
