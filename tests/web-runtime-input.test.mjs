import assert from 'node:assert/strict';
import test from 'node:test';
import * as runtime from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js';
import { deferred, settle } from './helpers/web-renderer-fixtures.mjs';

test('runtime dependencies share the WASM build version and cannot reuse bare cached modules', () => {
    const base = 'http://localhost:5052/js/web-runtime.js?v=build-2';
    for (const name of ['webgl2-renderer.js','web-audio.js','web-platform.js','web-gpu.js','web-desktop-sprite.js','web-microphone.js'])
        assert.equal(runtime.moduleUrl(name,base),`http://localhost:5052/js/${name}?v=build-2`);
    assert.equal(runtime.moduleUrl('web-audio.js','http://localhost:5052/js/web-runtime.js'),
        'http://localhost:5052/js/web-audio.js');
});

function setup(t, devicePixelRatio = 1) {
    const previousWindow = globalThis.window;
    const callbacks = new Map();
    let nextFrame = 0;
    function eventTarget() {
        const handlers = new Map();
        return {
            addEventListener: (name, handler) => handlers.set(name, handler),
            removeEventListener: name => handlers.delete(name),
            emit(name, event) { handlers.get(name)?.({ type: name, preventDefault() {}, ...event }); },
            handlers
        };
    }
    const browser = {
        ...eventTarget(), devicePixelRatio,
        requestAnimationFrame: callback => { callbacks.set(++nextFrame, callback); return nextFrame; },
        cancelAnimationFrame: id => callbacks.delete(id)
    };
    const canvas = {
        ...eventTarget(), width: 100, height: 100,
        getBoundingClientRect: () => ({ left: 10, top: 20, width: 100, height: 100 }),
        setPointerCapture() {}, releasePointerCapture() {}
    };
    const calls = [];
    let frameResult = () => Promise.resolve();
    const dotNet = {
        invokeMethodAsync(method, ...args) {
            calls.push({ method, args });
            return method === 'OnAnimationFrameWithInput' ? frameResult() : Promise.resolve();
        }
    };
    globalThis.window = browser;
    runtime.start(canvas, dotNet);
    t.after(() => {
        runtime.stop();
        if (previousWindow === undefined) delete globalThis.window;
        else globalThis.window = previousWindow;
    });
    return {
        browser, canvas, calls, callbacks, dotNet,
        setFrameResult: callback => { frameResult = callback; },
        pointer: (name, overrides = {}) => canvas.emit(name, { pointerId: 1, pointerType: 'mouse', clientX: 10, clientY: 20, button: 0, buttons: 1, ...overrides }),
        async frame(timestamp = 16) {
            const [id, callback] = callbacks.entries().next().value;
            callbacks.delete(id);
            callback(timestamp);
            await settle();
        },
        input: () => calls.filter(call => call.method === 'OnAnimationFrameWithInput').at(-1).args[1]
    };
}

test('a thousand mouse moves are accumulated in one frame call without losing motion', async t => {
    const app = setup(t);
    app.pointer('pointerdown');
    for (let i = 1; i <= 1000; i++) app.pointer('pointermove', { clientX: 10 + i, clientY: 20 + i * .5 });
    assert.equal(app.calls.filter(call => call.method === 'OnPointerInput').length, 0);
    assert.equal(app.calls.filter(call => call.method === 'OnAnimationFrameWithInput').length, 0);
    await app.frame();
    assert.deepEqual(app.input(), { lookX:0,lookY:0,panX:0,panY:0,zoom:0,pointerX:1000,pointerY:500,buttons:1,pointerDeltaX:1000,pointerDeltaY:500,mouseMotions:[{x:1000,y:500,buttons:1,alt:false,locked:false}] });
    await app.frame(32);
    assert.equal(app.input(), null);
});

test('touch transitions retain taps between frames, scale with DPR and cancel on blur', async t => {
    const app=setup(t,2);
    app.pointer('pointerdown',{pointerType:'touch',pointerId:7,clientX:20,clientY:30,pressure:.5});
    app.pointer('pointerup',{pointerType:'touch',pointerId:7,clientX:21,clientY:31,buttons:0});
    await app.frame();
    assert.deepEqual(app.input().touchTransitions.map(t=>[t.id,t.phase,t.x,t.y]),[[7,0,20,20],[7,3,22,22]]);
    assert.equal(app.input().touches.length,0);
    app.pointer('pointerdown',{pointerType:'touch',pointerId:8});await app.frame(32);
    assert.equal(app.input().touches.length,1);
    app.browser.emit('blur',{});await app.frame(48);
    assert.equal(app.input().touches.length,0);assert.equal(app.input().touchTransitions[0].phase,4);
});

test('two fingers pan around their center and pinch without also orbiting',async t=>{
    const app=setup(t,2);
    app.pointer('pointerdown',{pointerType:'touch',pointerId:1,clientX:30,clientY:30});
    app.pointer('pointerdown',{pointerType:'touch',pointerId:2,clientX:70,clientY:30});await app.frame();
    app.pointer('pointermove',{pointerType:'touch',pointerId:2,clientX:90,clientY:30});await app.frame(32);
    assert.equal(app.input().lookX,0);assert.equal(app.input().panX,20);assert.equal(app.input().zoom,-.4);
});

test('right dragging and wheel motion preserve the final button state', async t => {
    const app = setup(t);
    app.pointer('pointerdown', { button: 2, buttons: 2 });
    app.pointer('pointermove', { button: 2, buttons: 2, clientX: 50, clientY: 60 });
    app.canvas.emit('wheel', { clientX: 50, clientY: 60, deltaY: 120 });
    await app.frame();
    assert.equal(app.input().mouseMotions[0].x, 40);
    assert.equal(app.input().mouseMotions[0].y, 40);
    assert.equal(app.input().wheel, -1.2);
    assert.equal(app.input().buttons, 2);
    app.pointer('pointerup', { button: 2, buttons: 0, clientX: 50, clientY: 60 });
    app.pointer('pointermove', { buttons: 0, clientX: 80, clientY: 80 });
    await app.frame(32);
    assert.equal(app.input().lookX, 0);
    assert.equal(app.input().panX, 0);
    assert.equal(app.input().buttons, 0);
});

test('a complete right drag between frames retains its motion and modifier despite release', async t => {
    const app=setup(t);
    app.pointer('pointerdown',{button:2,buttons:2});
    app.pointer('pointermove',{buttons:2,altKey:true,clientX:34,clientY:32});
    app.pointer('pointerup',{button:2,buttons:0,clientX:34,clientY:32});
    await app.frame();
    assert.equal(app.input().buttons,0);
    assert.deepEqual(app.input().mouseMotions,[{x:24,y:12,buttons:2,alt:true,locked:false}]);
});

test('GUI captures only the finger that began on it, and frame touch deltas accumulate', async t => {
    const app=setup(t);
    runtime.setOverlayState({controls:[{x:0,y:0,width:20,height:20}]});
    app.pointer('pointerdown',{pointerType:'touch',pointerId:1,clientX:15,clientY:25});
    app.pointer('pointerdown',{pointerType:'touch',pointerId:2,clientX:60,clientY:70});
    await app.frame();
    app.pointer('pointermove',{pointerType:'touch',pointerId:1,clientX:90,clientY:90});
    app.pointer('pointermove',{pointerType:'touch',pointerId:2,clientX:70,clientY:75});
    app.pointer('pointermove',{pointerType:'touch',pointerId:2,clientX:80,clientY:80});
    await app.frame(32);
    assert.deepEqual(app.input().cameraTouches.map(t=>[t.id,t.deltaX,t.deltaY]),[[2,20,10]]);
    assert.equal(app.input().touches.length,2);
    assert.equal(app.input().buttons,0);
});

test('device pixel ratio scales mouse coordinates and deltas into native render pixels', async t => {
    const app = setup(t, 2);
    app.pointer('pointerdown');
    app.pointer('pointermove', { clientX: 35, clientY: 30 });
    await app.frame();
    assert.equal(app.input().pointerX, 50);
    assert.equal(app.input().pointerY, 20);
    assert.equal(app.input().pointerDeltaX, 50);
    assert.equal(app.input().pointerDeltaY, 20);
});

test('GUI capture clears queued motion and releases keys already held', async t => {
    const app = setup(t);
    app.browser.emit('keydown', { code: 'KeyW' });
    app.pointer('pointerdown');
    app.pointer('pointermove', { clientX: 80 });
    runtime.setInputCapture(true);
    app.pointer('pointermove', { clientX: 100 });
    await app.frame();
    assert.equal(app.input().lookX, 0);
    assert.equal(app.input().buttons, 0);
    assert.deepEqual(app.calls.filter(call => call.method === 'OnKeyInput').map(call => call.args), [['KeyW', true], ['KeyW', false]]);
    runtime.setInputCapture(false);
    app.pointer('pointerup', { buttons: 0 });
    await app.frame(32);
    assert.equal(app.input().buttons, 0);
});

test('GUI and sprite clicks remain separate from camera gestures', async t => {
    const app = setup(t);
    runtime.setOverlayState({ controls: [{ x: 0, y: 0, width: 20, height: 20 }] });
    app.pointer('pointerdown', { clientX: 15, clientY: 25 });
    app.pointer('pointermove', { clientX: 18, clientY: 28 });
    app.pointer('pointerup', { clientX: 18, clientY: 28, buttons: 0 });
    await app.frame();
    assert.equal(app.input(), null);
    assert.deepEqual(app.calls.filter(call => call.method === 'OnGuiPointer').map(c=>c.args), [[5,5,'down',0],[8,8,'up',0]]);
    runtime.setOverlayState({ sprites: [{ x: 0, y: 0, width: 20, height: 20 }] });
    app.pointer('pointerdown', { clientX: 15, clientY: 25 });
    app.pointer('pointerup', { clientX: 15, clientY: 25, buttons: 0 });
    await app.frame(32);
    assert.equal(app.input(), null);
    assert.ok(app.calls.some(call => call.method === 'OnSpritePointer'));
});

test('blur releases keyboard and mouse state without forwarding stale motion', async t => {
    const app = setup(t);
    app.browser.emit('keydown', { code: 'KeyW' });
    app.browser.emit('keydown', { code: 'KeyW', repeat: true });
    app.pointer('pointerdown');
    app.pointer('pointermove', { clientX: 80 });
    app.browser.emit('blur');
    await app.frame();
    assert.equal(app.input().buttons, 0);
    assert.equal(app.input().lookX, 0);
    assert.deepEqual(app.calls.filter(call => call.method === 'OnKeyInput').map(call => call.args), [['KeyW', true], ['KeyW', false]]);
});

test('a frame completing after restart cannot schedule another loop on the new runtime', async t => {
    const app = setup(t);
    const pending = deferred();
    app.setFrameResult(() => pending.promise);
    await app.frame();
    assert.equal(app.callbacks.size, 0);
    runtime.start(app.canvas, app.dotNet);
    assert.equal(app.callbacks.size, 1);
    pending.resolve();
    await settle();
    assert.equal(app.callbacks.size, 1);
});

test('frame failures are reported instead of stopping silently', async t => {
    const app = setup(t);
    app.setFrameResult(() => Promise.reject(new Error('pose update failed')));
    await app.frame();
    assert.equal(app.callbacks.size, 0);
    assert.match(app.calls.find(call => call.method === 'OnBrowserError').args[0], /Web runtime frame failed: pose update failed/);
});

test('the browser context menu does not interrupt right mouse camera controls', t => {
    const app = setup(t);
    let prevented = false;
    app.canvas.emit('contextmenu', { preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
});

test('right click requests the target in render pixels and right drag never opens a menu', t => {
    const app=setup(t,2);
    app.pointer('pointerdown',{button:2,buttons:2,clientX:40,clientY:60});
    app.canvas.emit('contextmenu',{clientX:40,clientY:60}); // Browsers that emit on press.
    assert.equal(app.calls.filter(c=>c.method==='OnContextMenuRequest').length,0);
    app.pointer('pointerup',{button:2,buttons:0,clientX:40,clientY:60});
    assert.deepEqual(app.calls.find(c=>c.method==='OnContextMenuRequest').args,[60,80]);
    app.calls.length=0;
    app.pointer('pointerdown',{button:2,buttons:2});
    app.canvas.emit('contextmenu',{clientX:10,clientY:20});
    app.pointer('pointermove',{buttons:2,clientX:40});
    app.pointer('pointermove',{buttons:2,clientX:10}); // Returning to the start is still a drag.
    app.pointer('pointerup',{button:2,buttons:0});
    assert.equal(app.calls.filter(c=>c.method==='OnContextMenuRequest').length,0);
});

test('touch hold opens once, cancels a GUI press and consumes the following release', async t => {
    t.mock.timers.enable({apis:['setTimeout']});
    const app=setup(t,2);
    runtime.setOverlayState({controls:[{id:'target',x:0,y:0,width:100,height:100}]});
    app.pointer('pointerdown',{pointerType:'touch',clientX:30,clientY:40});
    t.mock.timers.tick(499);
    assert.equal(app.calls.filter(c=>c.method==='OnContextMenuRequest').length,0);
    t.mock.timers.tick(1);
    assert.deepEqual(app.calls.find(c=>c.method==='OnContextMenuRequest').args,[40,40]);
    assert.deepEqual(app.calls.filter(c=>c.method==='OnGuiPointer').map(c=>c.args[2]),['down','cancel']);
    app.canvas.emit('contextmenu',{pointerType:'touch',clientX:30,clientY:40});
    app.pointer('pointerup',{pointerType:'touch',buttons:0,clientX:30,clientY:40});
    assert.equal(app.calls.filter(c=>c.method==='OnContextMenuRequest').length,1);
    assert.equal(app.calls.filter(c=>c.method==='OnGuiPointer'&&c.args[2]==='up').length,0);
    await app.frame();
    assert.equal(app.input().touches.length,0);
    assert.equal(app.input().touchTransitions.at(-1).phase,4);
});

for (const action of ['blur', 'capture', 'cancel']) {
    test(`${action} clears a pending right menu request`, t => {
        const app = setup(t);
        app.pointer('pointerdown', { button: 2, buttons: 2 });
        app.canvas.emit('contextmenu', { clientX: 10, clientY: 20 });
        if (action === 'blur') app.browser.emit('blur', {});
        if (action === 'capture') { runtime.setInputCapture(true); runtime.setInputCapture(false); }
        if (action === 'cancel') app.pointer('pointercancel', { button: 0, buttons: 0 });
        app.pointer('pointerdown', { button: 2, buttons: 2 });
        app.pointer('pointerup', { button: 2, buttons: 0 });
        assert.equal(app.calls.filter(c => c.method === 'OnContextMenuRequest').length, 0);
    });
}

test('blur cancels a GUI hold and consumes its later release', t => {
    const app = setup(t);
    runtime.setOverlayState({ controls: [{ id: 'target', x: 0, y: 0, width: 100, height: 100 }] });
    app.pointer('pointerdown', { pointerType: 'touch', clientX: 30, clientY: 40 });
    app.browser.emit('blur', {});
    app.pointer('pointerup', { pointerType: 'touch', buttons: 0, clientX: 30, clientY: 40 });
    assert.deepEqual(app.calls.filter(c => c.method === 'OnGuiPointer').map(c => c.args[2]), ['down', 'cancel']);
});

for(const action of ['drag','multitouch','cancel','release','blur','capture','stop']) {
    test(`${action} cancels touch long press`,t=>{
        t.mock.timers.enable({apis:['setTimeout']});
        const app=setup(t);
        app.pointer('pointerdown',{pointerType:'touch'});
        if(action==='drag')app.pointer('pointermove',{pointerType:'touch',clientX:19});
        if(action==='multitouch')app.pointer('pointerdown',{pointerType:'touch',pointerId:2});
        if(action==='cancel')app.pointer('pointercancel',{pointerType:'touch',buttons:0});
        if(action==='release')app.pointer('pointerup',{pointerType:'touch',buttons:0});
        if(action==='blur')app.browser.emit('blur',{});
        if(action==='capture')runtime.setInputCapture(true);
        if(action==='stop')runtime.stop();
        t.mock.timers.tick(1000);
        assert.equal(app.calls.filter(c=>c.method==='OnContextMenuRequest').length,0);
    });
}
