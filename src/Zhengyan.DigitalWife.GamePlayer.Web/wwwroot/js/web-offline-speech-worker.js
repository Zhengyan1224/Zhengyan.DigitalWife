let engine=null,stream=null,kind=null;
const pendingAssets=new Map();let nextAsset=0;
function asset(path){return new Promise((resolve,reject)=>{const id=++nextAsset;pendingAssets.set(id,{resolve,reject});postMessage({type:'asset',id,path});});}
async function putFile(path,name){
    if(!path)return;
    postMessage({type:'progress',message:'正在加载'+(kind==='tts'?'语音合成':'语音识别')+'模型：'+name});
    const bytes=await asset(path);try{Module.FS_unlink('/'+name);}catch{}
    Module.FS_createDataFile('/',name,bytes,true,false,true);
}
async function initialize(message){
    kind=message.kind;
    postMessage({type:'progress',message:'正在初始化'+(kind==='tts'?'语音合成':'语音识别')+'运行时'});
    const base=new URL('../vendor/sherpa/'+kind+'/',self.location.href);
    await new Promise((resolve,reject)=>{
        self.Module={noInitialRun:true,locateFile:path=>new URL(path,base).href,getPreloadedPackage:()=>new ArrayBuffer(0),
            onRuntimeInitialized:resolve,onAbort:reason=>reject(new Error(String(reason))),print:()=>{},printErr:text=>postMessage({type:'log',text})};
        importScripts(new URL('sherpa-onnx-wasm-main-'+kind+'.js',base).href);
    });
    importScripts(new URL('sherpa-onnx-'+kind+'.js',base).href);
    if(kind==='tts'){
        const c=message.voice;
        const modelKind=(c.modelKind||'vits').toLowerCase();
        if(!['matcha','vits'].includes(modelKind))throw new Error('This offline TTS build supports Matcha and VITS models; configured: '+c.modelKind);
        await putFile(c.modelPath,'model-steps-3.onnx');await putFile(c.vocoderPath,'vocoder.onnx');
        await putFile(c.tokensPath,'tokens.txt');await putFile(c.lexiconPath,'lexicon.txt');
        const matcha={acousticModel:modelKind==='matcha'?'./model-steps-3.onnx':'',vocoder:modelKind==='matcha'?'./vocoder.onnx':'',tokens:modelKind==='matcha'?'./tokens.txt':'',lexicon:c.lexiconPath?'./lexicon.txt':'',dataDir:'',noiseScale:1,lengthScale:1};
        engine=createOfflineTts(Module,{offlineTtsModelConfig:{offlineTtsVitsModelConfig:{model:modelKind==='vits'?'./model-steps-3.onnx':'',lexicon:c.lexiconPath?'./lexicon.txt':'',tokens:modelKind==='vits'?'./tokens.txt':'',dataDir:'',noiseScale:.667,noiseScaleW:.8,lengthScale:1},
            offlineTtsMatchaModelConfig:matcha,offlineTtsKokoroModelConfig:{model:'',voices:'',tokens:'',lexicon:'',dataDir:'',lengthScale:1},
            offlineTtsKittenModelConfig:{model:'',voices:'',tokens:'',lexicon:'',dataDir:'',lengthScale:1},offlineTtsZipVoiceModelConfig:{},
            offlineTtsPocketModelConfig:{},offlineTtsSupertonicModelConfig:{},numThreads:1,debug:0,provider:'cpu'},ruleFsts:'',ruleFars:'',maxNumSentences:1});
    }else{
        const c=message.asr.sherpa;
        await putFile(c.encoderPath,'encoder.onnx');await putFile(c.decoderPath,'decoder.onnx');await putFile(c.joinerPath,'joiner.onnx');await putFile(c.tokensPath,'tokens.txt');
        await putFile(c.modelPath,'model.onnx');
        const type=(c.modelKind||'OnlineTransducer').toLowerCase();
        if(!['onlinetransducer','onlineparaformer','onlinezipformer2ctc','onlinemodel'].includes(type))throw new Error('Unsupported streaming ASR model: '+c.modelKind);
        engine=createOnlineRecognizer(Module,{featConfig:{sampleRate:c.sampleRate||16000,featureDim:c.featureDim||80},
            modelConfig:{transducer:type==='onlinetransducer'?{encoder:'./encoder.onnx',decoder:'./decoder.onnx',joiner:'./joiner.onnx'}:{},
                paraformer:type==='onlineparaformer'?{encoder:'./encoder.onnx',decoder:'./decoder.onnx'}:{},zipformer2Ctc:type==='onlinezipformer2ctc'?{model:'./model.onnx'}:{},
                nemoCtc:{},toneCtc:{},tokens:'./tokens.txt',numThreads:1,provider:'cpu',debug:0,modelType:'',modelingUnit:'cjkchar',bpeVocab:''},
            decodingMethod:c.decodingMethod||'greedy_search',maxActivePaths:4,enableEndpoint:1,
            rule1MinTrailingSilence:2.4,rule2MinTrailingSilence:message.asr.endpointSilenceSeconds||.9,rule3MinUtteranceLength:20,
            hotwordsFile:'',hotwordsScore:1.5,ctcFstDecoderConfig:{graph:'',maxActive:3000},ruleFsts:'',ruleFars:''});
        stream=engine.createStream();
    }
    if(!engine?.handle)throw new Error('Offline '+kind+' model initialization failed.');
    postMessage({type:'ready',kind,sampleRate:engine.sampleRate});
}
self.onmessage=async event=>{
    const m=event.data;
    if(m.type==='asset'){const p=pendingAssets.get(m.id);if(p){pendingAssets.delete(m.id);m.error?p.reject(new Error(m.error)):p.resolve(m.bytes);}return;}
    try{
        if(m.type==='init'){await initialize(m);return;}
        if(m.type==='tts'){
            const result=engine.generate({text:m.text,sid:m.speakerId||0,speed:m.speed||1});
            postMessage({type:'result',id:m.id,samples:result.samples,sampleRate:engine.sampleRate},[result.samples.buffer]);
        }else if(m.type==='asr'){
            stream.acceptWaveform(m.sampleRate,m.samples);if(m.finished)stream.inputFinished();
            while(engine.isReady(stream))engine.decode(stream);
            const result=engine.getResult(stream);postMessage({type:'result',id:m.id,text:result.text||'',endpoint:engine.isEndpoint(stream)});
        }else if(m.type==='reset'){stream?.free();stream=engine.createStream();}
    }catch(error){postMessage({type:'error',id:m.id,message:error.stack||error.message||String(error)});}
};
