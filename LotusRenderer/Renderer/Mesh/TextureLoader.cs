using System;
using System.Collections.Generic;
using System.IO;
using SharpGLTF.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace LotusRenderer.Renderer.Mesh;

public class TextureLoader
{
    public const int TXT_WIDTH = 2048;
    public const int TXT_HEIGHT = 2048;
    public const int BYTES_PER_PIXEL = 4;
    
    public static byte[] ProcessTextures(IList<MemoryImage> images, out int layerCount)
    {
        layerCount = images.Count;
        if (layerCount == 0) return Array.Empty<byte>();

        // Allocate giant buffer: Width * Height * 4 * Layers
        long totalBytes = (long)TXT_WIDTH * TXT_HEIGHT * BYTES_PER_PIXEL * layerCount;
        byte[] textureData = new byte[totalBytes];

        // todo: refactor to parallel for loop
        for (int i = 0; i < layerCount; i++)
        {
            var gltfImage = images[i];
            
            // Open the bytes
            using var stream = new MemoryStream(gltfImage.Content.ToArray());
            using var image = Image.Load<Rgba32>(stream);

            // Resize to our standard format
            image.Mutate(x => x.Resize(TXT_WIDTH, TXT_HEIGHT));

            // Copy pixels directly to our giant buffer
            // Calculate offset for this layer
            int layerOffset = i * TXT_WIDTH * TXT_HEIGHT * BYTES_PER_PIXEL;
            
            // ImageSharp lets us copy raw pixel bytes
            image.CopyPixelDataTo(new Span<byte>(textureData, layerOffset, TXT_WIDTH * TXT_HEIGHT * BYTES_PER_PIXEL));
        }

        return textureData;
    }
}