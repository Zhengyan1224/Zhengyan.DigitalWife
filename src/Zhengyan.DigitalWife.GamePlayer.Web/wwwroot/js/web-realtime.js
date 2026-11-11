const asrUrl=new URL('./web-asr.js',import.meta.url);asrUrl.search=new URL(import.meta.url).search;
const asr=await import(asrUrl.href);
const offlineUrl=new URL('./web-offline.js',import.meta.url);offlineUrl.search=new URL(import.meta.url).search;
const offline=await import(offlineUrl.href);
let settings={},socket=null,connection=null,current=null,audioContext=null,nextAudioTime=0;
const sources=new Set();
export function configure(value){stopAll();settings=value||{};}
export function isAvailable(){return !!(globalThis.WebSocket && globalThis.fetch);}
function emit(state,eventName,callbackName,text='',delta='',error='',wakeWord=''){
    if(!state || state.cancelled || !callbackName)return;
    state.reference?.invokeMethodAsync('OnRealtimeVoiceEvent',state.command.entityId,{requestId:state.command.requestId,eventName,text,delta,
        accumulatedText:text,isFinal:['completed','transcription_completed','speech_completed'].includes(eventName),error,callbackName,wakeWord,recognizedText:text}).catch(()=>{});
}
function send(value){if(socket?.readyState!==1)throw new Error('Realtime WebSocket is not connected.');socket.send(JSON.stringify(value));}
function cancel(state){
    if(!state)return;state.cancelled=true;state.abort?.abort();state.capture?.stop();if(state.timer)clearTimeout(state.timer);
    for(const source of sources)source.stop();sources.clear();nextAudioTime=0;
}
export function stopAll(){if(offline.isEnabled())offline.executeRealtime({operation:'cancel_all'});cancel(current);current=null;asr.stopAll();socket?.close();socket=null;connection=null;}
async function ready(){
    if(connection)return connection;
    // Browser WebSockets cannot set arbitrary HTTP headers. A same-origin
    // authenticated relay may supply cookies, or explicitly configure protocols.
    if(settings.apiKey && !settings.webSocketProtocols?.length)throw new Error('Authenticated Realtime requires a browser-compatible relay or configured WebSocket protocols.');
    const url=new URL(settings.realtimePath||'/v1/realtime',settings.baseUrl||location.origin);url.protocol=url.protocol==='https:'?'wss:':'ws:';
    url.searchParams.set('model',settings.model||'zhengyan-realtime-voice');
    connection=new Promise((resolve,reject)=>{
        const ws=socket=new WebSocket(url.href,settings.webSocketProtocols||[]);
        const timer=setTimeout(()=>{reject(new Error('Realtime connection timed out.'));ws.close();},Math.max(1,settings.connectTimeoutSeconds||30)*1000);
        ws.onopen=()=>ws.send(JSON.stringify({type:'session.update',session:{model:settings.model,voice:settings.voice||'0',instructions:settings.instructions||'',
            modalities:['text','audio'],input_audio_format:'pcm16',output_audio_format:'pcm16',turn_detection:null,
            input_audio_transcription:{model:settings.inputTranscriptionModel||'whisper-1',language:settings.inputTranscriptionLanguage||'zh',prompt:settings.inputTranscriptionPrompt||''}}}));
        ws.onmessage=event=>{
            if(socket!==ws)return;
            let value;try{value=JSON.parse(event.data);}catch{return;}
            if(value.type==='session.updated'){clearTimeout(timer);resolve();return;}
            if(value.type==='error'){
                const error=value.error?.message||'Realtime protocol error.';clearTimeout(timer);reject(new Error(error));
                emit(current,'error',current?.command.errorCallback,'','',error);return;
            }
            handle(value);
        };
        ws.onerror=()=>{clearTimeout(timer);reject(new Error('Realtime WebSocket connection failed.'));};
        ws.onclose=()=>{clearTimeout(timer);if(socket===ws){connection=null;if(current && !current.cancelled)emit(current,'error',current.command.errorCallback,'','','Realtime connection closed.');}reject(new Error('Realtime connection closed.'));};
    });
    return connection;
}
function handle(event){
    const state=current;if(!state || state.cancelled)return;
    const c=state.command,type=event.type;
    if(type==='conversation.item.input_audio_transcription.completed'){
        const text=event.transcript||'';
        if(c.operation==='transcribe'){emit(state,'transcription_completed',c.completedCallback,text);if(state.timer)clearTimeout(state.timer);current=null;}
        else {emit(state,'transcription_completed',c.transcriptionCallback,text);send({type:'response.create',response:{modalities:['text','audio']}});}
    } else if(type==='conversation.item.input_audio_transcription.failed')emit(state,'error',c.errorCallback,'','',event.error?.message||'Realtime transcription failed.');
    else if(['response.text.delta','response.output_text.delta','response.audio_transcript.delta','response.output_audio_transcript.delta'].includes(type)){
        const audio=type.includes('audio'),key=audio?'transcript':'text';state[key]=(state[key]||'')+(event.delta||'');
        if(audio || !state.transcript)emit(state,'delta',c.deltaCallback,state.transcript||state.text,event.delta||'');
    } else if(type==='response.audio.delta' || type==='response.output_audio.delta'){
        state.audioTask=(state.audioTask||Promise.resolve()).then(()=>playPcm(event.delta,state));
        state.audioTask.catch(error=>emit(state,'error',c.errorCallback,'','',error.message));
    } else if(type==='response.done'){
        if(event.response?.status==='failed'){emit(state,'error',c.errorCallback,'','',event.response.status_details?.error?.message||'Realtime response failed.');return;}
        const text=state.transcript||state.text||'';
        Promise.resolve(state.audioTask).then(()=>{
            const delay=Math.max(0,(nextAudioTime-(audioContext?.currentTime||0))*1000);
            state.timer=setTimeout(()=>{emit(state,'completed',c.completedCallback,text);if(current===state)current=null;},delay);
        }).catch(error=>emit(state,'error',c.errorCallback,'','',error.message));
    }
}
async function context(){
    audioContext??=new (globalThis.AudioContext||globalThis.webkitAudioContext)();await audioContext.resume();return audioContext;
}
async function playPcm(base64,state){
    if(state.cancelled)return;
    const ctx=await context(),bytes=Uint8Array.from(atob(base64),c=>c.charCodeAt(0)),view=new DataView(bytes.buffer);
    const buffer=ctx.createBuffer(1,Math.floor(bytes.length/2),settings.outputAudioSampleRate||24000),samples=buffer.getChannelData(0);
    for(let i=0;i<samples.length;i++)samples[i]=view.getInt16(i*2,true)/32768;
    const source=ctx.createBufferSource();source.buffer=buffer;const gain=ctx.createGain();gain.gain.value=settings.outputVolume??1;source.connect(gain);gain.connect(ctx.destination);
    sources.add(source);source.onended=()=>{sources.delete(source);gain.disconnect();};
    const at=Math.max(ctx.currentTime+.02,nextAudioTime);source.start(at);nextAudioTime=at+buffer.duration;
}
export function encodePcm(samples,inputRate,outputRate){
    const ratio=inputRate/outputRate,count=Math.floor(samples.length/ratio),bytes=new Uint8Array(count*2),view=new DataView(bytes.buffer);
    for(let i=0;i<count;i++){const position=i*ratio,a=Math.floor(position),f=position-a;const value=samples[a]*(1-f)+(samples[Math.min(a+1,samples.length-1)]||0)*f;view.setInt16(i*2,Math.round(Math.max(-1,Math.min(1,value))*32767),true);}
    let binary='';for(let i=0;i<bytes.length;i++)binary+=String.fromCharCode(bytes[i]);return btoa(binary);
}
async function capture(state){
    const stream=await navigator.mediaDevices.getUserMedia({audio:true});
    if(state.cancelled){stream.getTracks().forEach(t=>t.stop());return;}
    const ctx=await context(),source=ctx.createMediaStreamSource(stream),processor=ctx.createScriptProcessor(4096,1,1),mute=ctx.createGain();mute.gain.value=0;
    source.connect(processor);processor.connect(mute);mute.connect(ctx.destination);
    let spoken=false,silence=0,stopped=false;
    const stop=()=>{if(stopped)return;stopped=true;processor.disconnect();source.disconnect();mute.disconnect();stream.getTracks().forEach(t=>t.stop());};
    state.capture={stop};
    const finish=()=>{if(stopped)return;stop();if(state.timer)clearTimeout(state.timer);if(state.cancelled)return;if(!spoken){emit(state,'timeout',state.command.timeoutCallback);if(current===state)current=null;return;}send({type:'input_audio_buffer.commit'});};
    processor.onaudioprocess=event=>{
        if(stopped || state.cancelled)return;
        const samples=event.inputBuffer.getChannelData(0),rms=Math.sqrt(samples.reduce((sum,s)=>sum+s*s,0)/samples.length);
        const speaking=rms>.008;spoken||=speaking;silence=speaking?0:silence+samples.length/ctx.sampleRate;
        send({type:'input_audio_buffer.append',audio:encodePcm(samples,ctx.sampleRate,settings.inputAudioSampleRate||24000)});
        if(spoken && silence>.9)finish();
    };
    state.timer=setTimeout(finish,Math.max(1,state.command.timeoutSeconds||30)*1000);
}
export function execute(command,reference){
    if(offline.isEnabled()){offline.executeRealtime(command,reference);return;}
    if(command.operation==='cancel'){if(current?.command.requestId===command.requestId){cancel(current);current=null;if(socket?.readyState===1)send({type:'response.cancel'});}return;}
    if(command.operation==='cancel_all' || command.operation==='reset'){stopAll();return;}
    if(command.operation==='stop_wake'){asr.execute({operation:'stop_wake'});return;}
    if(command.operation==='wake'){
        asr.execute({operation:'wake',requestId:command.requestId,entityId:command.entityId,wakeWords:settings.wakeWord?.keywords||[],completedCallback:command.completedCallback,errorCallback:command.errorCallback},
            {invokeMethodAsync:(_,entityId,event)=>{
                const raw=event.recognizedText||'',index=raw.indexOf(event.wakeWord||'');
                const text=event.wakeWord && index>=0?raw.slice(index+event.wakeWord.length).replace(/^[\s\p{P}]+/gu,''):'';
                return reference.invokeMethodAsync('OnRealtimeVoiceEvent',entityId,{requestId:event.requestId,eventName:event.eventName,text,delta:'',accumulatedText:'',
                    isFinal:false,error:event.error,callbackName:event.callbackName,wakeWord:event.wakeWord,recognizedText:raw});
            }});return;
    }
    cancel(current);
    const state=current={command,reference,cancelled:false,abort:new AbortController(),text:'',transcript:''};
    run().catch(error=>{if(!state.cancelled)emit(state,'error',command.errorCallback,'','',error.message);state.capture?.stop();});
    async function run(){
        if(command.operation==='speak'){
            const url=new URL(settings.audioSpeechPath||'/v1/audio/speech',settings.baseUrl||location.origin);
            const response=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json',...(settings.apiKey?{'Authorization':'Bearer '+settings.apiKey}:{})},
                body:JSON.stringify({model:settings.model,voice:settings.voice||'0',input:command.text,speed:command.speed||1,response_format:'wav'}),signal:state.abort.signal});
            if(!response.ok)throw new Error('Speech HTTP '+response.status);
            const ctx=await context(),buffer=await ctx.decodeAudioData(await response.arrayBuffer());if(state.cancelled)return;
            const source=ctx.createBufferSource();source.buffer=buffer;const gain=ctx.createGain();gain.gain.value=settings.outputVolume??1;source.connect(gain);gain.connect(ctx.destination);sources.add(source);
            source.onended=()=>{sources.delete(source);gain.disconnect();emit(state,'speech_completed',command.completedCallback,command.text);if(current===state)current=null;};source.start();return;
        }
        await ready();if(state.cancelled)return;
        if(command.operation==='response'){
            send({type:'conversation.item.create',item:{type:'message',role:'user',content:[{type:'input_text',text:command.text}]}});
            send({type:'response.create',response:{modalities:['text','audio']}});
        } else {send({type:'input_audio_buffer.clear'});await capture(state);}
    }
}
export async function dispose(){stopAll();if(audioContext){await audioContext.close();audioContext=null;}}
