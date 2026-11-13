import {spawn} from 'node:child_process';
import {mkdirSync, writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import assert from 'node:assert/strict';

const root=process.cwd(), temp=resolve(root,'temp/web-debug');
// Requires a published player with the DemoGame01 Web export in web-game.
// Runs real local models and one short request to the configured online LLM.
const realSpeech=true;
mkdirSync(temp,{recursive:true});
const port=5065, debugPort=9235, base=`http://127.0.0.1:${port}`;
const server=spawn('python',['scripts/web-preview-server.py','--port',String(port),'--bind','127.0.0.1','--directory','artifacts/web-player/wwwroot'],{windowsHide:true,stdio:'ignore'});
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',[
    '--headless=new',`--remote-debugging-port=${debugPort}`,`--user-data-dir=${resolve(temp,'edge-check-profile-'+Date.now())}`,
    '--use-fake-ui-for-media-stream','--use-fake-device-for-media-stream','--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-extensions',
    ...(realSpeech?[]:['--autoplay-policy=no-user-gesture-required']),'--window-size=1440,1000','about:blank'
],{windowsHide:true,stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let ws,seq=0;
const pending=new Map(), logs=[];
async function until(fn,timeout=180000){const end=Date.now()+timeout;while(Date.now()<end){try{const v=await fn();if(v)return v;}catch{}await sleep(500);}throw new Error('Timed out waiting for browser state');}
function send(method,params={}){return new Promise((ok,fail)=>{
    const id=++seq,timer=setTimeout(()=>{pending.delete(id);fail(new Error('Browser command timed out: '+method));},30000);
    pending.set(id,{ok:value=>{clearTimeout(timer);ok(value);},fail:error=>{clearTimeout(timer);fail(error);}});
    ws.send(JSON.stringify({id,method,params}));
});}
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.text);return r.result.value;}
try{
    const page=await until(async()=>{const r=await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`,{method:'PUT'});return r.ok&&await r.json();},20000);
    ws=new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((ok,fail)=>{ws.onopen=ok;ws.onerror=fail;});
    ws.onmessage=async e=>{
        const m=JSON.parse(e.data);
        if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p?.fail(new Error(m.error.message)):p?.ok(m.result);}
        if(m.method==='Runtime.consoleAPICalled'){
            const message=m.params.args.map(a=>a.value??a.description??'').join(' ');
            if(!message.startsWith('hit '))logs.push({type:m.params.type,message});
        }
        if(m.method==='Runtime.exceptionThrown')logs.push({type:'exception',message:m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text});
        if(m.method==='Fetch.requestPaused'){
            const p=m.params;
            try{
                const response=await send('Fetch.getResponseBody',{requestId:p.requestId});
                let body=response.base64Encoded?Buffer.from(response.body,'base64').toString():response.body;
                // Observe actual interop state without changing the files under test.
                if(p.request.url.includes('web-runtime.js')){
                    body=body.replace('runtime?.renderer?.setFrameState(frameState);','globalThis.__webFrame=frameState; runtime?.renderer?.setFrameState(frameState);');
                    body=body.replace('runtime?.renderer?.updateMeshes(meshes || []);', "globalThis.__boneHashes??={};for(const m of meshes||[]){const b=m.mesh?.bones;if(b)__boneHashes[m.id]=Array.from(b).reduce((h,v)=>Math.imul(h,31)+v|0,0);}runtime?.renderer?.updateMeshes(meshes || []);");
                    body+='\nglobalThis.__webRuntimeState=()=>runtime;globalThis.__windowTest={setGameSize,setGameFullscreen,configureGame};\n';
                }else if(p.request.url.includes('web-audio.js')){
                    body+='\nglobalThis.__gameAudio=()=>[...sounds].map(([key,entry])=>({key,paused:entry.audio.paused,time:entry.audio.currentTime}));\n';
                }else if(p.request.url.includes('web-platform.js') && !realSpeech){
                    body=body.replace("if(offline.isEnabled()){offline.speak(text||'',rate,volume,dotNetReference,entityId,callbackName,speakerId);return;}",
                        "globalThis.__speechCalls??=[];__speechCalls.push(text);dotNetReference?.invokeMethodAsync('OnSpeechCompleted',entityId,callbackName);return;");
                }else if(p.request.url.includes('web-platform.js')){
                    body=body.replace("if(offline.isEnabled()){offline.speak(text||'',rate,volume,dotNetReference,entityId,callbackName,speakerId);return;}",
                        "globalThis.__speechRequests??=[];__speechRequests.push(text);const original=dotNetReference;dotNetReference={invokeMethodAsync:(name,...args)=>{globalThis.__speechEvents??=[];__speechEvents.push({name,args});return original.invokeMethodAsync(name,...args);}};if(offline.isEnabled()){offline.speak(text||'',rate,volume,dotNetReference,entityId,callbackName,speakerId);return;}");
                }else if(p.request.url.includes('web-offline.js')){
                    body=body.replace("if(m.type==='result')", "if(this.kind==='asr'&&m.type==='result')globalThis.__asrResults=(globalThis.__asrResults||0)+1;if(m.type==='result')");
                    body+='\nglobalThis.__modelsWarm=()=>tts.warmed&&asr.warmed;\n';
                    body+=`\nglobalThis.__dispatchAsr=(eventName,text)=>{const s=asrSession;if(eventName==='completed')stopAsr(true);return s.reference.invokeMethodAsync('OnAsrEvent',s.command.entityId,{requestId:s.command.requestId,eventName,text,isFinal:eventName==='completed',error:'',callbackName:s.command.completedCallback,wakeWord:'小雨',recognizedText:text});};\n`;
                    body+='\nglobalThis.__asrDebug=()=>({initialized:asr.initialized,pending:asr.pending.size,session:asrSession&&{cancelled:asrSession.cancelled,operation:asrSession.command.operation,text:asrSession.text}});globalThis.__offlineDebug=()=>({enabled,initialized:tts.initialized,pending:tts.pending.size,worker:!!tts.worker,ready:!!tts.ready,audio:audioContext?.state});\n';
                    body=body.replace('if(generation!==speechGeneration)return;\n        const ctx=await context()', "globalThis.__speechPcm??=[];__speechPcm.push({samples:result.samples.length,sampleRate:result.sampleRate,peak:result.samples.reduce((v,s)=>Math.max(v,Math.abs(s)),0)});if(generation!==speechGeneration)return;\n        const ctx=await context()");
                    body=body.replace("if(m.type==='asset')", "if(m.type==='log')console.error('Speech worker:',m.text);if(m.type==='asset')");
                }else if(p.request.url.includes('web.manifest.json')){const m=JSON.parse(body);m.assets.find(a=>a.path==='game.project.json').sha256='';m.assets.find(a=>a.path==='game.project.json').size=-1;body=JSON.stringify(m);}else if(p.request.url.includes('game.project.json')){const m=JSON.parse(body);m.defaultScene='scenes/scene2.scene.json';body=JSON.stringify(m);}
                await send('Fetch.fulfillRequest',{requestId:p.requestId,responseCode:200,responseHeaders:[{name:'Content-Type',value:p.request.url.includes('.json')?'application/json':'text/javascript'}],body:Buffer.from(body).toString('base64')});
            }catch(err){logs.push({type:'interception',message:err.message});await send('Fetch.continueRequest',{requestId:p.requestId});}
        }
    };
    await send('Runtime.enable');await send('Page.enable');await send('Network.enable');
    await send('Network.setBypassServiceWorker',{bypass:true});
    await send('Page.addScriptToEvaluateOnNewDocument',{source:`
        globalThis.__loadingHistory=[];globalThis.__microphones=[];
        const capture=navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
        navigator.mediaDevices.getUserMedia=async(...args)=>{const stream=await capture(...args);__microphones.push(stream);return stream;};
        new MutationObserver(()=>{const loading=document.querySelector('.web-loading-screen');
            if(loading && __loadingHistory.at(-1)!==loading.innerText)__loadingHistory.push(loading.innerText);
        }).observe(document,{childList:true,subtree:true,characterData:true});
    `});
    await send('Fetch.enable',{patterns:[{urlPattern:'*js/web-runtime.js*',requestStage:'Response'},{urlPattern:'*js/web-platform.js*',requestStage:'Response'},{urlPattern:'*js/web-realtime.js*',requestStage:'Response'},{urlPattern:'*js/web-offline.js*',requestStage:'Response'},{urlPattern:'*js/web-audio.js*',requestStage:'Response'},{urlPattern:'*web.manifest.json*',requestStage:'Response'},{urlPattern:'*game.project.json*',requestStage:'Response'}]});
    await send('Page.navigate',{url:base});
    await until(()=>evaluate("!!document.querySelector('.web-loading-screen')"),30000);
    writeFileSync(resolve(temp,'web-loading.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
    await until(()=>evaluate("globalThis.__webFrame?.graphicsScenePath==='scenes/scene2.scene.json'&&!document.querySelector('.web-loading-screen')"),300000);
    assert(await evaluate('__modelsWarm()'),'Scene revealed before model warm-up');
    assert.equal(await evaluate('__microphones.length'),0,'Preload requested microphone permission');
    console.log('PASS scene2 loading completed after real TTS and ASR warm-up');
    async function click(name){
        const position=await evaluate(`(()=>{const c=__webRuntimeState().overlayState.controls.find(c=>c.name===${JSON.stringify(name)}),r=__webRuntimeState().canvas.getBoundingClientRect(),d=window.devicePixelRatio;return {x:r.left+(c.x+c.width/2)/d,y:r.top+(c.y+c.height/2)/d};})()`);
        await send('Input.dispatchMouseEvent',{type:'mousePressed',...position,button:'left',buttons:1,clickCount:1});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',...position,button:'left',buttons:0,clickCount:1});
    }
    await evaluate(`(()=>{const input=document.querySelector('[aria-label="PromptInput"]');input.value='只回复一个字：好。';input.dispatchEvent(new Event('input',{bubbles:true}));})()`);
    await click('SendButton');
    await until(()=>evaluate("globalThis.__speechEvents?.some(e=>e.name==='OnSpeechCompleted')"),120000);
    assert(!(await evaluate("JSON.stringify(__webRuntimeState().overlayState.bubbles)")).includes('LLM 出错'));
    console.log('PASS scene2 online LLM response and real local TTS playback');
    await evaluate('__loadingHistory=[]');
    await click('NextButton');
    await until(()=>evaluate("__webFrame?.graphicsScenePath==='scenes/scene3.scene.json'"),240000);
    assert((await evaluate('__loadingHistory')).length>0,'Transition did not display a loading screen');
    await until(()=>evaluate("__asrDebug()?.session?.operation==='wake'&&__microphones.some(s=>s.getTracks().some(t=>t.readyState==='live'))"));
    const before=await evaluate('__asrResults||0');
    for(let i=0;i<30;i++){
        await sleep(2000);
        assert.equal(await evaluate("__asrDebug()?.session?.operation"),'wake');
        assert(await evaluate("__microphones.some(s=>s.getTracks().some(t=>t.readyState==='live'))"),'Microphone stopped during silence');
        if(i%10===9)console.log(`ASR still capturing after ${(i+1)*2} seconds`);
    }
    assert((await evaluate('__asrResults'))>before+10,'Live microphone is no longer feeding the recognizer');
    console.log('PASS scene3 retains microphone and processes audio for 60 seconds');
    assert((await evaluate('__webFrame.sky.textureUri')).endsWith('/Skybox/dark_starry_moon.jpg'),'Night sky was lost from the export');
    await evaluate("__dispatchAsr('wake_detected','小雨')");
    await until(()=>evaluate("__asrDebug()?.session?.operation==='start'"),60000);
    const spoken=await evaluate("globalThis.__speechEvents.filter(e=>e.name==='OnSpeechCompleted').length");
    await evaluate("__dispatchAsr('completed','只回复一个字：好。')");
    await until(()=>evaluate(`globalThis.__speechEvents.filter(e=>e.name==='OnSpeechCompleted').length>${spoken}`),120000);
    assert(!(await evaluate("JSON.stringify(__webRuntimeState().overlayState.bubbles)")).includes('思考中'));
    console.log('PASS scene3 wake, transcript, online reply and local speech on the published runtime');
    await evaluate('__windowTest.setGameSize(640,480)');
    await until(()=>evaluate("Math.round(document.querySelector('.canvas-frame').getBoundingClientRect().width)===640"));
    await send('Page.bringToFront');
    await evaluate('__windowTest.setGameFullscreen(true)');
    const fullscreenPosition=await evaluate("(()=>{const r=document.querySelector('.game-fullscreen-prompt').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()");
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...fullscreenPosition,button:'left',buttons:1,clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...fullscreenPosition,button:'left',buttons:0,clickCount:1});
    await until(()=>evaluate("document.fullscreenElement===document.querySelector('.canvas-frame')"),10000);
    await evaluate('__windowTest.setGameFullscreen(false)');
    await until(()=>evaluate('!document.fullscreenElement'),10000);
    console.log('PASS browser canvas size and native fullscreen enter/exit');
    assert.equal(logs.filter(l=>['error','exception','interception'].includes(l.type)).length,0,JSON.stringify(logs));
    console.log('Logs:',JSON.stringify(logs));
}catch(error){
    console.error('Browser failure:',error.message);
    if(ws?.readyState===1)console.error(await evaluate("({error:document.querySelector('.runtime-error')?.innerText,loading:document.querySelector('.web-loading-screen')?.innerText,asr:globalThis.__asrDebug?.(),speech:globalThis.__speechEvents,logs:globalThis.__loadingHistory})").catch(()=>null));
    console.error(JSON.stringify(logs));
    throw error;
}finally{
    writeFileSync(resolve(temp,'scene3-browser-logs.json'),JSON.stringify(logs,null,2));
    if(ws?.readyState===1){try{await send('Browser.close');}catch{}ws.close();}
    browser.kill();server.kill();
}
