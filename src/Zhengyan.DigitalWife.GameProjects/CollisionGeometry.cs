using System.Numerics;

namespace Zhengyan.DigitalWife.GameProjects;

public readonly record struct CapsuleGeometry(Vector3 Start, Vector3 End, float Radius)
{
    public Vector3 Center => (Start + End) * 0.5f;
}

public readonly record struct BoxGeometry(Vector3 Center, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, Vector3 HalfExtents);

public readonly record struct ColliderGeometry(
    string Id,
    string Name,
    string Shape,
    CapsuleGeometry Capsule,
    BoxGeometry Box);

public static class CollisionGeometry
{
    public static CapsuleGeometry CreateCapsule(
        CollisionSettings settings,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        return CreateCapsule(new ColliderSettings
        {
            Enabled = settings.Enabled,
            Shape = settings.Shape,
            Position = settings.Center,
            Radius = settings.Radius,
            Height = settings.Height,
            Axis = settings.Axis
        }, position, rotation, scale);
    }

    public static CapsuleGeometry CreateCapsule(
        ColliderSettings settings,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        Quaternion localRotation = ToQuaternion(settings.RotationDegrees.ToVector3());
        Quaternion worldRotation = Quaternion.Normalize(localRotation * rotation);
        Vector3 center = TransformPoint(settings.Position.ToVector3(), position, rotation, scale);
        Vector3 axis = TransformDirection(ResolveAxis(settings.Axis), worldRotation, scale);
        float axisScale = MathF.Max(axis.Length(), 0.0001f);
        Vector3 axisDirection = Vector3.Normalize(axis);
        float height = MathF.Max(0.0f, settings.Height * axisScale);
        float radiusScale = ResolveRadiusScale(settings.Axis, scale);
        float radius = MathF.Max(0.0001f, settings.Radius * radiusScale);
        float halfSegment = MathF.Max(0.0f, (height * 0.5f) - radius);

        return new CapsuleGeometry(
            center - (axisDirection * halfSegment),
            center + (axisDirection * halfSegment),
            radius);
    }

    public static BoxGeometry CreateBox(
        ColliderSettings settings,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        Quaternion localRotation = ToQuaternion(settings.RotationDegrees.ToVector3());
        Quaternion worldRotation = Quaternion.Normalize(localRotation * rotation);
        Vector3 center = TransformPoint(settings.Position.ToVector3(), position, rotation, scale);
        Vector3 size = settings.Size.ToVector3();
        Vector3 halfExtents = new(
            MathF.Max(0.0001f, MathF.Abs(size.X * scale.X) * 0.5f),
            MathF.Max(0.0001f, MathF.Abs(size.Y * scale.Y) * 0.5f),
            MathF.Max(0.0001f, MathF.Abs(size.Z * scale.Z) * 0.5f));

        return new BoxGeometry(
            center,
            SafeNormalize(Vector3.Transform(Vector3.UnitX, worldRotation), Vector3.UnitX),
            SafeNormalize(Vector3.Transform(Vector3.UnitY, worldRotation), Vector3.UnitY),
            SafeNormalize(Vector3.Transform(Vector3.UnitZ, worldRotation), Vector3.UnitZ),
            halfExtents);
    }

    public static ColliderGeometry CreateCollider(
        ColliderSettings settings,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        string shape = NormalizeShape(settings.Shape);
        return shape == "box"
            ? new ColliderGeometry(settings.Id, settings.Name, shape, default, CreateBox(settings, position, rotation, scale))
            : new ColliderGeometry(settings.Id, settings.Name, "capsule", CreateCapsule(settings, position, rotation, scale), default);
    }

    public static bool TryRaycastCollider(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        ColliderGeometry collider,
        out float distance,
        out Vector3 point)
    {
        return collider.Shape == "box"
            ? TryRaycastBox(rayOrigin, rayDirection, collider.Box, out distance, out point)
            : TryRaycastCapsule(rayOrigin, rayDirection, collider.Capsule, out distance, out point);
    }

    public static bool CheckColliderCollision(ColliderGeometry left, ColliderGeometry right)
    {
        if (left.Shape == "box" && right.Shape == "box")
        {
            return CheckBoxCollision(left.Box, right.Box);
        }

        if (left.Shape == "box")
        {
            return CheckCapsuleBoxCollision(right.Capsule, left.Box);
        }

        if (right.Shape == "box")
        {
            return CheckCapsuleBoxCollision(left.Capsule, right.Box);
        }

        return CheckCapsuleCollision(left.Capsule, right.Capsule);
    }

    public static float DistanceBetweenColliders(ColliderGeometry left, ColliderGeometry right)
    {
        if (CheckColliderCollision(left, right))
        {
            return 0.0f;
        }

        if (left.Shape != "box" && right.Shape != "box")
        {
            return DistanceBetweenCapsules(left.Capsule, right.Capsule);
        }

        float leftRadius = left.Shape == "box" ? left.Box.HalfExtents.Length() : left.Capsule.Radius;
        float rightRadius = right.Shape == "box" ? right.Box.HalfExtents.Length() : right.Capsule.Radius;
        Vector3 leftCenter = left.Shape == "box" ? left.Box.Center : left.Capsule.Center;
        Vector3 rightCenter = right.Shape == "box" ? right.Box.Center : right.Capsule.Center;
        return MathF.Max(0.0f, Vector3.Distance(leftCenter, rightCenter) - leftRadius - rightRadius);
    }

    public static bool TryRaycastCapsule(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        CapsuleGeometry capsule,
        out float distance,
        out Vector3 point)
    {
        Vector3 direction = SafeNormalize(rayDirection, -Vector3.UnitZ);
        float closestDistance = ClosestDistanceRaySegment(rayOrigin, direction, capsule.Start, capsule.End, out float rayT);
        distance = rayT;
        point = rayOrigin + (direction * distance);
        return rayT >= 0.0f && closestDistance <= capsule.Radius;
    }

    public static bool TryRaycastBox(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        BoxGeometry box,
        out float distance,
        out Vector3 point)
    {
        Vector3 direction = SafeNormalize(rayDirection, -Vector3.UnitZ);
        Vector3 localOrigin = ToBoxLocal(rayOrigin, box);
        Vector3 localDirection = ToBoxLocalDirection(direction, box);

        if (!TryRaycastAabb(localOrigin, localDirection, -box.HalfExtents, box.HalfExtents, out distance))
        {
            point = default;
            return false;
        }

        point = rayOrigin + (direction * distance);
        return true;
    }

    public static bool CheckCapsuleCollision(CapsuleGeometry left, CapsuleGeometry right)
    {
        float distance = ClosestDistanceSegmentSegment(left.Start, left.End, right.Start, right.End);
        return distance <= left.Radius + right.Radius;
    }

    public static float DistanceBetweenCapsules(CapsuleGeometry left, CapsuleGeometry right)
    {
        float distance = ClosestDistanceSegmentSegment(left.Start, left.End, right.Start, right.End);
        return MathF.Max(0.0f, distance - left.Radius - right.Radius);
    }

    public static bool CheckCapsuleBoxCollision(CapsuleGeometry capsule, BoxGeometry box)
    {
        Vector3 start = ToBoxLocal(capsule.Start, box);
        Vector3 end = ToBoxLocal(capsule.End, box);
        Vector3 expanded = box.HalfExtents + new Vector3(capsule.Radius);
        return TryIntersectSegmentAabb(start, end, -expanded, expanded);
    }

    public static bool CheckBoxCollision(BoxGeometry left, BoxGeometry right)
    {
        Vector3[] leftAxes = [left.AxisX, left.AxisY, left.AxisZ];
        Vector3[] rightAxes = [right.AxisX, right.AxisY, right.AxisZ];
        float[] leftExtents = [left.HalfExtents.X, left.HalfExtents.Y, left.HalfExtents.Z];
        float[] rightExtents = [right.HalfExtents.X, right.HalfExtents.Y, right.HalfExtents.Z];
        float[,] rotation = new float[3, 3];
        float[,] absRotation = new float[3, 3];
        const float epsilon = 0.00001f;

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                rotation[i, j] = Vector3.Dot(leftAxes[i], rightAxes[j]);
                absRotation[i, j] = MathF.Abs(rotation[i, j]) + epsilon;
            }
        }

        Vector3 delta = right.Center - left.Center;
        float[] t =
        [
            Vector3.Dot(delta, leftAxes[0]),
            Vector3.Dot(delta, leftAxes[1]),
            Vector3.Dot(delta, leftAxes[2])
        ];

        for (int i = 0; i < 3; i++)
        {
            float ra = leftExtents[i];
            float rb = (rightExtents[0] * absRotation[i, 0]) + (rightExtents[1] * absRotation[i, 1]) + (rightExtents[2] * absRotation[i, 2]);
            if (MathF.Abs(t[i]) > ra + rb)
            {
                return false;
            }
        }

        for (int j = 0; j < 3; j++)
        {
            float ra = (leftExtents[0] * absRotation[0, j]) + (leftExtents[1] * absRotation[1, j]) + (leftExtents[2] * absRotation[2, j]);
            float rb = rightExtents[j];
            float projection = MathF.Abs((t[0] * rotation[0, j]) + (t[1] * rotation[1, j]) + (t[2] * rotation[2, j]));
            if (projection > ra + rb)
            {
                return false;
            }
        }

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                float ra = (leftExtents[(i + 1) % 3] * absRotation[(i + 2) % 3, j])
                    + (leftExtents[(i + 2) % 3] * absRotation[(i + 1) % 3, j]);
                float rb = (rightExtents[(j + 1) % 3] * absRotation[i, (j + 2) % 3])
                    + (rightExtents[(j + 2) % 3] * absRotation[i, (j + 1) % 3]);
                float projection = MathF.Abs((t[(i + 2) % 3] * rotation[(i + 1) % 3, j])
                    - (t[(i + 1) % 3] * rotation[(i + 2) % 3, j]));
                if (projection > ra + rb)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static ColliderSettings FromLegacy(CollisionSettings settings)
    {
        return new ColliderSettings
        {
            Name = "Capsule Collider",
            Enabled = settings.Enabled,
            Shape = "capsule",
            Position = settings.Center,
            Radius = settings.Radius,
            Height = settings.Height,
            Axis = settings.Axis
        };
    }

    private static bool TryRaycastAabb(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float distance)
    {
        distance = 0.0f;
        float tMin = 0.0f;
        float tMax = float.MaxValue;

        if (!TrySlab(origin.X, direction.X, min.X, max.X, ref tMin, ref tMax)
            || !TrySlab(origin.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax)
            || !TrySlab(origin.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax))
        {
            return false;
        }

        distance = tMin;
        return true;
    }

    private static bool TryIntersectSegmentAabb(Vector3 start, Vector3 end, Vector3 min, Vector3 max)
    {
        Vector3 direction = end - start;
        float tMin = 0.0f;
        float tMax = 1.0f;

        return TrySlab(start.X, direction.X, min.X, max.X, ref tMin, ref tMax)
            && TrySlab(start.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax)
            && TrySlab(start.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax);
    }

    private static bool TrySlab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) < 0.000001f)
        {
            return origin >= min && origin <= max;
        }

        float inverse = 1.0f / direction;
        float t1 = (min - origin) * inverse;
        float t2 = (max - origin) * inverse;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }

    private static Vector3 ToBoxLocal(Vector3 point, BoxGeometry box)
    {
        Vector3 delta = point - box.Center;
        return new Vector3(
            Vector3.Dot(delta, box.AxisX),
            Vector3.Dot(delta, box.AxisY),
            Vector3.Dot(delta, box.AxisZ));
    }

    private static Vector3 ToBoxLocalDirection(Vector3 direction, BoxGeometry box)
    {
        return new Vector3(
            Vector3.Dot(direction, box.AxisX),
            Vector3.Dot(direction, box.AxisY),
            Vector3.Dot(direction, box.AxisZ));
    }

    private static Vector3 TransformPoint(Vector3 point, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        return Vector3.Transform(point * scale, rotation) + position;
    }

    private static Vector3 TransformDirection(Vector3 direction, Quaternion rotation, Vector3 scale)
    {
        return Vector3.Transform(direction * scale, rotation);
    }

    private static Vector3 ResolveAxis(string axis)
    {
        return (axis ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "x" => Vector3.UnitX,
            "z" => Vector3.UnitZ,
            _ => Vector3.UnitY
        };
    }

    private static float ResolveRadiusScale(string axis, Vector3 scale)
    {
        string normalized = (axis ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "x" => MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)),
            "z" => MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y)),
            _ => MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Z))
        };
    }

    private static string NormalizeShape(string shape)
    {
        string normalized = (shape ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "box" or "cube" or "aabb" ? "box" : "capsule";
    }

    private static float ClosestDistanceRaySegment(Vector3 rayOrigin, Vector3 rayDirection, Vector3 segmentStart, Vector3 segmentEnd, out float rayT)
    {
        Vector3 u = rayDirection;
        Vector3 v = segmentEnd - segmentStart;
        Vector3 w = rayOrigin - segmentStart;
        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float denominator = (a * c) - (b * b);

        float s;
        float t;
        if (denominator < 0.000001f)
        {
            s = 0.0f;
            t = c > 0.000001f ? Math.Clamp(e / c, 0.0f, 1.0f) : 0.0f;
        }
        else
        {
            s = ((b * e) - (c * d)) / denominator;
            t = ((a * e) - (b * d)) / denominator;

            if (s < 0.0f)
            {
                s = 0.0f;
                t = c > 0.000001f ? Math.Clamp(e / c, 0.0f, 1.0f) : 0.0f;
            }
            else if (t < 0.0f)
            {
                t = 0.0f;
                s = Math.Max(0.0f, -d / a);
            }
            else if (t > 1.0f)
            {
                t = 1.0f;
                s = Math.Max(0.0f, (b - d) / a);
            }
        }

        rayT = Math.Max(0.0f, s);
        Vector3 rayPoint = rayOrigin + (u * rayT);
        Vector3 segmentPoint = segmentStart + (v * t);
        return Vector3.Distance(rayPoint, segmentPoint);
    }

    private static float ClosestDistanceSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        float s;
        float t;

        if (a <= 0.000001f && e <= 0.000001f)
        {
            return Vector3.Distance(p1, p2);
        }

        if (a <= 0.000001f)
        {
            s = 0.0f;
            t = Math.Clamp(f / e, 0.0f, 1.0f);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 0.000001f)
            {
                t = 0.0f;
                s = Math.Clamp(-c / a, 0.0f, 1.0f);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = (a * e) - (b * b);
                s = denominator != 0.0f ? Math.Clamp(((b * f) - (c * e)) / denominator, 0.0f, 1.0f) : 0.0f;
                t = (b * s + f) / e;

                if (t < 0.0f)
                {
                    t = 0.0f;
                    s = Math.Clamp(-c / a, 0.0f, 1.0f);
                }
                else if (t > 1.0f)
                {
                    t = 1.0f;
                    s = Math.Clamp((b - c) / a, 0.0f, 1.0f);
                }
            }
        }

        Vector3 c1 = p1 + (d1 * s);
        Vector3 c2 = p2 + (d2 * t);
        return Vector3.Distance(c1, c2);
    }

    private static Quaternion ToQuaternion(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180.0f);
        return Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() <= 0.000001f ? fallback : Vector3.Normalize(value);
    }
}
