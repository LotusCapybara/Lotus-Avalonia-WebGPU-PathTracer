using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

public unsafe class ComputePass : IDisposable
{
    private readonly WebGPU _api;
    private readonly Wgpu _wgpuExt;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private CommandEncoder* _encoder;
    private ComputePassEncoder* _passEncoder;
    private bool _isRecording = false;
    
    public ComputePass()
    {
        _api = LWGPU.Instance.Api;
        _wgpuExt = LWGPU.Instance.Extension;
        _device = LWGPU.Instance.Device;
        _queue = _api.DeviceGetQueue(_device);
    }

    // begins recording commands to be sent as a pass
    public void Begin(string label = "Compute Pass")
    {
        if (_encoder != null)
            throw new InvalidOperationException("Enconder re created");
        if (_isRecording)
            throw new InvalidOperationException("Pass already recording");
        
        byte* encoderLabelPtr = null;
        byte* passLabelPtr = null;
        
        try
        {
            encoderLabelPtr = (byte*)SilkMarshal.StringToPtr($"{label} Encoder");
            passLabelPtr = (byte*)SilkMarshal.StringToPtr(label);
            
            var encoderDescriptor = new CommandEncoderDescriptor
            {
                Label = encoderLabelPtr
            };
            _encoder = _api.DeviceCreateCommandEncoder(_device, encoderDescriptor);
            
            var passDescriptor = new ComputePassDescriptor
            {
                Label = passLabelPtr
            };
            _passEncoder = _api.CommandEncoderBeginComputePass(_encoder, passDescriptor);
            
            _isRecording = true;
        }
        finally
        {
            if (encoderLabelPtr != null) 
                SilkMarshal.Free((nint)encoderLabelPtr);
            if (passLabelPtr != null) 
                SilkMarshal.Free((nint)passLabelPtr);
        }
    }
    
    public void SetPipeline(GpuComputePipeline pipeline)
    {
        // pipeline can only be set if the pass is in recording state
        if (!_isRecording) 
            throw new InvalidOperationException("Pass not recording");
        
        _api.ComputePassEncoderSetPipeline(_passEncoder, pipeline.Handle);
    }
    
    public void SetBindGroup(uint groupIndex, BindGroup* bindGroup)
    {
        // group can only be set if the pass is in recording state
        if (!_isRecording) 
            throw new InvalidOperationException("Pass not recording");
        
        _api.ComputePassEncoderSetBindGroup(_passEncoder, groupIndex, bindGroup, 0, null);
    }
    
    public void Dispatch(uint workgroupsX, uint workgroupsY = 1, uint workgroupsZ = 1)
    {
        if (!_isRecording) 
            throw new InvalidOperationException("Pass not recording");
        
        _api.ComputePassEncoderDispatchWorkgroups(_passEncoder, workgroupsX, workgroupsY, workgroupsZ);
    }
    
    public void End()
    {
        if (!_isRecording) 
            throw new InvalidOperationException("Pass not recording");
        
        _api.ComputePassEncoderEnd(_passEncoder);
        _passEncoder = null;
        _isRecording = false;
    }
    
    public void CopyBufferToBuffer(GpuBuffer source, GpuBuffer destination, ulong size, ulong sourceOffset = 0, ulong destOffset = 0)
    {
        if (_isRecording) 
            throw new InvalidOperationException("Must call End() before copying buffers");
        
        _api.CommandEncoderCopyBufferToBuffer(_encoder, 
            source.Handle, sourceOffset, 
            destination.Handle, destOffset, 
            size);
    }
    
    public void Submit(string label = "Command Buffer")
    {
        if (_encoder == null)
        {
            Console.WriteLine("[ERROR][ComputePass] No encoder to submit.");
            throw new InvalidOperationException("No encoder to submit.");
        }

        if (_isRecording)
        {
            Console.WriteLine("[ERROR][ComputePass] Must call End() before Submit()");
            throw new InvalidOperationException("Must call End() before Submit()");
        }
        
        byte* labelPtr = (byte*)SilkMarshal.StringToPtr(label);
        try
        {
            var cmdBufferDescriptor = new CommandBufferDescriptor
            {
                Label = labelPtr
            };
            
            var cmdBuffer = _api.CommandEncoderFinish(_encoder, cmdBufferDescriptor);
            
            _api.QueueSubmit(_queue, 1, &cmdBuffer);
            _api.CommandBufferRelease(cmdBuffer);
            _api.CommandEncoderRelease(_encoder);
            _encoder = null;
        }
        finally
        {
            SilkMarshal.Free((nint)labelPtr);
        }
    }
    
    public void WriteBuffer<T>(GpuBuffer buffer, T[] data, ulong offset = 0) where T : unmanaged
    {
        ulong size = (ulong)(data.Length * Marshal.SizeOf<T>());
        fixed (T* dataPtr = data)
        {
            _api.QueueWriteBuffer(_queue, buffer.Handle, offset, dataPtr, (UIntPtr)size); ;
        }
    }
   
    public void ReadBuffer<T>(GpuBuffer stagingBuffer, T[] destination) where T : unmanaged
    {
        T[] localDest = destination;
        uint elementCount = (uint)localDest.Length;

        _api.BufferMapAsync(
            stagingBuffer.Handle,
            MapMode.Read,
            0,
            (nuint)(elementCount * (uint)sizeof(T)), // map only what we need
            new PfnBufferMapCallback((status, userData) =>
            {
                if (status != BufferMapAsyncStatus.Success)
                {
                    Console.WriteLine($"Buffer map failed: {status}");
                    return;
                }

                var resultPtr = (T*)_api.BufferGetMappedRange(
                    stagingBuffer.Handle,
                    0,
                    (nuint)(elementCount * (uint)sizeof(T)));

                for (int i = 0; i < elementCount; i++)
                {
                    localDest[i] = resultPtr[i];
                }
            }),
            null);

        _wgpuExt.DevicePoll(_device, true, null);
        _api.BufferUnmap(stagingBuffer.Handle);
    }


    public void Dispose()
    {
        if (_isRecording && _passEncoder != null)
        {
            _api.ComputePassEncoderEnd(_passEncoder);
            _passEncoder = null;
            _isRecording = false;
        }

        if (_encoder != null)
        {
            _api.CommandEncoderRelease(_encoder);
            _encoder = null;
        }
        
        GC.SuppressFinalize(this);
    }
}