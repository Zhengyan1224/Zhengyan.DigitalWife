import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source=await readFile(new URL('../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/sw.js',import.meta.url),'utf8');

function worker(network,manifest) {
    const events=new Map(),cached={version:'old'},puts=[],keys=[],matches=[];
    const context={URL,Promise,self:{location:{origin:'https://game.test',href:'https://game.test/sw.js'},
        assetsManifest:manifest,addEventListener:(name,handler)=>events.set(name,handler)},fetch:network,
        caches:{match:async key=>{matches.push(key);return cached;},open:async()=>({put:async(key,value)=>{keys.push(key);puts.push(value);}})}};
    vm.runInNewContext(source,context);
    return {events,cached,puts,keys,matches};
}

test('mutable game manifests bypass stale caches and retain an offline fallback', async()=>{
    const fresh={ok:true,version:'new',clone(){return this;}},app=worker(async()=>fresh);
    let response;
    app.events.get('fetch')({request:{url:'https://game.test/web-game/web.manifest.json',method:'GET',cache:'no-store',mode:'cors'},
        respondWith:value=>response=value});
    assert.equal(await response,fresh);
    const offline=worker(async()=>{throw new Error('offline');});
    offline.events.get('fetch')({request:{url:'https://game.test/web-game/web.manifest.json',method:'GET',cache:'no-store',mode:'cors'},
        respondWith:value=>response=value});
    assert.equal(await response,offline.cached);
});

test('versioned engine modules can use the active published asset while offline',async()=>{
    const app=worker(async()=>assert.fail('network was used'),{version:'new',assets:[{url:'js/web-offline.js'}]});let response;
    app.events.get('fetch')({request:{url:'https://game.test/js/web-offline.js?v=current-build',method:'GET',mode:'cors'},respondWith:value=>response=value});
    assert.equal(await response,app.cached);
});

test('ordinary no-store requests still bypass the service worker', ()=>{
    const app=worker(async()=>assert.fail('unexpected fetch'));
    app.events.get('fetch')({request:{url:'https://game.test/web-game/assets/Body.pmx',method:'GET',cache:'no-store'},
        respondWith:()=>assert.fail('no-store asset request was intercepted')});
});

test('host pages retain their own offline HTML without replacing the player', async()=>{
    const fresh={ok:true,clone(){return this;}},app=worker(async()=>fresh);
    const navigate=async(state,path)=>{
        let response;
        state.events.get('fetch')({request:{url:'https://game.test/'+path,method:'GET',mode:'navigate'},respondWith:value=>response=value});
        return await response;
    };
    await navigate(app,'?embed=1&desktopSprite=1');
    await navigate(app,'desktop-sprite.html');
    assert.deepEqual(app.keys,['./index.html','https://game.test/desktop-sprite.html']);
    const offline=worker(async()=>{throw new Error('offline');});
    await navigate(offline,'?embed=1');
    await navigate(offline,'desktop-sprite.html');
    assert.deepEqual(offline.matches,['./index.html','https://game.test/desktop-sprite.html']);
});
