import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {mkdirSync, readFileSync, writeFileSync} from 'node:fs';
import {resolve, extname, sep} from 'node:path';
import {buildEmbedExamples} from '../../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/embed-guide.js';

const root=process.cwd(), wwwroot=resolve(root,'src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot');
const temp=resolve(root,'temp/web-embed-guide-browser'); mkdirSync(temp,{recursive:true});
const pages=new Map(), messages=[];
let businessOrigin, playerOrigin;
const fixture=readFileSync(resolve(root,'tests/helpers/web-desktop-sprite-child.html'),'utf8')
    .replaceAll('../../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/','/')
    + `<script>parent.postMessage({type:'guide-fixture-ready',microphone:document.featurePolicy?.allowsFeature('microphone'),origin:location.origin},'*');</script>`;
const mime=path=>({'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json'})[extname(path)]||'application/octet-stream';
const player=createServer((req,res)=>{
    const path=new URL(req.url,'http://localhost').pathname;
    try{
        res.setHeader('Content-Type',mime(path==='/'?'index.html':path));
        res.setHeader('Cache-Control','no-store');
        if(path==='/'||path.endsWith('.html')) res.setHeader('Content-Security-Policy',`frame-ancestors 'self' ${businessOrigin};`);
        if(path==='/'||path==='/index.html'){res.end(fixture);return;}
        if(path==='/guide-index.html'){
            res.end(readFileSync(resolve(wwwroot,'index.html'),'utf8')
                .replace(/<script src="_framework[^\"]*"><\/script>/,'')
                .replace('<script type="importmap"></script>',''));
            return;
        }
        const file=resolve(wwwroot,'.'+decodeURIComponent(path));
        if(!file.startsWith(wwwroot+sep)){res.writeHead(403);res.end();return;}
        res.end(readFileSync(file));
    }catch{res.writeHead(404);res.end();}
});
const business=createServer((req,res)=>{
    const path=new URL(req.url,'http://localhost').pathname;
    res.setHeader('Content-Type','text/html; charset=utf-8');
    res.setHeader('Cache-Control','no-store');
    res.setHeader('Content-Security-Policy',`default-src 'self'; script-src 'self' ${playerOrigin} 'nonce-fixture'; frame-src 'self' ${playerOrigin}; style-src 'self' 'unsafe-inline';`);
    const headers=buildEmbedExamples({playerUrl:playerOrigin+'/',businessUrl:businessOrigin+'/',width:300,height:360}).businessHeaders;
    res.setHeader('Permissions-Policy',path==='/deny-microphone.html'?'microphone=()':headers.split('\n').find(line=>line.startsWith('Permissions-Policy: ')).slice(20));
    if(path==='/spoof.html'){
        res.end(`<script nonce="fixture">parent.postMessage({type:'zhengyan-desktop-sprite-region',enabled:true,rects:[]},'*');parent.postMessage({type:'spoof-ready'},'*');</script>`);
        return;
    }
    const example=pages.get(path);
    if(!example){res.writeHead(404);res.end();return;}
    res.end(`<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#e6f1ee}#behind{position:fixed;inset:0;border:0;background:transparent}</style>
        <button id="behind">业务页面</button>
        <script nonce="fixture">window.reports=[];window.pageClicks=0;document.querySelector('#behind').onclick=e=>{pageClicks++;window.trustedPageClick=e.isTrusted;};
        addEventListener('message',e=>{if(e.source===document.querySelector('iframe')?.contentWindow)reports.push({origin:e.origin,data:e.data});});</script>${example}`);
});
await new Promise(ok=>player.listen(0,'127.0.0.1',ok));
await new Promise(ok=>business.listen(0,'127.0.0.1',ok));
playerOrigin=`http://127.0.0.1:${player.address().port}`;
businessOrigin=`http://localhost:${business.address().port}`;
const debugPort=9261;
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',[
    '--headless=new',`--remote-debugging-port=${debugPort}`,`--user-data-dir=${resolve(temp,'profile-'+Date.now())}`,
    '--use-fake-ui-for-media-stream','--use-fake-device-for-media-stream','--no-first-run','--no-default-browser-check',
    '--disable-background-networking','--disable-extensions','--window-size=1400,1000','about:blank'
],{windowsHide:true,stdio:'ignore'});
let ws,seq=0; const pending=new Map(), errors=[];
const sleep=ms=>new Promise(ok=>setTimeout(ok,ms));
async function until(fn,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){try{const value=await fn();if(value)return value;}catch{}await sleep(100);}throw Error('Browser condition timed out');}
function send(method,params={}){return new Promise((ok,fail)=>{const id=++seq,timer=setTimeout(()=>{pending.delete(id);fail(Error(method+' timed out'));},30000);pending.set(id,{ok:v=>{clearTimeout(timer);ok(v);},fail:e=>{clearTimeout(timer);fail(e);}});ws.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(r.exceptionDetails.exception?.description||r.exceptionDetails.text);return r.result.value;}
async function click(x,y){await send('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',buttons:1,clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',x,y,button:'left',buttons:0,clickCount:1});}
try{
    const page=await until(async()=>{const r=await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`,{method:'PUT'});return r.ok&&r.json();});
    ws=new WebSocket(page.webSocketDebuggerUrl); await new Promise((ok,fail)=>{ws.onopen=ok;ws.onerror=fail;});
    ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p?.fail(Error(m.error.message)):p?.ok(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.exception?.description||m.params.exceptionDetails.text);};
    await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
    await send('Network.setBypassServiceWorker',{bypass:true});
    for(const [mode,path] of [['game','/guide-index.html'],['sprite','/desktop-sprite.html']]){
        await send('Page.navigate',{url:playerOrigin+path});
        await until(()=>evaluate(`!!document.querySelector('web-embed-guide')?.shadowRoot?.querySelector('link').sheet`));
        if(mode==='sprite') await until(()=>evaluate(`document.querySelector('[data-web-desktop-sprite]')?.style.clipPath.startsWith('path(')`));
        const example=await evaluate(`(()=>{const r=document.querySelector('web-embed-guide').shadowRoot,f=r.querySelector('form');
            f.elements.player.value=${JSON.stringify(playerOrigin+'/')};f.elements.business.value=${JSON.stringify(businessOrigin+'/')};
            f.elements.width.value='300';f.elements.height.value='360';f.dispatchEvent(new Event('input',{bubbles:true}));
            return {html:r.querySelector('[data-code=html]').textContent,headers:r.querySelector('[data-code=businessHeaders]').textContent,valid:r.querySelector('.validation').hidden};})()`);
        assert(example.valid); assert.match(example.headers,new RegExp(playerOrigin.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')));
        const expected=buildEmbedExamples({mode,playerUrl:playerOrigin+'/',businessUrl:businessOrigin+'/',width:300,height:360});
        assert.equal(example.html,expected.html);
        await evaluate(`(()=>{const r=document.querySelector('web-embed-guide').shadowRoot;
            Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async text=>{window.copied=text;}}});
            const b=r.querySelector('[data-copy=html]');b.addEventListener('click',e=>window.copyTrusted=e.isTrusted);b.scrollIntoView({block:'center'});})()`);
        const button=await evaluate(`(()=>{const b=document.querySelector('web-embed-guide').shadowRoot.querySelector('[data-copy=html]').getBoundingClientRect();return{x:b.x+b.width/2,y:b.y+b.height/2};})()`);
        await click(button.x,button.y); await until(()=>evaluate('!!window.copied'));
        assert.equal(await evaluate('window.copied'),expected.html); assert.equal(await evaluate('window.copyTrusted'),true);
        writeFileSync(resolve(temp,mode+'-guide.png'),Buffer.from((await send('Page.captureScreenshot',{format:'png'})).data,'base64'));
        await evaluate(`(()=>{window.guideKeyDowns=0;window.guideKeyUps=0;addEventListener('keydown',()=>guideKeyDowns++);addEventListener('keyup',()=>guideKeyUps++);document.querySelector('web-embed-guide').shadowRoot.querySelector('[name=player]').focus();})()`);
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'w',code:'KeyW',text:'w',windowsVirtualKeyCode:87});
        await send('Input.dispatchKeyEvent',{type:'keyUp',key:'w',code:'KeyW',windowsVirtualKeyCode:87});
        assert.equal(await evaluate('guideKeyDowns'),0,'Typing in the documentation reached the game keyboard handler');
        assert.equal(await evaluate('guideKeyUps'),1,'The documentation prevented release of an already held game key');
        await evaluate(`(()=>{const r=document.querySelector('web-embed-guide').shadowRoot,f=r.querySelector('form');f.elements.player.value='javascript:alert(1)';f.dispatchEvent(new Event('input',{bubbles:true}));})()`);
        assert.equal(await evaluate(`document.querySelector('web-embed-guide').shadowRoot.querySelector('[data-copy=html]').disabled`),true);
        console.log(`PASS ${mode} page: address-specific code and headers, trusted copy click and invalid-input protection`);
        pages.set('/host-'+mode+'.html',example.html);
        if(mode==='game') pages.set('/deny-microphone.html',example.html);
        await send('Page.navigate',{url:businessOrigin+'/host-'+mode+'.html'});
        await until(()=>evaluate(`reports.some(r=>r.data.type==='guide-fixture-ready')`));
        assert.equal(await evaluate(`reports.find(r=>r.data.type==='guide-fixture-ready').origin`),playerOrigin);
        assert.equal(await evaluate(`reports.find(r=>r.data.type==='guide-fixture-ready').data.microphone`),true);
        assert.equal(await evaluate(`document.querySelector('iframe').contentDocument`),null,'The fixture did not enforce the cross-origin DOM boundary');
        assert.equal((await fetch(playerOrigin+'/desktop-sprite.js')).headers.get('Access-Control-Allow-Origin'),null);
        if(mode==='sprite'){
            await until(()=>evaluate(`document.querySelector('iframe').style.clipPath.startsWith('path(')`));
            const rect=await evaluate(`(()=>{const r=document.querySelector('iframe').getBoundingClientRect();return{x:r.x,y:r.y};})()`);
            await click(rect.x+10,rect.y+100);
            assert.equal(await evaluate('pageClicks'),1);assert.equal(await evaluate('trustedPageClick'),true);
            const mask=await evaluate(`document.querySelector('iframe').style.clipPath`);
            await evaluate(`postMessage({type:'zhengyan-desktop-sprite-region',enabled:true,rects:[]},location.origin)`);
            await sleep(150); assert.equal(await evaluate(`document.querySelector('iframe').style.clipPath`),mask);
            await evaluate(`document.querySelector('iframe').src=${JSON.stringify(businessOrigin+'/spoof.html')}`);
            await until(()=>evaluate(`reports.some(r=>r.data.type==='spoof-ready')`));
            assert.equal(await evaluate(`document.querySelector('iframe').style.clipPath`),mask,'A different origin replaced the sprite mask');
        }
        console.log(`PASS ${mode} copied example: cross-origin iframe, permission delegation and no player-wide CORS requirement`);
    }
    await send('Page.navigate',{url:businessOrigin+'/deny-microphone.html'});
    await until(()=>evaluate(`reports.some(r=>r.data.type==='guide-fixture-ready')`));
    assert.equal(await evaluate(`reports.find(r=>r.data.type==='guide-fixture-ready').data.microphone`),false,'iframe allow bypassed a restrictive parent policy');
    await send('Page.navigate',{url:playerOrigin+'/guide-index.html?embed=1'});
    await until(()=>evaluate(`!!document.querySelector('web-embed-guide')?.shadowRoot`));
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.embed-guide-page')).display`),'none');
    await send('Page.navigate',{url:playerOrigin+'/guide-index.html'});
    await until(()=>evaluate(`!!document.querySelector('web-embed-guide')?.shadowRoot`));
    await evaluate(`document.documentElement.classList.add('game-transparent')`);
    assert.notEqual(await evaluate(`getComputedStyle(document.querySelector('.embed-guide-page')).display`),'none','A standalone sprite project hid the homepage documentation');
    await send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
    await send('Page.navigate',{url:playerOrigin+'/guide-index.html'});
    await until(()=>evaluate(`!!document.querySelector('web-embed-guide')?.shadowRoot?.querySelector('link').sheet`));
    assert(await evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'The guide overflowed the mobile page');
    assert.deepEqual(errors,[]);
    console.log('PASS parent-policy denial, hidden embedded docs, visible standalone sprite docs, mobile layout and origin validation');
}catch(error){console.error(messages);console.error(errors);throw error;}
finally{
    if(ws?.readyState===1){await send('Browser.close').catch(()=>{});ws.close();}browser.kill();
    player.closeAllConnections();business.closeAllConnections();
    await Promise.all([new Promise(ok=>player.close(ok)),new Promise(ok=>business.close(ok))]);
}
