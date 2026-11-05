import assert from 'node:assert/strict';
import test from 'node:test';
import * as audio from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-audio.js';
import { deferred, settle } from './helpers/web-renderer-fixtures.mjs';

function setup(t) {
    const previousWindow = globalThis.window, previousAudio = globalThis.Audio;
    const elements = [];
    class FakeAudio {
        constructor(uri) { this.uri = uri; this.loads = 0; this.plays = 0; this.currentTime = 0; elements.push(this); }
        set src(uri) { this.loads++; this.uri = uri; this.pending?.reject(new DOMException('interrupted by a new load request','AbortError')); }
        get src() { return this.uri; }
        play() { this.plays++; this.pending = deferred(); return this.pending.promise; }
        pause() { this.pending?.reject(new DOMException('interrupted by pause','AbortError')); }
        addEventListener() {} remove() {}
    }
    globalThis.Audio = FakeAudio;
    globalThis.window = { AudioContext: class { state = 'running'; close() {} } };
    audio.dispose();
    t.after(() => { audio.dispose(); globalThis.window = previousWindow; globalThis.Audio = previousAudio; });
    return elements;
}

test('overlapping play requests reuse the same source and pending play promise', async t => {
    const elements = setup(t);
    audio.prepare('dance','dance.ogg');
    const first = audio.play('dance','dance.ogg');
    const second = audio.play('dance','dance.ogg',true,.4);
    await settle();
    assert.equal(elements.length,1); assert.equal(elements[0].loads,1); assert.equal(elements[0].plays,1);
    assert.equal(first,second); assert.equal(elements[0].loop,true); assert.equal(elements[0].volume,.4);
    elements[0].pending.resolve(); await first;
});

test('queueing playback returns before media buffering completes and reports genuine failures', async t => {
    const elements = setup(t), errors = [];
    audio.setErrorHandler(message => errors.push(message));
    assert.equal(audio.queuePlay('dance','dance.ogg'),undefined);
    await settle();
    assert.equal(elements[0].plays,1);
    elements[0].pending.reject(new DOMException('Unsupported format','NotSupportedError'));
    await settle();
    assert.deepEqual(errors,['Web audio command failed: Unsupported format']);
});

test('stop cancels an in-flight play without a warning and the source can start again', async t => {
    const elements = setup(t), errors = [];
    audio.setErrorHandler(message => errors.push(message));
    audio.queuePlay('dance','dance.ogg'); await settle();
    audio.stop('dance'); await settle();
    assert.equal(elements[0].currentTime,0); assert.deepEqual(errors,[]);
    audio.queuePlay('dance','dance.ogg'); await settle();
    assert.equal(elements[0].loads,1); assert.equal(elements[0].plays,2);
    elements[0].pending.resolve(); await settle();
});

test('changing a source supersedes the old request without reporting its abort', async t => {
    const elements = setup(t), errors = [];
    audio.setErrorHandler(message => errors.push(message));
    audio.queuePlay('dance','first.ogg'); await settle();
    audio.queuePlay('dance','second.ogg'); await settle();
    assert.equal(elements[0].loads,2); assert.deepEqual(errors,[]);
    elements[0].pending.resolve(); await settle();
});

test('replaying a buffered sound seeks to its start without loading the source again', async t => {
    const elements = setup(t);
    const first = audio.play('dance','dance.ogg'); await settle();
    elements[0].pending.resolve(); await first;
    elements[0].currentTime = 20;
    const replay = audio.play('dance','dance.ogg'); await settle();
    assert.equal(elements[0].loads,1); assert.equal(elements[0].currentTime,0);
    elements[0].pending.resolve(); await replay;
});
