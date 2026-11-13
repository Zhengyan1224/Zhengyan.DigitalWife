import assert from 'node:assert/strict';
import test from 'node:test';
import {readFile} from 'node:fs/promises';
import {setTimeout as tick} from 'node:timers/promises';
const moduleUrl=new URL('../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-offline.js',import.meta.url);
const source=(await readFile(moduleUrl,'utf8')).replaceAll('import.meta.url',JSON.stringify(moduleUrl.href));

async function microphoneFixture(t, resume=async()=>{}) {
    const descriptors=new Map(['Worker','AudioContext','navigator'].map(key=>[key,Object.getOwnPropertyDescriptor(globalThis,key)]));
    const workers=[],processors=[],tracks=[],events=[];
    const node=()=>({connect(){},disconnect(){}});
    globalThis.Worker=class {
        constructor(){this.messages=[];workers.push(this);}
        postMessage(m){this.messages.push(m);if(m.type==='init')queueMicrotask(()=>this.onmessage({data:{type:'ready'}}));}
        terminate(){}
    };
    globalThis.AudioContext=class {
        sampleRate=48000;destination={};resume=resume;async close(){}
        createMediaStreamSource(){return node();}createGain(){return {...node(),gain:{value:0}};}
        createScriptProcessor(){const p=node();processors.push(p);return p;}
    };
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{mediaDevices:{getUserMedia:async()=>{
        const track={stops:0,stop(){this.stops++;}};tracks.push(track);return {getTracks:()=>[track]};
    }}}});
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64')+'#'+t.name);
    t.after(async()=>{await offline.dispose();for(const [key,d] of descriptors){if(d)Object.defineProperty(globalThis,key,d);else delete globalThis[key];}});
    await offline.configure({voice:{},asr:{}});
    const reference={invokeMethodAsync:async(...args)=>{events.push(args);}};
    const command={operation:'wake',requestId:'one',entityId:'actor',wakeWords:['hello'],completedCallback:'done',errorCallback:'error'};
    return {offline,tracks,processors,workers,events,reference,command,
        sample:()=>processors.at(-1).onaudioprocess({inputBuffer:{getChannelData:()=>new Float32Array(4096)}}),
        result:async(text='',endpoint=false)=>{
            const worker=workers[0],m=worker.messages.filter(m=>m.type==='asr').at(-1);
            assert(m);await worker.onmessage({data:{type:'result',id:m.id,text,endpoint}});await tick();
        }};
}

test('cancelling while audio resume is pending releases the microphone immediately',async t=>{
    let resume;
    const app=await microphoneFixture(t,()=>new Promise(resolve=>{resume=resolve;}));
    app.offline.executeAsr(app.command,app.reference);await tick();
    assert.equal(app.tracks.length,1);
    app.offline.stopAsr(true);assert.equal(app.tracks[0].stops,1);
    resume();await tick();assert.equal(app.processors.length,0);
});

test('wake monitoring retains the microphone across silence endpoints',async t=>{
    const app=await microphoneFixture(t);
    app.offline.executeAsr(app.command,app.reference);await tick();
    for(let i=0;i<4;i++){app.sample();await tick();await app.result('',true);}
    assert.equal(app.tracks[0].stops,0);
    app.sample();await tick();await app.result('hello');
    assert.equal(app.events[0][2].eventName,'wake_detected');
    assert.equal(app.tracks[0].stops,0);
});

test('wake timing and tail padding finish a segment without closing the microphone',async t=>{
    const app=await microphoneFixture(t);
    Object.assign(app.command,{chunkDurationSeconds:.1,extensionDurationSeconds:0,trailingSilencePaddingSeconds:.1});
    app.offline.executeAsr(app.command,app.reference);await tick();
    app.sample();await tick();await app.result();
    app.sample();await tick();await app.result();
    const finish=app.workers[0].messages.filter(m=>m.type==='asr').at(-1);
    assert.equal(finish.finished,true);assert.equal(finish.samples.length,1600);
    await app.result('hello');assert.equal(app.events.at(-1)[2].wakeWord,'hello');
    assert.equal(app.tracks[0].stops,0);
});

test('a stale endpoint cannot stop a replacement microphone or emit callbacks',async t=>{
    const app=await microphoneFixture(t);
    app.offline.executeAsr({...app.command,operation:'start',finishOnEndpoint:true},app.reference);await tick();
    app.sample();await tick();
    app.offline.executeAsr({...app.command,requestId:'two'},app.reference);await tick();
    await app.result('hello',true);
    assert.equal(app.tracks[1].stops,0);assert.equal(app.events.length,0);
});

test('replacement cancels a pending final transcript without finishing the new recognizer',async t=>{
    const app=await microphoneFixture(t);
    app.offline.executeAsr({...app.command,operation:'start'},app.reference);await tick();
    app.sample();await tick();app.offline.stopAsr(false);
    app.offline.executeAsr({...app.command,requestId:'two'},app.reference);await tick();
    await app.result('old question',true);
    assert.equal(app.workers[0].messages.some(m=>m.finished),false);
    assert.equal(app.tracks[1].stops,0);assert.equal(app.events.length,0);
});

test('local realtime voice streams Unicode from the configured online API into local TTS',async t=>{
    const app=await microphoneFixture(t),original=globalThis.fetch,requests=[];
    t.after(()=>{globalThis.fetch=original;});
    globalThis.fetch=async(url,options)=>{
        requests.push({url:String(url),options});
        const bytes=new TextEncoder().encode('data: {"choices":[{"delta":{"content":"你好"}}]}\r\n\r\ndata: [DONE]');
        return new Response(new ReadableStream({start(controller){for(const byte of bytes)controller.enqueue(Uint8Array.of(byte));controller.close();}}));
    };
    await app.offline.configure({voice:{},asr:{},realtimeVoice:{},llm:{enabled:true,baseUrl:'https://api.test/prefix',chatCompletionsPath:'/v1/chat/completions',model:'configured-model',apiKey:'fixture-key'}});
    app.offline.executeRealtime({operation:'response',requestId:'chat',entityId:'actor',text:'hello',deltaCallback:'delta',completedCallback:'done',errorCallback:'error'},app.reference);
    for(let i=0;i<50&&!app.workers[0]?.messages.some(m=>m.type==='tts');i++)await tick(5);
    assert.equal(requests[0].url,'https://api.test/prefix/v1/chat/completions');
    assert.equal(requests[0].options.headers.Authorization,'Bearer fixture-key');
    assert.equal(JSON.parse(requests[0].options.body).model,'configured-model');
    assert.equal(app.events.find(e=>e[2]?.eventName==='delta')[2].delta,'你好');
    assert.equal(app.workers[0].messages.find(m=>m.type==='tts').text,'你好');
});
test('cancelling ASR before WASM initialization never resets an uninitialized recognizer',async t=>{
    const oldWorker=globalThis.Worker,oldNavigator=Object.getOwnPropertyDescriptor(globalThis,'navigator'),workers=[];
    globalThis.Worker=class{constructor(){this.messages=[];workers.push(this);}postMessage(message){if(message.type==='reset'&&!this.initialized)throw new Error('Recognizer is not initialized');this.messages.push(message);}terminate(){}};
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{mediaDevices:{getUserMedia:async()=>{throw new Error('No test microphone');}}}});
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64')+'#asr-cancel');
    t.after(async()=>{await offline.dispose();await offline.configure({web:{runtimeMode:'connected'}});if(oldWorker===undefined)delete globalThis.Worker;else globalThis.Worker=oldWorker;if(oldNavigator)Object.defineProperty(globalThis,'navigator',oldNavigator);else delete globalThis.navigator;});
    await offline.configure({name:'ASR cancellation fixture',web:{runtimeMode:'offline'},asr:{},voice:{}});
    const events=[],reference={invokeMethodAsync:(...args)=>{events.push(args);return Promise.resolve();}};
    offline.executeAsr({operation:'start',requestId:'one',entityId:'actor',errorCallback:'error'},reference);
    offline.stopAsr(true);assert.deepEqual(workers[0].messages.map(m=>m.type),['init']);
    workers[0].initialized=true;await workers[0].onmessage({data:{type:'ready'}});await new Promise(r=>setTimeout(r,0));
    assert.equal(events.length,0);assert.deepEqual(workers[0].messages.map(m=>m.type),['init']);
});


test('loading prewarms both local speech engines once without opening a microphone',async t=>{
    const oldWorker=globalThis.Worker,workers=[],events=[];
    globalThis.Worker=class{
        constructor(){this.messages=[];workers.push(this);}
        postMessage(message){this.messages.push(message);if(message.type==='init')queueMicrotask(()=>this.onmessage({data:{type:'ready'}}));
            if(message.type==='tts'||message.type==='asr')queueMicrotask(()=>this.onmessage({data:{type:'result',id:message.id,samples:new Float32Array([0.1]),sampleRate:16000,text:''}}));}
        terminate(){this.terminated=true;}
    };
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64')+'#preload');
    t.after(async()=>{await offline.dispose();if(oldWorker===undefined)delete globalThis.Worker;else globalThis.Worker=oldWorker;});
    const project={name:'preload',voice:{enabled:true},asr:{enabled:true}};
    await offline.configure(project);
    await offline.preload({invokeMethodAsync:(...args)=>{events.push(args);return Promise.resolve();}});
    assert.deepEqual(workers.map(w=>w.messages.map(m=>m.type)),[['init','tts'],['init','asr','reset']]);
    assert.equal(events.length,2);
    await offline.configure(project);await offline.preload();assert.equal(workers.length,2);
    await offline.configure({...project,name:'another'});assert.ok(workers.every(w=>w.terminated));
});

test('local voice leaves the configured online LLM API and global fetch available',async t=>{
    const original=globalThis.fetch;
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64')+'#online');
    t.after(()=>offline.dispose());
    await offline.configure({web:{runtimeMode:'offline'},llm:{baseUrl:'https://api.test'}});
    assert.equal(globalThis.fetch,original);
    assert.equal(offline.getLanguageModel,undefined);
    assert.equal(offline.setLanguageModelFiles,undefined);
});

test('an old ASR failure cannot close the microphone of its replacement session',async t=>{
    const oldWorker=globalThis.Worker,oldAudio=globalThis.AudioContext,oldNavigator=Object.getOwnPropertyDescriptor(globalThis,'navigator');
    const workers=[],processors=[],tracks=[];
    const node=()=>({connect(){},disconnect(){}});
    globalThis.Worker=class{constructor(){workers.push(this);this.messages=[];}postMessage(m){this.messages.push(m);if(m.type==='init')queueMicrotask(()=>this.onmessage({data:{type:'ready'}}));}terminate(){}};
    globalThis.AudioContext=class{sampleRate=48000;destination={};async resume(){}async close(){}createMediaStreamSource(){return node();}createGain(){return {...node(),gain:{value:0}};}createScriptProcessor(){const p=node();processors.push(p);return p;}};
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{mediaDevices:{getUserMedia:async()=>{const track={stops:0,stop(){this.stops++;}};tracks.push(track);return {getTracks:()=>[track]};}}}});
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64')+'#stale-asr');
    t.after(async()=>{await offline.dispose();globalThis.Worker=oldWorker;globalThis.AudioContext=oldAudio;if(oldNavigator)Object.defineProperty(globalThis,'navigator',oldNavigator);else delete globalThis.navigator;});
    await offline.configure({voice:{},asr:{}});
    const reference={invokeMethodAsync:()=>Promise.resolve()},tick=()=>new Promise(r=>setTimeout(r,0));
    offline.executeAsr({operation:'wake',requestId:'old'},reference);await tick();
    processors[0].onaudioprocess({inputBuffer:{getChannelData:()=>new Float32Array(4096)}});await tick();
    const pending=workers[0].messages.find(m=>m.type==='asr');assert(pending);
    offline.executeAsr({operation:'wake',requestId:'new'},reference);await tick();
    assert.equal(tracks[0].stops,1);assert.equal(tracks[1].stops,0);
    await workers[0].onmessage({data:{type:'error',id:pending.id,message:'old request failed'}});await tick();
    assert.equal(tracks[1].stops,0);
    offline.stopAsr(true);assert.equal(tracks[1].stops,1);
});
