using System;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;
using Silk.NET.WebGPU;

namespace LotusRenderer.Renderer.Compute;

public unsafe class MegakernelPathTracing : IDisposable
{
    // from this frame onwards, we consider the integration somewhat stable, so we
    // can enable a few things like post processing, etc
    private const int STABLE_FRAME_THRESHOLD = 20;
    
    private Scene _scene;
    private FrameData _frameData;
    
    private GpuComputePipeline _pipeline;
    
    private object _renderLock = new object();
    
    private Random _random = new Random();

    private TraceBuffers _buffers;
    private BindGroups _bindGroups;

    private Film _film;
    
    
    private PipelineLayout* _pipelineLayout;

    public uint IterationNumber => _frameData.FrameNumber;
    public float FrameEta { get; private set; }
    public bool IsStable { get; private set; }
    
    public void Initialize(Scene scene, Film film, TraceBuffers buffers, BindGroups bindGroups)
    {
        // todo: remove film from the integrator kernel
        // and move it to a post processing kernel I'm creating soon
        _film = film;
        
        _scene = scene;
        _buffers = buffers;
        _bindGroups = bindGroups;
        
        _frameData = new FrameData();
        _frameData.FrameNumber = 0;
        FrameEta = 0;
        
        var layouts = stackalloc BindGroupLayout*[] 
        {
            _bindGroups.layout_uniforms,       // Group 0
            _bindGroups.layout1_sceneData,      // Group 1
            _bindGroups.layout2_textureTargets, // Group 2
            _bindGroups.layout3_textures        // Group 3
        };
        
        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayouts = layouts,
            BindGroupLayoutCount = 4 // Must match array length
        };
        
        _pipelineLayout = LWGPU.Instance.Api
            .DeviceCreatePipelineLayout(LWGPU.Instance.Device, pipelineLayoutDesc);
        
        _pipeline = GpuComputePipeline.FromEmbedded("kernel_mega_tracer", _pipelineLayout);
    }

    // this is called after Scene or some other things changed
    // that should generate things to be reset
    private void RecreateData()
    {
        lock (_renderLock)
        {
            if (_scene.HdriChanged)
            {
                _bindGroups.ReCreateBindGroup_0Uniforms();
                _bindGroups.ReCreateBindGroup_3Textures();
            }
            
            using var updatePass = new ComputePass();
            updatePass.WriteBuffer(_buffers.sceneData, [_scene.Data]);
            
            _buffers.ResetTextureTargets(_scene);
            _scene.ConfirmChangesWereHandled();
        }
    }
    
    public void RenderFrame(float deltaTime)
    {
        IsStable = _frameData.FrameNumber > STABLE_FRAME_THRESHOLD;
        
        if (_scene.SceneChanged)
        {
            FrameEta = 0;
            _frameData.FrameNumber = 0;
            _frameData.Rnd = (uint)_random.NextInt64(0, int.MaxValue);
            RecreateData();
        }

        FrameEta += deltaTime;

        lock (_renderLock)
        {

            if (_scene.ViewData.viewIsMoving == 1u)
            {
                _frameData.FrameNumber = 0;
            }
            else
            {
                _frameData.FrameNumber++;
                _frameData.Rnd = (uint)_random.NextInt64(0, int.MaxValue);
            }
            
            using var framePass = new ComputePass();

            framePass.WriteBuffer(_buffers.cameraData, [CameraController.GetData()]);
            framePass.WriteBuffer(_buffers.frameData, [_frameData]);
            framePass.WriteBuffer(_buffers.viewData, [_scene.ViewData]);
            framePass.WriteBuffer(_buffers.filmData, [_film.Data]);

            uint workgroupsX = (_scene.Width + 7) / 8;
            uint workgroupsY = (_scene.Height + 7) / 8;

            using (ProfileRegion.Start("Frame Pass"))
            {
                framePass.Begin("Megakernel Compute Pass");
                framePass.SetPipeline(_pipeline);
                framePass.SetBindGroup(0, _bindGroups.bindGroup_globalUniforms);
                framePass.SetBindGroup(1, _bindGroups.bindGroup1_sceneData);
                framePass.SetBindGroup(2, _bindGroups.bindGroup2_textureTargets);
                framePass.SetBindGroup(3, _bindGroups.bindGroup3_textures);
                framePass.Dispatch(workgroupsX, workgroupsY, 1);
                framePass.End();
                
                framePass.Submit("Megakernel Compute Pass"); 
                
                LWGPU.Instance.Extension.DevicePoll(LWGPU.Instance.Device, true, null);
            }
        }
    }

    public void Dispose()
    {
        _pipeline.Dispose();
        LWGPU.Instance.Api.PipelineLayoutRelease(_pipelineLayout);
    }
}