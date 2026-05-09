using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LotusRenderer.Renderer.Compute;
using LotusRenderer.Renderer.World;
using LotusRenderer.Views;

namespace LotusRenderer.Renderer;

public enum ERenderMode
{
    PathTracing, DebugTargets
}

public class Renderer : IDisposable
{
    public static object gpuLock = new();
    
    private Scene _scene;
    private Film _film;
    private PostProcessing _postProcessing;
    private TraceBuffers _traceBuffers;
    private BindGroups _bindGroups;
    
    private MegakernelPathTracing _megakernelPathTracing;
    private Kernel_DebugBuffers _kernelDebugBuffers;
    
    private WriteableBitmap _bitmap;

    private CancellationTokenSource _ctsGameLoop;
    private Task _gameLoopTask;

    public static Action onFrameFinished;
    
    private int _stepCounter = 0;

    private bool _isPaused = false;
    
    private byte[] _bitmapBuffer;
    
    private Stopwatch _swRender;
    private readonly Stopwatch _swDelta = new Stopwatch();
    
    public LWGPU LWGpu { get; private set; }
    public WriteableBitmap Bitmap => _bitmap;
    
    public Renderer()
    {
        LWGpu = new LWGPU();
        
        _swRender = Stopwatch.StartNew();
        
        // todo generate scene from files and viewport size
        // maybe I also need a viewport class?
        _scene = new Scene(1920, 1080);
        _film = new Film(_scene);

        _bitmap = new WriteableBitmap(
            new PixelSize((int)_scene.Width, (int)_scene.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            null
        );
        
        _traceBuffers = new TraceBuffers(_scene, _film);

        if (Path.Exists(Config.I().modelFilePath))
        {
            _scene.LoadGlbScene(Config.I().modelFilePath);
        }
        else
        {
            _scene.FreeMeshData();
        }
        
        _traceBuffers.SetSceneData(_scene);
        _bindGroups = new BindGroups(_traceBuffers, _scene);
        
        _megakernelPathTracing = new MegakernelPathTracing();
        _megakernelPathTracing.Initialize(_scene, _film, _traceBuffers, _bindGroups);

        _kernelDebugBuffers = new Kernel_DebugBuffers();
        _kernelDebugBuffers.Initialize(_scene, _traceBuffers, _bindGroups);
        
        _postProcessing = new PostProcessing(_scene, _traceBuffers, _bindGroups);
        
        _bitmapBuffer = new byte[_scene.Width * _scene.Height * 4];
        
        // re init after all system are up
        _traceBuffers.WriteInitialData(_scene, _film);
        
        InputController.OnKeyPressed += OnKeyPressed;
        
        GameLoop();

        SidePanel.ModelCleared += OnGlbSceneCleared;
        SidePanel.ModelChanged += OnGlbSceneChanged;
    }

    private void OnGlbSceneCleared()
    {
        lock (gpuLock)
        {
            _scene.FreeMeshData();
            _traceBuffers.SetSceneData(_scene);
            _bindGroups.ReCreateBindGroup_0Uniforms();
            _bindGroups.ReCreateBindGroup_1SceneData();
            _bindGroups.ReCreateBindGroup_3Textures();
            _traceBuffers.ResetTextureTargets(_scene);
        }
    }
    
    private void OnGlbSceneChanged()
    {
        lock (gpuLock)
        {
            _scene.LoadGlbScene(Config.I().modelFilePath);
            _traceBuffers.SetSceneData(_scene);
            _bindGroups.ReCreateBindGroup_0Uniforms();
            _bindGroups.ReCreateBindGroup_1SceneData();
            _bindGroups.ReCreateBindGroup_3Textures();
            _traceBuffers.ResetTextureTargets(_scene);
        }
    }
    

    private void OnKeyPressed(Key key)
    {
        if (key == Key.Space)
        {
            _isPaused = !_isPaused;
            if(_isPaused)
                _swRender.Stop();
            else 
                _swRender.Start();
                
            Global.isPaused = _isPaused;
            onFrameFinished?.Invoke();
        }
    }
    
    private void UpdateViewportTexture()
    {
        using (ProfileRegion.Start("ReadBack Staging"))
        {
            bool textureUpdated = false;
            
            
            switch (SelectedViewModes.RenderMode)
            {
                case ERenderMode.PathTracing:
                    textureUpdated = true;

                    using (ProfileRegion.Start("Post Pro"))
                    {
                        if (!_megakernelPathTracing.IsStable ||  _stepCounter % 5 == 0)
                        {
                            _postProcessing.Execute((int)_megakernelPathTracing.IterationNumber);
                            _postProcessing.GetPixelsData(_bitmapBuffer);
                        }
                    }
                    break;
                case ERenderMode.DebugTargets:
                    _kernelDebugBuffers.GetPixelsData(_bitmapBuffer);
                    textureUpdated = true;
                    break;
            }

            if (textureUpdated)
            {
                using (var locked = _bitmap.Lock())
                {
                    Marshal.Copy(
                        _bitmapBuffer,
                        0,
                        locked.Address,
                        _bitmapBuffer.Length
                    );
                }
            }
        }
    }
    
    private void RenderFrame(float deltaTime)
    {
        unsafe
        {
            // gpu frame iteration
            using (ProfileRegion.Start("RenderFrame"))
            {
                switch (SelectedViewModes.RenderMode)
                {
                    case ERenderMode.PathTracing:
                        _megakernelPathTracing.RenderFrame(deltaTime);
                        
                        break;
                    case ERenderMode.DebugTargets:
                        _kernelDebugBuffers.RenderFrame(deltaTime);
                        break;
                }
            }
        
            UpdateViewportTexture();
            Global.frameTimesString = ProfileTimer.GetFrameTimingAndReset();
        
            Dispatcher.UIThread.Post(() =>
            {
                onFrameFinished?.Invoke();
            });
        
            _stepCounter++;
        }
    }

    private void GameLoop()
    {
        _swDelta.Restart();
        
        _ctsGameLoop = new CancellationTokenSource();
        _gameLoopTask = Task.Run(async () =>
        {
            _swRender.Start();
            
            double lastTime = _swDelta.Elapsed.TotalSeconds;

            while (!_ctsGameLoop.IsCancellationRequested)
            {
                InputController.Tick();

                double now = _swDelta.Elapsed.TotalSeconds;
                // delta time is capped at 100ms. If a frame takes longer, then scene won't jump forward by seconds.
                // this is ok-ish in a renderer, but in simulations/game engines a different approach would be better
                float deltaTime = MathF.Min((float)(now - lastTime), 0.1f); 
                lastTime = now;

                if (!_isPaused)
                {
                    using (ProfileRegion.Start("Scene Tick"))
                    {
                        _scene.Tick(deltaTime);
                    }
                    
                    lock (gpuLock)
                    {
                        RenderFrame(deltaTime);
                    }
                    
                    // if the integrator just started/re-started, then we restart 
                    // the renderer and profiler values
                    if (Global.iterationNumber <= 1)
                    {
                        _swRender.Restart();
                        ProfileTimer.ResetAverages();
                    }

                    Global.iterationNumber = _megakernelPathTracing.IterationNumber;
                    Global.frameEta = (float)_swRender.Elapsed.TotalSeconds;

                    await Task.Delay(1, _ctsGameLoop.Token).ConfigureAwait(false);
                }
            }

        }, _ctsGameLoop.Token);
    }

    public void Dispose()
    {
        _ctsGameLoop.Cancel();
        
        try
        {
            _gameLoopTask.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
        {
            // Expected during shutdown. Task.Delay throws when the cancellation token fires
        }
        
        _megakernelPathTracing.Dispose();
        _kernelDebugBuffers.Dispose();
        _bindGroups.Dispose();
        _traceBuffers.Dispose();
        _scene.Dispose();
        
        _bitmap.Dispose();
        LWGpu.Dispose();
        
        InputController.OnKeyPressed -= OnKeyPressed;
        SidePanel.ModelCleared -= OnGlbSceneCleared;
        SidePanel.ModelChanged -= OnGlbSceneChanged;
        onFrameFinished = null; 

    }
}