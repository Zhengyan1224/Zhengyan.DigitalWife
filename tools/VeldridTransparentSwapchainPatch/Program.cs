using Mono.Cecil;
using Mono.Cecil.Cil;

const string MarkerTypeName = "ZhengyanTransparentSwapchainPatch";
const string EnvironmentVariableName = "ZHENGYAN_VULKAN_TRANSPARENT_SWAPCHAIN";

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: VeldridTransparentSwapchainPatch <Veldrid.dll>");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Veldrid assembly was not found: {assemblyPath}");
    return 3;
}

DefaultAssemblyResolver resolver = new();
resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
resolver.AddSearchDirectory(AppContext.BaseDirectory);

using ModuleDefinition module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    AssemblyResolver = resolver
});

if (module.Types.Any(type => type.Namespace == "Veldrid.Vk" && type.Name == MarkerTypeName))
{
    return 0;
}

TypeDefinition swapchainType = module.GetType("Veldrid.Vk.VkSwapchain")
    ?? throw new InvalidOperationException("Veldrid.Vk.VkSwapchain was not found. The Veldrid binary version is unsupported.");
MethodDefinition createSwapchain = swapchainType.Methods.Single(method =>
    method.Name == "CreateSwapchain" && method.Parameters.Count == 2);
Instruction compositeAlphaStore = createSwapchain.Body.Instructions.Single(instruction =>
    instruction.OpCode == OpCodes.Stfld
    && instruction.Operand is FieldReference field
    && field.Name == "compositeAlpha");
FieldReference compositeAlphaField = (FieldReference)compositeAlphaStore.Operand;
VariableDefinition capabilitiesVariable = createSwapchain.Body.Variables.Single(variable =>
    variable.VariableType.FullName == "Vulkan.VkSurfaceCapabilitiesKHR");
TypeReference capabilitiesType = capabilitiesVariable.VariableType;
FieldReference supportedAlphaField = new(
    "supportedCompositeAlpha",
    compositeAlphaField.FieldType,
    capabilitiesType);

TypeDefinition markerType = new(
    "Veldrid.Vk",
    MarkerTypeName,
    TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
    module.TypeSystem.Object);
module.Types.Add(markerType);

MethodDefinition selector = new(
    "SelectCompositeAlpha",
    MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
    compositeAlphaField.FieldType);
selector.Parameters.Add(new ParameterDefinition("supported", ParameterAttributes.None, compositeAlphaField.FieldType));
markerType.Methods.Add(selector);
BuildSelectorBody(module, selector);

ILProcessor processor = createSwapchain.Body.GetILProcessor();
Instruction previous = compositeAlphaStore.Previous
    ?? throw new InvalidOperationException("The compositeAlpha assignment has no value instruction.");
if (previous.OpCode != OpCodes.Ldc_I4_1)
{
    throw new InvalidOperationException("The Veldrid compositeAlpha assignment no longer uses OpaqueKHR. Refusing an unsafe patch.");
}

processor.Replace(previous, processor.Create(OpCodes.Ldloca, capabilitiesVariable));
processor.InsertBefore(compositeAlphaStore, processor.Create(OpCodes.Ldfld, supportedAlphaField));
processor.InsertBefore(compositeAlphaStore, processor.Create(OpCodes.Call, selector));

string temporaryPath = assemblyPath + ".transparent-swapchain.tmp";
module.Write(temporaryPath, new WriterParameters { WriteSymbols = false });
File.Move(temporaryPath, assemblyPath, overwrite: true);
Console.WriteLine($"Patched Vulkan transparent swapchain support: {assemblyPath}");
return 0;

static void BuildSelectorBody(ModuleDefinition module, MethodDefinition method)
{
    ILProcessor il = method.Body.GetILProcessor();
    MethodReference getEnvironmentVariable = module.ImportReference(
        typeof(Environment).GetMethod(nameof(Environment.GetEnvironmentVariable), [typeof(string)])!);
    MethodReference stringEquals = module.ImportReference(
        typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string)])!);

    Instruction normalOpaqueCheck = il.Create(OpCodes.Ldarg_0);
    Instruction transparentInheritCheck = il.Create(OpCodes.Ldarg_0);
    Instruction transparentPostCheck = il.Create(OpCodes.Ldarg_0);
    Instruction normalInheritCheck = il.Create(OpCodes.Ldarg_0);
    Instruction normalPremultipliedCheck = il.Create(OpCodes.Ldarg_0);
    Instruction normalPostCheck = il.Create(OpCodes.Ldarg_0);

    il.Append(il.Create(OpCodes.Ldstr, EnvironmentVariableName));
    il.Append(il.Create(OpCodes.Call, getEnvironmentVariable));
    il.Append(il.Create(OpCodes.Ldstr, "1"));
    il.Append(il.Create(OpCodes.Call, stringEquals));
    il.Append(il.Create(OpCodes.Brfalse, normalOpaqueCheck));

    AppendFlagReturn(il, flag: 2, next: transparentInheritCheck); // PreMultipliedKHR
    il.Append(transparentInheritCheck);
    AppendFlagReturnTail(il, flag: 8, next: transparentPostCheck); // InheritKHR
    il.Append(transparentPostCheck);
    AppendFlagReturnTail(il, flag: 4, next: normalOpaqueCheck); // PostMultipliedKHR

    il.Append(normalOpaqueCheck);
    AppendFlagReturnTail(il, flag: 1, next: normalInheritCheck); // OpaqueKHR
    il.Append(normalInheritCheck);
    AppendFlagReturnTail(il, flag: 8, next: normalPremultipliedCheck); // InheritKHR
    il.Append(normalPremultipliedCheck);
    AppendFlagReturnTail(il, flag: 2, next: normalPostCheck); // PreMultipliedKHR
    il.Append(normalPostCheck);
    AppendFlagReturnTail(il, flag: 4, next: null);
    il.Append(il.Create(OpCodes.Ldc_I4_1));
    il.Append(il.Create(OpCodes.Ret));
}

static void AppendFlagReturn(ILProcessor il, int flag, Instruction next)
{
    il.Append(il.Create(OpCodes.Ldarg_0));
    AppendFlagReturnTail(il, flag, next);
}

static void AppendFlagReturnTail(ILProcessor il, int flag, Instruction? next)
{
    Instruction missing = next ?? il.Create(OpCodes.Nop);
    il.Append(il.Create(OpCodes.Ldc_I4, flag));
    il.Append(il.Create(OpCodes.And));
    il.Append(il.Create(OpCodes.Brfalse, missing));
    il.Append(il.Create(OpCodes.Ldc_I4, flag));
    il.Append(il.Create(OpCodes.Ret));
    if (next is null)
    {
        il.Append(missing);
    }
}
