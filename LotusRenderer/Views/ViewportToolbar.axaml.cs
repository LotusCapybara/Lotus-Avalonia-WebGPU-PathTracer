using System;
using Avalonia.Controls;
using Avalonia.Input;
using LotusRenderer.Renderer;
using LotusRenderer.Renderer.World;

namespace LotusRenderer.Views;

public static class SelectedViewModes
{
    // todo: load/save values from persistent data
    public static ERenderMode RenderMode = ERenderMode.PathTracing;
    public static EDebugViewMode DebugDebugViewMode = EDebugViewMode.ColorBuffer;

    public static Action onChanged;
}

public partial class ViewportToolbar : UserControl
{
    public ViewportToolbar()
    {
        InitializeComponent();
        
        SelectedViewModes.RenderMode = Config.I().renderMode;
        SelectedViewModes.DebugDebugViewMode = Config.I().debugViewMode;

        ComboRenderMode.SelectedIndex = (int)SelectedViewModes.RenderMode;
        ComboDebugChannel.SelectedIndex = (int)SelectedViewModes.DebugDebugViewMode - 1;
    }

    private void ComboRenderMode_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if(ComboRenderMode == null)
            return;

        MainWindow.Get().ClearFocus();
        
        SelectedViewModes.RenderMode = (ERenderMode)ComboRenderMode.SelectedIndex ;
        SelectedViewModes.onChanged?.Invoke();
        
        Config.I().renderMode = SelectedViewModes.RenderMode; 
    }

    private void ComboDebugChannel_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if(ComboDebugChannel == null)
            return;
            
        
        MainWindow.Get().ClearFocus();
        
        // we do +1 because 0 is Pathtracer 
        // which we dont really want to expose and are not in the dropdown anyways
        // but are part of the logic.
        // if we want to use those values for some reason, we need to
        // hardcode them here just for debugging purposes
        SelectedViewModes.DebugDebugViewMode = (EDebugViewMode)ComboDebugChannel.SelectedIndex + 1;
        SelectedViewModes.onChanged?.Invoke();

        Config.I().debugViewMode = SelectedViewModes.DebugDebugViewMode;
    }
}