using System;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

public unsafe class GpuBuffer : IDisposable
{
    private Buffer* _buffer;
    
    public Buffer* Handle => _buffer;
    public ulong Size { get; private set; }
    public BufferUsage Usage { get; private set; }

    public static GpuBuffer CreateStorage(ulong size, string label)
    {
        return new GpuBuffer(size, BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc, label);
    }
    
    public static GpuBuffer CreateStaging(ulong size, string label)
    {
        return new GpuBuffer(size, BufferUsage.MapRead | BufferUsage.CopyDst, label);
    }
    
    public static GpuBuffer CreateUniform(ulong size, string label)
    {
        return new GpuBuffer(size, BufferUsage.CopySrc |BufferUsage.Uniform | BufferUsage.CopyDst, label);
    }
    
    public GpuBuffer(ulong size, BufferUsage usage, string label)
    {
        Size = size;
        Usage = usage;

        var api = LWGPU.Instance.Api;
        var device = LWGPU.Instance.Device;

        byte* labelPtr = (byte*)SilkMarshal.StringToPtr(label);

        try
        {
            var descriptor = new BufferDescriptor
            {
                Label = labelPtr,
                Usage = usage,
                Size = size,
                MappedAtCreation = false
            };

            _buffer = api.DeviceCreateBuffer(device, descriptor);

            if (_buffer == null)
                throw new Exception($"Failed to create buffer: {label}");

        }
        finally
        {
            SilkMarshal.Free((nint)labelPtr);
        }
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            var api = LWGPU.Instance.Api;
            api.BufferDestroy(_buffer);
            api.BufferRelease(_buffer);
            _buffer = null;
        }
        
        GC.SuppressFinalize(this);
    }
}