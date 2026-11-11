using System.Text;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed class WebSpeechLipSync
{
    private readonly Dictionary<string,string> _kana=new(StringComparer.OrdinalIgnoreCase),_vowels=new();
    private readonly Dictionary<string,float?> _previous=new();
    private WebPmxAsset? _asset;private GameProjectLipSyncSettings _settings=new();
    private string _text="",_current="";private IReadOnlyList<string> _frames=[];private float _elapsed,_period;private int _maxKey=1;
    public void Configure(GameProjectLipSyncSettings settings,string kana,string vowels)
    {
        Stop();_settings=settings;_kana.Clear();_vowels.Clear();
        void Parse(string data,Dictionary<string,string> target){foreach(var line in data.Split('\n')){if(line.TrimStart().StartsWith('#'))continue;var pair=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(pair.Length>=2)target[pair[0]]=pair[1];}}
        Parse(kana,_kana);Parse(vowels,_vowels);_maxKey=_kana.Keys.Select(k=>k.Length).DefaultIfEmpty(1).Max();
    }
    public void Begin(WebPmxAsset asset,string text,float speed)
    {
        Stop();if(!_settings.Enabled)return;_asset=asset;_text=text;_frames=Vowels(text);_elapsed=0;
        _period=Math.Clamp(.14f/Math.Max(.1f,speed),_settings.MinFramePeriodMilliseconds/1000,_settings.MaxFramePeriodMilliseconds/1000);
        foreach(string name in _settings.VowelMorphMap.Values.Distinct())_previous[name]=asset.Pose.TryGetManualMorphWeight(name,out var value)?value:null;
    }
    public IReadOnlyList<string> Vowels(string text)
    {
        StringBuilder kana=new();
        for(int i=0;i<text.Length;){string? match=null;int count=0;for(int n=Math.Min(_maxKey,text.Length-i);n>0;n--)if(_kana.TryGetValue(text.Substring(i,n),out match)){count=n;break;}
            if(count>0){kana.Append(match);i+=count;}else{kana.Append(text[i]);i++;}}
        return kana.ToString().Select(c=>_vowels.GetValueOrDefault(c.ToString(),_settings.UseFallbackVowelOnNoMatch?_settings.NoMatchFallbackVowel:""))
            .Where(v=>_settings.VowelMorphMap.ContainsKey(v)).ToArray();
    }
    public void Boundary(int index,int length){if(_asset is null)return;int start=Math.Clamp(index,0,_text.Length);_frames=Vowels(_text.Substring(start,Math.Min(Math.Max(1,length),_text.Length-start)));_elapsed=0;}
    public void Update(float delta)
    {
        if(_asset is null || _frames.Count==0)return;_elapsed+=delta;int frame=Math.Min((int)(_elapsed/Math.Max(.01f,_period)),_frames.Count-1);
        string name=_settings.VowelMorphMap[_frames[frame]];if(name==_current)return;
        foreach(string morph in _settings.VowelMorphMap.Values.Distinct())_asset.SetMorphWeight(morph,morph==name ? .7f : 0);
        _current=name;
    }
    public void Stop()
    {
        if(_asset is not null)foreach(var old in _previous){if(old.Value is float value)_asset.SetMorphWeight(old.Key,value);else _asset.ClearMorphWeight(old.Key);}
        _asset=null;_previous.Clear();_current="";_frames=[];
    }
}
