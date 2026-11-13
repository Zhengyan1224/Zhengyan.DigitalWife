using System.Numerics;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Input;

namespace Zhengyan.DigitalWife.GamePlayer.Runtime;

public sealed partial class RuntimeCamera
{
    private Vector3 _cinematicOffset = new(0, 1.8f, 5);
    private float _lookHeight = 1.5f, _yawDegrees, _pitchDegrees;
    private float _panSensitivity = 1, _zoomSensitivity = 1;
    private bool _lockOnNextInput,_unlockOnNextInput;
    public Vector3 Forward {get{var v=Target-Position;return v.LengthSquared()>1e-8f?Vector3.Normalize(v):-Vector3.UnitZ;}}
    public Vector3 Up => Settings.VmdHasUp ? Settings.VmdUp.ToVector3() : Vector3.UnitY;
    public Vector3 Right { get { var right = Vector3.Cross(Forward, Up); return right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.UnitX; } }
    public int Width => (int)_screenWidth;
    public int Height => (int)_screenHeight;
    public string ControlMode => Settings.ControlMode;
    public string TargetEntity { get => Settings.TargetEntity; set => Settings.TargetEntity = value ?? ""; }
    public string SubjectEntity { get => Settings.SubjectEntity; set => Settings.SubjectEntity = value ?? ""; }
    public float Distance { get => Settings.Distance; set => Settings.Distance = Math.Max(.01f,value); }
    public float HeightOffset { get => Settings.Height; set => Settings.Height = value; }
    public float ShoulderOffset { get => Settings.ShoulderOffset; set => Settings.ShoulderOffset = value; }
    public float Smoothing { get => Settings.Smoothing; set => Settings.Smoothing = Math.Max(0,value); }
    public float MoveSpeed { get => Settings.MoveSpeed; set => Settings.MoveSpeed = Math.Max(0,value); }
    public float MouseSensitivity { get => Settings.MouseSensitivity; set => Settings.MouseSensitivity = Math.Max(0,value); }
    public float Fov { get => FieldOfView; set => FieldOfView = value; }
    public float OrthographicSize { get => Settings.OrthographicSize; set => Settings.OrthographicSize = Math.Max(.001f,value); }
    public float NearClipPlane { get => Settings.NearClipPlane; set => Settings.NearClipPlane = Math.Max(.001f,value); }
    public float FarClipPlane { get => Settings.FarClipPlane; set => Settings.FarClipPlane = Math.Max(NearClipPlane+.001f,value); }
    public string MainCamera { get => SceneOwner?.MainCamera.Name ?? Name; set => SetMainCamera(value); }
    public void SetMainCamera(string cameraName,bool desktopParameterNames=true)=>SetMainCamera(cameraName);
    public string RenderTexture(string renderTextureName,bool desktopParameterNames=true)=>RenderTexture(renderTextureName);
    public void BindRenderTextureCamera(string renderTextureName,string cameraName,bool desktopParameterNames=true)=>BindRenderTextureCamera(renderTextureName,cameraName);
    public bool RaycastEntity(RuntimeRay ray,out RuntimeRaycastHit hit,float fallbackRadius=.5f,bool desktopParameterNames=true)
    {
        hit=default;float closest=float.MaxValue;
        foreach(var entity in SceneOwner?.Entities??[])
        {
            if(!entity.Visible)continue;
            if(RuntimePhysics.TryRaycastEntity(entity,ray,out var collider,out var distance,out var point))
            {if(distance<closest){closest=distance;hit=new(entity,collider.Id,collider.Name,collider.Shape,distance,point);}}
            else if(fallbackRadius>0&&ray.TryIntersectSphere(entity.Position,fallbackRadius,out distance)&&distance<closest)
            {closest=distance;hit=new(entity,"","","sphere",distance,ray.GetPoint(distance));}
        }
        return hit.Entity is not null;
    }
    public string MainCameraVmdPath => CameraVmdPath(MainCamera);
    public bool MainCameraVmdIsPlaying => CameraVmdIsPlaying(MainCamera);
    public float MainCameraVmdFrame => CameraVmdFrame(MainCamera);
    private RuntimeCamera? FindScriptCamera(string name) => SceneOwner?.GetCamera(name) ?? (name == Name || name == Id ? this : null);
    public string CameraVmdPath(string cameraName) => FindScriptCamera(cameraName)?.VmdPath ?? "";
    public bool CameraVmdIsPlaying(string cameraName) => FindScriptCamera(cameraName)?.VmdIsPlaying ?? false;
    public float CameraVmdFrame(string cameraName) => FindScriptCamera(cameraName)?.VmdFrame ?? 0;
    public void SetCameraLookAt(string cameraName,float positionX,float positionY,float positionZ,float targetX,float targetY,float targetZ)
        => FindScriptCamera(cameraName)?.SetLookAt(positionX,positionY,positionZ,targetX,targetY,targetZ);
    public void SetCameraVmd(string cameraName,string path,bool loop=true,float playbackSpeed=1,bool play=true) => FindScriptCamera(cameraName)?.SetVmd(path,loop,playbackSpeed,play);
    public void PlayCameraVmd(string cameraName,bool restart=false) => FindScriptCamera(cameraName)?.PlayVmd(restart);
    public void PauseCameraVmd(string cameraName) => FindScriptCamera(cameraName)?.PauseVmd();
    public void ClearCameraVmd(string cameraName) { if(FindScriptCamera(cameraName) is {} c){c.Settings.Vmd.Path="";c.Settings.Vmd.IsPlaying=false;c.Settings.VmdHasUp=false;} }
    public void SeekCameraVmd(string cameraName,float frame) => FindScriptCamera(cameraName)?.SeekVmd(frame);
    public void SetCameraVmdLoop(string cameraName,bool loop) { if(FindScriptCamera(cameraName) is {} c)c.Settings.Vmd.Loop=loop; }
    public void SetCameraVmdPlaybackSpeed(string cameraName,float playbackSpeed) { if(FindScriptCamera(cameraName) is {} c)c.Settings.Vmd.PlaybackSpeed=Math.Max(0,playbackSpeed); }
    public void SetCameraViewport(string cameraName,float x,float y,float width,float height,string layoutMode="relative")
    { if(FindScriptCamera(cameraName) is {} c){var v=c.Definition.Viewport;v.Enabled=true;v.X=Math.Max(0,x);v.Y=Math.Max(0,y);v.Width=Math.Max(1,width);v.Height=Math.Max(1,height);v.LayoutMode=LayoutResolver.NormalizeLayoutMode(layoutMode);} }
    public void EnableCameraViewport(string cameraName,bool enabled) { if(FindScriptCamera(cameraName) is {} c)c.Definition.Viewport.Enabled=enabled; }
    public void SetLookAt(float positionX,float positionY,float positionZ,float targetX,float targetY,float targetZ) => SetLookAt(new(positionX,positionY,positionZ),new(targetX,targetY,targetZ));
    public void SetLookAt(Vector3 position,Vector3 target) { Position=position;Target=target; }
    public void SetControlMode(string mode)
    {var next=(mode??"custom").Trim().ToLowerInvariant().Replace('-','_');_unlockOnNextInput=Settings.ControlMode=="locked_fps"&&next!="locked_fps";Settings.ControlMode=next;}
    public void SetMode(string mode) => SetControlMode(mode);
    public void ConfigureControl(float? distance=null,float? height=null,float? shoulderOffset=null,float? smoothing=null,float? moveSpeed=null,float? mouseSensitivity=null,float? safeRadius=null,float? autoOrbitSpeed=null)
    {
        if(distance is {} d)Distance=d;if(height is {} h)HeightOffset=h;if(shoulderOffset is {} so)ShoulderOffset=so;
        if(smoothing is {} s)Smoothing=s;if(moveSpeed is {} m)MoveSpeed=m;if(mouseSensitivity is {} ms)MouseSensitivity=ms;
        if(safeRadius is {} sr)Settings.SafeRadius=Math.Max(0,sr);if(autoOrbitSpeed is {} a)Settings.AutoOrbitSpeed=a;
    }
    public void SetYawPitch(float yawDegrees,float pitchDegrees) { _yawDegrees=yawDegrees;_pitchDegrees=Math.Clamp(pitchDegrees,-89,89);Orbit(yawDegrees-CurrentYaw(),pitchDegrees-CurrentPitch()); }
    private float CurrentYaw() {var o=Position-Target;return MathF.Atan2(o.X,o.Z)*180/MathF.PI;}
    private float CurrentPitch() {var o=Position-Target;return MathF.Asin(Math.Clamp(o.Y/Math.Max(.001f,o.Length()),-1,1))*180/MathF.PI;}
    public void SetMouseLook(bool enabled,bool requireRightMouse=true) { Settings.EnableMouseLook=enabled;Settings.RequireRightMouseForMouseLook=requireRightMouse; }
    public void UseCustomMode()=>SetMode("custom");
    public void UseEditorOrbitMode(float orbitSensitivity=.2f,float panSensitivity=1,float zoomSensitivity=1) {SetMode("editor");MouseSensitivity=orbitSensitivity;_panSensitivity=panSensitivity;_zoomSensitivity=zoomSensitivity;}
    public void UseMaxEditorMode(float orbitSensitivity=.2f,float panSensitivity=1,float zoomSensitivity=1)=>UseEditorOrbitMode(orbitSensitivity,panSensitivity,zoomSensitivity);
    public void UseThirdPersonMode(string target,float distance=5,float height=1.5f,float shoulderOffset=0,float smoothing=12) {SetMode("third_person");TargetEntity=target;SubjectEntity=target;ConfigureControl(distance,height,shoulderOffset,smoothing);}
    public void UseTpsMode(string target,float distance=5,float height=1.5f,float shoulderOffset=0,float smoothing=12)=>UseThirdPersonMode(target,distance,height,shoulderOffset,smoothing);
    public void UseShoulderMode(string target,float distance=4,float height=1.6f,float shoulderOffset=.55f,float smoothing=12)=>UseThirdPersonMode(target,distance,height,shoulderOffset,smoothing);
    public void UseLockOnMode(string subject,string target,float distance=5,float height=1.6f,float smoothing=12,float safeRadius=.25f,float shoulderOffset=0) {SetMode("lock_on");TargetEntity=target;SubjectEntity=subject;ConfigureControl(distance,height,shoulderOffset,smoothing,safeRadius:safeRadius);}
    public void UseFirstPersonMode(string target,float eyeHeight=1.65f,float smoothing=18) {SetMode("first_person");TargetEntity=target;SubjectEntity=target;ConfigureControl(height:eyeHeight,smoothing:smoothing);}
    public void UseFpsMode(string target,float eyeHeight=1.65f,float smoothing=18)=>UseFirstPersonMode(target,eyeHeight,smoothing);
    public void UseFpsControlMode(string target,float eyeHeight=1.65f,float smoothing=18,float mouseSensitivity=.15f) {UseFirstPersonMode(target,eyeHeight,smoothing);SetMode("fps_control");MouseSensitivity=mouseSensitivity;}
    public void UseLockedFpsMode(string target,float eyeHeight=1.65f,float smoothing=18,float mouseSensitivity=.15f){UseFpsControlMode(target,eyeHeight,smoothing,mouseSensitivity);SetMode("locked_fps");_lockOnNextInput=true;}
    public void UseFreeFlyMode(float moveSpeed=5,float mouseSensitivity=.15f) {SetMode("free_fly");ConfigureControl(moveSpeed:moveSpeed,mouseSensitivity:mouseSensitivity);}
    public void UseRtsMode(float height=12,float pitch=55,float moveSpeed=8) {SetMode("rts");HeightOffset=height;_pitchDegrees=pitch;MoveSpeed=moveSpeed;}
    public void UseTopDownMode(string target="",float height=12,float smoothing=12) {SetMode("top_down");TargetEntity=target;ConfigureControl(height:height,smoothing:smoothing);}
    public void UseIsometricMode(string target="",float distance=12,float height=0,float smoothing=12) {SetMode("isometric");TargetEntity=target;ConfigureControl(distance,height,smoothing:smoothing);}
    public void UseSideScrollerMode(string target,float distance=10,float height=1.5f,float smoothing=12) {SetMode("side_scroller");TargetEntity=target;ConfigureControl(distance,height,smoothing:smoothing);}
    public void UseFixedMode(float positionX,float positionY,float positionZ,float targetX,float targetY,float targetZ)=>UseFixedMode(new(positionX,positionY,positionZ),new(targetX,targetY,targetZ));
    public void UseFixedMode(Vector3 position,Vector3 target) {SetMode("fixed");SetLookAt(position,target);}
    public void UseCinematicFollowMode(string target,float offsetX=0,float offsetY=1.8f,float offsetZ=5,float lookHeight=1.5f,float smoothing=8) {SetMode("cinematic_follow");TargetEntity=target;_cinematicOffset=new(offsetX,offsetY,offsetZ);_lookHeight=lookHeight;Smoothing=smoothing;}
    public void UseOrbitalFollowMode(string target,float distance=6,float height=1.5f,float yawSpeed=30,float smoothing=12) {SetMode("auto_orbit");TargetEntity=target;ConfigureControl(distance,height,smoothing:smoothing,autoOrbitSpeed:yawSpeed);}
    public void Orbit(float deltaYawDegrees,float deltaPitchDegrees) {var o=Position-Target;float r=Math.Max(.01f,o.Length()),yaw=(CurrentYaw()+deltaYawDegrees)*MathF.PI/180,pitch=Math.Clamp((CurrentPitch()+deltaPitchDegrees)*MathF.PI/180,-1.52f,1.52f);Position=Target+new Vector3(MathF.Sin(yaw)*MathF.Cos(pitch),MathF.Sin(pitch),MathF.Cos(yaw)*MathF.Cos(pitch))*r;}
    public void Pan(float deltaPixelsX,float deltaPixelsY) {var offset=(-Right*deltaPixelsX+Vector3.Normalize(Vector3.Cross(Right,Forward))*deltaPixelsY)*Math.Max(.01f,Vector3.Distance(Position,Target))*.0025f;Position+=offset;Target+=offset;}
    public void Dolly(float delta) {var o=Position-Target;Position=Target+Vector3.Normalize(o)*Math.Clamp(o.Length()*MathF.Exp(delta*.08f),.05f,FarClipPlane);}
    public RuntimeRay ViewportPointToRay(float viewportX,float viewportY)=>ScreenPointToRay(_screenX+viewportX*_screenWidth,_screenY+viewportY*_screenHeight);
    public RuntimeRay MousePointToRay(RuntimeCameraInput input)=>ScreenPointToRay(input.MouseX,input.MouseY);
    public RuntimeRay TouchPointToRay(TouchPoint touch)=>ScreenPointToRay(touch.X,touch.Y);
    public RuntimeEntity? PickEntity(float screenX,float screenY,float radius=.5f)=>RaycastEntity(ScreenPointToRay(screenX,screenY),out var hit,fallbackRadius:radius)?hit.Entity:null;

    private bool ApplyScriptCameraMode(RuntimeScene scene,float delta,RuntimeCameraInput input,string mode)
    {
        if(input.CursorLockSetter is not null)
        {if(_lockOnNextInput){input.LockCursor();_lockOnNextInput=false;}if(_unlockOnNextInput){input.UnlockCursor();_unlockOnNextInput=false;}}
        if(mode is "fixed" or "custom")return true;
        var target=scene.GetEntity(TargetEntity);var subject=scene.GetEntity(SubjectEntity)??target;
        var focus=target?.Position??Target;Vector3 desired;
        bool look=Settings.EnableMouseLook&&(!Settings.RequireRightMouseForMouseLook||input.SecondaryButtonDown||input.IsCursorLocked);
        switch(mode)
        {
            case "fps_control": case "locked_fps":
                if(subject is null)return true;
                if(look){_yawDegrees-=input.LookDelta.X*MouseSensitivity;_pitchDegrees=Math.Clamp(_pitchDegrees-input.LookDelta.Y*MouseSensitivity,-89,89);}
                var heading=Quaternion.CreateFromAxisAngle(Vector3.UnitY,_yawDegrees*MathF.PI/180);
                var front=Vector3.Transform(-Vector3.UnitZ,heading);var right=Vector3.Transform(Vector3.UnitX,heading);
                var motion=Vector3.Zero;
                motion-=front*input.LeftStickY;motion+=right*input.LeftStickX;
                if(input.IsKeyDown("W")||input.IsKeyDown("ArrowUp"))motion+=front;if(input.IsKeyDown("S")||input.IsKeyDown("ArrowDown"))motion-=front;
                if(input.IsKeyDown("D")||input.IsKeyDown("ArrowRight"))motion+=right;if(input.IsKeyDown("A")||input.IsKeyDown("ArrowLeft"))motion-=right;
                if(motion.LengthSquared()>0)subject.Position+=Vector3.Normalize(motion)*MoveSpeed*Math.Max(0,delta);
                subject.RotationDegrees=new(0,_yawDegrees,0);
                Position=subject.Position+Vector3.UnitY*HeightOffset;
                float pitch=_pitchDegrees*MathF.PI/180;Target=Position+front*MathF.Cos(pitch)+Vector3.UnitY*MathF.Sin(pitch);
                return true;
            case "top_down": desired=focus+Vector3.UnitY*Math.Max(.01f,HeightOffset);break;
            case "isometric": desired=focus+Vector3.Normalize(new Vector3(1,1,1))*Distance+Vector3.UnitY*HeightOffset;break;
            case "side_scroller": focus+=Vector3.UnitY*HeightOffset;desired=focus+Vector3.UnitZ*Distance;break;
            case "cinematic_follow":
                var origin=target?.Position??focus;
                var rotation=target is null?Quaternion.Identity:target.Rotation;
                desired=origin+Vector3.Transform(_cinematicOffset,rotation);focus=origin+Vector3.UnitY*_lookHeight;break;
            case "lock_on":
                if(subject is null)return true;
                var back=Vector3.Transform(Vector3.UnitZ,subject.Rotation);
                desired=subject.Position+back*Distance+Vector3.UnitY*HeightOffset+Vector3.Cross(back,Vector3.UnitY)*ShoulderOffset;
                focus+=Vector3.UnitY*HeightOffset;break;
            case "rts":
                var move=new Vector3((input.IsKeyDown("D")?1:0)-(input.IsKeyDown("A")?1:0),0,(input.IsKeyDown("S")?1:0)-(input.IsKeyDown("W")?1:0));
                focus+=move*MoveSpeed*Math.Max(0,delta);focus.Y=0;
                desired=focus+new Vector3(0,Math.Max(.01f,HeightOffset),Math.Max(.01f,HeightOffset)/MathF.Tan(Math.Clamp(_pitchDegrees,1,89)*MathF.PI/180));break;
            default:return false;
        }
        float blend=Smoothing<=0?1:1-MathF.Exp(-Smoothing*Math.Max(0,delta));
        Position=Vector3.Lerp(Position,desired,blend);Target=Vector3.Lerp(Target,focus,blend);return true;
    }
}
