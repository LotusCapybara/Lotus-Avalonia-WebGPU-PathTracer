using System;
using System.IO;
using System.Numerics;
using LotusRenderer.Renderer;
using LotusRenderer.Renderer.World;
using Newtonsoft.Json;

[Serializable]
public class ConfigObj
{
    // HDRI
    // TODO this goes, we are loading the HDRi
    public string hdriFilePath = "";
    public float hdriNormalizedIntensity = 0.5f;
    public float hdriOrientation = 0f;
    public EHdriDisplayMode hdriDisplayMode = EHdriDisplayMode.ShowImage;
    
    public float linearFogDensity = 0.0f;
    public float linearFogG = 0.6f;
    public float linearFogStartDist = 0f;
    public Vector3 linearFogColor = new Vector3(0f, 0f, 0f);
    public float fogNoiseSize = 10f;
    public float fogNoiseStrength = 0.8f;

    public string modelFilePath = "";

    // Camera Data
    public Vector3 cameraPosition = new Vector3(0f, 2.8f, -10.0f);
    public Vector3 cameraForward = new Vector3(0.0f, 0.0f, 0.0f);
    public Vector3 cameraRight  = new Vector3(1.0f, 0.0f, 0.0f);
    public Vector3 cameraUp = new Vector3(0.0f, 1.0f, 0.0f);
    public float cameraFOV = 70f;
    public float camPitch = 0f;
    public float camYaw = 0f;

    public float camExposure = 0f;
    public float bloomIntensity = 1f;
    public float bloomThreshold = 1f;
    public float bloomRadius = 1.5f;
    public float acesIntensity = 1f;
    
    public ERenderMode renderMode;
    public EDebugViewMode debugViewMode;
    
    // UI
    public bool expandedModel = true;
    public bool expandedEnvironment = true;
    public bool expandedCamera = true;
}

public static class Config
{
    private static ConfigObj? _obj = null;
    
    public static ConfigObj I()
    {
        if (_obj == null)
        {
            if (File.Exists("Config.json"))
            {
                try
                {
                    string json = File.ReadAllText("Config.json");
                    _obj = JsonConvert.DeserializeObject<ConfigObj>(json);
                }
                // if for some reason the config file is corrupted, fallback to default config
                catch (Exception e)
                {
                    _obj = null;
                }
                
            }

            _obj ??= new ConfigObj();
        }

        return _obj;
    }

    public static void Save()
    {
        if(_obj == null)
            return;
        
        string json = JsonConvert.SerializeObject(_obj, Formatting.Indented);
        File.WriteAllText("Config.json", json);
    }
}