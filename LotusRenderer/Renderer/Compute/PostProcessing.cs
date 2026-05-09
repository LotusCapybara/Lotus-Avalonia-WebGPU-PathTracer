using System;
using System.Diagnostics;
using LotusRenderer.Renderer.Compute.Post;
using LotusRenderer.Renderer.World;
using Buffer = System.Buffer;

namespace LotusRenderer.Renderer.Compute;

public unsafe class PostProcessing : IDisposable
{
    // Bloom is enabled after these amount of iterations, to avoid strong spikes
    private const int BLOOM_SKIP_FRAMES = 5;
    
    private Scene _scene;
    private TraceBuffers _buffers;
    
    // post processing buffers
    public GpuBuffer bfPackedOutput;  // the final output that's readback to cpu
    public GpuBuffer bfColorInput;    // one width*height linear buffer used for inputs
    public GpuBuffer bfColorOutput;   // one width*height linear buffer used for outputs
    
    
    // -- read back
    private object _readLock = new object();
    private bool _readbackInFlight = false;
    private uint[] _readbackBuffer;

    // -- sub steps
    
    private Post_PackToFinal _postPackToFinal;
    private Post_Exposure _postExposure;
    private Post_Bloom _postBloom;
    private Post_ACES _postAces;
    
    public PostProcessing(Scene scene, TraceBuffers buffers, BindGroups globalBinds)
    {
        _scene = scene;
        _buffers = buffers;

        MakeBuffers();
        
        int elementCount = (int)(_scene.Width * _scene.Height);
        _readbackBuffer = new uint[elementCount];

        _postPackToFinal = new Post_PackToFinal(this, buffers, scene, globalBinds);
        _postExposure = new Post_Exposure(this, scene, globalBinds);
        _postBloom = new Post_Bloom(this, buffers, scene, globalBinds);
        _postAces = new Post_ACES(this, scene, globalBinds);
    }

    private void MakeBuffers()
    {
        ulong u32ImgSize  = _scene.Width * _scene.Height * sizeof(uint);
        ulong fpImgSize   = _scene.Width * _scene.Height * sizeof(float) * 4;
        
        bfPackedOutput = GpuBuffer.CreateStorage(u32ImgSize, "ppPackedOutput");
        bfColorInput   = GpuBuffer.CreateStorage(fpImgSize, "ppInput");
        bfColorOutput  = GpuBuffer.CreateStorage(fpImgSize, "ppOutput");
    }

    public void Execute(int frameNumber)
    {
        lock (_readLock)
        {
            Debug.Assert(_buffers.accumulation.Size == bfColorInput.Size, "Accumulation/bfColorInput size mismatch");
            
            using var copyAccumPass = new ComputePass();
            copyAccumPass.Begin();
            copyAccumPass.End();
            copyAccumPass.CopyBufferToBuffer(_buffers.accumulation, bfColorInput, _buffers.accumulation.Size);
            copyAccumPass.Submit("Prepare Input for Postprocessing");

            _postExposure.Execute();

            if (frameNumber > BLOOM_SKIP_FRAMES)
            {
                _postBloom.Execute();
                _postAces.Execute();
            }
            
            _postPackToFinal.Execute();
        }

        LWGPU.Instance.Extension.DevicePoll(LWGPU.Instance.Device, true, null);
    }

    public void GetPixelsData(byte[] outputData)
    {
        if(_readbackInFlight)
            return;

        lock (_readLock)
        {
            Debug.Assert(_readbackBuffer != null);
            _readbackInFlight = true;
            using var readPass = new ComputePass();
            readPass.ReadBuffer<uint>(_buffers.staging, _readbackBuffer);
            Buffer.BlockCopy(_readbackBuffer, 0, outputData, 0, outputData.Length);
            _readbackInFlight = false;
        }
    }
    

    public void Dispose()
    {
        _postExposure.Dispose();
        _postPackToFinal.Dispose();
        _postBloom.Dispose();
        _postAces.Dispose();
        
        bfPackedOutput.Dispose();
        bfColorInput.Dispose();
        bfColorOutput.Dispose();
    }
}