import {spawn} from 'node:child_process';
import {mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import assert from 'node:assert/strict';

const root=process.cwd(), temp=resolve(root,'temp/web-debug');
// Requires a published player with the DemoGame01 Web export in web-game.
// Uses an empty local fixture to check iframe transparency without API calls.
const realSpeech=true;
mkdirSync(temp,{recursive:true});
const port=5067, debugPort=9237, base=`http://127.0.0.1:${port}`;
const server=spawn('python',['scripts/web-preview-server.py','--port',String(port),'--bind','127.0.0.1','--directory','artifacts/web-player/wwwroot'],{windowsHide:true,stdio:'ignore'});
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',[
    '--headless=new',`--remote-debugging-port=${debugPort}`,`--user-data-dir=${resolve(temp,'edge-embed-profile-'+Date.now())}`,
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
    const page=await until(async()=>{const r=await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`,{method:'PUT'});return r.ok&&await r.json();},60000);
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
                if(p.request.url.includes('web-runtime.js'))body+='\nglobalThis.__webRuntimeState=()=>runtime;globalThis.__windowTest={setGameFullscreen};\n';
                else if(p.request.url.includes('web.manifest.json'))body=JSON.stringify({formatVersion:1,entryScene:'scenes/main.scene.json',assets:[{path:'game.project.json',size:-1},{path:'scenes/main.scene.json',size:-1},{path:'compiled/web/api-fixture.dll',size:-1}],scripts:[{id:'api',source:'api-fixture.csx',assembly:'compiled/web/api-fixture.dll',entity:'actor',apiVersion:'1.0',type:'Zhengyan.GeneratedWebScripts.Script'}]});
                else if(p.request.url.includes('game.project.json'))body=JSON.stringify({name:'embed-fixture',defaultScene:'scenes/main.scene.json',window:{desktopSpriteMode:true},voice:{enabled:false},asr:{enabled:false},llm:{enabled:false}});
                else if(p.request.url.includes('/scenes/'))body=JSON.stringify({name:'Transparent fixture',entities:[{id:'actor',type:'empty',scripts:[{path:'api-fixture.csx',language:'csharp',enabled:true}]}],guiControls:[{id:'api-run',name:'api-run',type:'button',text:'Run API',x:20,y:20,width:140,height:40,targetEntity:'actor'}],skybox:{enabled:true,texturePath:'app:Resources/Skybox/dark_starry_moon.jpg'}});
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
    await send('Fetch.enable',{patterns:[{urlPattern:'*js/web-runtime.js*',requestStage:'Response'},{urlPattern:'*web.manifest.json*',requestStage:'Response'},{urlPattern:'*game.project.json*',requestStage:'Response'},{urlPattern:'*web-game/scenes/*.json*',requestStage:'Response'}]});
    mkdirSync(resolve(root,'artifacts/web-player/wwwroot/web-game/compiled/web'),{recursive:true});
    writeFileSync(resolve(root,'artifacts/web-player/wwwroot/web-game/compiled/web/api-fixture.dll'),readFileSync(resolve(temp,'api-fixture.dll')));
    writeFileSync(resolve(root,'artifacts/web-player/wwwroot/embed-regression.html'),`<!doctype html><body style="margin:0;background:rgb(234,90,130)"><h1>Host page</h1><iframe title="Pet" src="/?embed=1" allow="microphone; autoplay; fullscreen" allowfullscreen style="width:640px;height:480px;border:0;background:transparent"></iframe></body>`);
    await send('Page.navigate',{url:base+'/embed-regression.html'});
    await until(()=>evaluate("document.querySelector('iframe')?.contentDocument?.querySelector('.renderer-ready')&&!document.querySelector('iframe').contentDocument.querySelector('.web-loading-screen')"),90000);
    const result=await evaluate(`(()=>{const w=document.querySelector('iframe').contentWindow,d=w.document,r=w.__webRuntimeState(),gl=r.canvas.getContext('webgl2');r.renderer.render(performance.now());const pixel=new Uint8Array(4);gl.readPixels(20,20,1,1,gl.RGBA,gl.UNSIGNED_BYTE,pixel);return {pixel:[...pixel],alpha:gl.getContextAttributes().alpha,root:w.getComputedStyle(d.documentElement).backgroundColor,body:w.getComputedStyle(d.body).backgroundColor,frame:w.getComputedStyle(r.canvas.parentElement).backgroundColor,canvas:w.getComputedStyle(r.canvas).backgroundColor,header:w.getComputedStyle(d.querySelector('.web-runtime-header')).display,panel:w.getComputedStyle(d.querySelector('.runtime-panel')).display,width:r.canvas.getBoundingClientRect().width,height:r.canvas.getBoundingClientRect().height};})()`);
    assert.equal(result.pixel[3],0,JSON.stringify(result));assert.equal(result.alpha,true);
    for(const name of ['root','body','frame','canvas'])assert.equal(result[name],'rgba(0, 0, 0, 0)');
    assert.equal(result.header,'none');assert.equal(result.panel,'none');assert.equal(result.width,640);assert.equal(result.height,480);
    const screenshot=(await send('Page.captureScreenshot',{format:'png'})).data;
    writeFileSync(resolve(temp,'web-embedded-pet.png'),Buffer.from(screenshot,'base64'));
    const composited=await evaluate(`(async()=>{const img=new Image();img.src='data:image/png;base64,${screenshot}';await img.decode();const c=document.createElement('canvas');c.width=img.width;c.height=img.height;const ctx=c.getContext('2d');ctx.drawImage(img,0,0);return [...ctx.getImageData(100,150,1,1).data];})()`);
    assert.deepEqual(composited,[234,90,130,255],'The iframe compositor obscured the host background despite transparent WebGL pixels');
    console.log('PASS iframe hides chrome, fills its container and preserves transparent WebGL pixels',JSON.stringify(result));
    await send('Page.bringToFront');
    await evaluate("document.querySelector('iframe').contentWindow.__windowTest.setGameFullscreen(true)");
    const pos=await evaluate("(()=>{const f=document.querySelector('iframe'),a=f.getBoundingClientRect(),b=f.contentDocument.querySelector('.game-fullscreen-prompt').getBoundingClientRect();return {x:a.x+b.x+b.width/2,y:a.y+b.y+b.height/2};})()");
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...pos,button:'left',buttons:1,clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...pos,button:'left',buttons:0,clickCount:1});
    await until(()=>evaluate("!!document.querySelector('iframe').contentDocument.fullscreenElement"),10000);
    await evaluate("document.querySelector('iframe').contentWindow.__windowTest.setGameFullscreen(false)");
    await until(()=>evaluate("!document.querySelector('iframe').contentDocument.fullscreenElement"),10000);
    console.log('PASS native iframe fullscreen via a trusted button click and exit');
    assert.equal(await evaluate("document.querySelector('iframe').contentWindow.__webRuntimeState().overlayState.bubbles.find(b=>b.text==='api-ready')?.text"),'api-ready','AOT script API did not reject native capabilities or expose script time');
    const gui=await evaluate("(()=>{const a=document.querySelector('iframe').getBoundingClientRect();return{x:a.x+90,y:a.y+40};})()");
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...gui,button:'left',buttons:1,clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...gui,button:'left',buttons:0,clickCount:1});
    await until(()=>evaluate("document.querySelector('iframe').contentWindow.__webRuntimeState().overlayState.bubbles.some(b=>b.text===JSON.stringify({answer:42}))"),10000);
    console.log('PASS AOT script API and reentrant C# script tool return values');
    writeFileSync(resolve(root,'artifacts/web-player/wwwroot/script-api-gpu-fixture.mjs'),readFileSync(resolve(root,'tests/helpers/web-script-api-gpu-fixture.mjs')));
    const shader=await evaluate("(async()=>{const f=await import('/script-api-gpu-fixture.mjs'),r=await import('/js/webgl2-renderer.js');return f.verifyScriptShader(r.createWebGl2Renderer);})()");
    console.log('PASS real WebGL2 custom shader, uniform updates, clearing and standard shader restoration',JSON.stringify(shader));
}catch(error){
    console.error('Browser failure:',error.message);
    if(ws?.readyState===1)console.error(await evaluate("({error:document.querySelector('.runtime-error')?.innerText,loading:document.querySelector('.web-loading-screen')?.innerText,asr:globalThis.__asrDebug?.(),speech:globalThis.__speechEvents,logs:globalThis.__loadingHistory})").catch(()=>null));
    console.error(JSON.stringify(logs));
    throw error;
}finally{
    writeFileSync(resolve(temp,'embed-browser-logs.json'),JSON.stringify(logs,null,2));
    if(ws?.readyState===1){try{await send('Browser.close');}catch{}ws.close();}
    browser.kill();server.kill();
}
