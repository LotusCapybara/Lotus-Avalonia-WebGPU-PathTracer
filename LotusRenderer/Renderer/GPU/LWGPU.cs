using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

public unsafe class LWGPU : IDisposable
{
    public static LWGPU? Instance { get; private set; }
    
    private readonly WebGPU _wgpu;
    private Adapter* _adapter;
    private readonly Instance* _instance;
    private Device* _device;
    private Wgpu _wgpuSpecific;

    private PfnLogCallback _logCallDelegate;
    private PfnErrorCallback _errorCallbackDelegate;
    
    public WebGPU Api => _wgpu;
    public Wgpu Extension => _wgpuSpecific;
    public Device* Device => _device;
    public Adapter* Adapter => _adapter;

    public LWGPU()
    {
        Instance = this;
        
        _wgpu = WebGPU.GetApi();
        if (!_wgpu.TryGetDeviceExtension(null, out _wgpuSpecific))
            throw new Exception("Failed to get WGPU device extension");

        _logCallDelegate = new PfnLogCallback(GlobalLogCallback);
        _wgpuSpecific.SetLogCallback(_logCallDelegate, null);
        _wgpuSpecific.SetLogLevel(LogLevel.Warn);

        InstanceDescriptor instanceDescriptor = new InstanceDescriptor();
        _instance = _wgpu.CreateInstance(&instanceDescriptor);
        if (_instance == null)
            throw new Exception("Failed to create WebGPU instance");
        
        var requestAdapterOptions = new RequestAdapterOptions();
        _wgpu.InstanceRequestAdapter(
            _instance, 
            &requestAdapterOptions, 
            new PfnRequestAdapterCallback(RequestAdapterCallback), 
            null);

        SupportedLimits supportedLimits = new SupportedLimits();
        _wgpu.AdapterGetLimits(_adapter, &supportedLimits);
        
        supportedLimits.Limits.MaxStorageBuffersPerShaderStage = 16;
        
        var limits = new RequiredLimits
        {
            Limits = supportedLimits.Limits,
        };
        
        var deviceDescriptor = new DeviceDescriptor
        {
            RequiredLimits = &limits
        };
        
        _wgpu.AdapterRequestDevice(
            _adapter, 
            deviceDescriptor, 
            new PfnRequestDeviceCallback(RequestDeviceCallback), 
            null);
        
        SetupDeviceCallbacks();
    }

    private void GlobalLogCallback(LogLevel level, byte* msg, void* userData)
    {
        // todo: add some flag to use or not use this one
        if (level == LogLevel.Debug)
        {
            string message = SilkMarshal.PtrToString((nint) msg) ?? "Unknown null WebGPU message";
        
            var color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warn => ConsoleColor.Yellow,
                LogLevel.Info => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };
        
            Console.ForegroundColor = color;
            Console.WriteLine($"[WGPU {level}] {message}");
            Console.ResetColor();
        }
    }

    private unsafe void RequestAdapterCallback(RequestAdapterStatus arg0, Adapter* received, byte* arg2, void* userdata)
    {
        if(arg0 != RequestAdapterStatus.Success)
        {
            throw new Exception($"Unable to get WebGPU Adapter! status: {arg0} message: {SilkMarshal.PtrToString((nint)arg2)}");
        }

        _adapter = received;
    }
    
    private void RequestDeviceCallback(RequestDeviceStatus arg0, Device* received, byte* arg2, void* arg3)
    {
        if(arg0 != RequestDeviceStatus.Success)
        {
            throw new Exception($"Unable to get WebGPU Device! status: {arg0} message: {SilkMarshal.PtrToString((nint)arg2)}");
        }
        _device = received;
    }
    
    // The Uncaptured Error (Validation Errors)
    private void UncapturedErrorCallback(ErrorType type, byte* msg, void* userdata)
    {
        string rawMessage = Marshal.PtrToStringUTF8((nint)msg) ?? "Unknown Error";
        WGPULog.LogUncaptured(type, rawMessage);
    }

    private void SetupDeviceCallbacks()
    {
        _errorCallbackDelegate = new PfnErrorCallback(UncapturedErrorCallback);
        _wgpu.DeviceSetUncapturedErrorCallback(_device, _errorCallbackDelegate, null);
    }
    
    public void Dispose()
    {
        // Release in reverse order of creation, with null checks
        if (_device != null)
            _wgpu.DeviceRelease(_device);
        
        if (_adapter != null)
            _wgpu.AdapterRelease(_adapter);
        
        if (_instance != null)
            _wgpu.InstanceRelease(_instance);
        
        Instance = null;
        
        GC.SuppressFinalize(this);
    }
}