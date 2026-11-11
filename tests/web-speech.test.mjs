import assert from 'node:assert/strict';
import test from 'node:test';
import * as speech from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-platform.js';

function setup(t){
    const oldWindow=globalThis.window,oldUtterance=globalThis.SpeechSynthesisUtterance,utterances=[],events=[];
    globalThis.window={speechSynthesis:{cancel(){},speak(utterance){utterances.push(utterance);}}};
    globalThis.SpeechSynthesisUtterance=class {constructor(text){this.text=text;}};
    const reference={invokeMethodAsync:(name,...args)=>{events.push({name,args});return Promise.resolve();}};
    t.after(()=>{speech.stopSpeaking();globalThis.window=oldWindow;globalThis.SpeechSynthesisUtterance=oldUtterance;});
    return {utterances,events,reference};
}
test('speech boundaries and completion drive the correct entity and callback',t=>{
    const app=setup(t);speech.speakWithCallback('你好','zh-CN',1,1,1,app.reference,'actor','done');
    const utterance=app.utterances[0];utterance.onstart();utterance.onboundary({charIndex:1,charLength:1});utterance.onend();
    assert.deepEqual(app.events.map(e=>e.name),['OnSpeechStarted','OnSpeechBoundary','OnSpeechFinished','OnSpeechCompleted']);
    assert.deepEqual(app.events.at(-1).args,['actor','done']);
});
test('replacing speech resets its mouth and ignores stale completion',t=>{
    const app=setup(t);speech.speakWithCallback('one','en-US',1,1,1,app.reference,'one','old');
    speech.speakWithCallback('two','en-US',1,1,1,app.reference,'two','new');app.utterances[0].onend();app.utterances[1].onend();
    assert.equal(app.events.filter(e=>e.name==='OnSpeechCompleted').length,1);assert.deepEqual(app.events.at(-1).args,['two','new']);
});
test('synthesis failure clears lip sync and reports an error without reporting success',t=>{
    const app=setup(t);speech.speakWithCallback('text','en-US',1,1,1,app.reference,'actor','done');app.utterances[0].onerror({error:'synthesis-failed'});
    assert.deepEqual(app.events.map(e=>e.name),['OnSpeechFinished','OnSpeechFailed']);assert.equal(app.events.at(-1).args[2],'synthesis-failed');
});
