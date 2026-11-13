let project=null,assetUris={},enabled=false;
let speechGeneration=0,audioContext=null,audioSource=null,asrSession=null,finishingAsr=null;
let speaking=null,realtimeSession=null,identity=null;const conversation=[];
const versionUrl=name=>{const u=new URL(name,import.meta.url);u.search=new URL(import.meta.url).search;return u.href;};
const blobUrls=new Set();
export function clearAssetUrls(){for(const url of blobUrls)URL.revokeObjectURL(url);blobUrls.clear();}
export function createAssetUrl(bytes){const url=URL.createObjectURL(new Blob([bytes]));blobUrls.add(url);return url;}
export function isEnabled(){return enabled;}
export async function configure(value,uris={},assetIdentity=''){
    const nextIdentity=JSON.stringify([value?.name,value?.web,value?.voice,value?.asr,assetIdentity]);
    if(identity!==null && identity!==nextIdentity){await dispose();}
    identity=nextIdentity;
    project=value;assetUris=uris;enabled=(value?.web?.runtimeMode||'offline')==='offline';
}
async function localAsset(path){
    const uri=assetUris[path];if(!uri)throw new Error('Offline model asset is not registered: '+path);
    const response=await fetch(uri);if(!response.ok)throw new Error('Offline asset HTTP '+response.status+': '+path);
    return new Uint8Array(await response.arrayBuffer());
}
// Language requests use the same configured API as PC and Android.
async function* generateChat(request,signal){
    const settings=project.llm||{};
    const url=new URL((settings.chatCompletionsPath||'/v1/chat/completions').replace(/^\/+/,''),(settings.baseUrl||location.origin).replace(/\/?$/,'/'));
    const response=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json',...(settings.apiKey?{'Authorization':'Bearer '+settings.apiKey}:{})},
        body:JSON.stringify({...request,model:settings.model,stream:true}),signal});
    if(!response.ok)throw new Error('LLM HTTP '+response.status+': '+(await response.text()).slice(0,500));
    const reader=response.body.getReader(),decoder=new TextDecoder();let pending='';
    try{
        while(true){
            const {done,value}=await reader.read();pending+=decoder.decode(value,{stream:!done});
            if(done && pending && !pending.endsWith('\n'))pending+='\n';
            let end;
            while((end=pending.indexOf('\n'))>=0){
                const line=pending.slice(0,end).trim();pending=pending.slice(end+1);
                if(!line.startsWith('data:'))continue;
                const data=line.slice(5).trim();if(data==='[DONE]')return;if(data)yield JSON.parse(data);
            }
            if(done)return;
        }
    }finally{await reader.cancel().catch(()=>{});reader.releaseLock();}
}
class SpeechWorker{
    constructor(kind){this.kind=kind;this.worker=null;this.pending=new Map();this.nextId=0;this.ready=null;this.initialized=false;this.warmed=false;this.progressReference=null;}
    initialize(){
        if(this.ready)return this.ready;
        this.ready=new Promise((resolve,reject)=>{
            this.worker=new Worker(versionUrl('./web-offline-speech-worker.js'));
            this.worker.onerror=event=>{reject(new Error(event.message));for(const p of this.pending.values())p.reject(new Error(event.message));this.pending.clear();};
            this.worker.onmessage=async event=>{const m=event.data;
                if(m.type==='progress'){notify(this.progressReference,'OnModelLoadingProgress',this.kind,m.message);return;}
                if(m.type==='asset'){try{const bytes=await localAsset(m.path);this.worker.postMessage({type:'asset',id:m.id,bytes},[bytes.buffer]);}catch(error){this.worker.postMessage({type:'asset',id:m.id,error:error.message});}return;}
                if(m.type==='ready'){this.initialized=true;resolve(m);return;}
                if(m.type==='error'){if(m.id){const p=this.pending.get(m.id);this.pending.delete(m.id);p?.reject(new Error(m.message));}else reject(new Error(m.message));return;}
                if(m.type==='result'){const p=this.pending.get(m.id);this.pending.delete(m.id);p?.resolve(m);}
            };
            this.worker.postMessage({type:'init',kind:this.kind,voice:project.voice,asr:project.asr});
        }).catch(error=>{this.worker?.terminate();this.worker=null;this.ready=null;this.initialized=false;throw error;});return this.ready;
    }
    async request(message,transfer=[]){await this.initialize();const id=++this.nextId;return new Promise((resolve,reject)=>{this.pending.set(id,{resolve,reject});this.worker.postMessage({...message,id},transfer);});}
    reset(){if(this.initialized)this.worker?.postMessage({type:'reset'});}
    dispose(){this.worker?.terminate();this.worker=null;this.ready=null;this.initialized=false;this.warmed=false;this.progressReference=null;for(const p of this.pending.values())p.reject(new Error('Offline speech worker disposed.'));this.pending.clear();}
}
const tts=new SpeechWorker('tts'),asr=new SpeechWorker('asr');
export async function preload(reference){
    if(!enabled)return;
    for(const worker of [tts,asr]){
        if(!project[worker.kind==='tts'?'voice':'asr']?.enabled || worker.warmed)continue;
        worker.progressReference=reference;
        notify(reference,'OnModelLoadingProgress',worker.kind,'正在初始化'+(worker.kind==='tts'?'语音合成':'语音识别')+'模型');
        try{
            await worker.initialize();
            if(worker.kind==='tts')await worker.request({type:'tts',text:project.voice.warmUpText||'你好',speed:1,speakerId:project.voice.defaultSpeakerId||0});
            else{await worker.request({type:'asr',samples:new Float32Array(16000),sampleRate:16000});worker.reset();}
            worker.warmed=true;
        }finally{worker.progressReference=null;}
    }

}
async function context(){const ctx=audioContext??=new (globalThis.AudioContext||globalThis.webkitAudioContext)();await ctx.resume();return ctx;}
export async function unlockAudio(){if(enabled)await context();}
const notify=(reference,name,...args)=>reference?.invokeMethodAsync(name,...args).catch(()=>{});
export function stopSpeaking(){speechGeneration++;audioSource?.stop();audioSource=null;if(speaking){notify(speaking.reference,'OnSpeechFinished',speaking.entityId);speaking=null;}}
export function speak(text,rate,volume,reference,entityId,callbackName,speakerId=null){
    stopSpeaking();const generation=speechGeneration;
    speaking={reference,entityId};
    tts.request({type:'tts',text,speed:rate||1,speakerId:speakerId??project.voice.defaultSpeakerId??0}).then(async result=>{
        if(generation!==speechGeneration)return;
        const ctx=await context();if(generation!==speechGeneration)return;
        const buffer=ctx.createBuffer(1,result.samples.length,result.sampleRate);buffer.copyToChannel(result.samples,0);
        const source=audioSource=ctx.createBufferSource();source.buffer=buffer;const gain=ctx.createGain();gain.gain.value=volume??1;source.connect(gain);gain.connect(ctx.destination);
        notify(reference,'OnSpeechStarted',entityId,text,rate||1);source.onended=()=>{gain.disconnect();if(generation!==speechGeneration)return;audioSource=null;speaking=null;notify(reference,'OnSpeechFinished',entityId);notify(reference,'OnSpeechCompleted',entityId,callbackName);};source.start();
    }).catch(error=>{if(generation===speechGeneration){speaking=null;notify(reference,'OnSpeechFinished',entityId);notify(reference,'OnSpeechFailed',entityId,callbackName,error.message);}});
}
export function stopAsr(cancel=false){
    if(cancel && finishingAsr){finishingAsr.cancelled=true;finishingAsr=null;}
    const session=asrSession;if(!session)return;asrSession=null;session.cancelled=cancel;session.stopping=true;session.stopCapture?.();
    if(!cancel){finishingAsr=session;session.tail=session.tail.then(async()=>{if(session.cancelled)return;const result=await asr.request({type:'asr',samples:new Float32Array(8000),sampleRate:16000,finished:true});emitAsr(session,'completed',session.command.completedCallback,result.text);})
        .catch(error=>emitAsr(session,'error',session.command.errorCallback,'',error.message))
        .finally(()=>{if(finishingAsr===session)finishingAsr=null;});}
    else asr.reset();
}
function emitAsr(session,eventName,callbackName,text='',error='',wakeWord=''){
    if(session.cancelled || !callbackName)return;
    notify(session.reference,'OnAsrEvent',session.command.entityId,{requestId:session.command.requestId,eventName,text,isFinal:eventName==='completed',error,callbackName,wakeWord,recognizedText:text,offsetSeconds:0});
}
function resample(samples,inputRate,outputRate){const ratio=inputRate/outputRate,result=new Float32Array(Math.floor(samples.length/ratio));for(let i=0;i<result.length;i++){const p=i*ratio,a=Math.floor(p),f=p-a;result[i]=samples[a]*(1-f)+(samples[Math.min(a+1,samples.length-1)]||0)*f;}return result;}
export function executeAsr(command,reference){
    if(command.operation==='preload')return asr.initialize();
    if(command.operation==='stop'){if(asrSession?.command.requestId===command.requestId)stopAsr();return;}
    if(command.operation==='stop_wake'){if(asrSession?.command.operation==='wake')stopAsr(true);return;}
    stopAsr(true);const session=asrSession={command,reference,cancelled:false,tail:Promise.resolve(),text:'',segmentSeconds:0};
    const wakeDuration=command.chunkDurationSeconds??(command.extensionDurationSeconds!=null?2:null);
    session.wakeDeadline=wakeDuration==null?null:Math.max(.1,wakeDuration);
    session.tail=asr.initialize().then(()=>{if(!session.cancelled)asr.reset();});
    (async()=>{
        await session.tail;if(session.cancelled || session.stopping)return;
        const stream=await navigator.mediaDevices.getUserMedia({audio:true});if(session.cancelled || session.stopping){stream.getTracks().forEach(t=>t.stop());return;}
        // Own the tracks before resume(), which can wait for a user gesture.
        session.stopCapture=()=>stream.getTracks().forEach(t=>t.stop());
        const ctx=await context();if(session.cancelled || session.stopping || asrSession!==session)return;
        const source=ctx.createMediaStreamSource(stream),processor=ctx.createScriptProcessor(4096,1,1),mute=ctx.createGain();mute.gain.value=0;
        source.connect(processor);processor.connect(mute);mute.connect(ctx.destination);
        session.stopCapture=()=>{processor.disconnect();source.disconnect();mute.disconnect();stream.getTracks().forEach(t=>t.stop());};
        let queued=0;
        processor.onaudioprocess=event=>{
            if(session.cancelled || session.stopping || asrSession!==session || queued>=8)return;
            const samples=resample(event.inputBuffer.getChannelData(0),ctx.sampleRate,16000);queued++;
            const seconds=samples.length/16000,speech=samples.some(v=>Math.abs(v)>Math.max(.0001,project.asr?.speechRmsThreshold??.008));
            session.tail=session.tail.then(async()=>{
                if(session.cancelled)return;let result=await asr.request({type:'asr',samples,sampleRate:16000},[samples.buffer]);
                if(session.cancelled || session.stopping || asrSession!==session)return;
                session.text=result.text;
                if(command.operation==='wake'){
                    session.segmentSeconds+=seconds;
                    if(speech&&session.wakeDeadline!=null)session.wakeDeadline=Math.min(30,Math.max(session.wakeDeadline,session.segmentSeconds+Math.max(0,command.extensionDurationSeconds??1.2)));
                    const timed=session.wakeDeadline!=null&&session.segmentSeconds+1e-6>=session.wakeDeadline;
                    if(timed&&!session.detected){result=await asr.request({type:'asr',samples:new Float32Array(Math.round(16000*Math.max(0,Math.min(2,command.trailingSilencePaddingSeconds??.4)))),sampleRate:16000,finished:true});if(session.cancelled||session.stopping||asrSession!==session)return;}
                    const word=(command.wakeWords||[]).find(w=>result.text.includes(w));if(word && !session.detected){session.detected=true;emitAsr(session,'wake_detected',command.completedCallback,result.text,'',word);}
                    if(result.endpoint||timed){asr.reset();session.detected=false;session.segmentSeconds=0;session.wakeDeadline=wakeDuration==null?null:Math.max(.1,wakeDuration);}
                }else{emitAsr(session,'partial',command.partialCallback,result.text);if(command.finishOnEndpoint && result.endpoint)stopAsr(false);}
            }).catch(error=>{if(asrSession===session){emitAsr(session,'error',command.errorCallback,'',error.message);stopAsr(true);}}).finally(()=>queued--);
        };
    })().catch(error=>{if(asrSession===session){emitAsr(session,'error',command.errorCallback,'',error.message);stopAsr(true);}});
}
function emitRealtime(state,eventName,callbackName,text='',delta='',error='',wakeWord=''){
    if(state.cancelled || !callbackName)return;
    notify(state.reference,'OnRealtimeVoiceEvent',state.command.entityId,{requestId:state.command.requestId,eventName,text,delta,accumulatedText:text,
        isFinal:['completed','transcription_completed','speech_completed'].includes(eventName),error,callbackName,wakeWord,recognizedText:text});
}
export function executeRealtime(command,reference){
    if(['reset','cancel_all'].includes(command.operation)){cancelRealtime();if(command.operation==='reset')conversation.length=0;return;}
    if(command.operation==='cancel'){if(realtimeSession?.command.requestId===command.requestId)cancelRealtime();return;}
    if(command.operation==='stop_wake'){if(asrSession?.command.operation==='wake')stopAsr(true);return;}
    cancelRealtime();
    const state=realtimeSession={command,reference,cancelled:false,text:'',abort:new AbortController()};
    const voiceReference={invokeMethodAsync:async(name,entityId,...args)=>{
        if(state.cancelled)return;
        if(name==='OnSpeechCompleted')emitRealtime(state,command.operation==='speak'?'speech_completed':'completed',command.completedCallback,state.text||command.text);
        else if(name==='OnSpeechFailed')emitRealtime(state,'error',command.errorCallback,'','',args[1]);
        else await reference.invokeMethodAsync(name,entityId,...args);
    }};
    async function response(text){
        const stream=generateChat({messages:[{role:'system',content:project.realtimeVoice.instructions||'你是中文助手。'},...conversation,{role:'user',content:text}],max_tokens:project.realtimeVoice.maxOutputTokens||512},state.abort.signal);
        for await(const chunk of stream){if(state.cancelled)break;const delta=chunk.choices?.[0]?.delta?.content||'';state.text+=delta;if(delta)emitRealtime(state,'delta',command.deltaCallback,state.text,delta);}
        if(state.cancelled)return;conversation.push({role:'user',content:text},{role:'assistant',content:state.text});
        speak(state.text,1,project.realtimeVoice.outputVolume??1,voiceReference,command.entityId,command.completedCallback);
    }
    if(command.operation==='response'){response(command.text).catch(error=>emitRealtime(state,'error',command.errorCallback,'','',error.message));return;}
    if(command.operation==='speak'){speak(command.text,command.speed,project.realtimeVoice.outputVolume??1,voiceReference,command.entityId,command.completedCallback);return;}
    const asrCommand={operation:command.operation==='wake'?'wake':'start',requestId:command.requestId,entityId:command.entityId,
        wakeWords:project.realtimeVoice.wakeWord?.keywords||[],finishOnEndpoint:true,partialCallback:'partial',completedCallback:'complete',errorCallback:'error'};
    executeAsr(asrCommand,{invokeMethodAsync:async(_,entityId,event)=>{
        if(state.cancelled)return;
        if(event.eventName==='error')emitRealtime(state,'error',command.errorCallback,'','',event.error);
        else if(event.eventName==='wake_detected')emitRealtime(state,'wake_detected',command.completedCallback,event.text,'','',event.wakeWord);
        else if(event.isFinal){
            if(state.timer)clearTimeout(state.timer);
            if(!event.text.trim()){emitRealtime(state,'timeout',command.timeoutCallback);return;}
            emitRealtime(state,'transcription_completed',command.operation==='turn'?command.transcriptionCallback:command.completedCallback,event.text);
            if(command.operation==='turn')await response(event.text);
        }
    }});
    if(command.operation!=='wake')state.timer=setTimeout(()=>{if(!state.cancelled)stopAsr(false);},Math.max(1,command.timeoutSeconds||30)*1000);
}
function cancelRealtime(){if(realtimeSession){realtimeSession.cancelled=true;realtimeSession.abort?.abort();clearTimeout(realtimeSession.timer);}realtimeSession=null;stopAsr(true);stopSpeaking();}
export async function dispose(){cancelRealtime();tts.dispose();asr.dispose();conversation.length=0;if(audioContext){await audioContext.close();audioContext=null;}}
