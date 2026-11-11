import assert from 'node:assert/strict';
import test from 'node:test';
import * as realtime from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-realtime.js';

const settle=()=>new Promise(resolve=>setTimeout(resolve,10));
function setup(t){
    const old=globalThis.WebSocket,sockets=[],events=[];
    globalThis.WebSocket=class {
        constructor(url,protocols){this.url=url;this.protocols=protocols;this.readyState=1;this.sent=[];sockets.push(this);queueMicrotask(()=>this.onopen?.());}
        send(text){const value=JSON.parse(text);this.sent.push(value);if(value.type==='session.update')queueMicrotask(()=>this.message({type:'session.updated'}));}
        message(value){this.onmessage?.({data:JSON.stringify(value)});}
        close(){this.readyState=3;queueMicrotask(()=>this.onclose?.());}
    };
    realtime.configure({baseUrl:'http://fixture.invalid',model:'fixture',voice:'0',connectTimeoutSeconds:1});
    const reference={invokeMethodAsync:(method,entity,event)=>{events.push({method,entity,event});return Promise.resolve();}};
    t.after(async()=>{await realtime.dispose();globalThis.WebSocket=old;});
    return {sockets,events,reference};
}
test('Realtime text response opens a session, sends the user turn and dispatches Unicode deltas and completion',async t=>{
    const app=setup(t);realtime.execute({operation:'response',entityId:'actor',requestId:'turn',text:'你好',deltaCallback:'delta',completedCallback:'done',errorCallback:'error'},app.reference);
    await settle();const socket=app.sockets[0];
    assert.equal(socket.sent[0].type,'session.update');assert.equal(socket.sent[1].type,'conversation.item.create');assert.equal(socket.sent[2].type,'response.create');
    socket.message({type:'response.text.delta',delta:'你'});socket.message({type:'response.text.delta',delta:'好'});socket.message({type:'response.done',response:{status:'completed'}});
    await settle();assert.deepEqual(app.events.map(e=>[e.event.callbackName,e.event.text]),[['delta','你'],['delta','你好'],['done','你好']]);
});
test('cancelled or replaced Realtime sessions cannot send stale callbacks',async t=>{
    const app=setup(t),command={operation:'response',entityId:'actor',requestId:'one',text:'one',completedCallback:'done',errorCallback:'error'};
    realtime.execute(command,app.reference);await settle();const old=app.sockets[0];
    realtime.execute({operation:'reset'},app.reference);realtime.execute({...command,requestId:'two'},app.reference);await settle();
    old.message({type:'response.done',response:{status:'completed'}});await settle();assert.equal(app.events.length,0);
    realtime.execute({operation:'cancel',requestId:'two'},app.reference);app.sockets[1].message({type:'response.done',response:{status:'completed'}});
    await settle();assert.equal(app.events.length,0);assert.ok(app.sockets[1].sent.some(m=>m.type==='response.cancel'));
});
test('Realtime protocol errors and unsupported header authentication report the configured error callback',async t=>{
    const app=setup(t),command={operation:'response',entityId:'actor',requestId:'one',text:'hello',errorCallback:'error'};
    realtime.execute(command,app.reference);await settle();app.sockets[0].message({type:'error',error:{message:'bad session'}});
    assert.equal(app.events[0].event.error,'bad session');
    realtime.configure({baseUrl:'http://fixture.invalid',apiKey:'fixture-token'});realtime.execute(command,app.reference);await settle();
    assert.match(app.events.at(-1).event.error,/browser-compatible relay/);assert.ok(!app.events.at(-1).event.error.includes('fixture-token'));
});
test('PCM16 input resamples without sending floating point or platform byte order',()=>{
    const binary=atob(realtime.encodePcm(new Float32Array([1,0,-1,0]),48000,24000));
    assert.equal(binary.length,4);const view=new DataView(Uint8Array.from(binary,c=>c.charCodeAt(0)).buffer);
    assert.equal(view.getInt16(0,true),32767);assert.equal(view.getInt16(2,true),-32767);
});
