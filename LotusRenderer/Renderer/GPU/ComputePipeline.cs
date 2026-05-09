using System;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

public unsafe class GpuComputePipeline : IDisposable
{
    private ShaderModule* _shaderModule;
    private ComputePipeline* _pipeline;

    public ComputePipeline* Handle => _pipeline;
    
    public static GpuComputePipeline FromEmbedded(string resourceName, PipelineLayout* layout, string entryPoint = "main", string label = null)
    {
        string shaderCode = ShaderLoader.LoadEmbedded(resourceName);
        label ??= resourceName;
        return new GpuComputePipeline(shaderCode, layout, entryPoint, label);
    }
    
    
    public GpuComputePipeline(string shaderCode, PipelineLayout* layout, string entryPoint = "main", string label = "Compute Pipeline")
    {
        var api = LWGPU.Instance.Api;
        var device = LWGPU.Instance.Device;

        byte* shaderCodePtr = null;
        byte* shaderLabelPtr = null;
        byte* pipelineLabelPtr = null;
        byte* entryPointPtr = null;

        try
        {
            shaderCodePtr = (byte*)SilkMarshal.StringToPtr(shaderCode);
            shaderLabelPtr = (byte*)SilkMarshal.StringToPtr($"{label} Shader");
            pipelineLabelPtr = (byte*)SilkMarshal.StringToPtr(label);
            entryPointPtr = (byte*)SilkMarshal.StringToPtr(entryPoint);

            var shaderModuleWGSLDescriptor = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct
                {
                    SType = SType.ShaderModuleWgslDescriptor,
                    Next = null
                },
                Code = shaderCodePtr
            };

            var shaderModuleDescriptor = new ShaderModuleDescriptor
            {
                NextInChain = (ChainedStruct*)(&shaderModuleWGSLDescriptor),
                Label = shaderLabelPtr
            };
            
            _shaderModule = api.DeviceCreateShaderModule(device, shaderModuleDescriptor);
            if (_shaderModule == null)
            {
                
                
                throw new Exception($"Failed to create shader module: {label}");
            }
                
            var pipelineDescriptor = new ComputePipelineDescriptor
            {
                Label = pipelineLabelPtr,
                Layout = layout,  
                Compute = new ProgrammableStageDescriptor
                {
                    Module = _shaderModule,
                    EntryPoint = entryPointPtr
                }
            };
            
            _pipeline = api.DeviceCreateComputePipeline(device, pipelineDescriptor);
            if (_pipeline == null)
                throw new Exception($"Failed to create compute pipeline: {label}");
        }
        finally
        {
            if(shaderCodePtr != null)
                SilkMarshal.Free((nint)shaderCodePtr);
            if(shaderLabelPtr != null)
                SilkMarshal.Free((nint)shaderLabelPtr);
            if(pipelineLabelPtr != null)
                SilkMarshal.Free((nint)pipelineLabelPtr);
            if(entryPointPtr != null)
                SilkMarshal.Free((nint)entryPointPtr);
        }
    }

    public BindGroupLayout* GetBindGroupLayout(uint index)
    {
        var api = LWGPU.Instance.Api;
        return api.ComputePipelineGetBindGroupLayout(_pipeline, index);
    }
    
    ~GpuComputePipeline() => Dispose();
    
    public void Dispose()
    {
        var api = LWGPU.Instance.Api;
        
        if(_pipeline != null)
            api.ComputePipelineRelease(_pipeline);
            
        if(_shaderModule != null)
            api.ShaderModuleRelease(_shaderModule);

        _pipeline = null;
        _shaderModule = null;
        
        GC.SuppressFinalize(this);
    }
}