import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:http';
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {resolve,extname,sep} from 'node:path';

const root=process.cwd(),temp=resolve(root,'temp/context-menu-browser');mkdirSync(temp,{recursive:true});
const server=createServer((req,res)=>{
    const path=resolve(root,'.'+decodeURIComponent(new URL(req.url,'http://localhost').pathname));
    if(!path.startsWith(root+sep)){res.writeHead(403);res.end();return;}
    try{res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css'})[extname(path)]||'application/octet-stream');res.end(readFileSync(path));}
    catch{res.writeHead(404);res.end();}
});
await new Promise(ok=>server.listen(0,'127.0.0.1',ok));
const debugPort=9248;
const browser=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',[
    '--headless=new',`--remote-debugging-port=${debugPort}`,`--user-data-dir=${resolve(temp,'profile-'+Date.now())}`,
    '--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-extensions','--window-size=600,500','about:blank'
],{windowsHide:true,stdio:'ignore'});
let ws,seq=0;const pending=new Map(),errors=[];
const sleep=ms=>new Promise(ok=>setTimeout(ok,ms));
async function until(fn,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){try{const result=await fn();if(result)return result;}catch{}await sleep(100);}throw Error('Browser condition timed out');}
function send(method,params={}){return new Promise((ok,fail)=>{const id=++seq;const timer=setTimeout(()=>{pending.delete(id);fail(Error(method+' timed out'));},30000);pending.set(id,{ok:v=>{clearTimeout(timer);ok(v);},fail:e=>{clearTimeout(timer);fail(e);}});ws.send(JSON.stringify({id,method,params}));});}
async function evaluate(expression){const result=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description||result.exceptionDetails.text);return result.result.value;}
try{
    const page=await until(async()=>{const r=await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`,{method:'PUT'});return r.ok&&r.json();});
    ws=new WebSocket(page.webSocketDebuggerUrl);await new Promise((ok,fail)=>{ws.onopen=ok;ws.onerror=fail;});
    ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p?.fail(Error(m.error.message)):p?.ok(m.result);}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);};
    await send('Runtime.enable');
    for(const ratio of [1,2]){
        await send('Emulation.setDeviceMetricsOverride',{width:600,height:500,deviceScaleFactor:ratio,mobile:false});
        await send('Page.navigate',{url:`http://127.0.0.1:${server.address().port}/tests/helpers/web-context-menu.html`});
        await until(()=>evaluate('window.ready'));
        await send('Input.dispatchMouseEvent',{type:'mousePressed',x:150,y:120,button:'right',buttons:2,clickCount:1});
        await send('Input.dispatchMouseEvent',{type:'mouseReleased',x:150,y:120,button:'right',buttons:0,clickCount:1});
        await until(()=>evaluate('!!document.querySelector("[role=menu]")'));
        const bounds=await evaluate(`(()=>{const menu=document.querySelector('[role=menu]'),frame=document.getElementById('fixture-frame'),m=menu.getBoundingClientRect(),f=frame.getBoundingClientRect(),s=getComputedStyle(menu),b=menu.querySelector('button'),c=document.getElementById('game-canvas').getBoundingClientRect();return {inside:m.left>=f.left&&m.top>=f.top&&m.right<=f.right+.1&&m.bottom<=f.bottom+.1,canvas:[c.left,c.top,c.width,c.height],menu:[m.left,m.top,m.right,m.bottom],frame:[f.left,f.top,f.right,f.bottom],width:m.width,padding:s.paddingLeft,border:s.borderTopWidth,font:getComputedStyle(b).fontSize,focused:document.activeElement===b};})()`);
        assert.equal(bounds.inside,true,JSON.stringify(bounds));assert.equal(bounds.width,198);assert.equal(bounds.padding,'8px');assert.equal(bounds.border,'1px');assert.equal(bounds.font,'18px');assert.equal(bounds.focused,true);
        await evaluate(`document.querySelectorAll('[role=menuitem]')[1].click()`);
        assert.equal(await evaluate(`calls.filter(c=>c.method==='OnContextMenuItem').length`),0);
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'ArrowDown',code:'ArrowDown',windowsVirtualKeyCode:40});
        assert.equal(await evaluate(`document.activeElement.textContent`),'关闭精灵');
        await evaluate(`document.activeElement.click()`);
        assert.deepEqual(await evaluate(`calls.find(c=>c.method==='OnContextMenuItem').args`),['actor-menu','item-2','fixture-token']);
        assert.equal(await evaluate(`!!document.querySelector('[role=menu]')`),false);
        await evaluate(`window.menu(undefined,undefined,20)`);
        assert.equal(await evaluate(`(()=>{const m=document.querySelector('[role=menu]');return m.scrollHeight>m.clientHeight&&m.getBoundingClientRect().height<=180;})()`),true);
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'End',code:'End',windowsVirtualKeyCode:35});
        assert.equal(await evaluate(`(()=>{const m=document.querySelector('[role=menu]'),b=document.activeElement,r=b.getBoundingClientRect(),p=m.getBoundingClientRect();return b.textContent==='菜单项 20'&&m.scrollTop>0&&r.top>=p.top&&r.bottom<=p.bottom;})()`),true);
        await send('Input.dispatchKeyEvent',{type:'keyDown',key:'Home',code:'Home',windowsVirtualKeyCode:36});
        assert.equal(await evaluate(`(()=>{const m=document.querySelector('[role=menu]'),b=document.activeElement,r=b.getBoundingClientRect(),p=m.getBoundingClientRect();return b.textContent==='切换动作'&&r.top>=p.top&&r.bottom<=p.bottom;})()`),true);
        if(ratio===1){const shot=await send('Page.captureScreenshot',{format:'png'});writeFileSync(resolve(temp,'web-context-menu.png'),Buffer.from(shot.data,'base64'));}
        await evaluate(`window.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))`);
        assert.equal(await evaluate(`!!document.querySelector('[role=menu]')`),false);
        await send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:2});
        await evaluate('window.calls=[]');
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:150,y:120,id:1}]});
        await until(()=>evaluate(`!!document.querySelector('[role=menu]')`));
        await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        await sleep(100);
        assert.equal(await evaluate(`calls.filter(c=>c.method==='OnContextMenuRequest').length`),1);
        assert.equal(await evaluate(`calls.filter(c=>c.method==='OnContextMenuItem').length`),0);
        await send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:30,y:30,id:2}]});
        await send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.equal(await evaluate(`!!document.querySelector('[role=menu]')`),false);
        await evaluate(`runtime.setInputCapture(true);window.menu();runtime.closeContextMenu();window.calls=[];document.getElementById('game-canvas').dispatchEvent(new MouseEvent('contextmenu',{clientX:150,clientY:120}));`);
        assert.equal(await evaluate(`calls.filter(c=>c.method==='OnContextMenuRequest').length`),0);
        await send('Emulation.setTouchEmulationEnabled',{enabled:false});
        console.log(`PASS browser menu: DPR=${ratio}, right click, touch hold, disabled items, keyboard, bounds, scrolling, dismissal and input capture`);
    }
    assert.deepEqual(errors,[]);
}finally{ws?.close();browser.kill();await new Promise(ok=>server.close(ok));}
