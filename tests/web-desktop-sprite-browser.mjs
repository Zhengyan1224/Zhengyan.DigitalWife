import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {readFileSync,mkdirSync,writeFileSync} from 'node:fs';
import {resolve,extname,sep} from 'node:path';
import {createHash} from 'node:crypto';
if(process.argv.includes('--embed-guide')){
    await import('./helpers/web-embed-guide-browser.mjs');
    process.exit(0);
}
const root=process.cwd(),temp=resolve(root,'temp/web-desktop-sprite-browser');mkdirSync(temp,{recursive:true});
const aotSmoke=process.argv.includes('--aot-smoke');
const smokeProject={name:'AOT input smoke',defaultScene:'scenes/main.scene.json',scenes:['scenes/main.scene.json'],window:{width:360,height:480,desktopSpriteMode:true,desktopSpriteClickThrough:false,desktopSpriteDragButton:'left'},voice:{enabled:false},asr:{enabled:false},llm:{enabled:false}};
const smokeScene={name:'Smoke',cameras:[{id:'main',name:'Main Camera',isMain:true,enabled:true,camera:{position:{x:0,y:2,z:8},target:{x:0,y:1.2,z:0},controlMode:'editor',fov:45}}]};
const smokeFiles=new Map([['game.project.json',JSON.stringify(smokeProject)],['scenes/main.scene.json',JSON.stringify(smokeScene)]]);
smokeFiles.set('web.manifest.json',JSON.stringify({formatVersion:1,runtime:'web',renderer:'webgl2',apiVersion:'1.0',entryScene:'scenes/main.scene.json',scripts:[],assets:[...smokeFiles].map(([path,body])=>({path,kind:'json',size:Buffer.byteLength(body),sha256:createHash('sha256').update(body).digest('hex')}))}));
const server=createServer((req,res)=>{
    const pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
    const file=pathname==='/'?'index.html':pathname.slice(1);
    const path=aotSmoke?resolve(root,'artifacts/web-player/wwwroot',file):resolve(root,'.'+pathname);
    if(!path.startsWith(root+sep)){res.writeHead(403);res.end();return;}
    try{
        res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.json':'application/json','.wasm':'application/wasm'})[extname(path)]||'application/octet-stream');
        let body=aotSmoke&&smokeFiles.has(file.replace(/^web-game\//,''))?smokeFiles.get(file.replace(/^web-game\//,'')):readFileSync(path);
        if(aotSmoke&&pathname.endsWith('/web-runtime.js'))body=body.toString().replace('runtime?.renderer?.setFrameState(frameState);','globalThis.__aotFrame=frameState; runtime?.renderer?.setFrameState(frameState);');
        res.end(body);
    }catch{res.writeHead(404);res.end();}
});
await new Promise(ok=>server.listen(0,'127.0.0.1',ok));
const probe=process.argv.includes('--probe-microphone');
const audioSandboxTest=process.argv.includes('--audio-sandbox-test');
const waveAudioTest=process.argv.includes('--wave-audio-test');
const nativeAudioLogs=[];
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--remote-debugging-port=9250',`--user-data-dir=${resolve(temp,'profile-'+Date.now())}`,...(waveAudioTest?['--force-wave-audio']:[]),...(audioSandboxTest?['--disable-features=AudioServiceSandbox']:[]),...(probe?['--enable-logging=stderr','--v=1','--vmodule=audio_low_latency_input_win=3,wasapi_audio_input_stream=3']:['--use-fake-device-for-media-stream']),'--no-first-run','--disable-background-networking','--disable-extensions','--window-size=600,500','about:blank'],{windowsHide:true,stdio:['ignore','ignore','pipe']});
browser.stderr.on('data',data=>{for(const line of data.toString().split('\n'))if(/wasapi|audio.*error|audio_low_latency_input|HRESULT/i.test(line))nativeAudioLogs.push(line);});
let ws,seq=0;const pending=new Map(),errors=[];const sleep=ms=>new Promise(ok=>setTimeout(ok,ms));
async function until(fn,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){try{const value=await fn();if(value)return value;}catch{}await sleep(100);}throw Error('Browser condition timed out');}
function send(method,params={}){return new Promise((ok,fail)=>{const id=++seq,timer=setTimeout(()=>{pending.delete(id);fail(Error(method+' timeout'));},probe?120000:30000);pending.set(id,{ok:v=>{clearTimeout(timer);ok(v);},fail:e=>{clearTimeout(timer);fail(e);}});ws.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;}
const child=expression=>evaluate(`document.querySelector('iframe').contentWindow.eval(${JSON.stringify(expression)})`);
async function click(x,y,button='left'){await send('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button,buttons:button==='left'?1:2,clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',x,y,button,buttons:0,clickCount:1});}
async function clickChild(selector){const p=await evaluate(`(()=>{const f=document.querySelector('iframe'),a=f.getBoundingClientRect(),b=f.contentDocument.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return {x:a.x+b.x+b.width/2,y:a.y+b.y+b.height/2};})()`);await click(p.x,p.y);}
try{
    const page=await until(async()=>{const r=await fetch('http://127.0.0.1:9250/json/new?about:blank',{method:'PUT'});return r.ok&&r.json();});
    ws=new WebSocket(page.webSocketDebuggerUrl);await new Promise((ok,fail)=>{ws.onopen=ok;ws.onerror=fail;});
    ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p?.fail(Error(m.error.message)):p?.ok(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text);};
    await send('Runtime.enable');await send('Page.enable');
    if(aotSmoke){
        await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/desktop-sprite.html`});
        await until(()=>child('!!window.__aotFrame'),180000);
        const rect=await evaluate('(()=>{const r=document.querySelector("iframe").getBoundingClientRect();return{x:r.x,y:r.y};})()');
        const frame=()=>child('JSON.stringify(window.__aotFrame.viewProjection)');
        let before=await frame();
        await send('Input.dispatchMouseEvent',{type:'mousePressed',x:rect.x+180,y:rect.y+240,button:'right',buttons:2,clickCount:1});
        await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+210,y:rect.y+255,button:'right',buttons:2});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',x:rect.x+210,y:rect.y+255,button:'right',buttons:0,clickCount:1});
        await until(async()=>await frame()!==before);
        before=await frame();
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'w',code:'KeyW',windowsVirtualKeyCode:87});
        await until(async()=>await frame()!==before);
        await send('Input.dispatchKeyEvent',{type:'keyUp',key:'w',code:'KeyW',windowsVirtualKeyCode:87});
        await sleep(150);before=await frame();
        await send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:2});
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:rect.x+180,y:rect.y+200,id:1}]});
        await sleep(150);
        await send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:rect.x+200,y:rect.y+210,id:1}]});
        await until(async()=>await frame()!==before);
        await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.deepEqual(errors,[]);
        console.log('PASS published AOT: real JS/.NET mouse-motion records, keyboard controls and touch snapshots change the camera.');
    }else if(probe){
        await send('Browser.grantPermissions',{origin:'http://127.0.0.1:5052',permissions:['audioCapture']});
        // Keep the same origin, without loading the game and its concurrent ASR capture.
        await send('Page.navigate',{url:'http://127.0.0.1:5052/css/app.css'});
        await until(()=>evaluate('location.pathname==="/css/app.css"'));
        console.log('Probe origin ready:',await evaluate('({origin:location.origin,secure:isSecureContext,media:!!navigator.mediaDevices})'));
        console.log(JSON.stringify(await evaluate(`(async()=>{const result={secure:isSecureContext,permission:(await navigator.permissions.query({name:'microphone'})).state};const devices=(await navigator.mediaDevices.enumerateDevices()).filter(d=>d.kind==='audioinput');result.inputs=devices.map(d=>({id:d.deviceId,label:d.label}));return result;})()`)));
        const devices=await evaluate('(async()=> (await navigator.mediaDevices.enumerateDevices()).filter(d=>d.kind==="audioinput"&&d.deviceId!=="default"&&d.deviceId!=="communications").map(d=>({id:d.deviceId,label:d.label})))()');
        for(const device of devices){
            for(const processing of [false,true]){
                console.log('Starting capture:',device.label,'processing:',processing);
                console.log(JSON.stringify({device:device.label,processing,...await evaluate(`(async()=>{try{const s=await navigator.mediaDevices.getUserMedia({audio:{deviceId:{exact:${JSON.stringify(device.id)}},echoCancellation:${processing},noiseSuppression:${processing},autoGainControl:${processing}}});s.getTracks().forEach(t=>t.stop());return {capture:'ok'};}catch(e){return {error:e.name,message:e.message};}})()`)}));
            }
        }
        console.log('Native audio diagnostics:',nativeAudioLogs.slice(-30));
        await send('Page.navigate',{url:'edge://media-internals/'});await sleep(1000);
        await evaluate('(()=>{const tab=[...document.querySelectorAll("a,button,[role=tab],.tab")].find(e=>e.textContent.trim()==="Audio");tab?.click();})()');
        console.log('Media internals:',await evaluate('document.body.innerText.slice(-5000)'));
    }else{
    for(const dpr of [1,2]){
        await send('Emulation.setDeviceMetricsOverride',{width:600,height:500,deviceScaleFactor:dpr,mobile:false});
        await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/tests/helpers/web-desktop-sprite.html`});
        await until(()=>child('window.ready'));await until(()=>evaluate('document.querySelector("iframe").style.clipPath.startsWith("path(")'));
        const rect=await evaluate('(()=>{const r=document.querySelector("iframe").getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height};})()');
        await click(rect.x+10,rect.y+100);
        assert.equal(await evaluate('window.pageClicks'),1);assert.equal(await evaluate('window.lastPageClickTrusted'),true);
        await click(rect.x+160,rect.y+150);assert.equal(await evaluate('window.pageClicks'),1);
        await click(rect.x+50,rect.y+25);assert(await child('calls.some(c=>c.method==="OnGuiPointer"&&c.args[2]==="down")'));
        await click(rect.x+160,rect.y+150,'right');await until(()=>child('!!document.querySelector("[role=menu]")'));
        await sleep(200);await clickChild('[role=menuitem]');await until(()=>child('calls.some(c=>c.method==="OnContextMenuItem")'));
        await click(rect.x+160,rect.y+150,'right');await until(()=>child('!!document.querySelector("[role=menu]")'));await sleep(200);
        await click(rect.x+10,rect.y+100);await until(()=>child('!document.querySelector("[role=menu]")'));
        assert.equal(await evaluate('window.pageClicks'),2);
        await send('Input.dispatchMouseEvent',{type:'mousePressed',x:rect.x+160,y:rect.y+150,button:'left',buttons:1,clickCount:1});
        await sleep(150);await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+5,y:rect.y+100,button:'left',buttons:1});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',x:rect.x+5,y:rect.y+100,button:'left',buttons:0,clickCount:1});
        assert.equal(await evaluate('window.pageClicks'),2,'Dragging a character released a click on the underlying page');
        await sleep(200);await send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:1});
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:rect.x+10,y:rect.y+100}]});await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.equal(await evaluate('window.pageClicks'),3);
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:rect.x+160,y:rect.y+150}]});await until(()=>child('!!document.querySelector("[role=menu]")'));
        await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await child('runtime.closeContextMenu()');
        await send('Emulation.setTouchEmulationEnabled',{enabled:false});
        await child('window.startAsr()');await until(()=>child('!!document.querySelector(".game-microphone-prompt")'));
        assert.equal(await child('window.microphoneCalls'),0,'Startup requested microphone access before consent');
        await child('window.microphoneFailure="NotAllowedError"');await clickChild('.game-microphone-prompt button');
        await until(()=>child('document.querySelector(".game-microphone-prompt button")?.textContent==="重试麦克风"'));
        assert.equal(await child('window.microphoneCalls'),1);
        await child('for(let i=0;i<12;i++)window.startAsr()');await sleep(150);
        assert.equal(await child('window.microphoneCalls'),1,'Permission denial caused an automatic request loop');
        await child('window.microphoneFailure=null');await clickChild('.game-microphone-prompt button');
        await until(()=>child('window.recognizers.at(-1)?.starts===1'));
        assert.equal(await child('window.recognizers.filter(r=>r.starts).length'),1,'A cancelled pending listener started after consent');
        await child('window.recognizers.at(-1).onerror({error:"audio-capture"})');
        await until(()=>child('!!document.querySelector(".game-microphone-prompt")'));
        const before=await child('window.microphoneCalls');await sleep(200);assert.equal(await child('window.microphoneCalls'),before,'Audio-capture failure retried without user action');
        const starts=await child('window.recognizers.at(-1).starts');
        await clickChild('.game-microphone-prompt button');await until(()=>child(`window.recognizers.at(-1)?.starts===${starts+1}`));
        await child('window.stopAsr()');
        // The project setting moves the host iframe, with screen coordinates remaining stable as it moves.
        await child('runtime.configureGame(300,360,true,true,"left")');
        const beforeDrag=await evaluate('(()=>{const r=document.querySelector("iframe").getBoundingClientRect();return {x:r.x,y:r.y};})()');
        await send('Input.dispatchMouseEvent',{type:'mousePressed',x:beforeDrag.x+160,y:beforeDrag.y+150,button:'left',buttons:1,clickCount:1});
        await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:beforeDrag.x+90,y:beforeDrag.y+100,button:'left',buttons:1});
        await until(()=>evaluate(`Math.abs(document.querySelector('iframe').getBoundingClientRect().x-${beforeDrag.x-70})<1`));
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',x:beforeDrag.x+90,y:beforeDrag.y+100,button:'left',buttons:0,clickCount:1});
        assert.equal(await evaluate('document.querySelector("iframe").getBoundingClientRect().y'),beforeDrag.y-50);
        await until(()=>evaluate('document.querySelector("iframe").style.clipPath.startsWith("path(")'));
        console.log(`PASS DPR=${dpr}: trusted page clicks through alpha holes, character/GUI/menu hits, drag capture, touch hold, microphone consent, denial and audio-capture retry`);
    }
    assert.deepEqual(errors,[]);
    writeFileSync(resolve(temp,'sprite.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
    }
}catch(error){console.error(await child('({calls:window.calls.slice(-10),mic:window.microphoneCalls,prompt:document.querySelector(".game-microphone-prompt")?.innerText})').catch(()=>null));throw error;}
finally{if(ws?.readyState===1){await send('Browser.close').catch(()=>{});ws.close();}browser.kill();await new Promise(ok=>server.close(ok));}
