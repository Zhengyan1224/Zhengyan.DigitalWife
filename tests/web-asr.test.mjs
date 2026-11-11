import assert from 'node:assert/strict';
import test from 'node:test';
import * as asr from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-asr.js';

function setup(t) {
    const old=globalThis.SpeechRecognition,instances=[],events=[];
    globalThis.SpeechRecognition=class {
        constructor(){instances.push(this);}
        start(){this.starts=(this.starts||0)+1;}
        stop(){this.onend?.();}
        abort(){this.aborted=true;this.onend?.();}
    };
    const reference={invokeMethodAsync:(method,...args)=>{events.push({method,args});return Promise.resolve();}};
    t.after(()=>{asr.stopAll();if(old===undefined)delete globalThis.SpeechRecognition;else globalThis.SpeechRecognition=old;});
    const result=(text,final=false)=>Object.assign([{transcript:text}],{isFinal:final});
    return {instances,events,reference,result};
}
test('streaming ASR reports partial and final text, and replaces active sessions',t=>{
    const app=setup(t),command={operation:'start',requestId:'one',entityId:'actor',partialCallback:'partial',completedCallback:'done',errorCallback:'error'};
    asr.execute(command,app.reference);app.instances[0].onresult({results:[app.result('你好')]});
    assert.equal(app.events[0].args[1].text,'你好');assert.equal(app.events[0].args[1].callbackName,'partial');
    asr.execute({operation:'stop',requestId:'one'},app.reference);
    assert.equal(app.events[1].args[1].isFinal,true);assert.equal(app.events[1].args[1].text,'你好');
    asr.execute(command,app.reference);asr.execute({...command,requestId:'two'},app.reference);
    assert.equal(app.instances[1].aborted,true);app.instances[1].onresult({results:[app.result('stale')]});
    assert.equal(app.events.length,2);
});
test('wake words produce the configured callback and stop cancels late results',t=>{
    const app=setup(t);asr.execute({operation:'wake',requestId:'wake',entityId:'actor',wakeWords:['小雨'],completedCallback:'detected',errorCallback:'error'},app.reference);
    app.instances[0].onresult({results:[app.result('你好，小雨。')]});
    assert.equal(app.events[0].args[1].wakeWord,'小雨');assert.equal(app.events[0].args[1].callbackName,'detected');
    asr.execute({operation:'stop_wake'},app.reference);app.instances[0].onresult({results:[app.result('小雨')]});assert.equal(app.events.length,1);
});
test('unavailable recognition and permission errors reach the script error callback',t=>{
    const app=setup(t),command={operation:'start',requestId:'one',entityId:'actor',errorCallback:'failure'};
    asr.execute(command,app.reference);app.instances[0].onerror({error:'not-allowed'});
    assert.equal(app.events[0].args[1].error,'not-allowed');assert.equal(app.instances[0].aborted,true);
    delete globalThis.SpeechRecognition;asr.execute(command,app.reference);
    assert.match(app.events[1].args[1].error,/does not provide speech recognition/);
});
