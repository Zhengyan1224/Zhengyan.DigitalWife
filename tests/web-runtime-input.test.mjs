import assert from 'node:assert/strict';
import test from 'node:test';
import * as runtime from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-runtime.js';
import { deferred, settle } from './helpers/web-renderer-fixtures.mjs';

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
    assert.deepEqual(app.input(), { lookX: 1000, lookY: 500, panX: 0, panY: 0, zoom: 0, pointerX: 1000, pointerY: 500, buttons: 1 });
    await app.frame(32);
    assert.equal(app.input(), null);
});

test('right dragging and wheel motion preserve the final button state', async t => {
    const app = setup(t);
    app.pointer('pointerdown', { button: 2, buttons: 2 });
    app.pointer('pointermove', { button: 2, buttons: 2, clientX: 50, clientY: 60 });
    app.canvas.emit('wheel', { clientX: 50, clientY: 60, deltaY: 120 });
    await app.frame();
    assert.equal(app.input().panX, 40);
    assert.equal(app.input().panY, 40);
    assert.equal(app.input().zoom, 1.2);
    assert.equal(app.input().buttons, 2);
    app.pointer('pointerup', { button: 2, buttons: 0, clientX: 50, clientY: 60 });
    app.pointer('pointermove', { buttons: 0, clientX: 80, clientY: 80 });
    await app.frame(32);
    assert.equal(app.input().lookX, 0);
    assert.equal(app.input().panX, 0);
    assert.equal(app.input().buttons, 0);
});

test('device pixel ratio scales pointer coordinates while preserving gesture sensitivity', async t => {
    const app = setup(t, 2);
    app.pointer('pointerdown');
    app.pointer('pointermove', { clientX: 35, clientY: 30 });
    await app.frame();
    assert.equal(app.input().pointerX, 50);
    assert.equal(app.input().pointerY, 20);
    assert.equal(app.input().lookX, 25);
    assert.equal(app.input().lookY, 10);
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
    assert.deepEqual(app.calls.find(call => call.method === 'OnGuiPointer').args, [8, 8, 'up', 0]);
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
