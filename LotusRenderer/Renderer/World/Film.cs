using LotusRenderer.Renderer.Types;
using LotusRenderer.Views;

namespace LotusRenderer.Renderer.World;

public class Film
{
    public FilmData Data;
    
    
    public Film(Scene scene)
    {
        Data = new FilmData
        {
            screenWidth = scene.Width,
            screenHeight = scene.Height,
            cameraExposure = Config.I().camExposure,
            bloomThreshold = Config.I().bloomThreshold,
            bloomIntensity = Config.I().bloomIntensity,
            bloomSoftKnee = 0.5f,
            acesPower = Config.I().acesIntensity
        };

        SidePanel.CameraDataChanged += OnCameraDataChanged;
        OnCameraDataChanged();
    }

    private void OnCameraDataChanged()
    {
        Data.cameraExposure = Config.I().camExposure;
        Data.bloomIntensity = Config.I().bloomIntensity;
        Data.bloomThreshold = Config.I().bloomThreshold;
        Data.bloomRadius = Config.I().bloomRadius;
        Data.acesPower = Config.I().acesIntensity;
    }
}