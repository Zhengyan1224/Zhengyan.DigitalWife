extern alias MmdDesktop;

using System.Numerics;
using System.Text;
using Mmd = MmdDesktop::Zhengyan.DigitalWife.Mmd;
using Core = Zhengyan.DigitalWife.Mmd;

internal static class MmdIkLimitTests
{
    internal static void TestAxes()
    {
        Verify(new(-.1f, -.3f, 0), new(.2f, .35f, 0), Quaternion.CreateFromYawPitchRoll(1, -.7f, .4f));
        Quaternion oneSided = Verify(new(-.2f, 0, 0), new(.2f, .3f, 0), Quaternion.CreateFromYawPitchRoll(.8f, .1f, 0));
        Check(Angles(oneSided).Y > .05f, "A one-sided Y range was incorrectly treated as a locked axis.");
        Verify(new(-.2f, -.3f, .05f), new(.25f, .35f, .15f), Quaternion.CreateFromYawPitchRoll(.8f, -.6f, .4f));
        Verify(new(0, 0, 0), new(1.2f, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .8f));
        Verify(new(0, -1.2f, 0), new(0, 1.2f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .8f));
        Verify(new(0, 0, 0), new(0, 0, 1.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .8f), Vector3.UnitX);
        Verify(Vector3.Zero, Vector3.Zero, Quaternion.CreateFromYawPitchRoll(.8f, .4f, 0));
        Verify(new(-.1f, -.3f, 0), new(.2f, .35f, 0), Quaternion.CreateFromYawPitchRoll(1, -.7f, .4f),
            animatedRotation: Quaternion.CreateFromYawPitchRoll(.2f, .1f, .15f));
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 direction = axis == 2 ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 unit = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            foreach (float sign in new[] { -1f, 1f })
            {
                Quaternion goal = Quaternion.CreateFromAxisAngle(unit, .8f * sign);
                Quaternion actual = Verify(Vector3.Min(Vector3.Zero, unit * 1.2f * sign),
                    Vector3.Max(Vector3.Zero, unit * 1.2f * sign), goal, direction);
                Check(RotationDifference(actual, goal) < .01f, $"Single-axis IK did not reach its target on axis {axis}, sign {sign}.");
            }
        }
    }

    private static Quaternion Verify(Vector3 lower, Vector3 upper, Quaternion goal, Vector3? effectorDirection = null,
        Quaternion? animatedRotation = null)
    {
        using Mmd.FixedArray<Matrix4x4> globals = new(3);
        using Mmd.FixedArray<Matrix4x4> inverse = new(3);
        Mmd.PmxNode link = new(globals, inverse) { Index = 0, EnableIK = true, AnimRotate = animatedRotation ?? Quaternion.Identity };
        Vector3 direction = effectorDirection ?? Vector3.UnitZ;
        Mmd.PmxNode effector = new(globals, inverse) { Index = 1, Translate = direction };
        Mmd.PmxNode target = new(globals, inverse) { Index = 2, Translate = Vector3.Transform(direction, goal) };
        link.AddChild(effector);
        foreach (Mmd.PmxNode node in new[] { link, effector, target }) { node.SaveInitialTRS(); node.UpdateLocalTransform(); }
        link.UpdateGlobalTransform(); target.UpdateGlobalTransform();
        Mmd.MMDIkSolver solver = new() { IkNode = target, IkTarget = effector, IterateCount = 30, LimitAngle = 1 };
        solver.AddIkChain(link, true, lower, upper);
        solver.Solve();
        Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(link.Local));
        Vector3 angles = Angles(rotation);
        Check(float.IsFinite(angles.X) && float.IsFinite(angles.Y) && float.IsFinite(angles.Z), "IK produced a non-finite rotation.");
        Vector3 minimum = Vector3.Min(lower, upper), maximum = Vector3.Max(lower, upper);
        for (int axis = 0; axis < 3; axis++) Check(angles[axis] >= minimum[axis] - .0001f && angles[axis] <= maximum[axis] + .0001f,
            $"IK exceeded axis {axis}: {angles}, limits {minimum}..{maximum}");
        return rotation;
    }

    internal static void TestPmxLimitConversion()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mmd-ik-limits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "limits.pmx");
        bool oldOpenCl = Mmd.Kernel.UseOpenCL;
        Mmd.Kernel.UseOpenCL = false;
        try
        {
            foreach ((Vector3 lower, Vector3 upper, Vector3 axis, Vector3 direction) in new[]
            {
                (new Vector3(-.9f,0,0),new Vector3(-.2f,0,0),-Vector3.UnitX,Vector3.UnitZ),
                (new Vector3(0,-.9f,0),new Vector3(0,-.2f,0),-Vector3.UnitY,Vector3.UnitZ),
                (new Vector3(0,0,.2f),new Vector3(0,0,.9f),Vector3.UnitZ,Vector3.UnitX),
                (new Vector3(-.1f,-.1f,.2f),new Vector3(.1f,.1f,.9f),Vector3.UnitZ,Vector3.UnitX)
            })
            {
                Quaternion goal = Quaternion.CreateFromAxisAngle(axis, .6f);
                File.WriteAllBytes(path, CreatePmxLimitFixture(lower, upper, direction, Vector3.Transform(direction, goal)));
                Core.PmxParsing parsing = Core.PmxParsing.ParsingByFile(path)!;
                using Core.PmxPoseEvaluator reference = new(parsing, [], false, Vector3.Zero, true);
                reference.Update(0, 1, false, [], 0, skinVertices: false);
                using Mmd.PmxModel model = new();
                Check(model.Load(path, directory), "IK limit PMX fixture failed to load.");
                model.InitializeAnimation();
                model.BeginAnimation(); model.LoadBaseAnimation(); model.UpdateMorphAnimation();
                model.UpdateNodeAnimation(false); model.UpdateNodeAnimation(true); model.EndAnimation();
                Quaternion expected = new(-goal.X, -goal.Y, goal.Z, goal.W);
                Quaternion actual = Quaternion.CreateFromRotationMatrix(model.GetNodes()[0].Local);
                Quaternion web = Quaternion.CreateFromRotationMatrix(reference.GlobalPose[0]);
                Check(RotationDifference(actual, expected) < .01f, $"PC/Android PMX limit conversion failed for {lower}..{upper}.");
                Check(RotationDifference(web, expected) < .01f, $"Web PMX limit conversion failed for {lower}..{upper}.");
            }
        }
        finally
        {
            Mmd.Kernel.UseOpenCL = oldOpenCl;
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static byte[] CreatePmxLimitFixture(Vector3 lower, Vector3 upper, Vector3 effector, Vector3 target)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("PMX ")); writer.Write(2f);
        writer.Write(new byte[] { 8, 1, 0, 4, 4, 4, 4, 4, 4 });
        for (int i = 0; i < 4; i++) writer.Write(0); // model info strings
        writer.Write(1); // one BDEF1 vertex for the desktop skinning path
        WriteVector(Vector3.Zero); WriteVector(Vector3.UnitY);
        writer.Write(0f); writer.Write(0f); writer.Write((byte)0); writer.Write(0); writer.Write(1f);
        for (int i = 0; i < 3; i++) writer.Write(0); // indices, textures, materials
        writer.Write(3);
        WriteBone("link", Vector3.Zero, -1, false);
        WriteBone("effector", effector, 0, false);
        WriteBone("IK", target, -1, true);
        writer.Write(1); writer.Write(30); writer.Write(1f); // target index, iterations, step limit
        writer.Write(1); writer.Write(0); writer.Write(true); // one limited link at bone 0
        WriteVector(lower); WriteVector(upper);
        for (int i = 0; i < 4; i++) writer.Write(0); // morphs, display frames, rigid bodies, joints
        return stream.ToArray();

        void WriteBone(string name, Vector3 position, int parent, bool ik)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            writer.Write(bytes.Length); writer.Write(bytes); writer.Write(0);
            WriteVector(position); writer.Write(parent); writer.Write(ik ? 1 : 0);
            writer.Write((ushort)(Core.PmxBoneFlags.AllowRotate | (ik ? Core.PmxBoneFlags.IK : 0)));
            WriteVector(Vector3.Zero);
        }
        void WriteVector(Vector3 value)
        {
            writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z);
        }
    }

    internal static void TestNeneGaze()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string data = Path.Combine(root, "src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/web-game");
        string modelPath = Path.Combine(data, "assets/models/Nene_r2328_kihon_natsu_e511/Nene_r2328_kihon_natsu_e511.pmx");
        string motionPath = Path.Combine(data, "assets/motions/Lamb.vmd");
        Core.PmxParsing pmx = Core.PmxParsing.ParsingByFile(modelPath)!;
        Core.VmdParsing motion = Core.VmdParsing.ParsingByFile(motionPath)!;
        VerifyAdjacentKey(pmx, motion);
        bool oldOpenCl = Mmd.Kernel.UseOpenCL;
        Mmd.Kernel.UseOpenCL = false;
        try
        {
            using Mmd.PmxModel model = new();
            Check(model.Load(modelPath, Path.GetDirectoryName(modelPath)!), "Nene PMX failed to load.");
            model.InitializeAnimation();
            using Mmd.VmdAnimation animation = new();
            Check(animation.Load(motionPath, model), "Lamb VMD failed to load.");
            using Core.PmxPoseEvaluator reference = new(pmx, [(motion, 1)], false, new(0, -98, 0), true) { UsePartialIkUpdates = true };
            Mmd.MMDNode[] nodes = model.GetNodes();
            int gaze = Array.FindIndex(pmx.Bones, bone => bone.Name == "視線");
            int[] eyes = pmx.Bones.Select((bone, index) => (bone, index)).Where(item => item.bone.Name.Contains('目') || item.index == gaze)
                .Select(item => item.index).ToArray();
            SortedSet<float> samples = new();
            // Cover the whole motion, including the late adjacent head keys
            // that previously put the two solvers on different gaze limits.
            for (float frame = 0; frame <= animation.MaxKeyTime; frame += .5f) samples.Add(frame);
            samples.Add(animation.MaxKeyTime);
            foreach (var key in motion.Motions.Where(key => key.BoneName == "両目" || key.BoneName == "視線IK"))
            {
                samples.Add(key.Frame); if (key.Frame > .5f) samples.Add(key.Frame - .5f); samples.Add(key.Frame + .5f);
            }
            Check(gaze >= 0 && eyes.Length >= 7, "Nene gaze/eye chain is missing from the regression fixture.");
            int[] knees = pmx.Bones.Select((bone, index) => (bone, index))
                .Where(item => item.bone.Name is "左ひざ" or "右ひざ").Select(item => item.index).ToArray();
            Check(knees.Length == 2, "Nene knee chains are missing from the regression fixture.");
            float maximumError = 0;
            int[] legs = pmx.Bones.Select((bone, index) => (bone, index))
                .Where(item => item.bone.Name is "左足" or "右足" or "左ひざ" or "右ひざ" or "左足首" or "右足首")
                .Select(item => item.index).ToArray();
            foreach (float frame in samples)
            {
                model.BeginAnimation(); model.LoadBaseAnimation(); animation.ResetPlaybackCursor(); animation.Evaluate(frame);
                // PC and Android normalize the single motion layer's rotation
                // in PmxModelComponent.EvaluateMotionLayers before solving IK.
                foreach (Mmd.MMDNode node in nodes) node.AnimRotate = Quaternion.Normalize(node.AnimRotate);
                model.UpdateMorphAnimation(); model.UpdateNodeAnimation(false); model.UpdateNodeAnimation(true); model.EndAnimation();
                reference.SetMotionLayerFrame(0, frame); reference.Update(0, 1, false, [], 0, skinVertices: false);
                Vector3 angles = Angles(Quaternion.CreateFromRotationMatrix(nodes[gaze].Local));
                Check(angles.X >= -8 * MathF.PI / 180 - .0001f && angles.X <= 16 * MathF.PI / 180 + .0001f
                    && MathF.Abs(angles.Y) <= 30 * MathF.PI / 180 + .0001f && MathF.Abs(angles.Z) < .0001f, $"Gaze limit failed at frame {frame}: {angles}");
                foreach (int index in eyes)
                {
                    Matrix4x4 local = reference.GlobalPose[index];
                    int parent = pmx.Bones[index].ParentBoneIndex;
                    if (parent >= 0 && Matrix4x4.Invert(reference.GlobalPose[parent], out Matrix4x4 parentInverse)) local *= parentInverse;
                    float error = RotationDifference(nodes[index].Local, local);
                    maximumError = MathF.Max(maximumError, error);
                    // The two solvers have different convergence tolerances;
                    // allow a small numerical difference, while catching the
                    // original tens-of-degrees gaze deviation.
                    Check(float.IsFinite(error) && error < .3f, $"Eye rotation differs from Web at frame {frame}, {nodes[index].Name}: {error} degrees");
                }
                foreach (int index in knees)
                {
                    Quaternion knee = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(nodes[index].Local));
                    if (knee.W < 0) knee = -knee;
                    Check(float.IsFinite(knee.X) && knee.X >= -.0001f && MathF.Abs(knee.Y) < .0001f && MathF.Abs(knee.Z) < .0001f,
                        $"Knee left its positive X plane at frame {frame}, {nodes[index].Name}: {knee}");
                }
                foreach (int index in legs)
                {
                    Quaternion rotation = Quaternion.CreateFromRotationMatrix(nodes[index].Local);
                    Vector3 position = nodes[index].Global.Translation;
                    Check(float.IsFinite(rotation.LengthSquared()) && float.IsFinite(position.LengthSquared()),
                        $"Leg/foot IK was non-finite at frame {frame}, {nodes[index].Name}.");
                }
            }
            Console.WriteLine($"Nene gaze: {samples.Count} timeline samples, maximum eye rotation error {maximumError:G6} degrees");
        }
        finally { Mmd.Kernel.UseOpenCL = oldOpenCl; }
    }

    private static float RotationDifference(Matrix4x4 a, Matrix4x4 b)
    {
        return RotationDifference(Quaternion.CreateFromRotationMatrix(a), Quaternion.CreateFromRotationMatrix(b));
    }

    private static float RotationDifference(Quaternion a, Quaternion b)
    {
        Quaternion difference = Quaternion.Normalize(Quaternion.Normalize(a) * Quaternion.Conjugate(Quaternion.Normalize(b)));
        return 2 * MathF.Atan2(new Vector3(difference.X, difference.Y, difference.Z).Length(), MathF.Abs(difference.W)) * 180 / MathF.PI;
    }

    private static void VerifyAdjacentKey(Core.PmxParsing pmx, Core.VmdParsing motion)
    {
        int head = Array.FindIndex(pmx.Bones, bone => bone.Name == "頭");
        Core.VmdMotion[] keys = motion.Motions.Where(key => key.BoneName == "頭").OrderBy(key => key.Frame).ToArray();
        int upper = Enumerable.Range(1, keys.Length - 1).First(index => keys[index].Frame - keys[index - 1].Frame == 1);
        using Core.PmxPoseEvaluator pose = new(pmx, [(motion, 1)], false, Vector3.Zero, true) { UsePartialIkUpdates = true };
        foreach ((float frame, Core.VmdMotion key) in new[] { (keys[upper - 1].Frame + .5f, keys[upper - 1]), ((float)keys[upper].Frame, keys[upper]) })
        {
            pose.SetMotionLayerFrame(0, frame); pose.Update(0, 1, false, [], 0, skinVertices: false);
            Matrix4x4 local = pose.GlobalPose[head];
            Matrix4x4.Invert(pose.GlobalPose[pmx.Bones[head].ParentBoneIndex], out Matrix4x4 inverse);
            local *= inverse;
            Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(local));
            Quaternion expected = Quaternion.Normalize(new(-key.Quaternion.X, -key.Quaternion.Y, key.Quaternion.Z, key.Quaternion.W));
            Check(MathF.Abs(Quaternion.Dot(rotation, expected)) > .99999f, $"Adjacent VMD key was sampled incorrectly at frame {frame}.");
        }
    }
    private static Vector3 Angles(Quaternion rotation)
    {
        Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));
        float pitch = MathF.Asin(Math.Clamp(-matrix.M32, -1, 1));
        return MathF.Abs(MathF.Cos(pitch)) > 1e-5f ? new(pitch, MathF.Atan2(matrix.M31, matrix.M33), MathF.Atan2(matrix.M12, matrix.M22))
            : new(pitch, MathF.Atan2(-matrix.M13, matrix.M11), 0);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
