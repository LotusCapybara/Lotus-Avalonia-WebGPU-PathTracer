using System;
using System.Collections.Generic;
using Silk.NET.WebGPU;

public unsafe class LayoutBuilder : IDisposable
{
    private readonly List<BindGroupLayoutEntry> _entries = new();

    public LayoutBuilder AddUniform(uint binding, ShaderStage visibility = ShaderStage.Compute)
    {
        _entries.Add(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                MinBindingSize = 0 // Or sizeof(T) if you want strict validation
            }
        });
        return this;
    }

    public LayoutBuilder AddStorage(uint binding, bool readOnly = true, ShaderStage visibility = ShaderStage.Compute)
    {
        _entries.Add(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Buffer = new BufferBindingLayout
            {
                Type = readOnly ? BufferBindingType.ReadOnlyStorage : BufferBindingType.Storage,
                MinBindingSize = 0 
            }
        });
        return this;
    }
    
    public LayoutBuilder AddTexture(uint binding, 
        TextureSampleType type = TextureSampleType.Float, 
        TextureViewDimension dimension = TextureViewDimension.Dimension2D,
        ShaderStage visibility = ShaderStage.Compute)
    {
        _entries.Add(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Texture = new TextureBindingLayout
            {
                SampleType = type,
                ViewDimension = dimension,
                Multisampled = false
            }
        });
        return this;
    }
    
    public LayoutBuilder AddStorageTexture(
        uint binding, 
        TextureFormat format, 
        StorageTextureAccess access = StorageTextureAccess.WriteOnly, 
        TextureViewDimension dimension = TextureViewDimension.Dimension2D,
        ShaderStage visibility = ShaderStage.Compute)
    {
        _entries.Add(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            // We set the StorageTexture field, NOT the Texture field
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = access,
                Format = format,
                ViewDimension = dimension
            }
        });
        return this;
    }
    
    
    public LayoutBuilder AddSampler(uint binding, SamplerBindingType type = SamplerBindingType.Filtering, ShaderStage visibility = ShaderStage.Compute)
    {
        _entries.Add(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Sampler = new SamplerBindingLayout { Type = type }
        });
        return this;
    }

    public BindGroupLayout* Build(Device* device)
    {
        var entriesArr = _entries.ToArray();
        fixed (BindGroupLayoutEntry* ptr = entriesArr)
        {
            var desc = new BindGroupLayoutDescriptor
            {
                EntryCount = (uint)_entries.Count,
                Entries = ptr
            };
            return LWGPU.Instance.Api.DeviceCreateBindGroupLayout(device, desc);
        }
    }
    
    public void Dispose() { /* clean up entries if needed */ }
}