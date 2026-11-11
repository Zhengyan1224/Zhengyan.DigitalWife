let project=null,assetUris={},enabled=false,languageModel=null,modelPromise=null,modelFiles=null;
let originalFetch=null,speechGeneration=0,audioContext=null,audioSource=null,asrSession=null;
let speaking=null,realtimeSession=null,identity=null,modelQueue=Promise.resolve();const conversation=[];
const versionUrl=name=>{const u=new URL(name,import.meta.url);u.search=new URL(import.meta.url).search;return u.href;};
const blobUrls=new Set();
export function clearAssetUrls(){for(const url of blobUrls)URL.revokeObjectURL(url);blobUrls.clear();}
export function createAssetUrl(bytes){const url=URL.createObjectURL(new Blob([bytes]));blobUrls.add(url);return url;}
export function isEnabled(){return enabled;}
export async function configure(value,uris={},assetIdentity=''){
    const nextIdentity=JSON.stringify([value?.name,value?.web,value?.voice,value?.asr,assetIdentity]);
    if(identity!==null && identity!==nextIdentity){await dispose();modelFiles=null;}
    identity=nextIdentity;
    project=value;assetUris=uris;enabled=(value?.web?.runtimeMode||'offline')==='offline';
    if(enabled && !originalFetch){originalFetch=globalThis.fetch.bind(globalThis);globalThis.fetch=offlineFetch;}
    if(!enabled && originalFetch){globalThis.fetch=originalFetch;originalFetch=null;}
}
async function localAsset(path){
    const uri=assetUris[path];if(!uri)throw new Error('Offline model asset is not registered: '+path);
    const response=await (originalFetch||fetch)(uri);if(!response.ok)throw new Error('Offline asset HTTP '+response.status+': '+path);
    return new Uint8Array(await response.arrayBuffer());
}
export async function setLanguageModelFiles(files){
    cancelRealtime();await modelQueue;await languageModel?.exit();languageModel=null;modelFiles=Array.from(files);modelPromise=null;
    if(globalThis.indexedDB){const storage=await import(versionUrl('./web-storage.js'));await storage.putBlobFiles(modelSaveKey(),modelFiles);}
}
const modelSaveKey=()=>JSON.stringify([project?.name,project?.web?.languageModelPath]);
export async function getLanguageModel(){
    if(modelPromise)return modelPromise;
    modelPromise=(async()=>{
        const {Wllama}=await import(new URL('../vendor/wllama/esm/index.js',import.meta.url).href);
        languageModel??=new Wllama({default:new URL('../vendor/wllama/esm/wasm/wllama.wasm',import.meta.url).href},{suppressNativeLog:true});
        let files=modelFiles;
        if(!files?.length && globalThis.indexedDB){const storage=await import(versionUrl('./web-storage.js'));files=await storage.getBlobFiles(modelSaveKey());}
        if(!files?.length){const uri=assetUris[project.web.languageModelPath];if(!uri)throw new Error('Offline language model is not registered: '+project.web.languageModelPath);
            const response=await (originalFetch||fetch)(uri);if(!response.ok)throw new Error('Offline model HTTP '+response.status);files=[await response.blob()];}
        await languageModel.loadModel(files,{n_ctx:Math.max(512,project.web.languageModelContextSize||2048),
            n_threads:Math.max(1,project.web.languageModelThreads||2),n_gpu_layers:project.web.languageModelGpuLayers||0});
        return languageModel;
    })().catch(error=>{modelPromise=null;throw error;});return modelPromise;
}
async function* generateChat(request,signal){
    let release;const previous=modelQueue;modelQueue=new Promise(resolve=>release=resolve);
    let iterator;
    try{
        await previous;signal?.throwIfAborted();
        const llm=await getLanguageModel();signal?.throwIfAborted();
        iterator=await llm.createChatCompletion({...request,model:undefined,stream:true,max_tokens:request.max_tokens||512});
        for await(const chunk of iterator){signal?.throwIfAborted();yield chunk;}
    }finally{try{await iterator?.return?.();}finally{release();}}
}
async function offlineFetch(input,options={}){
    const url=new URL(typeof input==='string'?input:input.url||String(input),globalThis.location?.href||import.meta.url);
    const endpoint=new URL((project.llm?.chatCompletionsPath||'/v1/chat/completions').replace(/^\/+/,''),(project.llm?.baseUrl||location.origin).replace(/\/?$/,'/'));
    if(url.href===endpoint.href){
        let body=options.body;if(body==null && typeof input?.clone==='function')body=await input.clone().text();
        if(ArrayBuffer.isView(body))body=new TextDecoder().decode(body);if(body instanceof ArrayBuffer)body=new TextDecoder().decode(body);
        const request=JSON.parse(body),encoder=new TextEncoder(),abort=new AbortController(),signal=options.signal||input?.signal;
        signal?.throwIfAborted();const onAbort=()=>abort.abort();signal?.addEventListener('abort',onAbort,{once:true});
        let cancelled=false;
        return new Response(new ReadableStream({async start(controller){
            try{
                for await(const chunk of generateChat(request,abort.signal)){if(cancelled)return;controller.enqueue(encoder.encode('data: '+JSON.stringify(chunk)+'\n\n'));}
                controller.enqueue(encoder.encode('data: [DONE]\n\n'));controller.close();
            }catch(error){if(!cancelled)controller.error(error);}
            finally{signal?.removeEventListener('abort',onAbort);}
        },cancel(){cancelled=true;abort.abort();}}),{status:200,headers:{'Content-Type':'text/event-stream'}});
    }
    if(!['data:','blob:'].includes(url.protocol) && url.origin!==location.origin)throw new Error('Network services are unavailable in offline mode: '+url.origin);
    return originalFetch(input,options);
}
class SpeechWorker{
    constructor(kind){this.kind=kind;this.worker=null;this.pending=new Map();this.nextId=0;this.ready=null;this.initialized=false;}
    initialize(){
        if(this.ready)return this.ready;
        this.ready=new Promise((resolve,reject)=>{
            this.worker=new Worker(versionUrl('./web-offline-speech-worker.js'));
            this.worker.onerror=event=>{reject(new Error(event.message));for(const p of this.pending.values())p.reject(new Error(event.message));this.pending.clear();};
            this.worker.onmessage=async event=>{const m=event.data;
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
    dispose(){this.worker?.terminate();this.worker=null;this.ready=null;this.initialized=false;for(const p of this.pending.values())p.reject(new Error('Offline speech worker disposed.'));this.pending.clear();}
}
const tts=new SpeechWorker('tts'),asr=new SpeechWorker('asr');
async function context(){audioContext??=new (globalThis.AudioContext||globalThis.webkitAudioContext)();await audioContext.resume();return audioContext;}
const notify=(reference,name,...args)=>reference?.invokeMethodAsync(name,...args).catch(()=>{});
export function stopSpeaking(){speechGeneration++;audioSource?.stop();audioSource=null;if(speaking){notify(speaking.reference,'OnSpeechFinished',speaking.entityId);speaking=null;}}
export function speak(text,rate,volume,reference,entityId,callbackName){
    stopSpeaking();const generation=speechGeneration;
    speaking={reference,entityId};
    tts.request({type:'tts',text,speed:rate||1,speakerId:project.voice.defaultSpeakerId||0}).then(async result=>{
        if(generation!==speechGeneration)return;
        const ctx=await context(),buffer=ctx.createBuffer(1,result.samples.length,result.sampleRate);buffer.copyToChannel(result.samples,0);
        const source=audioSource=ctx.createBufferSource();source.buffer=buffer;const gain=ctx.createGain();gain.gain.value=volume??1;source.connect(gain);gain.connect(ctx.destination);
        notify(reference,'OnSpeechStarted',entityId,text,rate||1);source.onended=()=>{gain.disconnect();if(generation!==speechGeneration)return;audioSource=null;speaking=null;notify(reference,'OnSpeechFinished',entityId);notify(reference,'OnSpeechCompleted',entityId,callbackName);};source.start();
    }).catch(error=>{if(generation===speechGeneration){speaking=null;notify(reference,'OnSpeechFinished',entityId);notify(reference,'OnSpeechFailed',entityId,callbackName,error.message);}});
}
export function stopAsr(cancel=false){
    const session=asrSession;if(!session)return;asrSession=null;session.cancelled=cancel;session.stopping=true;session.stopCapture?.();
    if(!cancel)session.tail=session.tail.then(async()=>{const result=await asr.request({type:'asr',samples:new Float32Array(8000),sampleRate:16000,finished:true});emitAsr(session,'completed',session.command.completedCallback,result.text);})
        .catch(error=>emitAsr(session,'error',session.command.errorCallback,'',error.message));
    else asr.reset();
}
function emitAsr(session,eventName,callbackName,text='',error='',wakeWord=''){
    if(session.cancelled || !callbackName)return;
    notify(session.reference,'OnAsrEvent',session.command.entityId,{requestId:session.command.requestId,eventName,text,isFinal:eventName==='completed',error,callbackName,wakeWord,recognizedText:text,offsetSeconds:0});
}
function resample(samples,inputRate,outputRate){const ratio=inputRate/outputRate,result=new Float32Array(Math.floor(samples.length/ratio));for(let i=0;i<result.length;i++){const p=i*ratio,a=Math.floor(p),f=p-a;result[i]=samples[a]*(1-f)+(samples[Math.min(a+1,samples.length-1)]||0)*f;}return result;}
export function executeAsr(command,reference){
    if(command.operation==='stop'){if(asrSession?.command.requestId===command.requestId)stopAsr();return;}
    if(command.operation==='stop_wake'){if(asrSession?.command.operation==='wake')stopAsr(true);return;}
    stopAsr(true);const session=asrSession={command,reference,cancelled:false,tail:Promise.resolve(),text:''};
    session.tail=asr.initialize().then(()=>{if(!session.cancelled)asr.reset();});
    (async()=>{
        await session.tail;if(session.cancelled || session.stopping)return;
        const stream=await navigator.mediaDevices.getUserMedia({audio:true});if(session.cancelled || session.stopping){stream.getTracks().forEach(t=>t.stop());return;}
        const ctx=await context(),source=ctx.createMediaStreamSource(stream),processor=ctx.createScriptProcessor(4096,1,1),mute=ctx.createGain();mute.gain.value=0;
        source.connect(processor);processor.connect(mute);mute.connect(ctx.destination);
        session.stopCapture=()=>{processor.disconnect();source.disconnect();mute.disconnect();stream.getTracks().forEach(t=>t.stop());};
        let queued=0;
        processor.onaudioprocess=event=>{
            if(session.cancelled || asrSession!==session || queued>=8)return;
            const samples=resample(event.inputBuffer.getChannelData(0),ctx.sampleRate,16000);queued++;
            session.tail=session.tail.then(async()=>{
                if(session.cancelled)return;const result=await asr.request({type:'asr',samples,sampleRate:16000},[samples.buffer]);session.text=result.text;
                if(command.operation==='wake'){
                    const word=(command.wakeWords||[]).find(w=>result.text.includes(w));if(word && !session.detected){session.detected=true;emitAsr(session,'wake_detected',command.completedCallback,result.text,'',word);}
                    if(result.endpoint){asr.reset();session.detected=false;}
                }else{emitAsr(session,'partial',command.partialCallback,result.text);if(command.finishOnEndpoint && result.endpoint)stopAsr(false);}
            }).catch(error=>{emitAsr(session,'error',command.errorCallback,'',error.message);stopAsr(true);}).finally(()=>queued--);
        };
    })().catch(error=>{emitAsr(session,'error',command.errorCallback,'',error.message);stopAsr(true);});
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
export async function dispose(){cancelRealtime();tts.dispose();asr.dispose();await modelQueue;await languageModel?.exit();languageModel=null;modelPromise=null;conversation.length=0;if(audioContext){await audioContext.close();audioContext=null;}}
