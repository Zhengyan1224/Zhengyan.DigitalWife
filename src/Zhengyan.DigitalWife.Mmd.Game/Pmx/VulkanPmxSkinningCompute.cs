using System.Numerics;
using System.Runtime.InteropServices;
using Zhengyan.DigitalWife.Mmd.Game.Graphics;
using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Pmx;

/// <summary>
/// Vulkan compute implementation of the PMX CPU/OpenCL skinning contract.
/// Dispatches use a three-slot input/output/staging ring. Completed results are
/// consumed opportunistically so the update thread does not wait on every frame;
/// a wait is only required for the first result or when the GPU falls behind the
/// ring.
/// </summary>
internal sealed unsafe class VulkanPmxSkinningCompute :
    Zhengyan.DigitalWife.Mmd.IPmxSkinningCompute,
    Zhengyan.DigitalWife.Mmd.IPmxGpuSkinningCompute
{
    private const uint WorkgroupSize = 64;
    private const int SlotCount = 3;

    private readonly VulkanRenderer _renderer;
    private readonly int _vertexCount;
    private readonly int _boneCount;
    private readonly ResourceLayout _layout;
    private readonly DeviceBuffer _staticVertexInputs;
    private readonly DeviceBuffer _staticBoneInputs;
    private readonly Shader _shader;
    private readonly Pipeline _pipeline;
    private VertexInputGpu[]? _vertexInputData;
    private readonly MorphInputGpu[] _morphInputData;
    private BoneInputGpu[]? _boneInputData;
    private readonly TransformInputGpu[] _transformData;
    private readonly OutputGpu[] _latestOutput;
    private readonly ComputeSlot[] _slots;
    private int _nextSlot;
    private long _submissionId;
    private long _morphRevision;
    private long _transformRevision;
    private bool _hasCompletedOutput;
    private bool _staticInputsInitialized;
    private bool _hasMorphSnapshot;
    private bool _hasTransformSnapshot;
    private bool _gpuOutputValid;
    private DeviceBuffer? _gpuPositionOutput;
    private DeviceBuffer? _gpuNormalOutput;
    private DeviceBuffer? _gpuUvOutput;
    private bool _disposed;

    public VulkanPmxSkinningCompute(VulkanRenderer renderer, int vertexCount, int boneCount)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _vertexCount = Math.Max(vertexCount, 1);
        _boneCount = Math.Max(boneCount, 1);
        ResourceFactory factory = renderer.ResourceFactory;

        _vertexInputData = new VertexInputGpu[_vertexCount];
        _morphInputData = new MorphInputGpu[_vertexCount];
        _boneInputData = new BoneInputGpu[_vertexCount];
        _transformData = new TransformInputGpu[_boneCount];
        _staticVertexInputs = CreateStructuredBuffer<VertexInputGpu>(
            factory, _vertexCount, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic);
        _staticBoneInputs = CreateStructuredBuffer<BoneInputGpu>(
            factory, _vertexCount, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic);

        _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SkinningParameters", ResourceKind.UniformBuffer, ShaderStages.Compute),
            new ResourceLayoutElementDescription("VertexInputs", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("MorphInputs", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("BoneInputs", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("Transforms", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
            new ResourceLayoutElementDescription("PositionOutputs", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("NormalOutputs", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
            new ResourceLayoutElementDescription("UvOutputs", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute)));
        ShaderDescription shaderDescription = VulkanShaderCompiler.CompileSource(
            "pmx_skinning.comp", ComputeShaderSource, ShaderStages.Compute);
        _shader = factory.CreateFromSpirv(shaderDescription);
        _pipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
            _shader, _layout, WorkgroupSize, 1, 1));
        _latestOutput = new OutputGpu[_vertexCount];
        _slots = new ComputeSlot[SlotCount];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new ComputeSlot(
                factory,
                _layout,
                _staticVertexInputs,
                _staticBoneInputs,
                _vertexCount,
                _boneCount);
            renderer.Device.UpdateBuffer(
                _slots[i].Parameters,
                0,
                new Vector4(_vertexCount, _boneCount, 0.0f, 0.0f));
        }
    }

    public string BackendName => "Vulkan Compute";

    public bool IsGpuOutputBound
        => _gpuPositionOutput is not null && _gpuNormalOutput is not null && _gpuUvOutput is not null;

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
            EnsureStaticInputs(vertexCount, positions, normals, uvs, vertexBoneInfos);
            PopulateDynamicInputs(vertexCount, boneCount, morphPositions, morphUVs, updateTransforms, globalTransforms);

            RetireCompletedSlots();
            ComputeSlot slot = AcquireSlot();
            UploadMorphInputsIfNeeded(slot);
            UploadTransformsIfNeeded(slot);

            slot.Commands.Begin();
            slot.Commands.SetPipeline(_pipeline);
            slot.Commands.SetComputeResourceSet(0, slot.ResourceSet);
            slot.Commands.Dispatch((uint)((vertexCount + WorkgroupSize - 1) / WorkgroupSize), 1, 1);
            slot.Commands.CopyBuffer(slot.PositionOutputs, 0, slot.PositionStaging, 0, slot.PositionStaging.SizeInBytes);
            slot.Commands.CopyBuffer(slot.NormalOutputs, 0, slot.NormalStaging, 0, slot.NormalStaging.SizeInBytes);
            slot.Commands.CopyBuffer(slot.UvOutputs, 0, slot.UvStaging, 0, slot.UvStaging.SizeInBytes);
            slot.Commands.End();

            _renderer.Device.ResetFence(slot.Fence);
            _renderer.Device.SubmitCommands(slot.Commands, slot.Fence);
            slot.InFlight = true;
            slot.SubmissionId = ++_submissionId;
            _nextSlot = (_nextSlot + 1) % _slots.Length;

            // The first result must be valid. Subsequent frames consume the most
            // recent completed result and avoid blocking the update thread.
            if (!_hasCompletedOutput)
            {
                _renderer.Device.WaitForFence(slot.Fence);
                RetireCompletedSlots();
            }

            CopyLatestOutput(updatePositions, updateNormals, updateUVs, vertexCount);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Vulkan Compute skinning failed; falling back to CPU: {ex.Message}");
            return false;
        }
    }

    public bool TryBindGpuOutput(object positionBuffer, object normalBuffer, object uvBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (positionBuffer is not DeviceBuffer positions
            || normalBuffer is not DeviceBuffer normals
            || uvBuffer is not DeviceBuffer uvs)
        {
            return false;
        }

        uint positionBytes = checked((uint)(_vertexCount * 3 * sizeof(float)));
        uint uvBytes = checked((uint)(_vertexCount * 2 * sizeof(float)));
        if (positions.SizeInBytes < positionBytes || normals.SizeInBytes < positionBytes || uvs.SizeInBytes < uvBytes)
        {
            return false;
        }

        foreach (ComputeSlot slot in _slots)
        {
            if (slot.InFlight && !slot.Fence.Signaled)
            {
                _renderer.Device.WaitForFence(slot.Fence);
            }

            slot.InFlight = false;
        }

        _gpuPositionOutput = positions;
        _gpuNormalOutput = normals;
        _gpuUvOutput = uvs;
        _gpuOutputValid = false;
        return true;
    }

    public bool ExecuteGpu(
        int vertexCount,
        int boneCount,
        Vector3* positions,
        Vector3* normals,
        Vector2* uvs,
        Zhengyan.DigitalWife.Mmd.VertexBoneInfo* vertexBoneInfos,
        Vector3* morphPositions,
        Vector4* morphUVs,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (vertexCount != _vertexCount || boneCount != _boneCount || !IsGpuOutputBound)
        {
            return false;
        }

        try
        {
            EnsureStaticInputs(vertexCount, positions, normals, uvs, vertexBoneInfos);
            long previousMorphRevision = _morphRevision;
            long previousTransformRevision = _transformRevision;
            PopulateDynamicInputs(vertexCount, boneCount, morphPositions, morphUVs, updateTransforms, globalTransforms);
            if (_gpuOutputValid
                && previousMorphRevision == _morphRevision
                && previousTransformRevision == _transformRevision)
            {
                return true;
            }

            ComputeSlot slot = AcquireSlot();
            UploadMorphInputsIfNeeded(slot);
            UploadTransformsIfNeeded(slot);

            slot.Commands.Begin();
            slot.Commands.SetPipeline(_pipeline);
            slot.Commands.SetComputeResourceSet(0, slot.ResourceSet);
            slot.Commands.Dispatch((uint)((vertexCount + WorkgroupSize - 1) / WorkgroupSize), 1, 1);
            slot.Commands.CopyBuffer(slot.PositionOutputs, 0, _gpuPositionOutput!, 0, slot.PositionOutputs.SizeInBytes);
            slot.Commands.CopyBuffer(slot.NormalOutputs, 0, _gpuNormalOutput!, 0, slot.NormalOutputs.SizeInBytes);
            slot.Commands.CopyBuffer(slot.UvOutputs, 0, _gpuUvOutput!, 0, slot.UvOutputs.SizeInBytes);
            slot.Commands.End();

            _renderer.Device.ResetFence(slot.Fence);
            _renderer.Device.SubmitCommands(slot.Commands, slot.Fence);
            slot.InFlight = true;
            slot.SubmissionId = ++_submissionId;
            _nextSlot = (_nextSlot + 1) % _slots.Length;
            _gpuOutputValid = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Vulkan GPU-only skinning failed; falling back to CPU: {ex.Message}");
            return false;
        }
    }

    private void EnsureStaticInputs(
        int vertexCount,
        Vector3* positions,
        Vector3* normals,
        Vector2* uvs,
        Zhengyan.DigitalWife.Mmd.VertexBoneInfo* vertexBoneInfos)
    {
        if (_staticInputsInitialized)
        {
            return;
        }

        VertexInputGpu[] vertexInputData = _vertexInputData
            ?? throw new InvalidOperationException("Static PMX vertex input data has already been released.");
        BoneInputGpu[] boneInputData = _boneInputData
            ?? throw new InvalidOperationException("Static PMX bone input data has already been released.");

        for (int i = 0; i < vertexCount; i++)
        {
            vertexInputData[i] = new VertexInputGpu
            {
                Position = new Vector4(positions[i], 1.0f),
                Normal = new Vector4(normals[i], 0.0f),
                Uv = new Vector4(uvs[i], 0.0f, 0.0f)
            };

            Zhengyan.DigitalWife.Mmd.VertexBoneInfo info = vertexBoneInfos[i];
            boneInputData[i] = new BoneInputGpu
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

        _renderer.Device.UpdateBuffer(_staticVertexInputs, 0, vertexInputData);
        _renderer.Device.UpdateBuffer(_staticBoneInputs, 0, boneInputData);

        _staticInputsInitialized = true;
        _vertexInputData = null;
        _boneInputData = null;
    }

    private void PopulateDynamicInputs(
        int vertexCount,
        int boneCount,
        Vector3* morphPositions,
        Vector4* morphUVs,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms)
    {
        bool morphChanged = !_hasMorphSnapshot;
        for (int i = 0; i < vertexCount; i++)
        {
            MorphInputGpu input = new()
            {
                Position = new Vector4(morphPositions[i], 0.0f),
                Uv = morphUVs[i]
            };
            morphChanged |= _morphInputData[i].Position != input.Position || _morphInputData[i].Uv != input.Uv;
            _morphInputData[i] = input;
        }

        if (morphChanged)
        {
            _morphRevision++;
            _hasMorphSnapshot = true;
        }

        bool transformsChanged = !_hasTransformSnapshot;
        for (int i = 0; i < boneCount; i++)
        {
            TransformInputGpu input = new()
            {
                Update = updateTransforms[i],
                Global = globalTransforms[i]
            };
            transformsChanged |= _transformData[i].Update != input.Update || _transformData[i].Global != input.Global;
            _transformData[i] = input;
        }

        if (transformsChanged)
        {
            _transformRevision++;
            _hasTransformSnapshot = true;
        }
    }

    private void UploadMorphInputsIfNeeded(ComputeSlot slot)
    {
        if (slot.MorphRevision == _morphRevision)
        {
            return;
        }

        _renderer.Device.UpdateBuffer(slot.MorphInputs, 0, _morphInputData);
        slot.MorphRevision = _morphRevision;
    }

    private void UploadTransformsIfNeeded(ComputeSlot slot)
    {
        if (slot.TransformRevision == _transformRevision)
        {
            return;
        }

        _renderer.Device.UpdateBuffer(slot.Transforms, 0, _transformData);
        slot.TransformRevision = _transformRevision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ComputeSlot slot in _slots)
        {
            if (slot.InFlight && !slot.Fence.Signaled)
            {
                _renderer.Device.WaitForFence(slot.Fence);
            }
            slot.Dispose();
        }
        _pipeline.Dispose();
        _shader.Dispose();
        _layout.Dispose();
        _staticBoneInputs.Dispose();
        _staticVertexInputs.Dispose();
        GC.SuppressFinalize(this);
    }

    private ComputeSlot AcquireSlot()
    {
        ComputeSlot slot = _slots[_nextSlot];
        if (slot.InFlight && !slot.Fence.Signaled)
        {
            // Three slots normally keep the CPU ahead of the GPU. Waiting here
            // only means the GPU has fallen more than two dispatches behind.
            _renderer.Device.WaitForFence(slot.Fence);
        }

        slot.InFlight = false;
        return slot;
    }

    private void RetireCompletedSlots()
    {
        ComputeSlot? newest = null;
        foreach (ComputeSlot slot in _slots)
        {
            if (!slot.InFlight || !slot.Fence.Signaled)
            {
                continue;
            }

            if (newest is null || slot.SubmissionId > newest.SubmissionId)
            {
                newest = slot;
            }
        }

        if (newest is null)
        {
            return;
        }

        MappedResource positions = _renderer.Device.Map(newest.PositionStaging, MapMode.Read);
        MappedResource normals = _renderer.Device.Map(newest.NormalStaging, MapMode.Read);
        MappedResource uvs = _renderer.Device.Map(newest.UvStaging, MapMode.Read);
        try
        {
            float* positionValues = (float*)positions.Data.ToPointer();
            float* normalValues = (float*)normals.Data.ToPointer();
            float* uvValues = (float*)uvs.Data.ToPointer();
            for (int i = 0; i < _vertexCount; i++)
            {
                int vector3Offset = i * 3;
                int vector2Offset = i * 2;
                _latestOutput[i] = new OutputGpu
                {
                    Position = new Vector4(
                        positionValues[vector3Offset],
                        positionValues[vector3Offset + 1],
                        positionValues[vector3Offset + 2],
                        1.0f),
                    Normal = new Vector4(
                        normalValues[vector3Offset],
                        normalValues[vector3Offset + 1],
                        normalValues[vector3Offset + 2],
                        0.0f),
                    Uv = new Vector4(
                        uvValues[vector2Offset],
                        uvValues[vector2Offset + 1],
                        0.0f,
                        0.0f)
                };
            }
        }
        finally
        {
            _renderer.Device.Unmap(newest.UvStaging);
            _renderer.Device.Unmap(newest.NormalStaging);
            _renderer.Device.Unmap(newest.PositionStaging);
        }

        foreach (ComputeSlot slot in _slots)
        {
            if (slot.InFlight && slot.Fence.Signaled)
            {
                slot.InFlight = false;
            }
        }

        _hasCompletedOutput = true;
    }

    private void CopyLatestOutput(Vector3* updatePositions, Vector3* updateNormals, Vector2* updateUVs, int vertexCount)
    {
        for (int i = 0; i < vertexCount; i++)
        {
            OutputGpu result = _latestOutput[i];
            updatePositions[i] = new Vector3(result.Position.X, result.Position.Y, result.Position.Z);
            updateNormals[i] = new Vector3(result.Normal.X, result.Normal.Y, result.Normal.Z);
            updateUVs[i] = new Vector2(result.Uv.X, result.Uv.Y);
        }
    }

    private sealed class ComputeSlot : IDisposable
    {
        public ComputeSlot(
            ResourceFactory factory,
            ResourceLayout layout,
            DeviceBuffer staticVertexInputs,
            DeviceBuffer staticBoneInputs,
            int vertexCount,
            int boneCount)
        {
            Parameters = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            MorphInputs = CreateStructuredBuffer<MorphInputGpu>(
                factory, vertexCount, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic);
            Transforms = CreateStructuredBuffer<TransformInputGpu>(
                factory, boneCount, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic);
            PositionOutputs = CreateFloatBuffer(factory, vertexCount * 3, BufferUsage.StructuredBufferReadWrite);
            NormalOutputs = CreateFloatBuffer(factory, vertexCount * 3, BufferUsage.StructuredBufferReadWrite);
            UvOutputs = CreateFloatBuffer(factory, vertexCount * 2, BufferUsage.StructuredBufferReadWrite);
            PositionStaging = CreateStagingBuffer(factory, PositionOutputs.SizeInBytes);
            NormalStaging = CreateStagingBuffer(factory, NormalOutputs.SizeInBytes);
            UvStaging = CreateStagingBuffer(factory, UvOutputs.SizeInBytes);
            ResourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                layout, Parameters, staticVertexInputs, MorphInputs, staticBoneInputs, Transforms,
                PositionOutputs, NormalOutputs, UvOutputs));
            Commands = factory.CreateCommandList();
            Fence = factory.CreateFence(false);
        }

        public DeviceBuffer Parameters { get; }
        public DeviceBuffer MorphInputs { get; }
        public DeviceBuffer Transforms { get; }
        public DeviceBuffer PositionOutputs { get; }
        public DeviceBuffer NormalOutputs { get; }
        public DeviceBuffer UvOutputs { get; }
        public DeviceBuffer PositionStaging { get; }
        public DeviceBuffer NormalStaging { get; }
        public DeviceBuffer UvStaging { get; }
        public ResourceSet ResourceSet { get; }
        public CommandList Commands { get; }
        public Fence Fence { get; }
        public bool InFlight { get; set; }
        public long SubmissionId { get; set; }
        public long MorphRevision { get; set; } = -1;
        public long TransformRevision { get; set; } = -1;

        public void Dispose()
        {
            Fence.Dispose();
            Commands.Dispose();
            ResourceSet.Dispose();
            UvStaging.Dispose();
            NormalStaging.Dispose();
            PositionStaging.Dispose();
            UvOutputs.Dispose();
            NormalOutputs.Dispose();
            PositionOutputs.Dispose();
            Transforms.Dispose();
            MorphInputs.Dispose();
            Parameters.Dispose();
        }

        private static DeviceBuffer CreateStagingBuffer(ResourceFactory factory, uint sizeInBytes)
            => factory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.Staging));
    }

    private static DeviceBuffer CreateFloatBuffer(ResourceFactory factory, int count, BufferUsage usage)
        => factory.CreateBuffer(new BufferDescription(
            checked((uint)(count * sizeof(float))), usage, sizeof(float)));

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
        public Vector4 Uv;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MorphInputGpu
    {
        public Vector4 Position;
        public Vector4 Uv;
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
            vec4 Uv;
        };

        struct MorphInput
        {
            vec4 Position;
            vec4 Uv;
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

        layout(set = 0, binding = 1, std430) readonly buffer VertexInputs { VertexInput Values[]; } b_Vertices;
        layout(set = 0, binding = 2, std430) readonly buffer MorphInputs { MorphInput Values[]; } b_Morphs;
        layout(set = 0, binding = 3, std430) readonly buffer BoneInputs { BoneInput Values[]; } b_Bones;
        layout(set = 0, binding = 4, std430) readonly buffer Transforms { TransformInput Values[]; } b_Transforms;
        layout(set = 0, binding = 5, std430) writeonly buffer PositionOutputs { float Values[]; } b_Positions;
        layout(set = 0, binding = 6, std430) writeonly buffer NormalOutputs { float Values[]; } b_Normals;
        layout(set = 0, binding = 7, std430) writeonly buffer UvOutputs { float Values[]; } b_Uvs;

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
            MorphInput morph = b_Morphs.Values[index];
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

            vec3 position = vertex.Position.xyz + morph.Position.xyz;
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

            uint vector3Offset = index * 3u;
            uint vector2Offset = index * 2u;
            b_Positions.Values[vector3Offset] = outputPosition.x;
            b_Positions.Values[vector3Offset + 1u] = outputPosition.y;
            b_Positions.Values[vector3Offset + 2u] = outputPosition.z;
            b_Normals.Values[vector3Offset] = outputNormal.x;
            b_Normals.Values[vector3Offset + 1u] = outputNormal.y;
            b_Normals.Values[vector3Offset + 2u] = outputNormal.z;
            vec2 outputUv = vertex.Uv.xy + morph.Uv.xy;
            b_Uvs.Values[vector2Offset] = outputUv.x;
            b_Uvs.Values[vector2Offset + 1u] = outputUv.y;
        }
        """;
}
