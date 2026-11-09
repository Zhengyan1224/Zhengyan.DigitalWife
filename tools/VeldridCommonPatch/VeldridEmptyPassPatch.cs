using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

/// <summary>Skip framebuffer passes which have neither draws nor queued clears.</summary>
internal static class VeldridEmptyPassPatch
{
    private const string HelperName = "ZhengyanHasPendingFramebufferClears";

    internal static bool Apply(ModuleDefinition module)
    {
        TypeDefinition type = module.GetType("Veldrid.Vk.VkCommandList");
        if (type.Methods.Any(m => m.Name == HelperName)) { Check(module); return false; }
        MethodDefinition setter = type.Methods.Single(m => m.Name == "SetFramebufferCore");
        setter.Body.SimplifyMacros();
        FieldDefinition depth = type.Fields.Single(f => f.Name == "_depthClearValue");
        FieldDefinition colors = type.Fields.Single(f => f.Name == "_validColorClearValues");
        MethodDefinition helper = new(HelperName, MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Boolean);
        type.Methods.Add(helper);
        helper.Body.InitLocals = true;
        VariableDefinition index = new(module.TypeSystem.Int32);
        helper.Body.Variables.Add(index);
        MethodReference hasValue = new("get_HasValue", module.TypeSystem.Boolean, depth.FieldType) { HasThis = true };
        ILProcessor il = helper.Body.GetILProcessor();
        Instruction yes = il.Create(OpCodes.Ldc_I4_1);
        Instruction loop = il.Create(OpCodes.Ldarg_0);
        Instruction condition = il.Create(OpCodes.Ldloc, index);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldflda, depth));
        il.Append(il.Create(OpCodes.Call, hasValue));
        il.Append(il.Create(OpCodes.Brtrue, yes));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stloc, index));
        il.Append(il.Create(OpCodes.Br, condition));
        il.Append(loop);
        il.Append(il.Create(OpCodes.Ldfld, colors));
        il.Append(il.Create(OpCodes.Ldloc, index));
        il.Append(il.Create(OpCodes.Ldelem_U1));
        il.Append(il.Create(OpCodes.Brtrue, yes));
        il.Append(il.Create(OpCodes.Ldloc, index));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Add));
        il.Append(il.Create(OpCodes.Stloc, index));
        il.Append(condition);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, colors));
        il.Append(il.Create(OpCodes.Ldlen));
        il.Append(il.Create(OpCodes.Conv_I4));
        il.Append(il.Create(OpCodes.Blt, loop));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(yes);
        il.Append(il.Create(OpCodes.Ret));

        Instruction begin = setter.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "BeginCurrentRenderPass");
        Instruction end = begin.Next;
        if (!IsThisLoad(begin.Previous) || !IsThisLoad(end)
            || end.Next.Operand is not MethodReference endMethod || endMethod.Name != "EndCurrentRenderPass")
            throw new InvalidOperationException("Unexpected Veldrid framebuffer clear-only pass.");
        Instruction blockStart = begin.Previous;
        Instruction blockEnd = end.Next.Next;
        ILProcessor setterIl = setter.Body.GetILProcessor();
        Instruction guard = setterIl.Create(OpCodes.Ldarg_0);
        foreach (Instruction instruction in setter.Body.Instructions)
            if (ReferenceEquals(instruction.Operand, blockStart)) instruction.Operand = guard;
        setterIl.InsertBefore(blockStart, guard);
        setterIl.InsertBefore(blockStart, setterIl.Create(OpCodes.Call, helper));
        setterIl.InsertBefore(blockStart, setterIl.Create(OpCodes.Brfalse, blockEnd));
        helper.Body.OptimizeMacros();
        setter.Body.OptimizeMacros();
        Check(module);
        return true;
    }

    internal static void Check(ModuleDefinition module)
    {
        TypeDefinition type = module.GetType("Veldrid.Vk.VkCommandList");
        MethodDefinition helper = type.Methods.Single(m => m.Name == HelperName);
        MethodDefinition setter = type.Methods.Single(m => m.Name == "SetFramebufferCore");
        setter.Body.SimplifyMacros();
        Instruction guard = setter.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == HelperName);
        Instruction begin = setter.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "BeginCurrentRenderPass");
        Instruction end = begin.Next.Next;
        if (guard.Next.OpCode != OpCodes.Brfalse || !ReferenceEquals(guard.Next.Operand, end.Next)
            || !helper.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_depthClearValue")
            || !helper.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_validColorClearValues")
            || !helper.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldelem_U1))
            throw new InvalidOperationException("Framebuffer passes must preserve queued depth and color clears.");
    }

    private static bool IsThisLoad(Instruction instruction)
        => instruction.OpCode == OpCodes.Ldarg_0 || instruction.OpCode == OpCodes.Ldarg
            && instruction.Operand is ParameterDefinition { Index: -1 };
}
