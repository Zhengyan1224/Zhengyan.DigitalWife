using System.Numerics;
using Zhengyan.DigitalWife.GamePlayer.Runtime;
using Zhengyan.DigitalWife.GameProjects;

namespace Zhengyan.DigitalWife.GamePlayer.Web.Runtime;

public sealed record WebRipple(float[] Center, float Age, float Radius, float Strength);
public sealed record WebParticleBatch(string Id, string TextureUri, string TexturePreset, string BlendMode,
    bool UseTextureColor, bool CastShadows, IReadOnlyList<WebParticleVertex> Vertices);
public sealed record WebParticleVertex(float[] Position, float[] Color, float[] Size, float Rotation, float[] Velocity, bool VelocityAligned);

public sealed class WebSceneSimulation
{
    private sealed class Particle
    {
        public Vector3 Position, Previous, Velocity;
        public float Age, Lifetime, Size, Rotation, RotationSpeed;
    }
    private sealed record Ripple(Vector3 Center, float Born, float Radius, float Strength, bool Particle);
    private readonly Dictionary<string, Particle[]> _particles = new();
    private readonly Dictionary<string, List<Ripple>> _ripples = new();
    private readonly Dictionary<string, float> _lastContact = new();
    private readonly Random _random = new(1729);
    public float Time { get; private set; }

    public void Update(RuntimeScene scene, float delta)
    {
        delta = Math.Clamp(delta, 0, .25f);
        Time += delta;
        foreach (RuntimeEntity entity in scene.ParticleSystems)
        {
            var settings = entity.Definition.Particle;
            int count = Math.Clamp(settings.ParticleCount, 0, 100000);
            if (!_particles.TryGetValue(entity.Id, out var particles) || particles.Length != count)
            {
                particles = Enumerable.Range(0, count).Select(_ => Spawn(entity, settings.RandomizeInitialAge)).ToArray();
                _particles[entity.Id] = particles;
            }
            float dt = delta * Math.Max(settings.SimulationSpeed, 0);
            foreach (var particle in particles)
            {
                particle.Previous = particle.Position;
                particle.Age += dt;
                if (particle.Age >= particle.Lifetime) { Reset(particle, entity, false); continue; }
                particle.Position += particle.Velocity * dt + settings.Acceleration.ToVector3() * (dt * dt * .5f);
                particle.Velocity += settings.Acceleration.ToVector3() * dt;
                particle.Rotation += particle.RotationSpeed * dt;
                if (settings.EnableColliderCollision)
                {
                    Vector3 movement = particle.Position - particle.Previous;
                    if (movement.LengthSquared() > 1e-8f && scene.Physics.Raycast(new(particle.Previous, movement), out var hit, movement.Length(), entity))
                    {
                        Vector3 normal = particle.Previous - hit.Point;
                        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
                        particle.Position = hit.Point + normal * .01f;
                        particle.Velocity = Vector3.Reflect(particle.Velocity, normal) * Math.Clamp(settings.CollisionBounce, 0, 1);
                        particle.Velocity *= Math.Clamp(settings.CollisionDamping, 0, 1);
                        if (settings.KillOnColliderContact) particle.Age = particle.Lifetime;
                    }
                }
            }
        }
        foreach (var water in scene.WaterSurfaces)
        {
            var settings = water.Definition.Water;
            if (!_ripples.TryGetValue(water.Id, out var ripples)) _ripples[water.Id] = ripples = [];
            ripples.RemoveAll(r => Time - r.Born >= Math.Max(.05f, settings.RippleLifetimeSeconds));
            if (!settings.EnableInteraction) continue;
            foreach (var entity in scene.Entities.Where(e => e.Id != water.Id && !e.IsParticleSystem))
            foreach (var collider in RuntimePhysics.CreateColliders(entity))
            {
                if (collider.Shape == "mesh") continue;
                Vector3 center = collider.Shape == "box" ? collider.Box.Center : collider.Capsule.Center;
                float radius = collider.Shape == "box" ? collider.Box.HalfExtents.Length()
                    : collider.Capsule.Radius + Vector3.Distance(collider.Capsule.Start, collider.Capsule.End) * .5f;
                if (TrySurfaceHeight(water, center, Time, out float height) && MathF.Abs(center.Y - height) <= radius)
                    Contact(water, $"{entity.Id}:{collider.Id}", new(center.X, height, center.Z), false, .35f);
            }
            foreach (var entity in scene.ParticleSystems.Where(e => e.Definition.Particle.EnableWaterInteraction))
            {
                if (!_particles.TryGetValue(entity.Id, out var particles)) continue;
                for (int i = 0; i < particles.Length; i++)
                {
                    var p = particles[i];
                    if (!TrySurfaceHeight(water, p.Position, Time, out float height)) continue;
                    float band = Math.Max(.01f, settings.InteractionRadius);
                    bool contact = MathF.Abs(p.Position.Y - height) <= band
                        || (p.Previous.Y - height) * (p.Position.Y - height) <= 0;
                    if (contact && p.Age < p.Lifetime)
                    {
                        Contact(water, $"{entity.Id}:{i}", new(p.Position.X, height, p.Position.Z), true, settings.ParticleRippleMinIntervalSeconds);
                        if (entity.Definition.Particle.KillOnWaterContact) p.Age = p.Lifetime;
                    }
                }
            }
        }
    }

    private void Contact(RuntimeEntity water, string id, Vector3 position, bool particle, float interval)
    {
        string key = water.Id + ":" + id;
        if (_lastContact.TryGetValue(key, out float last) && Time - last < Math.Max(interval, 0)) return;
        _lastContact[key] = Time;
        var settings = water.Definition.Water;
        var list = _ripples[water.Id];
        // Merge only recent particle contacts. Repeated rain must not keep
        // resetting one ripple's age, or its phase never propagates.
        if (particle && list.Any(r => r.Particle && Time - r.Born < interval
            && Vector3.DistanceSquared(r.Center, position) < settings.ParticleRippleMergeDistance * settings.ParticleRippleMergeDistance)) return;
        if (list.Count >= 48)
        {
            int oldestParticle=list.FindIndex(r=>r.Particle);
            if(particle && oldestParticle<0)return;
            list.RemoveAt(oldestParticle>=0?oldestParticle:0);
        }
        list.Add(new(position, Time, Math.Max(.01f, settings.InteractionRadius), Math.Clamp(settings.InteractionStrength, 0, 4), particle));
    }

    public IReadOnlyList<WebRipple> GetRipples(string id) => _ripples.TryGetValue(id, out var list)
        ? list.Select(r => new WebRipple([r.Center.X, r.Center.Y, r.Center.Z], Time - r.Born, r.Radius, r.Strength)).ToArray() : [];

    public IReadOnlyList<WebParticleBatch> GetParticles(RuntimeScene scene, Func<string?, string> texture)
        => scene.ParticleSystems.Where(entity=>entity.Visible).Select(entity =>
        {
            var s = entity.Definition.Particle;
            var vertices = (_particles.GetValueOrDefault(entity.Id) ?? []).Where(p => p.Age < p.Lifetime).Select(p =>
            {
                float t = Math.Clamp(p.Age / p.Lifetime, 0, 1);
                Vector4 color = Vector4.Lerp(s.StartColor.ToVector4(), s.EndColor.ToVector4(), t);
                color.W *= Math.Clamp(s.Opacity, 0, 1);
                float size = p.Size * float.Lerp(s.StartSizeScale, s.EndSizeScale, t);
                return new WebParticleVertex([p.Position.X, p.Position.Y, p.Position.Z], [color.X, color.Y, color.Z, color.W],
                    [size * s.WidthScale, size * s.HeightScale], p.Rotation,
                    [p.Velocity.X, p.Velocity.Y, p.Velocity.Z], s.OrientationMode.Equals("velocityAligned", StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            return new WebParticleBatch(entity.Id, texture(s.TexturePath), s.TexturePreset, s.BlendMode, s.UseTextureColor, s.CastShadows, vertices);
        }).ToArray();

    private Particle Spawn(RuntimeEntity entity, bool randomAge)
    {
        Particle particle = new(); Reset(particle, entity, randomAge); return particle;
    }
    private void Reset(Particle p, RuntimeEntity entity, bool randomAge)
    {
        var s = entity.Definition.Particle;
        float Between(float min, float max) => min + _random.NextSingle() * Math.Max(max - min, 0);
        Vector3 Jitter(Vector3 extent) => new((_random.NextSingle() * 2 - 1) * extent.X, (_random.NextSingle() * 2 - 1) * extent.Y, (_random.NextSingle() * 2 - 1) * extent.Z);
        p.Lifetime = Math.Max(.01f, Between(s.MinLifetime, s.MaxLifetime));
        p.Age = randomAge ? _random.NextSingle() * p.Lifetime : 0;
        p.Position = Vector3.Transform(Jitter(s.SpawnBoxHalfExtents.ToVector3()), entity.TransformMatrix);
        p.Velocity = Vector3.TransformNormal(s.BaseVelocity.ToVector3() + Jitter(s.VelocityJitter.ToVector3()), entity.TransformMatrix);
        p.Position += p.Velocity * p.Age + s.Acceleration.ToVector3() * (p.Age * p.Age * .5f);
        p.Velocity += s.Acceleration.ToVector3() * p.Age;
        p.Previous = p.Position;
        p.Size = Between(s.MinSize, s.MaxSize);
        p.Rotation = _random.NextSingle() * MathF.Tau;
        p.RotationSpeed = Between(s.MinRotationSpeedRadians, s.MaxRotationSpeedRadians);
    }

    public static bool TrySurfaceHeight(RuntimeEntity entity, Vector3 world, float time, out float height)
    {
        height = 0;
        if (!Matrix4x4.Invert(entity.TransformMatrix, out var inverse)) return false;
        Vector3 local = Vector3.Transform(world, inverse);
        float half = Math.Max(.1f, entity.Definition.Water.Size) * .5f;
        if (MathF.Abs(local.X) > half || MathF.Abs(local.Z) > half) return false;
        EvaluateWaves(entity.Definition.Water, local.X, local.Z, time, out var displacement, out _);
        height = Vector3.Transform(new Vector3(local.X, 0, local.Z) + displacement, entity.TransformMatrix).Y;
        return true;
    }

    public static void EvaluateWaves(WaterSurfaceSettings s, float x, float z, float time, out Vector3 displacement, out Vector3 normal)
    {
        displacement = Vector3.Zero; Vector2 gradient = Vector2.Zero;
        if (s.GerstnerWavesEnabled)
        for (int i = 0; i < Math.Clamp(s.GerstnerWaveCount, 1, 4); i++)
        {
            float angle = s.GerstnerDirectionDegrees * MathF.PI / 180 + (i - 1.5f) * .75f;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            float amplitude = Math.Max(s.GerstnerAmplitude, 0) * MathF.Pow(.55f, i);
            float k = MathF.Tau / Math.Max(.1f, s.GerstnerWavelength / (1 + i * .55f));
            float phase = k * (direction.X * x + direction.Y * z - Math.Max(s.GerstnerSpeed, 0) * (1 + i * .18f) * time);
            float horizontal = Math.Clamp(s.GerstnerSteepness, 0, 1) * amplitude * MathF.Cos(phase);
            displacement += new Vector3(direction.X * horizontal, amplitude * MathF.Sin(phase), direction.Y * horizontal);
            gradient += direction * amplitude * k * MathF.Cos(phase);
        }
        normal = Vector3.Normalize(new(-gradient.X, 1, -gradient.Y));
    }

    public static WebPmxMesh CreateWaterMesh(WaterSurfaceSettings s)
    {
        int n = Math.Clamp(s.GerstnerMeshResolution, 8, 256), count = (n + 1) * (n + 1);
        float size = Math.Max(.1f, s.Size);
        float[] positions = new float[count * 3], normals = new float[count * 3], uvs = new float[count * 2];
        uint[] indices = new uint[n * n * 6];
        for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
        {
            int v = z * (n + 1) + x;
            positions[v * 3] = (x / (float)n - .5f) * size;
            positions[v * 3 + 2] = (z / (float)n - .5f) * size;
            normals[v * 3 + 1] = 1;
            uvs[v * 2] = x / (float)n; uvs[v * 2 + 1] = z / (float)n;
            if (x < n && z < n)
            {
                int start = (z * n + x) * 6; uint a = (uint)v, b = a + 1, c = a + (uint)n + 1, d = c + 1;
                indices[start] = a; indices[start + 1] = c; indices[start + 2] = b;
                indices[start + 3] = b; indices[start + 4] = c; indices[start + 5] = d;
            }
        }
        return new(positions, normals, uvs, indices, new(-size * .5f, -s.GerstnerAmplitude * 2, -size * .5f),
            new(size * .5f, s.GerstnerAmplitude * 2, size * .5f), "Water",
            [new(0, indices.Length, new(s.DeepColor.X, s.DeepColor.Y, s.DeepColor.Z, Math.Clamp(s.Alpha, 0, 1)), Vector3.One, Vector4.Zero, 0, true, false, false, false, null, "Water")]);
    }
}
