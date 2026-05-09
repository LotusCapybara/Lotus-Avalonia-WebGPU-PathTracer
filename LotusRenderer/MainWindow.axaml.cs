using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using LotusRenderer.UI;

namespace LotusRenderer;

public partial class MainWindow : Window
{
    private static MainWindow? s_instance;
    public static MainWindow? Get() => s_instance;
    
    private Editor _editor;
    
    private InputController _input;
    private Renderer.Renderer _renderer;
    
    public TextBlock TextGpuName => textGPUName;

    public MainWindow()
    {
        s_instance = this;
        
        InitializeComponent();

        textGPUName.Text = "GPU Name: ??? ";
        
        // here and there I'm having some unhandled exceptions that might come from different worker threads
        // that stall the app without any visible error, so I'm adding these 2 as a rough patch so at least
        // they make some noise and it's more obvious what's going on
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            Console.WriteLine("[UNHANDLED] " + ex);
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Console.WriteLine("[UNOBSERVED] " + e.Exception);
            e.SetObserved();
        };
        
        
        _editor = new Editor(); 
        _input = new InputController();
        _renderer = new Renderer.Renderer();

        Renderer.Renderer.onFrameFinished += OnFrameFinished;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        s_instance = null;
        Config.Save();
        base.OnClosing(e);
    }

    public void ClearFocus()
    {
        this.FocusManager.ClearFocus();
    }

    private void OnFrameFinished()
    {
        textFrameTime.Text = $"Iteration: {Global.iterationNumber} ";
        textFrameTime.Text += $"eta: { (Global.frameEta):F0} s";

        if (Global.isPaused)
            textFrameTime.Text += "  [PAUSED]";
        
        // this forces Avalonia to refresh the source, quite dumb....
        imgViewport.Source = null;      
        imgViewport.Source = _renderer.Bitmap;
        imgViewport.InvalidateVisual();
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderer.Dispose();
    }
}