using System;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace LotusRenderer.Views;

public partial class SidePanel : UserControl
{
    // todo: these could go to some global bus instead of 
    // here in a UI class I know.. weird pattern
    public static event Action HdriChanged;
    public static event Action SceneDataChanged;
    public static event Action ModelChanged;
    public static event Action ModelCleared;
    public static event Action CameraDataChanged;

    private bool created = false;
    
    public SidePanel()
    {
        InitializeComponent();

        var o = Config.I();
        
        SliderHdrIntensity.Value = Config.I().hdriNormalizedIntensity;
        SliderHdrOrientation.Value = Config.I().hdriOrientation;
        
        SliderCameraExposure.Value = Config.I().camExposure;
        SliderBloomIntensity.Value = Config.I().bloomIntensity;
        SliderBloomThreshold.Value = Config.I().bloomThreshold;
        SliderBloomRadius.Value = Config.I().bloomRadius;
        SliderAcesIntensity.Value = Config.I().acesIntensity;
        
        SliderLinearFogDensity.Value = Config.I().linearFogDensity;
        SliderLinearFogG.Value = Config.I().linearFogG;
        SliderLinearFogStartDist.Value = Config.I().linearFogStartDist;
        SliderFogNoiseSize.Value = Config.I().fogNoiseSize;
        SliderFogNoiseStrength.Value = Config.I().fogNoiseStrength;

        var fogColor = Config.I().linearFogColor;
        SliderLinearFogColor.Color = new Color(
            255, (byte)(fogColor.X * 255), (byte)(fogColor.Y * 255), (byte)(fogColor.Z * 255));

        ExpanderModel.IsExpanded = Config.I().expandedModel;
        ExpanderEnvironment.IsExpanded = Config.I().expandedEnvironment;
        ExpanderCamera.IsExpanded = Config.I().expandedCamera;
        
        ComboHdriDisplayMode.SelectedIndex = (int)Config.I().hdriDisplayMode;
        
        Renderer.Renderer.onFrameFinished += OnFrameFinished;
        CameraDataChanged?.Invoke();

        created = true;
    }

    private void OnFrameFinished()
    {
        TxtTimingOfThings.Text = Global.frameTimesString;
    }

    private async void BtnLoadHdri_OnClick(object? sender, RoutedEventArgs e)
    {
        // Get the TopLevel (needed for File Picker in Avalonia 11)
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select HDRI Environment",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("EXR Images") { Patterns = new[] { "*.exr" } }
            }
        });

        if (files.Count >= 1)
        {
            Config.I().hdriFilePath = files[0].Path.LocalPath;
            HdriChanged?.Invoke();
        }
    }

    private void SliderCameraExposure_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().camExposure = (float)e.NewValue;
        CameraDataChanged?.Invoke();
    }
    
    private void SliderBloomIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().bloomIntensity = (float)e.NewValue;
        CameraDataChanged?.Invoke();
    }
    
    private void SliderBloomThreshold_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().bloomThreshold = (float)e.NewValue;
        CameraDataChanged?.Invoke();
    }
    
    private void SliderBloomRadius_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().bloomRadius = (float)e.NewValue;
        CameraDataChanged?.Invoke();
    }
    
    private void SliderHdriIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().hdriNormalizedIntensity = (float)e.NewValue;
        SceneDataChanged?.Invoke();
    }
    
    private void SliderAcesIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().acesIntensity = (float)e.NewValue;
        CameraDataChanged?.Invoke();
    }
    
    private void SliderHdriOrientation_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        Config.I().hdriOrientation = (float)e.NewValue;
        SceneDataChanged?.Invoke();
    }

    private async void BtnLoadModel_OnClick(object? sender, RoutedEventArgs e)
    {
        // Get the TopLevel (needed for File Picker in Avalonia 11)
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Model",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("GLB Models") { Patterns = new[] { "*.glb" } }
            }
        });

        if (files.Count >= 1)
        {
            // Trigger the event with the selected path
            Config.I().modelFilePath = files[0].Path.LocalPath;
            ModelChanged?.Invoke();
        }
    }

    private void BtnClearModel_OnClick(object? sender, RoutedEventArgs e)
    {
        Config.I().modelFilePath = "";
        ModelCleared?.Invoke();
    }

    
    private void Expanders_Changed(object? sender, RoutedEventArgs e)
    {
        Config.I().expandedModel = ExpanderModel.IsExpanded;
        Config.I().expandedEnvironment = ExpanderEnvironment.IsExpanded;
        Config.I().expandedCamera = ExpanderCamera.IsExpanded;
    }

    private void ComboHdriDisplayMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if(ComboHdriDisplayMode == null)
            return;
        
        Config.I().hdriDisplayMode = (EHdriDisplayMode) ComboHdriDisplayMode.SelectedIndex ;
        
        SceneDataChanged?.Invoke();
    }

    private void SliderLinearFogDensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if(SliderLinearFogDensity == null)
            return;

        Config.I().linearFogDensity = (float)e.NewValue;
        
        SceneDataChanged?.Invoke();
    }

    private void SliderLinearFogG_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if(SliderLinearFogG == null)
            return;
        
        Config.I().linearFogG = (float)e.NewValue;
        
        SceneDataChanged?.Invoke();
    }

    private void SliderLinearFogColor_ValueChanged(object? sender, ColorChangedEventArgs e)
    {
        if(SliderLinearFogColor == null)
            return;

        if (!created)
            return;

        Config.I().linearFogColor =  new Vector3(e.NewColor.R / 255f, e.NewColor.G / 255f, e.NewColor.B / 255f);
        SceneDataChanged?.Invoke();
    }

    private void SliderLinearStartDist_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if(SliderLinearFogStartDist == null)
            return;

        if (!created)
            return;

        Config.I().linearFogStartDist = (float)e.NewValue;
        SceneDataChanged?.Invoke();
    }

    private void SliderFogNoiseSize_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!created)
            return;

        Config.I().fogNoiseSize = (float)e.NewValue;
        SceneDataChanged?.Invoke();
    }

    private void SliderFogNoiseStrength_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!created)
            return;

        Config.I().fogNoiseStrength = (float)e.NewValue;
        SceneDataChanged?.Invoke();
    }
}