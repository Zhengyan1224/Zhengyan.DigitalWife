"""Remove bundled model preload metadata; models come from the game's assets."""
from pathlib import Path
import re
root=Path(__file__).resolve().parent.parent/'src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/vendor/sherpa'
for kind in ['tts','asr']:
    path=root/kind/f'sherpa-onnx-wasm-main-{kind}.js'
    if not path.exists():continue
    source=path.read_text(encoding='utf-8')
    source,count=re.subn(r'loadPackage\(\{files:\[.*?\],remote_package_size:\d+\}\)', 'loadPackage({files:[],remote_package_size:0})',source,flags=re.S)
    if count!=1:raise RuntimeError(f'{path}: expected one model preload metadata block, got {count}')
    path.write_text(source,encoding='utf-8')
    print(f'Prepared model-independent {kind} WASM loader.')
