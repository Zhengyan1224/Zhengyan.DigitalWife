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
    private const int ValidationDispatchCount = 3;

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
    private readonly int[] _skinningTypeCounts = new int[5];
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
    private int _validatedDispatchCount;
    private float _maxValidationPositionError;
    private float _maxValidationNormalError;
    private float _maxValidationUvError;
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
                new SkinningParametersGpu((uint)_vertexCount, (uint)_boneCount, 0, 0));
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

    public void InvalidateGpuOutput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gpuOutputValid = false;
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
            bool validateOutput = _validatedDispatchCount < ValidationDispatchCount;

            slot.Commands.Begin();
            slot.Commands.SetPipeline(_pipeline);
            slot.Commands.SetComputeResourceSet(0, slot.ResourceSet);
            slot.Commands.Dispatch((uint)((vertexCount + WorkgroupSize - 1) / WorkgroupSize), 1, 1);
            slot.Commands.CopyBuffer(slot.PositionOutputs, 0, _gpuPositionOutput!, 0, slot.PositionOutputs.SizeInBytes);
            slot.Commands.CopyBuffer(slot.NormalOutputs, 0, _gpuNormalOutput!, 0, slot.NormalOutputs.SizeInBytes);
            slot.Commands.CopyBuffer(slot.UvOutputs, 0, _gpuUvOutput!, 0, slot.UvOutputs.SizeInBytes);
            if (validateOutput)
            {
                slot.Commands.CopyBuffer(slot.PositionOutputs, 0, slot.PositionStaging, 0, slot.PositionStaging.SizeInBytes);
                slot.Commands.CopyBuffer(slot.NormalOutputs, 0, slot.NormalStaging, 0, slot.NormalStaging.SizeInBytes);
                slot.Commands.CopyBuffer(slot.UvOutputs, 0, slot.UvStaging, 0, slot.UvStaging.SizeInBytes);
            }
            slot.Commands.End();

            _renderer.Device.ResetFence(slot.Fence);
            _renderer.Device.SubmitCommands(slot.Commands, slot.Fence);
            slot.InFlight = true;
            slot.SubmissionId = ++_submissionId;
            _nextSlot = (_nextSlot + 1) % _slots.Length;

            // Rendering consumes these buffers from a different command-list
            // submission. Veldrid does not expose a semaphore dependency here,
            // so complete the transfer before the vertex-input submission.
            _renderer.Device.WaitForFence(slot.Fence);
            slot.InFlight = false;
            if (validateOutput && !ValidateGpuOutput(
                    slot,
                    vertexCount,
                    positions,
                    normals,
                    uvs,
                    vertexBoneInfos,
                    morphPositions,
                    morphUVs,
                    updateTransforms,
                    globalTransforms))
            {
                return false;
            }

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
            int skinningType = (int)info.SkinningType;
            if ((uint)skinningType < (uint)_skinningTypeCounts.Length)
            {
                _skinningTypeCounts[skinningType]++;
            }

            boneInputData[i] = new BoneInputGpu
            {
                BoneIndices = new Int4(
                    SanitizeBoneIndex(info.BoneIndices[0]),
                    SanitizeBoneIndex(info.BoneIndices[1]),
                    SanitizeBoneIndex(info.BoneIndices[2]),
                    SanitizeBoneIndex(info.BoneIndices[3])),
                BoneWeights = new Vector4(info.BoneWeights[0], info.BoneWeights[1], info.BoneWeights[2], info.BoneWeights[3]),
                SdefIndicesAndType = new Int4(
                    SanitizeBoneIndex(info.SDEF.BoneIndices[0]),
                    SanitizeBoneIndex(info.SDEF.BoneIndices[1]),
                    (int)info.SkinningType,
                    0),
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

    private int SanitizeBoneIndex(int index) => Math.Clamp(index, 0, _boneCount - 1);

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
                Update = MatrixRowsGpu.FromMatrix(updateTransforms[i]),
                Global = MatrixRowsGpu.FromMatrix(globalTransforms[i])
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

    private bool ValidateGpuOutput(
        ComputeSlot slot,
        int vertexCount,
        Vector3* positions,
        Vector3* normals,
        Vector2* uvs,
        Zhengyan.DigitalWife.Mmd.VertexBoneInfo* vertexBoneInfos,
        Vector3* morphPositions,
        Vector4* morphUVs,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms)
    {
        MappedResource mappedPositions = _renderer.Device.Map(slot.PositionStaging, MapMode.Read);
        MappedResource mappedNormals = _renderer.Device.Map(slot.NormalStaging, MapMode.Read);
        MappedResource mappedUvs = _renderer.Device.Map(slot.UvStaging, MapMode.Read);
        float maxPositionError = 0.0f;
        float maxNormalError = 0.0f;
        float maxUvError = 0.0f;
        int failedVertex = -1;
        Vector3 failedExpectedPosition = default;
        Vector3 failedActualPosition = default;
        Vector3 failedExpectedNormal = default;
        Vector3 failedActualNormal = default;
        Zhengyan.DigitalWife.Mmd.SkinningType failedSkinningType = default;
        try
        {
            float* positionValues = (float*)mappedPositions.Data.ToPointer();
            float* normalValues = (float*)mappedNormals.Data.ToPointer();
            float* uvValues = (float*)mappedUvs.Data.ToPointer();
            for (int i = 0; i < vertexCount; i++)
            {
                CalculateReferenceSkinning(
                    positions[i],
                    normals[i],
                    uvs[i],
                    vertexBoneInfos[i],
                    morphPositions[i],
                    morphUVs[i],
                    updateTransforms,
                    globalTransforms,
                    out Vector3 expectedPosition,
                    out Vector3 expectedNormal,
                    out Vector2 expectedUv);

                int vector3Offset = i * 3;
                int vector2Offset = i * 2;
                Vector3 actualPosition = new(
                    positionValues[vector3Offset],
                    positionValues[vector3Offset + 1],
                    positionValues[vector3Offset + 2]);
                Vector3 actualNormal = new(
                    normalValues[vector3Offset],
                    normalValues[vector3Offset + 1],
                    normalValues[vector3Offset + 2]);
                Vector2 actualUv = new(uvValues[vector2Offset], uvValues[vector2Offset + 1]);

                float positionError = MaxComponentError(actualPosition, expectedPosition);
                float normalError = MaxComponentError(actualNormal, expectedNormal);
                float uvError = MaxComponentError(actualUv, expectedUv);
                if (float.IsFinite(positionError)) maxPositionError = Math.Max(maxPositionError, positionError);
                if (float.IsFinite(normalError)) maxNormalError = Math.Max(maxNormalError, normalError);
                if (float.IsFinite(uvError)) maxUvError = Math.Max(maxUvError, uvError);

                if (failedVertex < 0
                    && (!IsWithinTolerance(actualPosition, expectedPosition, 2e-3f, 2e-4f)
                        || !IsWithinTolerance(actualNormal, expectedNormal, 2e-3f, 1e-3f)
                        || !IsWithinTolerance(actualUv, expectedUv, 1e-4f, 1e-5f)))
                {
                    failedVertex = i;
                    failedExpectedPosition = expectedPosition;
                    failedActualPosition = actualPosition;
                    failedExpectedNormal = expectedNormal;
                    failedActualNormal = actualNormal;
                    failedSkinningType = vertexBoneInfos[i].SkinningType;
                }
            }
        }
        finally
        {
            _renderer.Device.Unmap(slot.UvStaging);
            _renderer.Device.Unmap(slot.NormalStaging);
            _renderer.Device.Unmap(slot.PositionStaging);
        }

        if (failedVertex >= 0)
        {
            Console.Error.WriteLine(
                $"Vulkan Compute validation failed at vertex {failedVertex}: " +
                $"skinning={failedSkinningType}, " +
                $"position expected={failedExpectedPosition}, actual={failedActualPosition}, " +
                $"normal expected={failedExpectedNormal}, actual={failedActualNormal}, " +
                $"max errors position={maxPositionError:G6}, normal={maxNormalError:G6}, uv={maxUvError:G6}; " +
                "falling back to CPU");
            return false;
        }

        _maxValidationPositionError = Math.Max(_maxValidationPositionError, maxPositionError);
        _maxValidationNormalError = Math.Max(_maxValidationNormalError, maxNormalError);
        _maxValidationUvError = Math.Max(_maxValidationUvError, maxUvError);
        _validatedDispatchCount++;
        if (_validatedDispatchCount == ValidationDispatchCount)
        {
            Console.WriteLine(
                $"[Vulkan Compute] PMX skinning validated against CPU across {ValidationDispatchCount} dispatches " +
                $"({_vertexCount} vertices; max errors position={_maxValidationPositionError:G6}, " +
                $"normal={_maxValidationNormalError:G6}, uv={_maxValidationUvError:G6}; " +
                $"BDEF1={_skinningTypeCounts[0]}, BDEF2={_skinningTypeCounts[1]}, " +
                $"BDEF4={_skinningTypeCounts[2]}, SDEF={_skinningTypeCounts[3]}, QDEF={_skinningTypeCounts[4]})");
        }

        return true;
    }

    private void CalculateReferenceSkinning(
        Vector3 position,
        Vector3 normal,
        Vector2 uv,
        Zhengyan.DigitalWife.Mmd.VertexBoneInfo info,
        Vector3 morphPosition,
        Vector4 morphUv,
        Matrix4x4* updateTransforms,
        Matrix4x4* globalTransforms,
        out Vector3 outputPosition,
        out Vector3 outputNormal,
        out Vector2 outputUv)
    {
        int i0 = SanitizeBoneIndex(info.BoneIndices[0]);
        int i1 = SanitizeBoneIndex(info.BoneIndices[1]);
        int i2 = SanitizeBoneIndex(info.BoneIndices[2]);
        int i3 = SanitizeBoneIndex(info.BoneIndices[3]);
        Matrix4x4 skin = Matrix4x4.Identity;
        switch (info.SkinningType)
        {
            case Zhengyan.DigitalWife.Mmd.SkinningType.Weight1:
                skin = updateTransforms[i0];
                break;
            case Zhengyan.DigitalWife.Mmd.SkinningType.Weight2:
                skin = updateTransforms[i0] * info.BoneWeights[0]
                    + updateTransforms[i1] * info.BoneWeights[1];
                break;
            case Zhengyan.DigitalWife.Mmd.SkinningType.Weight4:
                skin = updateTransforms[i0] * info.BoneWeights[0]
                    + updateTransforms[i1] * info.BoneWeights[1]
                    + updateTransforms[i2] * info.BoneWeights[2]
                    + updateTransforms[i3] * info.BoneWeights[3];
                break;
            case Zhengyan.DigitalWife.Mmd.SkinningType.SDEF:
            {
                int sdef0 = SanitizeBoneIndex(info.SDEF.BoneIndices[0]);
                int sdef1 = SanitizeBoneIndex(info.SDEF.BoneIndices[1]);
                float weight0 = info.SDEF.BoneWeight;
                float weight1 = 1.0f - weight0;
                Quaternion q0 = Quaternion.CreateFromRotationMatrix(globalTransforms[sdef0]);
                Quaternion q1 = Quaternion.CreateFromRotationMatrix(globalTransforms[sdef1]);
                Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(q0, q1, weight1));
                Vector3 posedPosition = position + morphPosition;
                outputPosition = Vector3.Transform(posedPosition - info.SDEF.C, rotation)
                    + Vector3.Transform(info.SDEF.R0, updateTransforms[sdef0]) * weight0
                    + Vector3.Transform(info.SDEF.R1, updateTransforms[sdef1]) * weight1;
                outputNormal = Vector3.Normalize(Vector3.TransformNormal(normal, rotation));
                outputUv = uv + new Vector2(morphUv.X, morphUv.Y);
                return;
            }
        }

        outputPosition = Vector3.Transform(position + morphPosition, skin);
        outputNormal = Vector3.Normalize(Vector3.TransformNormal(normal, skin));
        outputUv = uv + new Vector2(morphUv.X, morphUv.Y);
    }

    private static bool IsWithinTolerance(Vector3 actual, Vector3 expected, float absolute, float relative)
        => MaxComponentError(actual, expected) <= absolute + relative * MaxAbsComponent(expected);

    private static bool IsWithinTolerance(Vector2 actual, Vector2 expected, float absolute, float relative)
        => MaxComponentError(actual, expected) <= absolute + relative * MaxAbsComponent(expected);

    private static float MaxComponentError(Vector3 left, Vector3 right)
        => Math.Max(ComponentError(left.X, right.X),
            Math.Max(ComponentError(left.Y, right.Y), ComponentError(left.Z, right.Z)));

    private static float MaxComponentError(Vector2 left, Vector2 right)
        => Math.Max(ComponentError(left.X, right.X), ComponentError(left.Y, right.Y));

    private static float MaxAbsComponent(Vector3 value)
        => Math.Max(FiniteAbs(value.X), Math.Max(FiniteAbs(value.Y), FiniteAbs(value.Z)));

    private static float MaxAbsComponent(Vector2 value)
        => Math.Max(FiniteAbs(value.X), FiniteAbs(value.Y));

    private static float ComponentError(float actual, float expected)
    {
        if (float.IsFinite(actual) && float.IsFinite(expected))
        {
            return Math.Abs(actual - expected);
        }

        if ((float.IsNaN(actual) && float.IsNaN(expected)) || actual.Equals(expected))
        {
            return 0.0f;
        }

        return float.PositiveInfinity;
    }

    private static float FiniteAbs(float value) => float.IsFinite(value) ? Math.Abs(value) : 0.0f;

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
    private readonly record struct SkinningParametersGpu(uint VertexCount, uint BoneCount, uint Padding0, uint Padding1);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Int4(int X, int Y, int Z, int W);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MatrixRowsGpu(Vector4 Row0, Vector4 Row1, Vector4 Row2, Vector4 Row3)
    {
        public static MatrixRowsGpu FromMatrix(Matrix4x4 matrix)
            => new(
                new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
                new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
                new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
                new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44));
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
        public Int4 BoneIndices;
        public Vector4 BoneWeights;
        public Int4 SdefIndicesAndType;
        public Vector4 SdefWeightAndCenterXyz;
        public Vector4 SdefR0;
        public Vector4 SdefR1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformInputGpu
    {
        public MatrixRowsGpu Update;
        public MatrixRowsGpu Global;
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
            uvec4 Counts;
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
            ivec4 BoneIndices;
            vec4 BoneWeights;
            ivec4 SdefIndicesAndType;
            vec4 SdefWeightAndCenterXyz;
            vec4 SdefR0;
            vec4 SdefR1;
        };

        struct MatrixRows
        {
            vec4 Row0;
            vec4 Row1;
            vec4 Row2;
            vec4 Row3;
        };

        struct TransformInput
        {
            MatrixRows Update;
            MatrixRows Global;
        };

        layout(set = 0, binding = 1, std430) readonly buffer VertexInputs { VertexInput Values[]; } b_Vertices;
        layout(set = 0, binding = 2, std430) readonly buffer MorphInputs { MorphInput Values[]; } b_Morphs;
        layout(set = 0, binding = 3, std430) readonly buffer BoneInputs { BoneInput Values[]; } b_Bones;
        layout(set = 0, binding = 4, std430) readonly buffer Transforms { TransformInput Values[]; } b_Transforms;
        layout(set = 0, binding = 5, std430) writeonly buffer PositionOutputs { float Values[]; } b_Positions;
        layout(set = 0, binding = 6, std430) writeonly buffer NormalOutputs { float Values[]; } b_Normals;
        layout(set = 0, binding = 7, std430) writeonly buffer UvOutputs { float Values[]; } b_Uvs;

        vec3 TransformPosition(vec3 position, MatrixRows matrix)
        {
            return vec3(
                position.x * matrix.Row0.x + position.y * matrix.Row1.x + position.z * matrix.Row2.x + matrix.Row3.x,
                position.x * matrix.Row0.y + position.y * matrix.Row1.y + position.z * matrix.Row2.y + matrix.Row3.y,
                position.x * matrix.Row0.z + position.y * matrix.Row1.z + position.z * matrix.Row2.z + matrix.Row3.z);
        }

        vec3 TransformNormal(vec3 normal, MatrixRows matrix)
        {
            return vec3(
                normal.x * matrix.Row0.x + normal.y * matrix.Row1.x + normal.z * matrix.Row2.x,
                normal.x * matrix.Row0.y + normal.y * matrix.Row1.y + normal.z * matrix.Row2.y,
                normal.x * matrix.Row0.z + normal.y * matrix.Row1.z + normal.z * matrix.Row2.z);
        }

        vec4 QuaternionFromMatrix(MatrixRows matrix)
        {
            float m11 = matrix.Row0.x;
            float m12 = matrix.Row0.y;
            float m13 = matrix.Row0.z;
            float m21 = matrix.Row1.x;
            float m22 = matrix.Row1.y;
            float m23 = matrix.Row1.z;
            float m31 = matrix.Row2.x;
            float m32 = matrix.Row2.y;
            float m33 = matrix.Row2.z;
            float trace = m11 + m22 + m33;
            vec4 q = vec4(0.0);
            if (trace > 0.0)
            {
                float s = sqrt(trace + 1.0);
                q.w = s * 0.5;
                s = 0.5 / s;
                q.x = (m23 - m32) * s;
                q.y = (m31 - m13) * s;
                q.z = (m12 - m21) * s;
            }
            else if (m11 >= m22 && m11 >= m33)
            {
                float s = sqrt(1.0 + m11 - m22 - m33);
                float invS = 0.5 / s;
                q.x = 0.5 * s;
                q.y = (m12 + m21) * invS;
                q.z = (m13 + m31) * invS;
                q.w = (m23 - m32) * invS;
            }
            else if (m22 > m33)
            {
                float s = sqrt(1.0 + m22 - m11 - m33);
                float invS = 0.5 / s;
                q.x = (m21 + m12) * invS;
                q.y = 0.5 * s;
                q.z = (m32 + m23) * invS;
                q.w = (m31 - m13) * invS;
            }
            else
            {
                float s = sqrt(1.0 + m33 - m11 - m22);
                float invS = 0.5 / s;
                q.x = (m31 + m13) * invS;
                q.y = (m32 + m23) * invS;
                q.z = 0.5 * s;
                q.w = (m12 - m21) * invS;
            }
            return q;
        }

        vec4 QuaternionSlerp(vec4 a, vec4 b, float amount)
        {
            float cosine = dot(a, b);
            bool flip = cosine < 0.0;
            if (flip) cosine = -cosine;
            float s1;
            float s2;
            if (cosine > 1.0 - 1e-6)
            {
                s1 = 1.0 - amount;
                s2 = flip ? -amount : amount;
            }
            else
            {
                float omega = acos(clamp(cosine, -1.0, 1.0));
                float invSinOmega = 1.0 / sin(omega);
                s1 = sin((1.0 - amount) * omega) * invSinOmega;
                s2 = (flip ? -sin(amount * omega) : sin(amount * omega)) * invSinOmega;
            }
            return s1 * a + s2 * b;
        }

        vec3 RotateByQuaternion(vec3 value, vec4 q)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
            float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
            float m11 = 1.0 - 2.0 * (yy + zz);
            float m12 = 2.0 * (xy + wz);
            float m13 = 2.0 * (xz - wy);
            float m21 = 2.0 * (xy - wz);
            float m22 = 1.0 - 2.0 * (zz + xx);
            float m23 = 2.0 * (yz + wx);
            float m31 = 2.0 * (xz + wy);
            float m32 = 2.0 * (yz - wx);
            float m33 = 1.0 - 2.0 * (yy + xx);
            return vec3(
                value.x * m11 + value.y * m21 + value.z * m31,
                value.x * m12 + value.y * m22 + value.z * m32,
                value.x * m13 + value.y * m23 + value.z * m33);
        }

        void main()
        {
            uint index = gl_GlobalInvocationID.x;
            if (index >= uint(u_Parameters.Counts.x)) return;

            VertexInput vertex = b_Vertices.Values[index];
            MorphInput morph = b_Morphs.Values[index];
            BoneInput bone = b_Bones.Values[index];
            int skinningType = bone.SdefIndicesAndType.z;
            int lastBone = max(int(u_Parameters.Counts.y) - 1, 0);
            ivec4 indices = clamp(bone.BoneIndices, ivec4(0), ivec4(lastBone));

            vec3 position = vertex.Position.xyz + morph.Position.xyz;
            vec3 outputPosition;
            vec3 outputNormal;
            if (skinningType == 3)
            {
                int i0 = clamp(int(bone.SdefIndicesAndType.x), 0, lastBone);
                int i1 = clamp(int(bone.SdefIndicesAndType.y), 0, lastBone);
                float w0 = bone.SdefWeightAndCenterXyz.x;
                float w1 = 1.0 - w0;
                vec3 center = bone.SdefWeightAndCenterXyz.yzw;
                vec4 rotation = QuaternionSlerp(
                    QuaternionFromMatrix(b_Transforms.Values[i0].Global),
                    QuaternionFromMatrix(b_Transforms.Values[i1].Global), w1);
                outputPosition = RotateByQuaternion(position - center, rotation)
                    + TransformPosition(bone.SdefR0.xyz, b_Transforms.Values[i0].Update) * w0
                    + TransformPosition(bone.SdefR1.xyz, b_Transforms.Values[i1].Update) * w1;
                outputNormal = normalize(RotateByQuaternion(vertex.Normal.xyz, rotation));
            }
            else if (skinningType == 0)
            {
                outputPosition = TransformPosition(position, b_Transforms.Values[indices.x].Update);
                outputNormal = normalize(TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.x].Update));
            }
            else if (skinningType == 1)
            {
                outputPosition = TransformPosition(position, b_Transforms.Values[indices.x].Update) * bone.BoneWeights.x
                    + TransformPosition(position, b_Transforms.Values[indices.y].Update) * bone.BoneWeights.y;
                outputNormal = normalize(
                    TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.x].Update) * bone.BoneWeights.x
                    + TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.y].Update) * bone.BoneWeights.y);
            }
            else if (skinningType == 2)
            {
                outputPosition = TransformPosition(position, b_Transforms.Values[indices.x].Update) * bone.BoneWeights.x
                    + TransformPosition(position, b_Transforms.Values[indices.y].Update) * bone.BoneWeights.y
                    + TransformPosition(position, b_Transforms.Values[indices.z].Update) * bone.BoneWeights.z
                    + TransformPosition(position, b_Transforms.Values[indices.w].Update) * bone.BoneWeights.w;
                outputNormal = normalize(
                    TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.x].Update) * bone.BoneWeights.x
                    + TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.y].Update) * bone.BoneWeights.y
                    + TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.z].Update) * bone.BoneWeights.z
                    + TransformNormal(vertex.Normal.xyz, b_Transforms.Values[indices.w].Update) * bone.BoneWeights.w);
            }
            else
            {
                outputPosition = position;
                outputNormal = normalize(vertex.Normal.xyz);
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
