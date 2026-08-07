using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>
/// Performs the validation that Veldrid otherwise defers until pipeline creation.
/// A custom Vulkan shader must be a valid vertex/fragment SPIR-V pair and may only
/// reference resources exposed by the engine pass layout.
/// </summary>
public static class VulkanShaderContract
{
    public static void ValidatePair(
        string vertexPath,
        string fragmentPath,
        IReadOnlySet<string>? allowedResourceNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentPath);

        byte[] vertexBytes = ReadModule(vertexPath, "vertex");
        byte[] fragmentBytes = ReadModule(fragmentPath, "fragment");

        try
        {
            VertexFragmentCompilationResult result = SpirvCompilation.CompileVertexFragment(
                vertexBytes,
                fragmentBytes,
                CrossCompileTarget.GLSL);

            if (allowedResourceNames is null)
            {
                return;
            }

            foreach (ResourceLayoutDescription layout in result.Reflection.ResourceLayouts)
            {
                foreach (ResourceLayoutElementDescription element in layout.Elements)
                {
                    if (!allowedResourceNames.Contains(element.Name))
                    {
                        throw new InvalidDataException(
                            $"Custom Vulkan shader resource '{element.Name}' is not part of the engine descriptor contract.");
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"Custom Vulkan shader contract validation failed for '{vertexPath}' and '{fragmentPath}'. " +
                "The pair must contain valid SPIR-V modules with compatible vertex/fragment interfaces and descriptor bindings.",
                exception);
        }
    }

    private static byte[] ReadModule(string path, string stage)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Custom Vulkan {stage} SPIR-V shader was not found.", fullPath);
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 20 || (bytes.Length & 3) != 0 || BitConverter.ToUInt32(bytes, 0) != 0x07230203)
        {
            throw new InvalidDataException($"'{fullPath}' is not a valid {stage} SPIR-V module.");
        }

        return bytes;
    }
}
