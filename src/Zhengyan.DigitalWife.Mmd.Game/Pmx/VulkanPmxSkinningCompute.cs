using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

/// <summary>Vulkan compute implementation of the PMX CPU/OpenCL skinning contract.</summary>
internal sealed unsafe class VulkanPmxSkinningCompute : Zhengyan.DigitalWife.Mmd.IPmxSkinningCompute
{
    private const uint WorkgroupSize = 64;

    private readonly VulkanRenderer _renderer;
    private readonly int _vertexCount;
    private readonly int _boneCount;
    private readonly DeviceBuffer _parameters;
    private readonly DeviceBuffer _vertexInputs;
    private readonly DeviceBuffer _boneInputs;
    private readonly DeviceBuffer _transforms;
    private readonly DeviceBuffer _outputs;
    private readonly DeviceBuffer _outputStaging;
    private readonly ResourceLayout _layout;
    private readonly ResourceSet _resourceSet;
    private readonly Shader _shader;
    private readonly Pipeline _pipeline;
    private readonly CommandList _commands;
    private readonly Fence _fence;
    private readonly VertexInputGpu[] _vertexInputData;
    private readonly BoneInputGpu[] _boneInputData;
    private readonly TransformInputGpu[] _transformData;
    private bool _submissionPending;
    private bool _disposed;

    public VulkanPmxSkinningCompute(VulkanRenderer renderer, int vertexCount, int boneCount)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _vertexCount = Math.Max(vertexCount, 1);
        _boneCount = Math.Max(boneCount, 1);
        ResourceFactory factory = renderer.ResourceFactory;

        _vertexInputData = new VertexInputGpu[_vertexCount];
        _boneInputData = new BoneInputGpu[_vertexCount];
        _transformData = new TransformInputGpu[_boneCount];

        _parameters = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _vertexInputs = CreateStructuredBuffer<VertexInputGpu>(factory, _vertexCount, BufferUsage.StructuredBufferReadOnly);
        _boneInputs = CreateStructuredBuffer<BoneInputGpu>(factory, _vertexCount, BufferUsage.StructuredBufferReadOnly);
        _transforms = CreateStructuredBuffer<TransformInputGpu>(factory, _boneCount, BufferUsage.StructuredBufferReadOnly);
        _outputs = CreateStructuredBuffer<OutputGpu>(factory, _vertexCount, BufferUsage.StructuredBufferReadWrite);
        _outputStaging = factory.CreateBuffer(new BufferDescription(
            checked((uint)(_vertexCount * Marshal.SizeOf<OutputGpu>())), BufferUsage.Staging));

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SkinningParameters", ResourceKind.UniformBuffer, ShaderStages.Compute),
            new ResourceLayoutElementDescription("VertexInputs", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("BoneInputs", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("Transforms", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("Outputs", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));
        _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            _layout, _parameters, _vertexInputs, _boneInputs, _transforms, _outputs));

        ShaderDescription shaderDescription = VulkanShaderCompiler.CompileSource(
            "pmx_skinning.comp", ComputeShaderSource, ShaderStages.Compute);
        _shader = factory.CreateFromSpirv(shaderDescription);
        _pipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
            _shader, _layout, WorkgroupSize, 1, 1));
        _commands = factory.CreateCommandList();
        _fence = factory.CreateFence(false);
    }

    public string BackendName => "Vulkan Compute";

    public bool Execute(
        int vertexCount,
        int boneCount,
        Vector3* positions,
        Vector3* normals,
        Vector2* uvs,
        Zhengyan.DigitalWife.Mmd.VertexBoneInfo* vertexBoneInfos,
        Vector3* morphPositions,
        Vector4* morphUVs,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms,
        Vector3* updatePositions,
        Vector3* updateNormals,
        Vector2* updateUVs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (vertexCount != _vertexCount || boneCount != _boneCount)
        {
            return false;
        }

        try
        {
            for (int i = 0; i < vertexCount; i++)
            {
                _vertexInputData[i] = new VertexInputGpu
                {
                    Position = new Vector4(positions[i], 1.0f),
                    Normal = new Vector4(normals[i], 0.0f),
                    MorphPosition = new Vector4(morphPositions[i], 0.0f),
                    Uv = new Vector4(uvs[i], 0.0f, 0.0f),
                    MorphUv = morphUVs[i]
                };

                Zhengyan.DigitalWife.Mmd.VertexBoneInfo info = vertexBoneInfos[i];
                _boneInputData[i] = new BoneInputGpu
                {
                    BoneIndices = new Vector4(info.BoneIndices[0], info.BoneIndices[1], info.BoneIndices[2], info.BoneIndices[3]),
                    BoneWeights = new Vector4(info.BoneWeights[0], info.BoneWeights[1], info.BoneWeights[2], info.BoneWeights[3]),
                    SdefIndicesAndType = new Vector4(
                        info.SDEF.BoneIndices[0], info.SDEF.BoneIndices[1], (int)info.SkinningType, 0.0f),
                    SdefWeightAndCenterXyz = new Vector4(info.SDEF.BoneWeight, info.SDEF.C.X, info.SDEF.C.Y, info.SDEF.C.Z),
                    SdefR0 = new Vector4(info.SDEF.R0, 0.0f),
                    SdefR1 = new Vector4(info.SDEF.R1, 0.0f)
                };
            }

            for (int i = 0; i < boneCount; i++)
            {
                _transformData[i] = new TransformInputGpu
                {
                    Update = updateTransforms[i],
                    Global = globalTransforms[i]
                };
            }

            _renderer.Device.UpdateBuffer(_parameters, 0, new Vector4(vertexCount, boneCount, 0.0f, 0.0f));
            _renderer.Device.UpdateBuffer(_vertexInputs, 0, _vertexInputData);
            _renderer.Device.UpdateBuffer(_boneInputs, 0, _boneInputData);
            _renderer.Device.UpdateBuffer(_transforms, 0, _transformData);

            _commands.Begin();
            _commands.SetPipeline(_pipeline);
            _commands.SetComputeResourceSet(0, _resourceSet);
            _commands.Dispatch((uint)((vertexCount + WorkgroupSize - 1) / WorkgroupSize), 1, 1);
            _commands.CopyBuffer(_outputs, 0, _outputStaging, 0, _outputStaging.SizeInBytes);
            _commands.End();

            _renderer.Device.ResetFence(_fence);
            _renderer.Device.SubmitCommands(_commands, _fence);
            _submissionPending = true;
            _renderer.Device.WaitForFence(_fence);
            _submissionPending = false;

            MappedResource mapped = _renderer.Device.Map(_outputStaging, MapMode.Read);
            try
            {
                OutputGpu* results = (OutputGpu*)mapped.Data.ToPointer();
                for (int i = 0; i < vertexCount; i++)
                {
                    updatePositions[i] = new Vector3(results[i].Position.X, results[i].Position.Y, results[i].Position.Z);
                    updateNormals[i] = new Vector3(results[i].Normal.X, results[i].Normal.Y, results[i].Normal.Z);
                    updateUVs[i] = new Vector2(results[i].Uv.X, results[i].Uv.Y);
                }
            }
            finally
            {
                _renderer.Device.Unmap(_outputStaging);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Vulkan Compute skinning failed; falling back to CPU: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_submissionPending)
        {
            _renderer.Device.WaitForFence(_fence);
            _submissionPending = false;
        }
        _fence.Dispose();
        _commands.Dispose();
        _pipeline.Dispose();
        _shader.Dispose();
        _resourceSet.Dispose();
        _layout.Dispose();
        _outputStaging.Dispose();
        _outputs.Dispose();
        _transforms.Dispose();
        _boneInputs.Dispose();
        _vertexInputs.Dispose();
        _parameters.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DeviceBuffer CreateStructuredBuffer<T>(ResourceFactory factory, int count, BufferUsage usage)
        where T : unmanaged
    {
        uint stride = (uint)Marshal.SizeOf<T>();
        return factory.CreateBuffer(new BufferDescription(checked((uint)count * stride), usage, stride));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexInputGpu
    {
        public Vector4 Position;
        public Vector4 Normal;
        public Vector4 MorphPosition;
        public Vector4 Uv;
        public Vector4 MorphUv;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoneInputGpu
    {
        public Vector4 BoneIndices;
        public Vector4 BoneWeights;
        public Vector4 SdefIndicesAndType;
        public Vector4 SdefWeightAndCenterXyz;
        public Vector4 SdefR0;
        public Vector4 SdefR1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformInputGpu
    {
        public Matrix4x4 Update;
        public Matrix4x4 Global;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputGpu
    {
        public Vector4 Position;
        public Vector4 Normal;
        public Vector4 Uv;
    }

    private const string ComputeShaderSource = """
        layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

        layout(set = 0, binding = 0, std140) uniform SkinningParameters
        {
            vec4 Counts;
        } u_Parameters;

        struct VertexInput
        {
            vec4 Position;
            vec4 Normal;
            vec4 MorphPosition;
            vec4 Uv;
            vec4 MorphUv;
        };

        struct BoneInput
        {
            vec4 BoneIndices;
            vec4 BoneWeights;
            vec4 SdefIndicesAndType;
            vec4 SdefWeightAndCenterXyz;
            vec4 SdefR0;
            vec4 SdefR1;
        };

        struct TransformInput
        {
            mat4 Update;
            mat4 Global;
        };

        struct OutputData
        {
            vec4 Position;
            vec4 Normal;
            vec4 Uv;
        };

        layout(set = 0, binding = 1, std430) readonly buffer VertexInputs { VertexInput Values[]; } b_Vertices;
        layout(set = 0, binding = 2, std430) readonly buffer BoneInputs { BoneInput Values[]; } b_Bones;
        layout(set = 0, binding = 3, std430) readonly buffer Transforms { TransformInput Values[]; } b_Transforms;
        layout(set = 0, binding = 4, std430) writeonly buffer Outputs { OutputData Values[]; } b_Outputs;

        vec4 QuaternionFromMatrix(mat4 m)
        {
            vec4 q;
            float trace = m[0][0] + m[1][1] + m[2][2];
            if (trace > 0.0)
            {
                float s = sqrt(trace + 1.0) * 2.0;
                q = vec4((m[1][2] - m[2][1]) / s, (m[2][0] - m[0][2]) / s, (m[0][1] - m[1][0]) / s, 0.25 * s);
            }
            else if (m[0][0] > m[1][1] && m[0][0] > m[2][2])
            {
                float s = sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2.0;
                q = vec4(0.25 * s, (m[1][0] + m[0][1]) / s, (m[2][0] + m[0][2]) / s, (m[1][2] - m[2][1]) / s);
            }
            else if (m[1][1] > m[2][2])
            {
                float s = sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2.0;
                q = vec4((m[1][0] + m[0][1]) / s, 0.25 * s, (m[2][1] + m[1][2]) / s, (m[2][0] - m[0][2]) / s);
            }
            else
            {
                float s = sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2.0;
                q = vec4((m[2][0] + m[0][2]) / s, (m[2][1] + m[1][2]) / s, 0.25 * s, (m[0][1] - m[1][0]) / s);
            }
            return normalize(q);
        }

        vec4 QuaternionSlerp(vec4 a, vec4 b, float amount)
        {
            float cosine = dot(a, b);
            if (cosine < 0.0) { b = -b; cosine = -cosine; }
            if (cosine > 0.9995) return normalize(mix(a, b, amount));
            float angle = acos(clamp(cosine, -1.0, 1.0));
            return normalize((sin((1.0 - amount) * angle) * a + sin(amount * angle) * b) / sin(angle));
        }

        mat4 MatrixFromQuaternion(vec4 q)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
            float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
            return mat4(
                1.0 - 2.0 * (yy + zz), 2.0 * (xy + wz), 2.0 * (xz - wy), 0.0,
                2.0 * (xy - wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz + wx), 0.0,
                2.0 * (xz + wy), 2.0 * (yz - wx), 1.0 - 2.0 * (xx + yy), 0.0,
                0.0, 0.0, 0.0, 1.0);
        }

        void main()
        {
            uint index = gl_GlobalInvocationID.x;
            if (index >= uint(u_Parameters.Counts.x)) return;

            VertexInput vertex = b_Vertices.Values[index];
            BoneInput bone = b_Bones.Values[index];
            int skinningType = int(bone.SdefIndicesAndType.z);
            ivec4 indices = ivec4(bone.BoneIndices);
            mat4 skin = mat4(1.0);

            if (skinningType == 0) skin = b_Transforms.Values[indices.x].Update;
            else if (skinningType == 1) skin = b_Transforms.Values[indices.x].Update * bone.BoneWeights.x + b_Transforms.Values[indices.y].Update * bone.BoneWeights.y;
            else if (skinningType == 2)
            {
                skin = b_Transforms.Values[indices.x].Update * bone.BoneWeights.x
                    + b_Transforms.Values[indices.y].Update * bone.BoneWeights.y
                    + b_Transforms.Values[indices.z].Update * bone.BoneWeights.z
                    + b_Transforms.Values[indices.w].Update * bone.BoneWeights.w;
            }

            vec3 position = vertex.Position.xyz + vertex.MorphPosition.xyz;
            vec3 outputPosition;
            vec3 outputNormal;
            if (skinningType == 3)
            {
                int i0 = int(bone.SdefIndicesAndType.x);
                int i1 = int(bone.SdefIndicesAndType.y);
                float w0 = bone.SdefWeightAndCenterXyz.x;
                float w1 = 1.0 - w0;
                vec3 center = bone.SdefWeightAndCenterXyz.yzw;
                mat4 rotation = MatrixFromQuaternion(QuaternionSlerp(
                    QuaternionFromMatrix(b_Transforms.Values[i0].Global),
                    QuaternionFromMatrix(b_Transforms.Values[i1].Global), w1));
                outputPosition = (rotation * vec4(position - center, 1.0)).xyz
                    + (b_Transforms.Values[i0].Update * vec4(bone.SdefR0.xyz, 1.0)).xyz * w0
                    + (b_Transforms.Values[i1].Update * vec4(bone.SdefR1.xyz, 1.0)).xyz * w1;
                outputNormal = normalize(mat3(rotation) * vertex.Normal.xyz);
            }
            else
            {
                outputPosition = (skin * vec4(position, 1.0)).xyz;
                outputNormal = normalize(mat3(skin) * vertex.Normal.xyz);
            }

            b_Outputs.Values[index].Position = vec4(outputPosition, 1.0);
            b_Outputs.Values[index].Normal = vec4(outputNormal, 0.0);
            b_Outputs.Values[index].Uv = vec4(vertex.Uv.xy + vertex.MorphUv.xy, 0.0, 0.0);
        }
        """;
}
