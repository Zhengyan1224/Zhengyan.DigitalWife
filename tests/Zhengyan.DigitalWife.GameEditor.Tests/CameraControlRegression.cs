extern alias AndroidRuntime;
extern alias AndroidProjects;

using System.Numerics;
using System.Reflection;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Core = AndroidRuntime::Zhengyan.DigitalWife.GamePlayer.Runtime;
using Projects = AndroidProjects::Zhengyan.DigitalWife.GameProjects;

internal static class CameraControlRegression
{
    public static void Run()
    {
        // Compare against the original native camera implementation, not a second copy of the formula.
        foreach (var gesture in new[] { new CameraGesture(new(24, 12), default, 0),
            new CameraGesture(default, new(24, 12), 0), new CameraGesture(default, default, -.6f) })
        {
            OrbitCamera native = new() { Height=720, Width=1280, Fov=45 };
            native.SetLookAt(new(0,2,8), new(0,1.2f,0));
            var result=CameraControlPolicy.Update(new(){Fov=45,ViewportHeight=720,Smoothing=0}, native.Position,native.Target,
                native.Yaw,native.Pitch,gesture,default,0,_=>false,1f/60);
            native.Orbit(gesture.Look.X*.2f,-gesture.Look.Y*.2f);
            native.Pan(gesture.Pan.X,gesture.Pan.Y);native.Dolly(gesture.Zoom);
            Near(result.Position,native.Position,"Native mouse camera position");
            Near(result.Target,native.Target,"Native mouse camera target");
        }
        var delta=new Vector2(24,12);
        Check(CameraInputPolicy.Mouse(delta,true,false,false,false,true)==default,"Left drag must leave editor camera alone.");
        Check(CameraInputPolicy.Mouse(delta,false,true,false,false,true).Look==delta,"Right drag orbits.");
        Check(CameraInputPolicy.Mouse(delta,false,false,true,false,true).Pan==delta,"Middle drag pans.");
        Check(CameraInputPolicy.Mouse(delta,false,true,false,true,true).Zoom==-.6f,"Alt-right drag dollies.");
        Check(CameraInputPolicy.Mouse(delta,true,false,false,true,true).Look==delta,"Alt-left drag orbits.");
        Check(CameraInputPolicy.Mouse(delta,false,false,true,true,true).Look==delta,"Alt-middle drag orbits.");
        var fly = CameraControlPolicy.Update(new(){Mode="free_fly",Smoothing=0},new(0,0,8),Vector3.Zero,
            -90,0,default,new(default,new(4,2),-.5f),0,_=>false,1f/60);
        Check(fly.Position.X>0&&fly.Position.Y<0&&fly.Position.Z<8,"Free-flight touch must translate and pinch toward the view.");
        Check(CameraInputPolicy.Touch([new(1,new(20,20),delta,true)])==default,"New touch must rebase.");
        Check(CameraInputPolicy.Touch([new(1,new(20,20),delta)]).Look==delta,"Single finger rotates.");
        var pinch=CameraInputPolicy.Touch([new(1,new(0,0),default),new(2,new(120,0),new(20,0))]);
        Check(pinch.Look==default&&pinch.Pan==new Vector2(10,0)&&Math.Abs(pinch.Zoom+.2f)<1e-5,"Two fingers pan and pinch without orbiting.");

        string[] modes=["editor","fps","fps_control","tps","shoulder","free_fly","rts","top_down","isometric",
            "side_scroller","cinematic_follow","orbital_follow","lock_on","fixed","custom","vmd"];
        foreach(string mode in modes)
        foreach(int buttons in new[]{0,1,2,4})
        foreach(bool alt in new[]{false,true})
        {
            var definition=new Projects.GameProjectScene
            {
                Cameras=[new(){IsMain=true,Camera=new(){ControlMode=mode,TargetEntity="target",SubjectEntity="subject",Smoothing=0,Fov=45}}],
                Entities=[new(){Id="target",Transform=new(){Position=new(2,0,0)}},new(){Id="subject",Transform=new(){Position=new(-2,0,0)}}]
            };
            using var scene=new Core.RuntimeScene("main",definition,p=>p);
            var camera=scene.MainCamera;
            camera.ResolveViewport(1280,720,1280,720);
            var front=Vector3.Normalize(camera.Target-camera.Position);
            float yaw=MathF.Atan2(front.Z,front.X)*180/MathF.PI,pitch=MathF.Asin(front.Y)*180/MathF.PI;
            bool Key(string key)=>key is "W" or "E";
            var mouse=CameraInputPolicy.Mouse(delta,(buttons&1)!=0,(buttons&2)!=0,(buttons&4)!=0,alt,mode is "editor" or "rts",
                requireRight:mode!="fps_control");
            var touch=new CameraGesture(new(3,4),new(2,1),-.1f);
            var expected=CameraControlPolicy.Update(new(){Mode=mode,Distance=camera.Distance,Fov=45,ViewportHeight=720,Smoothing=0},camera.Position,camera.Target,
                yaw,pitch,mouse,touch,1,Key,1f/60,new(2,0,0),new(-2,0,0));
            var input=new Core.RuntimeCameraInput(touch.Look,touch.Pan,touch.Zoom,KeyState:Key)
            {
                MouseInputAvailable=true,WheelDelta=1,
                MouseMotions=[new Projects.CameraMouseMotion(delta.X,delta.Y,buttons,alt)]
            };
            typeof(Core.RuntimeCamera).GetMethod("UpdateControl",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(camera,[scene,1f/60,input]);
            Near(camera.Position,expected.Position,$"{mode} buttons={buttons} alt={alt} position");
            Near(camera.Target,expected.Target,$"{mode} buttons={buttons} alt={alt} target");
        }
        Console.WriteLine("Verified 16 camera modes, mouse/modifier combinations, keyboard, touch and native orbit math.");
        CameraTouchGestureTracker tracker=new();
        Check(tracker.Update([new(1,new(20,20),delta)])==default,"First contact must rebase.");
        Check(tracker.Update([new(1,new(44,32),delta)]).Look==delta,"Ongoing contact must move.");
        Check(tracker.Update([new(1,new(44,32),delta),new(2,new(80,32),delta)])==default,"Adding a contact must rebase.");
        Check(tracker.Update([new(1,new(44,32),delta)])==default,"Removing a contact must rebase.");
    }
    private static void Near(Vector3 a,Vector3 b,string label)=>Check(Vector3.Distance(a,b)<1e-4,$"{label}: {a} != {b}");
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
