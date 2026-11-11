let active = null;
const offlineUrl=new URL('./web-offline.js',import.meta.url);offlineUrl.search=new URL(import.meta.url).search;
const offline=await import(offlineUrl.href);
export function isAvailable() { return offline.isEnabled()?!!globalThis.navigator?.mediaDevices?.getUserMedia:typeof (globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition) === 'function'; }
const notify = (state, eventName, callbackName, text = '', error = '', wakeWord = '') => {
    if (!callbackName || state.cancelled) return;
    state.reference?.invokeMethodAsync('OnAsrEvent', state.command.entityId, {
        requestId:state.command.requestId,eventName,text,isFinal:eventName==='completed',error,callbackName,
        offsetSeconds:Math.max(0,(Date.now()-state.started)/1000),wakeWord,recognizedText:text
    }).catch(()=>{});
};
export function stopAll() {
    if(offline.isEnabled()){offline.stopAsr(true);return;}
    const state=active;active=null;
    if(state){state.cancelled=true;state.recognition?.abort();if(state.restart)clearTimeout(state.restart);}
}
export function execute(command, reference) {
    if(offline.isEnabled()){offline.executeAsr(command,reference);return;}
    if(command.operation==='stop' || command.operation==='stop_wake') {
        if(!active || (command.operation==='stop' && active.command.requestId!==command.requestId) ||
            (command.operation==='stop_wake' && active.command.operation!=='wake'))return;
        if(command.operation==='stop_wake')stopAll();else {
            const state=active;state.stopping=true;
            if(state.restart){clearTimeout(state.restart);notify(state,'completed',state.command.completedCallback,state.text);active=null;}
            else try{state.recognition.stop();}catch{notify(state,'completed',state.command.completedCallback,state.text);active=null;}
        }
        return;
    }
    stopAll();
    const state={command,reference,started:Date.now(),cancelled:false,stopping:false,finals:new Map(),text:'',committed:''};
    active=state;
    if(!isAvailable()) {notify(state,'error',command.errorCallback,'','This browser does not provide speech recognition.');active=null;return;}
    const Recognition=globalThis.SpeechRecognition||globalThis.webkitSpeechRecognition;
    const recognition=state.recognition=new Recognition();recognition.lang='zh-CN';recognition.continuous=true;recognition.interimResults=true;
    recognition.onresult=event=>{
        if(active!==state || state.cancelled)return;
        let text=state.committed;
        for(let i=0;i<event.results.length;i++) {
            const result=event.results[i];text+=result[0]?.transcript||'';
            if(result.isFinal)state.finals.set(i,result[0]?.transcript||'');
        }
        state.text=text;
        if(command.operation==='wake') {
            const normalized=text.replace(/[\s\p{P}]/gu,'').toLowerCase();
            const word=(command.wakeWords||[]).find(w=>normalized.includes(w.replace(/[\s\p{P}]/gu,'').toLowerCase()));
            if(word && Date.now()-(state.lastWake||0)>1500){state.lastWake=Date.now();notify(state,'wake_detected',command.completedCallback,text,'',word);}
        } else notify(state,'partial',command.partialCallback,text);
    };
    recognition.onerror=event=>{
        if(active!==state || state.cancelled || event.error==='aborted')return;
        if(event.error==='no-speech' && !state.stopping)return;
        notify(state,'error',command.errorCallback,'',event.error||'Speech recognition failed.');stopAll();
    };
    recognition.onend=()=>{
        if(active!==state || state.cancelled)return;
        if(state.stopping) {notify(state,'completed',command.completedCallback,state.text);active=null;return;}
        // Speech providers can end a continuous session after silence.
        // Keep the script's listening window until its explicit Stop command.
        state.committed=state.text;state.finals.clear();state.restart=setTimeout(()=>{
            state.restart=null;
            if(active!==state || state.cancelled)return;
            try{recognition.start();}catch(error){notify(state,'error',command.errorCallback,'',error.message);stopAll();}
        },150);
    };
    try{recognition.start();}catch(error){notify(state,'error',command.errorCallback,'',error.message);stopAll();}
}
