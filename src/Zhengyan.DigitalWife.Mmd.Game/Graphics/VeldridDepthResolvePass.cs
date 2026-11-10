using Veldrid;
using Veldrid.SPIRV;

namespace Zhengyan.DigitalWife.Mmd.Game.Graphics;

/// <summary>Resolve sampled MSAA depth only for captures that need depth, never for mirror textures.</summary>
internal sealed class VeldridDepthResolvePass : IDisposable
{
    private readonly Framebuffer _target;
    private readonly ResourceSet _resources;
    private readonly Pipeline _pipeline;
    public VeldridDepthResolvePass(VulkanRenderer renderer,TextureView source,Framebuffer target)
    {
        _target=target;
        SharedResources shared=renderer.DepthResolveResources;
        _pipeline=shared.Pipeline;
        _resources=renderer.ResourceFactory.CreateResourceSet(new ResourceSetDescription(shared.Layout,source,shared.Sampler));
    }
    public void Draw(CommandList commands)
    {
        commands.SetFramebuffer(_target); commands.SetFullViewports(); commands.SetFullScissorRects();
        commands.SetPipeline(_pipeline); commands.SetGraphicsResourceSet(0,_resources); commands.Draw(3);
    }
    public void Dispose()=>_resources.Dispose();

    internal sealed class SharedResources : IDisposable
    {
        public ResourceLayout Layout { get; }
        public Sampler Sampler { get; }
        public Pipeline Pipeline { get; }
        private readonly Shader[] _shaders;
        public SharedResources(VulkanRenderer renderer)
        {
        ResourceFactory factory=renderer.ResourceFactory;
        Layout=factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Depth",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Sampler",ResourceKind.Sampler,ShaderStages.Fragment)));
        Sampler=factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,null,0,0,0,0,SamplerBorderColor.OpaqueWhite));
        _shaders=factory.CreateFromSpirv(
            VulkanShaderCompiler.CompileSource("depth-resolve.vert","void main(){vec2 p=gl_VertexIndex==0?vec2(-1,-1):gl_VertexIndex==1?vec2(3,-1):vec2(-1,3);gl_Position=vec4(p,0,1);}",ShaderStages.Vertex),
            VulkanShaderCompiler.CompileSource("depth-resolve.frag","""
                layout(set=0,binding=0) uniform texture2DMS sourceDepth;
                layout(set=0,binding=1) uniform sampler sourceSampler;
                void main(){
                    ivec2 p=ivec2(gl_FragCoord.xy);
                    int count=textureSamples(sampler2DMS(sourceDepth,sourceSampler));
                    float depth=1.0;
                    for(int i=0;i<16;i++){if(i>=count)break;depth=min(depth,texelFetch(sampler2DMS(sourceDepth,sourceSampler),p,i).r);}
                    gl_FragDepth=depth;
                }
                """,ShaderStages.Fragment));
        Pipeline=factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.Empty,
            new DepthStencilStateDescription(true,true,ComparisonKind.Always),RasterizerStateDescription.CullNone,PrimitiveTopology.TriangleList,
            new ShaderSetDescription([],_shaders),[Layout],
            new OutputDescription(new OutputAttachmentDescription(PixelFormat.D24_UNorm_S8_UInt),Array.Empty<OutputAttachmentDescription>())));
        }
        public void Dispose(){Pipeline.Dispose();Layout.Dispose();Sampler.Dispose();foreach(var shader in _shaders)shader.Dispose();}
    }
}
