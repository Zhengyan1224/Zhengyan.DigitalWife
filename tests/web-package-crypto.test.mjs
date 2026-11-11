import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { decryptGamePackage } from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/js/web-package-crypto.js';

test('Web Crypto opens a real native multi-chunk AES-GCM package and rejects wrong passwords or corruption',async()=>{
    const bytes=new Uint8Array(await readFile(new URL('../temp/web-debug/encrypted-fixture.dwgame',import.meta.url)));
    const plain=await decryptGamePackage(bytes,'fixture-password'),view=new DataView(plain.buffer),length=view.getInt32(8,true);
    const header=JSON.parse(new TextDecoder().decode(plain.subarray(12,12+length)));
    assert.equal(header.crypto,undefined);assert.equal(header.payloadLength,plain.length-12-length);
    assert.deepEqual([...plain.subarray(12+length,16+length)],[80,75,3,4]);
    assert.ok(header.payloadLength>2097152,'The native fixture does not exercise more than one nonce.');
    await assert.rejects(decryptGamePackage(bytes,'wrong'),/password is incorrect/);
    const damaged=bytes.slice();damaged[damaged.length-1]^=1;
    await assert.rejects(decryptGamePackage(damaged,'fixture-password'),/damaged/);
    await assert.rejects(decryptGamePackage(bytes,''),/requires a password/);
});
