using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Veldrid;
using Vulkan;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

internal static class VeldridDepthComparison
{
    private static readonly PropertyInfo? PhysicalDeviceProperty = FindPhysicalDeviceProperty();
    private static readonly PropertyInfo? FormatProperty = FindFormatProperty();

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Veldrid.Vk.VkGraphicsDevice", "Veldrid")]
    private static PropertyInfo? FindPhysicalDeviceProperty()
        => typeof(Veldrid.GraphicsDevice).Assembly.GetType("Veldrid.Vk.VkGraphicsDevice")?.GetProperty("PhysicalDevice");

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Veldrid.Vk.VkTexture", "Veldrid")]
    private static PropertyInfo? FindFormatProperty()
        => typeof(Texture).Assembly.GetType("Veldrid.Vk.VkTexture")?.GetProperty("VkFormat");

    internal static bool IsSupported(Veldrid.GraphicsDevice device, Texture texture)
    {
        if (device.BackendType != Veldrid.GraphicsBackend.Vulkan || texture.SampleCount != TextureSampleCount.Count1
            || (texture.Usage & (TextureUsage.DepthStencil | TextureUsage.Sampled)) != (TextureUsage.DepthStencil | TextureUsage.Sampled)
            || PhysicalDeviceProperty is null || FormatProperty is null) return false;
        VkPhysicalDevice physicalDevice = (VkPhysicalDevice)PhysicalDeviceProperty.GetValue(device)!;
        VkFormat format = (VkFormat)FormatProperty.GetValue(texture)!;
        VulkanNative.vkGetPhysicalDeviceFormatProperties(physicalDevice, format, out VkFormatProperties properties);
        const VkFormatFeatureFlags required = VkFormatFeatureFlags.SampledImage
            | VkFormatFeatureFlags.SampledImageFilterLinear | VkFormatFeatureFlags.DepthStencilAttachment;
        return (properties.optimalTilingFeatures & required) == required;
    }
}
