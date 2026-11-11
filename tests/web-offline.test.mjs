import assert from 'node:assert/strict';
import test from 'node:test';
import {readFile} from 'node:fs/promises';
const moduleUrl=new URL('../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-offline.js',import.meta.url);
const source=(await readFile(moduleUrl,'utf8')).replace(/import\(new URL\('\.\.\/vendor\/wllama\/esm\/index\.js',import.meta.url\).href\)/,'Promise.resolve({Wllama:globalThis.__offlineWllamaFixture})').replaceAll('import.meta.url',JSON.stringify(moduleUrl.href));
test('offline SSE uses the local model, serializes inference, cancels readers and replaces project models',async t=>{
    const originalFetch=globalThis.fetch,oldLocation=globalThis.location;let loads=0,exits=0,active=0,maxActive=0;
    globalThis.location={href:'https://game.test/',origin:'https://game.test'};
    globalThis.fetch=async input=>new Response(new Blob(['local GGUF']));
    globalThis.__offlineWllamaFixture=class{
        async loadModel(){loads++;}async exit(){exits++;}
        async createChatCompletion(){return (async function*(){active++;maxActive=Math.max(maxActive,active);try{yield {choices:[{delta:{content:'你'}}]};await new Promise(r=>setTimeout(r,10));yield {choices:[{delta:{content:'好'}}]};}finally{active--;}})();}
    };
    const offline=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
    t.after(async()=>{await offline.dispose();await offline.configure({web:{runtimeMode:'connected'}});globalThis.fetch=originalFetch;if(oldLocation===undefined)delete globalThis.location;else globalThis.location=oldLocation;delete globalThis.__offlineWllamaFixture;});
    const project={name:'one',web:{runtimeMode:'offline',languageModelPath:'model'},llm:{baseUrl:'https://remote.test',chatCompletionsPath:'/v1/chat/completions'}};
    await offline.configure(project,{model:'https://game.test/model'});
    await assert.rejects(fetch('https://external.test/path'),/unavailable in offline mode/);
    const request=()=>fetch('https://remote.test/v1/chat/completions',{method:'POST',body:JSON.stringify({messages:[{role:'user',content:'hello'}]})});
    const a=await request(),b=await request();const texts=await Promise.all([a.text(),b.text()]);assert.ok(texts.every(text=>text.includes('你')&&text.includes('好')&&text.includes('[DONE]')));assert.equal(loads,1);assert.equal(maxActive,1);
    const response=await request(),reader=response.body.getReader();await reader.read();await reader.cancel();
    const next=await request();await next.text();assert.equal(active,0);
    await offline.configure(project,{model:'https://game.test/model'});assert.equal(exits,0);
    await offline.configure({...project,name:'two'},{model:'https://game.test/new-model'});assert.equal(exits,1);await (await request()).text();assert.equal(loads,2);
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
