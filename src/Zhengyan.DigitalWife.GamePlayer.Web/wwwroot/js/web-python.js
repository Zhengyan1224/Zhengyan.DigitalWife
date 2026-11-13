let interpreter=null,loading=null,nextScript=0,execution=Promise.resolve();
const scripts=new Map();
async function runtime(){
    if(loading)return loading;
    loading=(async()=>{const url=new URL('../vendor/pyodide/',import.meta.url),{loadPyodide}=await import(new URL('pyodide.mjs',url).href);
        interpreter=await loadPyodide({indexURL:url.href,packageBaseUrl:url.href,stdout:()=>{},stderr:message=>console.warn(message)});
        return interpreter;
    })().catch(error=>{loading=null;throw error;});return loading;
}
export async function createScript(id,source,reference){
    const py=await runtime(),number=++nextScript,name='engine_rpc_'+number,root='/games/'+number;
    const rpc={call:(handle,operation,member,args,value)=>reference.invokeMethod('OnPythonCall',id,handle,operation,member,args,value),
        wait:handle=>reference.invokeMethodAsync('OnPythonAwait',id,handle)};
    py.registerJsModule(name,rpc);
    const scope=py.toPy({});scope.set('__name__','game_'+number);scope.set('__rpc_name',name);scope.set('__callbacks',py.toPy({}));
    const files=reference.invokeMethod('OnPythonFiles',id);py.FS.mkdirTree(root+'/save');writeFiles(py,root,files.project||{});writeFiles(py,root+'/save',files.save||{});
    scripts.set(id,{scope,reference,name,root,code:source});
    await py.runPythonAsync(bridgeSource,{globals:scope});
}
export async function executeScript(id,frame){
    return serial(async()=>{
    const py=await runtime(),script=scripts.get(id);if(!script)return;
    py.FS.chdir(script.root);script.baseline=script.reference.invokeMethod('OnPythonFiles',id).save||{};replaceFiles(py,script.root+'/save',script.baseline);
    script.scope.set('__project_root',script.root);
    py.runPython("sys.path.insert(0, __project_root) if __project_root not in sys.path else None",{globals:script.scope});
    for(const [name,value]of Object.entries(frame)){const key=name[0].toUpperCase()+name.slice(1);script.scope.set('__frame_'+key,py.toPy(value));
        py.runPython(`${key} = _decode(__frame_${key})`,{globals:script.scope});}
    try{await py.runPythonAsync(script.code,{globals:script.scope});}finally{saveFiles(py,id,script);}
    });
}
export async function callCallback(id,callback,value){return serial(async()=>{const script=scripts.get(id);if(script){interpreter.FS.chdir(script.root);script.baseline=script.reference.invokeMethod('OnPythonFiles',id).save||{};replaceFiles(interpreter,script.root+'/save',script.baseline);try{script.scope.set("__callback_value",interpreter.toPy(value??null));await interpreter.runPythonAsync(value===undefined?`__callbacks[${callback}]()`:`__callbacks[${callback}](_decode(__callback_value))`,{globals:script.scope});}finally{saveFiles(interpreter,id,script);}}});}
// A tool may execute while its caller awaits the LLM. Give that invocation
// its own globals and bypass the queue which contains the waiting caller.
export async function executeToolScript(id,frame){
    const py=await runtime(),script=scripts.get(id);if(!script)return null;
    const scope=py.runPython('dict(globals())',{globals:script.scope});
    try{
        for(const [name,value]of Object.entries(frame)){const key=name[0].toUpperCase()+name.slice(1);scope.set('__frame_'+key,py.toPy(value));py.runPython(`${key} = _decode(__frame_${key})`,{globals:scope});}
        scope.set('ToolResult',null);
        await py.runPythonAsync(script.code,{globals:scope});
        return py.runPython('ToolResult if isinstance(ToolResult, str) else (json.dumps(ToolResult, ensure_ascii=False) if ToolResult is not None else None)',{globals:scope});
    }finally{scope.destroy();}
}
export async function removeScript(id){return serial(()=>{const script=scripts.get(id);if(!script)return;script.scope.set('__project_root',script.root);interpreter.runPython("sys.path[:] = [p for p in sys.path if p != __project_root]\nfor key, module in list(sys.modules.items()):\n    if str(getattr(module, '__file__', '')).startswith(__project_root + '/'):\n        del sys.modules[key]",{globals:script.scope});script.scope.destroy();interpreter.unregisterJsModule(script.name);removeTree(interpreter,script.root);scripts.delete(id);});}
function serial(action){const result=execution.then(action);execution=result.catch(()=>{});return result;}
function writeFiles(py,root,files){for(const [path,text]of Object.entries(files)){if(path.startsWith('/') || path.split('/').some(p=>p==='..') || path.includes(':'))throw new Error('Invalid Python project path: '+path);const target=root+'/'+path;py.FS.mkdirTree(target.slice(0,target.lastIndexOf('/')));py.FS.writeFile(target,text);}}
function removeTree(py,path){for(const name of py.FS.readdir(path).filter(n=>n!=='.'&&n!=='..')){const file=path+'/'+name;if(py.FS.isDir(py.FS.stat(file).mode))removeTree(py,file);else py.FS.unlink(file);}py.FS.rmdir(path);}
function replaceFiles(py,root,files){removeTree(py,root);py.FS.mkdirTree(root);writeFiles(py,root,files);}
function saveFiles(py,id,script){const files={};function visit(path,prefix=''){for(const name of py.FS.readdir(path).filter(n=>n!=='.'&&n!=='..')){const file=path+'/'+name,key=prefix+name;if(py.FS.isDir(py.FS.stat(file).mode))visit(file,key+'/');else files[key]=py.FS.readFile(file,{encoding:'utf8'});}}visit(script.root+'/save');const baseline=script.baseline||{},written=Object.fromEntries(Object.entries(files).filter(([name,value])=>baseline[name]!==value)),deleted=Object.keys(baseline).filter(name=>!(name in files));script.reference.invokeMethod('OnPythonSave',id,JSON.stringify({written,deleted}));}

const bridgeSource=`
import json, importlib, math, sys, types
_rpc_module = importlib.import_module(__rpc_name)
_callback_counter = 0
class Vector:
    def __init__(self,*values): self.values=list(values)
    def __getattr__(self,name):
        if name in ('X','Y','Z','W'): return self.values['XYZW'.index(name)]
        raise AttributeError(name)
    def __setattr__(self,name,value):
        if name in ('X','Y','Z','W'): self.values['XYZW'.index(name)]=value
        else: object.__setattr__(self,name,value)
    def __add__(self,other): return type(self)(*(a+b for a,b in zip(self.values,other.values)))
    def __sub__(self,other): return type(self)(*(a-b for a,b in zip(self.values,other.values)))
    def __mul__(self,value): return type(self)(*(a*value for a in self.values))
    def __rmul__(self,value): return self*value
    def __truediv__(self,value): return self*(1/value)
    def Length(self): return math.sqrt(sum(a*a for a in self.values))
    @staticmethod
    def Normalize(value): return value*(1/max(value.Length(),1e-12))
class Vector2(Vector): pass
class Vector3(Vector): pass
class Vector4(Vector): pass
Vector3.Zero=Vector3(0,0,0);Vector3.One=Vector3(1,1,1)
Vector3.UnitX=Vector3(1,0,0);Vector3.UnitY=Vector3(0,1,0);Vector3.UnitZ=Vector3(0,0,1)
class RemoteTask:
    def __init__(self,handle): self.handle=handle
    def __await__(self):
        async def wait(): return _decode(await _rpc_module.wait(self.handle))
        return wait().__await__()
def _encode(value):
    global _callback_counter
    if isinstance(value,RemoteObject): return {'__handle':value.handle}
    if isinstance(value,Vector): return {'__vector':value.values}
    if callable(value):
        _callback_counter+=1;__callbacks[_callback_counter]=value;return {'__callback':_callback_counter}
    if isinstance(value,(list,tuple)): return [_encode(v) for v in value]
    if isinstance(value,dict): return {k:_encode(v) for k,v in value.items()}
    return value
def _decode(value):
    if hasattr(value,'to_py'): value=value.to_py()
    if isinstance(value,dict):
        if '__handle' in value: return RemoteObject(value['__handle'])
        if '__task' in value: return RemoteTask(value['__task'])
        if '__tuple' in value: return tuple(_decode(v) for v in value['__tuple'])
        if '__vector' in value:
            values=value['__vector'];return (Vector2 if len(values)==2 else Vector3 if len(values)==3 else Vector4)(*values)
        return {k:_decode(v) for k,v in value.items()}
    if isinstance(value,list): return [_decode(v) for v in value]
    return value
class RemoteMethod:
    def __init__(self,handle,name): self.handle,self.name=handle,name
    def __call__(self,*args,**kwargs): return _decode(_rpc_module.call(self.handle,'call',self.name,json.dumps(_encode(args)),json.dumps(_encode(kwargs))))
class RemoteObject:
    def __init__(self,handle): object.__setattr__(self,'handle',handle)
    def __getattr__(self,name):
        value=_rpc_module.call(self.handle,'get',name,'[]','null')
        if hasattr(value,'to_py'): value=value.to_py()
        if isinstance(value,dict) and '__method' in value: return RemoteMethod(self.handle,name)
        return _decode(value)
    def __setattr__(self,name,value): _rpc_module.call(self.handle,'set',name,'[]',json.dumps(_encode(value)))
    def __iter__(self): return iter(_decode(_rpc_module.call(self.handle,'iter','','[]','null')))
    def __getitem__(self,key): return _decode(_rpc_module.call(self.handle,'index','',json.dumps([key]),'null'))
system=types.ModuleType('System');numerics=types.ModuleType('System.Numerics')
numerics.Vector2=Vector2;numerics.Vector3=Vector3;numerics.Vector4=Vector4
engine_math=types.SimpleNamespace(Abs=abs,Min=min,Max=max,Clamp=lambda v,lo,hi:min(max(v,lo),hi),Sqrt=math.sqrt,Sin=math.sin,Cos=math.cos,Tan=math.tan,PI=math.pi)
system.Numerics=numerics;system.MathF=engine_math;system.Math=engine_math
sys.modules['System']=system;sys.modules['System.Numerics']=numerics
`;
