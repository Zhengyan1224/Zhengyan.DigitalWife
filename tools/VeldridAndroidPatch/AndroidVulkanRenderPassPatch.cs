using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Vulkan;

/// <summary>Preserve color/depth contents across interrupted and resumed passes.</summary>
internal static class AndroidVulkanRenderPassPatch
{
    private const int Stages = (int)(VkPipelineStageFlags.ColorAttachmentOutput |
        VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests);
    private const int Writes = (int)(VkAccessFlags.ColorAttachmentWrite | VkAccessFlags.DepthStencilAttachmentWrite);
    private const int ReadsAndWrites = Writes | (int)(VkAccessFlags.ColorAttachmentRead | VkAccessFlags.DepthStencilAttachmentRead);

    internal static void Apply(ModuleDefinition module)
    {
        MethodDefinition constructor = GetConstructor(module);
        Instruction[] fields = constructor.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.DeclaringType.FullName == "Vulkan.VkSubpassDependency").ToArray();
        ILProcessor il = constructor.Body.GetILProcessor();
        Instruction sourceStage = fields.Single(i => ((FieldReference)i.Operand).Name == "srcStageMask");
        // The original dependency has no srcAccessMask assignment. Initialize
        // it through the same local's address before setting its source stage.
        Instruction address = sourceStage.Previous.Previous;
        if (address.OpCode != OpCodes.Ldloca || address.Operand is not VariableDefinition dependency)
            throw new InvalidOperationException("Unexpected Vulkan subpass dependency initialization.");
        TypeDefinition dependencyType = ((FieldReference)sourceStage.Operand).DeclaringType.Resolve();
        FieldReference sourceAccess = module.ImportReference(dependencyType.Fields.Single(f => f.Name == "srcAccessMask"));
        il.InsertBefore(address, il.Create(OpCodes.Ldloca, dependency));
        il.InsertBefore(address, il.Create(OpCodes.Ldc_I4, Writes));
        il.InsertBefore(address, il.Create(OpCodes.Stfld, sourceAccess));
        SetConstant(sourceStage.Previous, Stages);
        SetConstant(fields.Single(i => ((FieldReference)i.Operand).Name == "dstStageMask").Previous, Stages);
        SetConstant(fields.Single(i => ((FieldReference)i.Operand).Name == "dstAccessMask").Previous, ReadsAndWrites);
    }

    internal static void Check(ModuleDefinition module)
    {
        MethodDefinition constructor = GetConstructor(module);
        constructor.Body.SimplifyMacros();
        foreach ((string name, int expected) in new[] {
            ("srcStageMask", Stages), ("dstStageMask", Stages),
            ("srcAccessMask", Writes), ("dstAccessMask", ReadsAndWrites) })
        {
            Instruction field = constructor.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld &&
                i.Operand is FieldReference f && f.DeclaringType.FullName == "Vulkan.VkSubpassDependency" && f.Name == name);
            if (field.Previous.OpCode != OpCodes.Ldc_I4 || !Equals(field.Previous.Operand, expected))
                throw new InvalidOperationException($"Vulkan render-pass dependency does not cover color/depth: {name}.");
        }
    }

    private static MethodDefinition GetConstructor(ModuleDefinition module)
        => module.GetType("Veldrid.Vk.VkFramebuffer").Methods.Single(m => m.IsConstructor && !m.IsStatic);

    private static void SetConstant(Instruction instruction, int value)
    {
        if (instruction.OpCode != OpCodes.Ldc_I4) throw new InvalidOperationException("Expected a Vulkan dependency constant.");
        instruction.Operand = value;
    }
}
