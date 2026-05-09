using System;
using ImageMagick;
using Silk.NET.WebGPU;
using Buffer = System.Buffer;

public enum EHdriDisplayMode
{
    ShowImage = 0,
    HideImage = 1,
}

public unsafe class HdrImage : IDisposable
{
    // Constants for WebGPU alignment
    private const uint BYTES_PER_PIXEL = 8; // Half (2 bytes) * 4 Channels
    private const uint ALIGNMENT = 256;
    
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    
    public TextureView* View  { get; private set; }
    public Sampler* Sampler { get; private set; }
    public Texture* Texture { get; private set; }

    public HdrImage(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                LoadFromFile(filePath);
                return;
            }
            catch (Exception)
            {
                // File missing or unreadable, fall through to fallback
                Console.WriteLine($"[HdrImage] Warning. Cannot Load image file: {filePath}. Creating fallback hdri");
            }
        }
        
        CreateFallback();

    }
    
    private void CreateFallback()
    {
        Width = 4;
        Height = 4;
        Half[] halfData = new Half[Width * Height * 4];     

        // solid neutral grey environment
        for (int i = 0; i < Width * Height; i++)
        {
            halfData[i * 4 + 0] = (Half)0.2f;
            halfData[i * 4 + 1] = (Half)0.2f;
            halfData[i * 4 + 2] = (Half)0.2f;
            halfData[i * 4 + 3] = (Half)1.0f;
        }
        
        // --- Upload Logic
        UploadToGPU(halfData);
    }

    private void LoadFromFile(string filePath)
    {
        
        using var img = new MagickImage(filePath);

        img.ColorSpace = ColorSpace.RGB;
        
        Width = img.Width;
        Height = img.Height;
        
        using var pixels = img.GetPixels();
        int pixelCount = (int) (Width * Height);
        int channelCount = (int)img.ChannelCount;
        Half[] halfData = new Half[pixelCount * 4];
        
        float scale = 1.0f / Quantum.Max;
        
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var pixel = pixels.GetPixel(x, y);
                int idx = (y * (int)Width + x) * 4;

                float r = pixel.GetChannel(0) * scale;
                // Handle Grayscale vs RGB
                float g = (channelCount > 1 ? pixel.GetChannel(1) : pixel.GetChannel(0)) * scale;
                float b = (channelCount > 2 ? pixel.GetChannel(2) : pixel.GetChannel(0)) * scale;
                
                
                halfData[idx + 0] = (Half)r;
                halfData[idx + 1] = (Half)g;
                halfData[idx + 2] = (Half)b;
                halfData[idx + 3] = (Half)1f;
            }
        }

        // --- Upload Logic
        UploadToGPU(halfData);
    }

    private void UploadToGPU(Half[] halfData)
    {
        uint unpaddedBytesPerRow = Width * BYTES_PER_PIXEL;
        uint align = ALIGNMENT - 1;
        uint paddedBytesPerRow = (uint) ((unpaddedBytesPerRow + align) & ~align);
        
        byte[] paddedData = new byte[paddedBytesPerRow * Height];

        fixed (Half* srcPtr = halfData)
        {
            fixed (byte* dstPtr = paddedData)
            {
                for (int y = 0; y < Height; y++)
                {
                    Half* rowSrc = srcPtr + (y * Width * 4);
                    byte* rowDst = dstPtr + (y * paddedBytesPerRow);
                    
                    Buffer.MemoryCopy(rowSrc, rowDst, paddedBytesPerRow, unpaddedBytesPerRow);
                }
            }
        }

        Extent3D size = new()
        {
            Width = Width,
            Height = Height,
            DepthOrArrayLayers = 1
        };

        TextureDescriptor textDesc = new()
        {
            Size = size,
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Rgba16float,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst
        };

        var api = LWGPU.Instance.Api;
        var device = LWGPU.Instance.Device;
        
        Texture = api.DeviceCreateTexture(device, &textDesc);
        
        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = (uint) paddedBytesPerRow,
            RowsPerImage = (uint) Height
        };
        
        ImageCopyTexture destination = new()
        {
            Aspect = TextureAspect.All,
            Texture =  Texture, 
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0)
        };

        fixed (byte* dataPtr = paddedData)
        {
            api.QueueWriteTexture(api.DeviceGetQueue(device), &destination, dataPtr, (nuint) paddedData.Length, &layout, &size);
        }

        TextureViewDescriptor viewDesc = new TextureViewDescriptor
        {
            Format = TextureFormat.Rgba16float,   // half precission
            Dimension = TextureViewDimension.Dimension2D,
            Aspect = TextureAspect.All,
            ArrayLayerCount = 1,
            MipLevelCount = 1
        };
        
        View = api.TextureCreateView(Texture, &viewDesc);

        SamplerDescriptor samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            MaxAnisotropy = 1
        };
        
        Sampler = api.DeviceCreateSampler(device, &samplerDesc);
    }
    
    public void Dispose()
    {
        var api = LWGPU.Instance.Api;
        
        if(Sampler != null)
            api.SamplerRelease(Sampler);            
        if(View != null)
            api.TextureViewRelease(View);
        if (Texture != null)
        {
            api.TextureDestroy(Texture);
            api.TextureRelease(Texture);
        }
            
        
        Sampler = null;
        View = null;
        Texture = null;
    }
}