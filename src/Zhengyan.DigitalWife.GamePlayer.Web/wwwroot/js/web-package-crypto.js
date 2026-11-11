// Native ZDWPKG1: PBKDF2-SHA256 / AES-256-GCM chunks, with a big-endian
// chunk index in the last eight bytes of the 12-byte nonce.
export async function decryptGamePackage(input,password){
    const bytes=input instanceof Uint8Array?input:new Uint8Array(input),view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
    const magic=[90,68,87,80,75,71,49,0];
    if(bytes.length<12 || magic.some((v,i)=>bytes[i]!==v))throw new Error('Invalid game package header.');
    const headerLength=view.getInt32(8,true);
    if(headerLength<=0 || headerLength>1048576 || 12+headerLength>bytes.length)throw new Error('Invalid game package header length.');
    const header=JSON.parse(new TextDecoder().decode(bytes.subarray(12,12+headerLength))),metadata=header.crypto;
    if(!metadata)return bytes;
    if(!password)throw new Error('An encrypted game package requires a password.');
    if(!globalThis.crypto?.subtle)throw new Error('Encrypted packages require Web Crypto (HTTPS or localhost).');
    if(header.version!==1 || header.payloadFormat!=='zip' || metadata.algorithm!=='AES-256-GCM' || metadata.kdf!=='PBKDF2-SHA256')throw new Error('Unsupported game package encryption.');
    const decode=value=>Uint8Array.from(atob(value),c=>c.charCodeAt(0)),salt=decode(metadata.salt),baseNonce=decode(metadata.baseNonce);
    if(baseNonce.length!==12 || salt.length!==16 || metadata.tagSize!==16 || metadata.chunkSize<=0 || metadata.chunkSize>1048576 || metadata.iterations<1000 || metadata.iterations>5000000)
        throw new Error('Invalid game package encryption parameters.');
    const secret=new TextEncoder().encode(password),material=await crypto.subtle.importKey('raw',secret,'PBKDF2',false,['deriveKey']);secret.fill(0);
    const key=await crypto.subtle.deriveKey({name:'PBKDF2',salt,iterations:metadata.iterations,hash:'SHA-256'},material,{name:'AES-GCM',length:256},false,['decrypt']);
    const end=12+headerLength+header.payloadLength;
    if(!Number.isSafeInteger(end) || end>bytes.length || header.payloadLength<0)throw new Error('Invalid encrypted payload length.');
    const parts=[];let offset=12+headerLength,total=0,index=0n;
    while(offset<end){
        if(end-offset<4)throw new Error('Truncated encrypted payload.');const length=view.getInt32(offset,true);offset+=4;
        if(length<=0 || length>metadata.chunkSize || offset+length+metadata.tagSize>end)throw new Error('Invalid encrypted chunk length.');
        const nonce=baseNonce.slice();new DataView(nonce.buffer).setBigUint64(4,index++,false);
        try{const plain=new Uint8Array(await crypto.subtle.decrypt({name:'AES-GCM',iv:nonce,tagLength:128},key,bytes.subarray(offset,offset+length+metadata.tagSize)));parts.push(plain);total+=plain.length;}
        catch{throw new Error('The package password is incorrect, or the encrypted package is damaged.');}
        offset+=length+metadata.tagSize;
    }
    if(total>2147483647)throw new Error('The decrypted package is too large for browser memory.');
    const payload=new Uint8Array(total);let written=0;for(const part of parts){payload.set(part,written);written+=part.length;part.fill(0);}
    const hash=[...new Uint8Array(await crypto.subtle.digest('SHA-256',payload))].map(b=>b.toString(16).padStart(2,'0')).join('');
    if(header.plainSha256 && hash!==header.plainSha256.toLowerCase())throw new Error('Game package payload checksum failed.');
    delete header.crypto;header.payloadLength=total;
    const encoded=new TextEncoder().encode(JSON.stringify(header)),result=new Uint8Array(12+encoded.length+total);
    result.set(magic);new DataView(result.buffer).setInt32(8,encoded.length,true);result.set(encoded,12);result.set(payload,12+encoded.length);payload.fill(0);return result;
}
