using System;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Renderer.World;
using Silk.NET.WebGPU;
using Buffer = System.Buffer;

namespace LotusRenderer.Renderer.Compute;

public unsafe class Kernel_DebugBuffers : IDisposable
{
    private Scene _scene;
    private FrameData _frameData;
    
    private GpuComputePipeline _pipeline;
    
    private object _renderLock = new object();
    
    private Random _random = new Random();

    private TraceBuffers _buffers;
    private BindGroups _bindGroups;
    
    // -- read back
    private object _readLock = new object();
    private bool _readbackInFlight = false;
    
    private uint[] _readbackBuffer;
    
    private PipelineLayout* _pipelineLayout;

    public float FrameEta { get; private set; }
    
    public void Initialize(Scene scene, TraceBuffers buffers, BindGroups bindGroups)
    {
        _scene = scene;
        _buffers = buffers;
        _bindGroups = bindGroups;
        
        _frameData = new FrameData();
        _frameData.FrameNumber = 0;
        FrameEta = 0;
        
        // init buffers: output
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
        
        _pipeline = GpuComputePipeline.FromEmbedded("kernel_debug_targets", _pipelineLayout);
        
        int elementCount = (int)(_scene.Width * _scene.Height);
        _readbackBuffer = new uint[elementCount];
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
            
            _scene.ConfirmChangesWereHandled();
        }
    }
    
    public void RenderFrame(float deltaTime)
    {
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

            using var framePass = new ComputePass();

            framePass.WriteBuffer(_buffers.cameraData, [CameraController.GetData()]);
            framePass.WriteBuffer(_buffers.frameData, [_frameData]);
            framePass.WriteBuffer(_buffers.viewData, [_scene.ViewData]);

            uint workgroupsX = (_scene.Width + 7) / 8;
            uint workgroupsY = (_scene.Height + 7) / 8;

            using (ProfileRegion.Start("Frame Pass"))
            {
                framePass.Begin("Debug Targets Compute Pass");
                framePass.SetPipeline(_pipeline);
                framePass.SetBindGroup(0, _bindGroups.bindGroup_globalUniforms);
                framePass.SetBindGroup(1, _bindGroups.bindGroup1_sceneData);
                framePass.SetBindGroup(2, _bindGroups.bindGroup2_textureTargets);
                framePass.SetBindGroup(3, _bindGroups.bindGroup3_textures);
                framePass.Dispatch(workgroupsX, workgroupsY, 1);
                framePass.End();
                
                bool canCopy;
                lock (_readLock)
                {
                    canCopy = !_readbackInFlight;
                }

                if (canCopy)
                {
                    framePass.CopyBufferToBuffer(_buffers.output, _buffers.staging, _buffers.output.Size);
                }
                
                framePass.Submit("Debug Targets Compute Pass"); 
            }
            
            _frameData.FrameNumber++;
            _frameData.Rnd = (uint)_random.NextInt64(0, int.MaxValue);
        }
    }

    public void GetPixelsData(byte[] outputData)
    {
        if(_readbackInFlight)
            return;

        lock (_readLock)
        {
            _readbackInFlight = true;

            
            using var readPass = new ComputePass();
            readPass.ReadBuffer<uint>(_buffers.staging, _readbackBuffer);
            Buffer.BlockCopy(_readbackBuffer, 0, outputData, 0, outputData.Length);

            _readbackInFlight = false;
        }
    }
    
    

    public void Dispose()
    {
        _pipeline.Dispose();
        LWGPU.Instance.Api.PipelineLayoutRelease(_pipelineLayout);
    }
}