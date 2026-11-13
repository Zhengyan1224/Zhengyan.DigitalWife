import test from 'node:test';
import assert from 'node:assert/strict';
import * as runtime from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js';

function setup(t) {
    const old=new Map(['window','document','navigator','location'].map(k=>[k,Object.getOwnPropertyDescriptor(globalThis,k)]));
    const target=()=>({handlers:new Map(),addEventListener(k,f){this.handlers.set(k,f);},removeEventListener(k){this.handlers.delete(k);},emit(k){this.handlers.get(k)?.({});}});
    const classes=new Set(),calls=[],frames=[];
    const doc={...target(),fullscreenElement:null,fullscreenEnabled:true,
        documentElement:{classList:{toggle(k,v){if(v)classes.add(k);else classes.delete(k);}}},
        createElement(){return {...target(),remove(){this.removed=true;}};},
        async exitFullscreen(){this.fullscreenElement=null;this.emit('fullscreenchange');}};
    const frame={style:{},children:[],append(child){this.children.push(child);},
        getBoundingClientRect(){return {width:parseInt(this.style.width)||800,height:parseInt(this.style.height)||450};},
        async requestFullscreen(){doc.fullscreenElement=this;doc.emit('fullscreenchange');}};
    const canvas={...target(),parentElement:frame,style:{},width:0,height:0,
        getBoundingClientRect:()=>({...frame.getBoundingClientRect(),left:0,top:0})};
    globalThis.document=doc;
    globalThis.window={...target(),devicePixelRatio:2,requestAnimationFrame(f){frames.push(f);return frames.length;},cancelAnimationFrame(){}};
    globalThis.location={search:'?embed=1'};
    Object.defineProperty(globalThis,'navigator',{configurable:true,value:{userActivation:{isActive:false}}});
    runtime.start(canvas,{invokeMethodAsync:async(name,...args)=>{calls.push({name,args});}});
    t.after(()=>{runtime.stop();for(const[k,d]of old){if(d)Object.defineProperty(globalThis,k,d);else delete globalThis[k];}});
    return {frame,doc,calls,classes};
}

test('script dimensions resize the canvas container and embed/pet mode can be changed',t=>{
    const app=setup(t);
    runtime.configureGame(1280,720,true);
    assert(app.classes.has('game-embedded'));assert(app.classes.has('game-transparent'));
    runtime.setGameSize(640,480);
    assert.equal(app.frame.style.width,'640px');assert.equal(app.frame.style.height,'480px');
    runtime.configureGame(1280,720,false);assert(!app.classes.has('game-transparent'));
});

test('fullscreen waits for explicit activation, reports actual state and supports escape/exit',async t=>{
    const app=setup(t);
    runtime.setGameFullscreen(true);
    assert.equal(app.doc.fullscreenElement,null);
    const button=app.frame.children[0];assert.equal(button.textContent,'点击进入全屏');
    button.emit('click');await Promise.resolve();await Promise.resolve();
    assert.equal(app.doc.fullscreenElement,app.frame);assert(button.removed);
    assert(app.calls.some(c=>c.name==='OnWindowStateChanged'&&c.args[2]===true));
    runtime.setGameFullscreen(false);await Promise.resolve();
    assert.equal(app.doc.fullscreenElement,null);
    assert.equal(app.calls.filter(c=>c.name==='OnWindowStateChanged').at(-1).args[2],false);
    runtime.setGameFullscreen(true);const pending=app.frame.children.at(-1);
    runtime.setGameFullscreen(false);assert(pending.removed);
});

test('user-activated fullscreen requests directly and denied iframe policy reports an error',async t=>{
    const app=setup(t);navigator.userActivation.isActive=true;
    runtime.setGameFullscreen(true);await Promise.resolve();assert.equal(app.doc.fullscreenElement,app.frame);
    runtime.setGameFullscreen(false);await Promise.resolve();app.doc.fullscreenEnabled=false;
    runtime.setGameFullscreen(true);
    assert(app.calls.some(c=>c.name==='OnBrowserWarning'&&c.args[0].includes('全屏')));
});
