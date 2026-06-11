using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Zhengyan.DigitalWife.Mmd.Game;
using Zhengyan.DigitalWife.Mmd.Game.Audio;
using Zhengyan.DigitalWife.Mmd.Game.Components;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd.Game.Pmx.TransformUpdater;

using QuickStartGame game = new();
game.Run();

internal sealed class QuickStartGame : Game
{
    private readonly OrbitCamera _camera = new();
    private readonly MmdCharacterGroup _characters;

    private MmdCharacter? _body;
    private MmdCharacter? _outfit;
    private string? _walkMotionPath;
    private string? _runMotionPath;
    private float _blendTimer;
    private bool _spaceWasDown;
    private AudioClip? _bgmClip;
    private AudioSource? _bgmSource;

    public QuickStartGame()
        : base(new GameOptions
        {
            Title = "Zhengyan.DigitalWife.Mmd.Game QuickStart",
            WindowSize = new Vector2D<int>(1280, 720),
            ClearColor = new Vector4(0.48f, 0.62f, 0.76f, 1.0f),
            UseOpenCL = true,
            EnableAudio = true,
            AnimationTimingMode = AnimationTimingMode.TimeSynchronized
        })
    {
        _characters = new MmdCharacterGroup(this, _camera);
    }

    protected override void LoadContent()
    {
        _camera.SetLookAt(new Vector3(0.0f, 2.3f, 8.0f), new Vector3(0.0f, 1.25f, 1.6f));
        _camera.Fov = 45.0f;

        AddComponent(new OrbitCameraController(_camera)
        {
            OrbitSensitivity = 0.2f,
            PanSensitivity = 1.0f,
            ZoomSensitivity = 1.0f,
            KeyboardPanSpeed = 4.0f
        });

        string bodyPath = ContentPath("GameData", "Character", "Body", "Body.pmx");
        string outfitPath = SingleFilePath("*.pmx", "GameData", "Character", "MaidOutfit");
        string classroomPath = ContentPath("GameData", "Scene", "Classroom", "classroom.pmx");
        _walkMotionPath = ContentPath("GameData", "Motion", "Basic", "basic_walk.vmd");
        _runMotionPath = ContentPath("GameData", "Motion", "Basic", "basic_run.vmd");

        _body = _characters.AddCharacter(bodyPath, name: "Body", configureModel: ConfigureCharacterModel);
        _body.SetMotionLayers(
        [
            new MotionLayerDefinition(_walkMotionPath, 1.0f),
            new MotionLayerDefinition(_runMotionPath, 0.0f)
        ]);
        _body.IsPlaying = true;
        _body.LoopMotion = true;

        _outfit = _characters.AddCharacter(outfitPath, name: "MaidOutfit", configureModel: ConfigureCharacterModel);
        _outfit.IsPlaying = false;

        RelationTransformUpdater relation = _characters.BindRelation(_outfit, _body, bindComponentTransform: true);
        relation.BindLighting = true;

        AddComponent(new PmxModelComponent(classroomPath)
        {
            Camera = _camera,
            Scale = new Vector3(0.2f),
            Position = Vector3.Zero,
            IsPlaying = false,
            EnablePhysical = false,
            EnableEdge = false,
            EnableShadow = false,
            LightDirection = new Vector3(-0.5f, -1.0f, -0.5f),
            AmbientLightColor = new Vector3(0.65f),
            AmbientLightStrength = 0.35f
        });

        AddComponent(new WaterSurfaceComponent(_camera, 120.0f)
        {
            Position = new Vector3(0.0f, -0.08f, 0.0f),
            Alpha = 0.45f,
            AnimationSpeed = 0.035f,
            NormalTiling = 80.0f,
            DrawOrder = 120
        });

        AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Cloud())
        {
            Position = new Vector3(0.0f, 11.0f, -9.0f),
            DrawOrder = 129
        });

        AddComponent(new ParticleSystemComponent(_camera, ParticleSystemPresets.Sakura())
        {
            Position = new Vector3(0.0f, 7.0f, 1.6f),
            Opacity = 0.55f,
            DrawOrder = 130
        });

        TryStartBackgroundMusic();
    }

    protected override void Update(GameTime gameTime)
    {
        UpdateWalkRunBlend(gameTime);
        TogglePlaybackWithSpace();
    }

    protected override void UnloadContent()
    {
        _bgmSource?.Dispose();
        _bgmClip?.Dispose();
        _bgmSource = null;
        _bgmClip = null;
    }

    private void ConfigureCharacterModel(PmxModelComponent model)
    {
        model.Scale = new Vector3(0.2f);
        model.Position = new Vector3(0.0f, 0.0f, 1.6f);
        model.EnablePhysical = true;
        model.EnableEdge = true;
        model.EnableShadow = true;
        model.DrawShadowInMainPass = true;
        model.LightDirection = new Vector3(-0.5f, -1.0f, -0.5f);
        model.AmbientLightColor = new Vector3(0.68f);
        model.AmbientLightStrength = 0.35f;
        model.ShadowColor = new Vector4(0.17f, 0.17f, 0.17f, 0.45f);
    }

    private void UpdateWalkRunBlend(GameTime gameTime)
    {
        if (_body is null || _walkMotionPath is null || _runMotionPath is null)
        {
            return;
        }

        _blendTimer += (float)gameTime.ElapsedSeconds;
        float runWeight = (MathF.Sin(_blendTimer * 0.55f) * 0.5f) + 0.5f;
        float walkWeight = 1.0f - runWeight;

        _body.SetMotionLayerWeight(_walkMotionPath, walkWeight);
        _body.SetMotionLayerWeight(_runMotionPath, runWeight);

        Title = $"Zhengyan.DigitalWife.Mmd.Game QuickStart - walk {walkWeight:0.00}, run {runWeight:0.00}";
    }

    private void TogglePlaybackWithSpace()
    {
        bool spaceDown = Input.IsKeyDown(Key.Space);
        if (spaceDown && !_spaceWasDown && _body is not null)
        {
            _body.IsPlaying = !_body.IsPlaying;
        }

        _spaceWasDown = spaceDown;
    }

    private void TryStartBackgroundMusic()
    {
        if (Audio is null)
        {
            Console.Error.WriteLine(AudioStatusMessage ?? "Audio is not available.");
            return;
        }

        string bgmPath = ContentPath("GameData", "BGM", "Lamb.ogg");
        if (!File.Exists(bgmPath))
        {
            Console.Error.WriteLine($"BGM file was not found: {bgmPath}");
            return;
        }

        _bgmClip = Audio.LoadClip(bgmPath);
        _bgmSource = Audio.CreateSource(_bgmClip);
        _bgmSource.Looping = true;
        _bgmSource.Volume = 0.45f;
        _bgmSource.Play();
    }

    private static string ContentPath(params string[] segments)
    {
        string path = AppContext.BaseDirectory;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static string SingleFilePath(string searchPattern, params string[] directorySegments)
    {
        string directory = ContentPath(directorySegments);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Content directory not found: {directory}");
        }

        string[] matches = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"No file matching '{searchPattern}' was found in: {directory}"),
            _ => throw new InvalidOperationException($"Expected exactly one file matching '{searchPattern}' in: {directory}")
        };
    }
}

