using System.Numerics;
using System.Reflection;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class WaterRippleRegression
{
    public static void ContactParity()
    {
        Vector3 waterPosition=new(0,1,0);
        foreach(Vector3 position in new[]{new Vector3(0,-30,0),new(0,1,0),new(9,1,0),new(11,1,0),new(0,4,0),new(6,1,7)})
        {
            ParticleCollisionSample sample=new(1,position,.75f);
            Vector3 delta=position-waterPosition;
            bool android=MathF.Abs(delta.Y)<=.891f+.75f && delta.X*delta.X+delta.Z*delta.Z<=20*20*.25f;
            Check(ParticleWaterRipplePolicy.IsRippleContact(sample,waterPosition,20,.891f)==android,"Desktop contact differs from Android's surface window.");
        }
        Check(!ParticleWaterRipplePolicy.IsRippleContact(new(1,new(0,-30,0),.75f),waterPosition,20,.891f),"An underwater spawn generated a false ripple.");
        Check(!ParticleWaterRipplePolicy.IsSurfaceContact(new(1,new(0,-30,0),.75f),1,.891f),"An underwater spawn was killed and immediately respawned.");
        Check(ParticleWaterRipplePolicy.IsSurfaceContact(new(1,new(0,.9f,0),.75f),1,.891f),"A falling particle no longer contacts the water surface.");
        string key=ParticleWaterRipplePolicy.BuildKey("water","rain",5);
        Check(key==ParticleWaterRipplePolicy.BuildKey("water","rain",5),"Particle throttling changed as the particle moved.");
        Check(key!=ParticleWaterRipplePolicy.BuildKey("water","other-rain",5),"Two emitters share the same throttle key.");
    }

    public static void PreserveCharacterRipple()
    {
        using WaterSurfaceComponent water=new(new OrbitCamera()){RippleLifetimeSeconds=10,GerstnerWavesEnabled=false};
        water.AddRipple(Vector3.Zero,1,1,0);
        Advance(water,2);
        water.AddParticleRipple(new(.1f,0,0),1,1,.6f);
        var character=Ripples(water).Single(r=>r.Active&&!r.Particle);
        Check(character.Age==2 && character.Center==Vector3.Zero,"Rain merged into and restarted a character wave.");
        for(int i=0;i<200;i++)water.AddParticleRipple(new(i+2,0,0),1,.2f,0);
        character=Ripples(water).Single(r=>r.Active&&!r.Particle);
        Check(character.Age==2 && character.Center==Vector3.Zero,"Rain overflow evicted a live character wave.");
        Check(Ripples(water).Count==48,"The shader ripple budget was expanded.");
        Advance(water,9);
        Check(!Ripples(water).Any(r=>r.Active&&!r.Particle),"Protected ripples no longer expire normally.");
    }

    public static void DemoRainSimulation()
    {
        string repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        string projectPath=Path.Combine(Directory.GetParent(repo)!.FullName,"GameEditorProjects","DemoGame01");
        if(!File.Exists(Path.Combine(projectPath,"game.project.json"))){Console.WriteLine("SKIP DemoGame01 rain fixture: project unavailable.");return;}
        GameProject project=GameProjectStore.Load(projectPath);
        GameProjectScene scene=GameProjectStore.LoadScene(projectPath,project.DefaultScene);
        GameEntity waterEntity=scene.Entities.First(e=>e.Type=="water_surface"&&e.Water.EnableInteraction);
        GameEntity rain=scene.Entities.First(e=>e.Type=="particle_system"&&e.Particle.EnableWaterInteraction);
        var p=rain.Particle;var w=waterEntity.Water;
        ParticleSystemComponent CreateParticles()
        {
        ParticleSystemComponent particles=new(new OrbitCamera(),new ParticleSystemSettings{
            ParticleCount=p.ParticleCount,SpawnBoxHalfExtents=p.SpawnBoxHalfExtents.ToVector3(),BaseVelocity=p.BaseVelocity.ToVector3(),
            VelocityJitter=p.VelocityJitter.ToVector3(),Acceleration=p.Acceleration.ToVector3(),MinLifetime=p.MinLifetime,MaxLifetime=p.MaxLifetime,
            MinSize=p.MinSize,MaxSize=p.MaxSize,StartSizeScale=p.StartSizeScale,EndSizeScale=p.EndSizeScale,WidthScale=p.WidthScale,HeightScale=p.HeightScale
        }){Position=rain.Transform.Position.ToVector3(),SimulationSpeed=p.SimulationSpeed};
        typeof(ParticleSystemComponent).GetField("_random",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(particles,new Random(12345));
        particles.ResetParticles(true);
        return particles;
        }
        using ParticleSystemComponent previousParticles=CreateParticles();
        using ParticleSystemComponent particles=CreateParticles();
        using ParticleSystemComponent androidParticles=CreateParticles();
        using WaterSurfaceComponent previous=new(new OrbitCamera(),w.Size){Position=waterEntity.Transform.Position.ToVector3(),RippleLifetimeSeconds=w.RippleLifetimeSeconds,GerstnerWavesEnabled=false};
        using WaterSurfaceComponent current=new(new OrbitCamera(),w.Size){Position=previous.Position,RippleLifetimeSeconds=w.RippleLifetimeSeconds,GerstnerWavesEnabled=false};
        Vector3 characterPosition=previous.Position;
        previous.AddRipple(characterPosition,w.InteractionRadius,w.InteractionStrength);
        current.AddRipple(characterPosition,w.InteractionRadius,w.InteractionStrength);
        Dictionary<string,double> oldTimes=[],newTimes=[],androidTimes=[];
        int oldEvents=0,newEvents=0,androidEvents=0,underwaterFalseEvents=0;
        for(int frame=0;frame<300;frame++)
        {
            double now=frame/60.0;
            foreach(var sample in previousParticles.GetCollisionSamples())
            {
                float vertical=sample.Position.Y-current.Position.Y;
                Vector3 point=new(sample.Position.X,current.Position.Y,sample.Position.Z);
                if(vertical<=sample.Radius)
                {
                    string key=$"{(int)MathF.Floor(sample.Position.X/w.ParticleRippleMergeDistance)}:{(int)MathF.Floor(sample.Position.Z/w.ParticleRippleMergeDistance)}";
                    if(!oldTimes.TryGetValue(key,out double time)||now-time>=w.ParticleRippleMinIntervalSeconds)
                    {
                        oldTimes[key]=now;oldEvents++;
                        if(MathF.Abs(vertical)>w.InteractionRadius+sample.Radius)underwaterFalseEvents++;
                        previous.AddRipple(point,w.InteractionRadius,w.InteractionStrength,w.ParticleRippleMergeDistance);
                    }
                }
                if(p.KillOnWaterContact && vertical<=sample.Radius)previousParticles.KillParticle(sample.Index);
            }
            foreach(var sample in particles.GetCollisionSamples())
            {
                Vector3 point=new(sample.Position.X,current.Position.Y,sample.Position.Z);
                if(ParticleWaterRipplePolicy.IsRippleContact(sample,current.Position,w.Size,w.InteractionRadius))
                {
                    string key=ParticleWaterRipplePolicy.BuildKey(waterEntity.Id,rain.Id,sample.Index);
                    if(!newTimes.TryGetValue(key,out double time)||now-time>=w.ParticleRippleMinIntervalSeconds)
                    {
                        newTimes[key]=now;newEvents++;
                        current.AddParticleRipple(point,w.InteractionRadius,w.InteractionStrength,w.ParticleRippleMergeDistance);
                    }
                }
                if(p.KillOnWaterContact && ParticleWaterRipplePolicy.IsSurfaceContact(sample,current.Position.Y,w.InteractionRadius))particles.KillParticle(sample.Index);
            }
            foreach(var sample in androidParticles.GetCollisionSamples())
            {
                if(!ParticleWaterRipplePolicy.IsRippleContact(sample,current.Position,w.Size,w.InteractionRadius))continue;
                string key=ParticleWaterRipplePolicy.BuildKey(waterEntity.Id,rain.Id,sample.Index);
                if(!androidTimes.TryGetValue(key,out double time)||now-time>=w.ParticleRippleMinIntervalSeconds)
                {androidTimes[key]=now;androidEvents++;}
            }
            GameTime tick=new(TimeSpan.FromSeconds(now),TimeSpan.FromSeconds(1.0/60),frame);
            previousParticles.Update(tick);particles.Update(tick);androidParticles.Update(tick);previous.Update(tick);current.Update(tick);
        }
        var character=Ripples(current).Single(r=>r.Active&&!r.Particle);
        Check(newEvents>0 && oldEvents>newEvents*3 && underwaterFalseEvents>0,"The real rain fixture did not reproduce excessive desktop event generation.");
        Check(androidEvents>0 && newEvents<androidEvents*2,"Desktop rain event rate remains far above the Android reference.");
        Check(character.Center==characterPosition && MathF.Abs(character.Age-5)<.001f,"The scene's ten-second character wave was removed or restarted by rain.");
        Console.WriteLine($"PASS DemoGame01 rain: count={p.ParticleCount}, lifetime={w.RippleLifetimeSeconds}s, old events={oldEvents}, aligned events={newEvents}, Android reference events={androidEvents}, underwater false events={underwaterFalseEvents}, character age={character.Age:F2}s");
    }

    private static void Advance(WaterSurfaceComponent water,double seconds)=>water.Update(new(TimeSpan.FromSeconds(seconds),TimeSpan.FromSeconds(seconds),0));
    private static List<(bool Active,bool Particle,Vector3 Center,float Age)> Ripples(WaterSurfaceComponent water)
    {
        Array states=(Array)typeof(WaterSurfaceComponent).GetField("_ripples",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(water)!;
        var result=new List<(bool,bool,Vector3,float)>();
        foreach(object state in states)
        {
            Type type=state.GetType();
            result.Add(((bool)type.GetField("Active")!.GetValue(state)!,(bool)type.GetField("IsParticle")!.GetValue(state)!,
                (Vector3)type.GetField("Center")!.GetValue(state)!,(float)type.GetField("Age")!.GetValue(state)!));
        }
        return result;
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
