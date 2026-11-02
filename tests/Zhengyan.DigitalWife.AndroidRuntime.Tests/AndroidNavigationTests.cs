using System.Collections;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Zhengyan.DigitalWife.GameEditor;
using Zhengyan.DigitalWife.GamePlayer;
using Zhengyan.DigitalWife.GamePlayer.Android;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;
using Zhengyan.DigitalWife.Mmd.Game.Pmx;
using Zhengyan.DigitalWife.Mmd;
using NativeEvent = Android.Views.MotionEvent;
using NativePhase = Android.Views.MotionEventActions;

internal static class AndroidNavigationTests
{
    internal static void TestLookAt()
    {
        using Fixture fixture = new([]);
        RuntimeEntity entity = fixture.Scene.GetEntity("ground")!;
        AndroidScriptEntity actor = new(entity, _ => { }, (_, _) => { }, () => null);
        object desktop = fixture.DesktopEntity;
        string root = Path.GetFullPath(Path.Combine("temp", "android-navigation-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string modelPath = Path.Combine(root, "unused.pmx");
        File.WriteAllBytes(modelPath, []);
        using PmxModelComponent model = new(modelPath);
        desktop.GetType().GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(desktop, model);
        PropertyInfo rotation = desktop.GetType().GetProperty("Rotation")!;
        MethodInfo lookAt = desktop.GetType().GetMethod("LookAt", [typeof(Vector3)])!;
        Vector3 position = new(3, 2, -4);
        actor.Position = position;
        desktop.GetType().GetProperty("Position")!.SetValue(desktop, position);
        foreach (Vector3 direction in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX,
            -Vector3.UnitX, new Vector3(1, 3, 1), new Vector3(-1, -2, -1), Vector3.UnitY,
            new Vector3(.0001f, 0, 0), Vector3.Zero })
        {
            actor.LookAt(position + direction);
            lookAt.Invoke(desktop, [position + direction]);
            Vector3 expected = Vector3.Transform(Vector3.UnitZ, (Quaternion)rotation.GetValue(desktop)!);
            Vector3 actual = Vector3.TransformNormal(Vector3.UnitZ, entity.TransformMatrix);
            Near(actual, expected, $"Android LookAt differs from desktop for {direction}.");
        }
    }

    internal static void TestSampling()
    {
        using Fixture fixture = new([new(Vector3.Zero, new(0, 0, 10), new(10, 0, 0))]);
        fixture.Bake();
        foreach (Vector3 position in new[] { new Vector3(9, 2, 9), new Vector3(-3, 1, -4),
            new Vector3(2, 2, 2), new Vector3(10, 0, 0) })
            fixture.CompareSample(position, 20);
        fixture.CompareSample(new(9, 0, 9), 1);
        fixture.CompareSample(new(2, 1, 2), -1);
    }

    internal static void TestBakeAndRepeatedPaths()
    {
        RuntimeMeshTriangle[] triangles = [.. Floor(0, 0, 0), .. Floor(10.2f, 1, 0),
            .. Floor(20.4f, 2, 0), .. Floor(0, 0, 20, reversed: true)];
        using Fixture fixture = new(triangles);
        fixture.Bake(maxStepHeight: 2.2f, maxStepHorizontalDistance: .35f);
        Vector3 current = new(2, .02f, 2);
        foreach (Vector3 target in new[] { new Vector3(18, 1, 8), new Vector3(28, 2, 2),
            new Vector3(2, 0, 8), new Vector3(12, 1, 2), new Vector3(8, 0, 2) })
        {
            RuntimeNavigationPath path = fixture.ComparePath(current, target, 5);
            Check(path.Success, "A later click could not find a route across the connected steps.");
            current = path.Points[^1] + new Vector3(0, .02f, 0);
        }
    }

    internal static void TestConnectedEndpoints()
    {
        using Fixture fixture = new([.. Floor(0, 0, 0), .. Floor(0, .1f, 0), .. Floor(10, 0, 0)]);
        fixture.Bake(maxStepHeight: 0, maxStepHorizontalDistance: 0);
        fixture.ComparePath(new(2, .09f, 2), new(18, 0, 8), 1);
        fixture.ComparePath(new(2, 0, 2), new(18, 0, 8), 1);
        fixture.ComparePath(new(2, .1f, 2), new(18, 0, 8), .05f);
        fixture.ComparePath(new(2, 0, 2), new(100, 0, 100), 1);
        fixture.ComparePath(new(100, 0, 100), new(2, 0, 2), 1);
        using Fixture empty = new([]);
        empty.ComparePath(Vector3.Zero, Vector3.One, 5);
    }

    internal static void TestPackagedClicks()
    {
        using Fixture fixture = new([.. Floor(0, 0, 0), .. Floor(10.2f, 1, 0), .. Floor(20.4f, 2, 0)]);
        fixture.Scene.MainCamera.Position = new(15, 30, 40);
        fixture.Scene.MainCamera.Target = new(15, 0, 5);
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ScriptSamples", "click-to-move.csx"));
        RunPackagedClicks(fixture, source, new(2, .02f, 2), [new(18, 1, 8), new(3, 0, 3), new(28, 2, 4)]);
    }

    internal static void RunProject(string projectDirectory)
    {
        GameProject project = GameProjectStore.Load(projectDirectory);
        GameProjectScene definition = GameProjectStore.LoadScene(projectDirectory, project.DefaultScene);
        GameEntity ground = definition.Entities.Single(entity => entity.Colliders.Any(collider => collider.Enabled && collider.Shape == "mesh" && collider.Walkable));
        GameEntity actor = definition.Entities.Single(entity => entity.Scripts.Any(binding => binding.Enabled && binding.Path.EndsWith("Body.csx", StringComparison.OrdinalIgnoreCase)));
        PmxParsing model = PmxParsing.ParsingByFile(GameProjectPath.ToAbsolute(projectDirectory, ground.AssetPath))!;
        RuntimeEntity groundEntity = new(ground);
        ColliderSettings collider = ground.Colliders.Single(settings => settings.Enabled && settings.Shape == "mesh" && settings.Walkable);
        Vector3 radians = collider.RotationDegrees.ToVector3() * (MathF.PI / 180);
        Matrix4x4 world = Matrix4x4.CreateScale(collider.Size.ToVector3())
            * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
            * Matrix4x4.CreateTranslation(collider.Position.ToVector3()) * groundEntity.TransformMatrix;
        Vector3 Vertex(uint index) => Vector3.Transform(model.Vertices[index].Position * new Vector3(1, 1, -1), world);
        RuntimeMeshTriangle[] triangles = model.Faces.Select(face => new RuntimeMeshTriangle(
            Vertex(face.Vertices[0]), Vertex(face.Vertices[1]), Vertex(face.Vertices[2])))
            .Where(triangle => Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() > .000001f).ToArray();
        using Fixture fixture = new(triangles);
        fixture.Scene.MainCamera.Position = definition.Camera.Position.ToVector3();
        fixture.Scene.MainCamera.Target = definition.Camera.Target.ToVector3();
        fixture.Scene.MainCamera.FieldOfView = definition.Camera.Fov;
        fixture.Bake(2.2f, .35f);
        Vector3 start = actor.Transform.Position.ToVector3();
        List<Vector3> targets = [];
        AndroidScriptScene scene = new(fixture.Scene, "", _ => { }, () => 60, null!, () => new(800, 600), new() { Width = 800, Height = 600 });
        for (int row = 300; row < 600 && targets.Count < 4; row += 45)
        {
            for (int column = 80; column < 800 && targets.Count < 4; column += 90)
            {
                RuntimeRay ray = scene.Camera.ScreenPointToRay(column, row);
                if (!fixture.Scene.Physics.Raycast(ray, out RuntimeRaycastHit hit, 200)
                    || !fixture.Scene.Navigation.TryFindPath(start, hit.Point, out RuntimeNavigationPath path, 5)
                    || path.Length < 2 || targets.Any(target => Vector3.Distance(target, hit.Point) < 2)) continue;
                targets.Add(hit.Point);
            }
        }
        Check(targets.Count >= 3, "The project did not provide enough visible navigation targets.");
        string script = actor.Scripts.Single(binding => binding.Enabled && binding.Path.EndsWith("Body.csx", StringComparison.OrdinalIgnoreCase)).Path;
        RunPackagedClicks(fixture, File.ReadAllText(GameProjectPath.ToAbsolute(projectDirectory, script)), start, targets.ToArray());
        Console.WriteLine($"PASS project navigation: triangles={fixture.Scene.Navigation.TriangleCount}, clicks={targets.Count}; original project read-only.");
    }

    private static void RunPackagedClicks(Fixture fixture, string source, Vector3 start, Vector3[] targets)
    {
        fixture.Bake(2.2f, .35f);
        string root = Path.GetFullPath(Path.Combine("temp", "android-navigation-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        RuntimeEntity entity = fixture.Scene.AddEntity(new() { Id = "actor", Name = "actor", Type = "pmx_model",
            Scripts = [new() { Path = "click-to-move.csx", Language = "csharp" }] });
        entity.Position = start;
        File.WriteAllText(Path.Combine(root, "click-to-move.csx"), source);
        GameProject project = new() { Scene = fixture.Scene.Definition };
        GameProjectStore.Save(root, project);
        AndroidScriptPrecompileResult compiled = AndroidCSharpScriptPrecompiler.Precompile(root, project);
        GameProjectPackageBuildResult package = GameProjectPackage.Create(root,
            new() { OutputPath = Path.Combine(root, "navigation.dwgame"), Password = "test-only" });
        using GameProjectPackageSession opened = GameProjectPackage.OpenOrExtract(package.OutputPath,
            new() { Password = "test-only", TempRootDirectory = Path.Combine(root, "extract"),
                SaveDirectory = Path.Combine(root, "saves"), UsePersistentCache = false });
        Assembly assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(opened.ProjectDirectory, compiled.Entries.Single().Assembly)));
        Func<object?[], Task<object?>> execute = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Single(method => method.Name == "<Factory>").CreateDelegate<Func<object?[], Task<object?>>>();
        using AndroidScriptServices services = new(fixture.Scene, root, _ => { }, _ => true, _ => true, _ => true, _ => false,
            (_, _, _) => false, _ => null, () => [], _ => { }, new(), Path.Combine(root, "saves"), new MainThreadDispatcher(), null!, null!, null!, null!);
        AndroidScriptScene scene = new(fixture.Scene, root, _ => { }, () => 60, services, () => new(800, 600), new() { Width = 800, Height = 600 });
        AndroidScriptEntity actor = new(entity, _ => { }, (_, _) => { }, () => null);
        AndroidTouchState touch = new();
        AndroidScriptInput input = new(AndroidInputSnapshot.Empty);
        AndroidScriptGlobals globals = new(scene, actor, input, new(_ => true, _ => true, _ => true), 0, true, false, services: services);
        Frame(isStart: true);
        foreach (Vector3 target in targets)
        {
            Vector4 clip = Vector4.Transform(new Vector4(target, 1), fixture.Scene.MainCamera.CreateView() * fixture.Scene.MainCamera.CreateProjection(800f / 600));
            Vector2 screen = new((clip.X / clip.W + 1) * 400, (1 - clip.Y / clip.W) * 300);
            Check(scene.Physics.Raycast(scene.Camera.ScreenPointToRay(screen.X, screen.Y), out RuntimeRaycastHit hit, 200, actor), "Tap missed the ground.");
            RuntimeNavigationPath expected = fixture.ComparePath(actor.Position, hit.Point, 5);
            Check(expected.Success, "Tap has no desktop navigation route.");
            touch.Apply(new NativeEvent(NativePhase.Down, screen.X, screen.Y, 7));
            touch.Apply(new NativeEvent(NativePhase.Up, screen.X, screen.Y, 7));
            Vector3 before = actor.Position;
            Frame();
            int frameCount = 0;
            Vector2 destination = new(expected.Points[^1].X, expected.Points[^1].Z);
            while (Vector2.Distance(new(actor.Position.X, actor.Position.Z), destination) > .061f && frameCount++ < 10000) Frame();
            Check(frameCount < 10000 && Vector3.Distance(before, actor.Position) > .5f, "A repeated packaged tap did not move the actor to its new destination.");
            for (int frame = 0; frame < 5; frame++) Frame();
            Vector3 arrived = actor.Position;
            Frame();
            Near(actor.Position, arrived, "Actor continued moving after completing its path.");
        }

        void Frame(bool isStart = false)
        {
            input.Update(touch.BeginFrame(800, 600));
            Vector3 before = actor.Position;
            globals.Update(1f / 30, isStart, null);
            execute([globals, null]).GetAwaiter().GetResult();
            Vector3 movement = actor.Position - before;
            movement.Y = 0;
            if (movement.LengthSquared() > .000001f)
                Near(Vector3.TransformNormal(Vector3.UnitZ, entity.TransformMatrix), Vector3.Normalize(movement), "Packaged actor is not facing its movement direction.");
        }
    }

    private static IEnumerable<RuntimeMeshTriangle> Floor(float offsetX, float elevation, float offsetZ, bool reversed = false)
    {
        Vector3 lowerLeft = new(offsetX, elevation, offsetZ), upperLeft = new(offsetX, elevation, offsetZ + 10);
        Vector3 lowerRight = new(offsetX + 10, elevation, offsetZ), upperRight = new(offsetX + 10, elevation, offsetZ + 10);
        yield return reversed ? new(lowerLeft, lowerRight, upperLeft) : new(lowerLeft, upperLeft, lowerRight);
        yield return reversed ? new(upperLeft, lowerRight, upperRight) : new(upperLeft, upperRight, lowerRight);
    }

    private static void Near(Vector3 actual, Vector3 expected, string message)
        => Check(Vector3.Distance(actual, expected) < .0001f, $"{message} expected={expected}, actual={actual}");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal RuntimeScene Scene { get; }
        internal object DesktopEntity { get; }
        private readonly object _desktopNavigation;

        internal Fixture(RuntimeMeshTriangle[] triangles)
        {
            GameEntity ground = new() { Id = "ground", Type = "empty",
                Colliders = [new() { Shape = "mesh", Walkable = true }] };
            Scene = new("main", new() { Entities = [ground] }, path => path,
                meshColliderResolver: (_, _) => new RuntimeMeshCollider(triangles));
            Assembly desktop = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Zhengyan.DigitalWife.GamePlayer.dll"));
            Type entityType = desktop.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeEntity")!;
            ConstructorInfo entityConstructor = entityType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(constructor => constructor.GetParameters().Length == 2);
            object definition = JsonSerializer.Deserialize(JsonSerializer.Serialize(ground), entityConstructor.GetParameters()[0].ParameterType)!;
            DesktopEntity = entityConstructor.Invoke([definition, (Func<string, string>)(path => path)]);
            object collider = ((IList)definition.GetType().GetProperty("Colliders")!.GetValue(definition)!)[0]!;
            object signature = entityType.GetMethod("CreateMeshColliderSignature", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(DesktopEntity, [collider])!;
            Type triangleType = desktop.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeMeshTriangle")!;
            Array desktopTriangles = Array.CreateInstance(triangleType, triangles.Length);
            for (int index = 0; index < triangles.Length; index++)
                desktopTriangles.SetValue(Activator.CreateInstance(triangleType,
                    triangles[index].A, triangles[index].B, triangles[index].C), index);
            object mesh = Activator.CreateInstance(desktop.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeMeshCollider")!, desktopTriangles)!;
            object cache = Activator.CreateInstance(entityType.GetNestedType("MeshColliderCache", BindingFlags.NonPublic)!, signature, mesh)!;
            ((IDictionary)entityType.GetField("_meshColliderCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(DesktopEntity)!)
                .Add(ground.Colliders[0].Id, cache);
            Type navigationType = desktop.GetType("Zhengyan.DigitalWife.GamePlayer.RuntimeSceneNavigation")!;
            ConstructorInfo navigationConstructor = navigationType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            Array entities = Array.CreateInstance(entityType, 1);
            entities.SetValue(DesktopEntity, 0);
            Delegate getEntities = Expression.Lambda(navigationConstructor.GetParameters()[0].ParameterType, Expression.Constant(entities)).Compile();
            _desktopNavigation = navigationConstructor.Invoke([getEntities]);
        }

        internal void Bake(float maxStepHeight = .45f, float maxStepHorizontalDistance = .35f)
        {
            RuntimeNavigationBakeResult actual = Scene.Navigation.Bake(.1f, maxStepHeight, maxStepHorizontalDistance);
            object expected = Invoke("Bake", .1f, maxStepHeight, maxStepHorizontalDistance)!;
            Check(actual.TriangleCount == Get<int>(expected, "TriangleCount"), "Android navmesh discarded desktop walkable triangles.");
            Check(actual.EdgeCount == Get<int>(expected, "EdgeCount"), "Android step adjacency differs from desktop.");
        }

        internal void CompareSample(Vector3 position, float maxDistance)
        {
            bool actual = Scene.Navigation.SamplePosition(position, out Vector3 point, maxDistance);
            object?[] arguments = [position, default(Vector3), maxDistance];
            bool expected = (bool)_desktopNavigation.GetType().GetMethod("SamplePosition",
                [typeof(Vector3), typeof(Vector3).MakeByRefType(), typeof(float)])!.Invoke(_desktopNavigation, arguments)!;
            Check(actual == expected, $"Android SamplePosition availability differs at {position}.");
            if (actual) Near(point, (Vector3)arguments[1]!, "Android projected onto an infinite plane instead of the triangle.");
        }

        internal RuntimeNavigationPath ComparePath(Vector3 start, Vector3 end, float snapDistance)
        {
            bool actual = Scene.Navigation.TryFindPath(start, end, out RuntimeNavigationPath path, snapDistance);
            object?[] arguments = [start, end, null, snapDistance];
            bool expected = (bool)_desktopNavigation.GetType().GetMethod("TryFindPath")!.Invoke(_desktopNavigation, arguments)!;
            Check(actual == expected, $"Android route availability differs: {start} -> {end}.");
            if (actual)
            {
                IReadOnlyList<Vector3> points = Get<IReadOnlyList<Vector3>>(arguments[2]!, "Points");
                Check(path.Points.Count == points.Count, "Android added different navigation waypoints.");
                for (int index = 0; index < points.Count; index++) Near(path.Points[index], points[index], "Android waypoint differs from desktop.");
                Check(MathF.Abs(path.Length - Get<float>(arguments[2]!, "Length")) < .0001f, "Android path length differs from desktop.");
            }
            RuntimeNavigationDebugInfo debug = Scene.Navigation.LastDebugInfo;
            object desktopDebug = Get<object>(_desktopNavigation, "LastDebugInfo");
            foreach (PropertyInfo property in typeof(RuntimeNavigationDebugInfo).GetProperties())
                Check(Equals(property.GetValue(debug), desktopDebug.GetType().GetProperty(property.Name)!.GetValue(desktopDebug)),
                    $"Navigation diagnostic {property.Name} differs from desktop.");
            return path;
        }

        private object? Invoke(string method, params object[] arguments)
            => _desktopNavigation.GetType().GetMethod(method)!.Invoke(_desktopNavigation, arguments);

        private static T Get<T>(object owner, string property) => (T)owner.GetType().GetProperty(property)!.GetValue(owner)!;
        public void Dispose() => Scene.Dispose();
    }
}
