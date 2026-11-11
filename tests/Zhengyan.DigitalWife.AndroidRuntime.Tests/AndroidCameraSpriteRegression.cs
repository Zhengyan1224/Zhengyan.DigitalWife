using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using NativeEvent = Android.Views.MotionEvent;
using NativePhase = Android.Views.MotionEventActions;

internal static class AndroidCameraSpriteRegression
{
    public static void Run()
    {
        GameProjectScene definition=new(){Sprites=[new(){Id="background",Path="background.png",X=50,Y=50,Width=600,Height=400,
            LayoutMode="relative",Visible=true,DrawOrder=-1,TargetEntity=""}],Entities=[new(){Id="body",Name="Body",Type="empty"}]};
        Verify(definition,1280,720,1280,720);
        Verify(definition,1920,1080,1280,720);
    }

    public static void RunProject(string directory)
    {
        var project=GameProjectStore.Load(directory);
        var definition=GameProjectStore.LoadScene(directory,"scenes/main.scene.json");
        Check(definition.Sprites.Any(s=>s.DrawOrder<0 && s.Visible && string.IsNullOrWhiteSpace(s.TargetEntity)),"Project does not contain the decorative background that reproduced the bug.");
        Verify(definition,1280,720,project.Window.Width,project.Window.Height);
        Verify(definition,1920,1080,project.Window.Width,project.Window.Height);
        Console.WriteLine("PASS actual DemoGame01 main.scene camera drags in all four quadrants at two surface resolutions");
    }

    private static void Verify(GameProjectScene definition,int width,int height,int referenceWidth,int referenceHeight)
    {
        using var scene=new RuntimeScene("main",definition,p=>p);
        AndroidCameraTouchInput camera=new();AndroidTouchState touches=new();
        RuntimeCameraInput Frame(){var input=touches.BeginFrame(width,height);return camera.GetInput(scene,input,width,height,referenceWidth,referenceHeight);}
        foreach(var start in new[]{new Vector2(width*.25f,height*.25f),new Vector2(width*.75f,height*.25f),new Vector2(width*.25f,height*.75f),new Vector2(width*.75f,height*.75f)})
        {
            touches.Apply(new NativeEvent(NativePhase.Down,start.X,start.Y,7));Frame();
            touches.Apply(new NativeEvent(NativePhase.Move,start.X+20,start.Y+12,7));var movement=Frame();
            Check(movement.LookDelta==new Vector2(20,12),$"Camera drag was captured at ({start.X},{start.Y}) on {width}x{height}.");
            var before=scene.MainCamera.CreateView();scene.Update(1f/60,movement);
            Check(before!=scene.MainCamera.CreateView(),"The unblocked gesture did not rotate the actual camera.");
            touches.Apply(new NativeEvent(NativePhase.Up,start.X+20,start.Y+12,7));Frame();
        }
        var background=definition.Sprites.First(s=>s.Visible && s.DrawOrder<0 && string.IsNullOrWhiteSpace(s.TargetEntity));
        string original=background.TargetEntity;background.TargetEntity=scene.GetEntity("Body")?.Id??"body";
        try
        {
            var rect=SpriteLayoutResolver.Resolve(background,width,height,referenceWidth,referenceHeight);
            var point=new Vector2(rect.X+rect.Width*.5f,rect.Y+rect.Height*.5f);
            touches.Apply(new NativeEvent(NativePhase.Down,point.X,point.Y,8));Frame();
            touches.Apply(new NativeEvent(NativePhase.Move,width-2,height-2,8));
            Check(Frame().LookDelta==Vector2.Zero,"Interactive sprite capture ended when the finger left its bounds.");
            touches.Apply(new NativeEvent(NativePhase.Up,width-2,height-2,8));Frame();
        }
        finally{background.TargetEntity=original;}
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
