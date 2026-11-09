using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

/// <summary>Preserve color and depth across resumed render passes on every Vulkan platform.</summary>
internal static class VeldridRenderPassSynchronizationPatch
{
    // Vulkan 1.0 flag values: ColorAttachmentOutput | EarlyFragmentTests | LateFragmentTests.
    private const int Stages = 0x400 | 0x100 | 0x200;
    // ColorAttachmentWrite | DepthStencilAttachmentWrite, then their matching reads.
    private const int Writes = 0x100 | 0x400;
    private const int ReadsAndWrites = Writes | 0x80 | 0x200;

    internal static bool Apply(ModuleDefinition module)
    {
        MethodDefinition constructor = GetConstructor(module);
        constructor.Body.SimplifyMacros();
        Instruction[] fields = constructor.Body.Instructions.Where(IsDependencyField).ToArray();
        if (fields.Any(i => ((FieldReference)i.Operand).Name == "srcAccessMask"))
        {
            Check(module);
            return false;
        }
        ILProcessor il = constructor.Body.GetILProcessor();
        Instruction sourceStage = fields.Single(i => ((FieldReference)i.Operand).Name == "srcStageMask");
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
        Check(module);
        return true;
    }

    internal static void Check(ModuleDefinition module)
    {
        MethodDefinition constructor = GetConstructor(module);
        constructor.Body.SimplifyMacros();
        foreach ((string name, int expected) in new[] {
            ("srcStageMask", Stages), ("dstStageMask", Stages),
            ("srcAccessMask", Writes), ("dstAccessMask", ReadsAndWrites) })
        {
            Instruction field = constructor.Body.Instructions.Where(IsDependencyField)
                .Single(i => ((FieldReference)i.Operand).Name == name);
            if (field.Previous.OpCode != OpCodes.Ldc_I4 || !Equals(field.Previous.Operand, expected))
                throw new InvalidOperationException($"Vulkan render-pass dependency does not cover color/depth: {name}.");
        }
    }

    private static bool IsDependencyField(Instruction instruction)
        => instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == "Vulkan.VkSubpassDependency";

    private static MethodDefinition GetConstructor(ModuleDefinition module)
        => module.GetType("Veldrid.Vk.VkFramebuffer").Methods.Single(m => m.IsConstructor && !m.IsStatic);

    private static void SetConstant(Instruction instruction, int value)
    {
        if (instruction.OpCode != OpCodes.Ldc_I4) throw new InvalidOperationException("Expected a Vulkan dependency constant.");
        instruction.Operand = value;
    }
}
