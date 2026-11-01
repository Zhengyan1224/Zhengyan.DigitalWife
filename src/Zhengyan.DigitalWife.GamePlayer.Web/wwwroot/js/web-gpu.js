export async function detectWebGpu() {
    if (!navigator.gpu) return { available: false, reason: "navigator.gpu is unavailable" };
    try {
        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) return { available: false, reason: "No WebGPU adapter" };
        const limits = adapter.limits;
        return {
            available: true,
            name: adapter.info?.description || adapter.info?.device || "WebGPU adapter",
            vendor: adapter.info?.vendor || "unknown",
            architecture: adapter.info?.architecture || "unknown",
            maxTextureDimension2D: limits.maxTextureDimension2D,
            maxBindGroups: limits.maxBindGroups
        };
    } catch (error) {
        return { available: false, reason: error?.message || String(error) };
    }
}
