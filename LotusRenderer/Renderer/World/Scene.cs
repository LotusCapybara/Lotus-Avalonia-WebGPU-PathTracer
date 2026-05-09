using System;
using System.Numerics;
using System.Threading.Tasks;
using LotusRenderer.Renderer.Mesh;
using LotusRenderer.Renderer.Types;
using LotusRenderer.Views;

namespace LotusRenderer.Renderer.World;

public enum EDebugViewMode : uint
{
    RenderPathTracing,
    DebugRandomColor,
    ColorBuffer,
    NormalBuffer,
    BvhDepth,
    HdriOnly,
    DepthBuffer,
    ZebraSpacing,
    GeometryId,
    UvCoordinates,
    Barycentrics,
    Roughness,
    Metallic,
    Emissive,
    AlphaValue
}

public class Scene : IDisposable
{
    public static Scene Instance { get; private set; }

    private const float CAMERA_SETTLE_TIME = 0.5f;
    
    public uint Width;
    public uint Height;
    
    public SdfSphere[] Spheres = [];
    public SdfBox[] Boxes = [];
    public Material[] Materials = [];
    
    // all data for all the meshes (vertices and triangles)
    // the limits of each is known by each MeshInfo values
    public MeshInfo[] MeshInfos = [];
    public MeshInstance[] MeshInstances = [];
    public PackedVertex[] PackedVertices = [];
    public uint[] TriangleIndices = [];
    public BVH2Node[] BLASNodes = [];
    public BVH2Node[] TLASNodes = [];
    
    public EmissiveTriangle[] EmissiveTriangles = [];
    public float TotalEmissiveArea = 0f;
    
    // texture data
    public byte[] RawTextureArray = [];
    public uint TextureLayersCount = 0;

    public SceneData Data;
    public HdrImage HdrImage { get; private set; }
    public ViewData ViewData { get; private set; }
    
    public CameraController CameraController;

    // how far ago in time scene changed for last time
    private float _etaSceneChanged = 0f;

    public bool SceneChanged { get; private set; } = false;
    public bool HdriChanged { get; private set; } = false;
    public bool CameraChanged { get; private set; } = false;

    public Scene(uint w, uint h)
    {
        Instance = this;
        
        Width = w;
        Height = h;

        // todo: add on change events        
        ViewData = new ViewData
        {
            Width = Width,
            Height =Height,
            // todo: use some UI to select this?
            RenderMode = (uint) SelectedViewModes.DebugDebugViewMode
        };
        
        // debug scenes
        // DebugScenes.OneTempSphere(this);
        // DebugScenes.ManySpheres(this);
        // DebugScenes.FloorAndTopLights(this);

        var NewData = new SceneData();
        NewData.QtySpheres = Spheres.Length;
        NewData.QtyBoxes = Boxes.Length;
        

        if (Spheres.Length == 0)
        {
            Spheres=[new SdfSphere()];
            NewData.QtySpheres = 0;
        }

        if (Boxes.Length == 0)
        {
            Boxes=[new SdfBox()];
            NewData.QtyBoxes = 0;
        }

        Data = NewData;
        
        float aspect = Width / (float) Height;

        var config = Config.I();
        CameraController = new CameraController(
            position: config.cameraPosition,
            forwardDir: config.cameraForward,
            rightDir: config.cameraRight,
            upDir: config.cameraUp,
            pitch: config.camPitch,
            yaw: config.camYaw,
            fovDegrees: config.cameraFOV,
            aspectRatio: aspect
        );
        
        // default hdr background
        HdrImage = new HdrImage(Config.I().hdriFilePath);
        SidePanel.HdriChanged += OnHdriChanged;
        SidePanel.SceneDataChanged += OnSceneDataChanged;
        
        OnSceneDataChanged();
    }

    public void FreeMeshData()
    {
        PackedVertices   = Array.Empty<PackedVertex>();
        TriangleIndices  = Array.Empty<uint>();
        BLASNodes        = Array.Empty<BVH2Node>();
        TLASNodes        = Array.Empty<BVH2Node>();
        MeshInfos        = Array.Empty<MeshInfo>();
        MeshInstances    = Array.Empty<MeshInstance>();
        EmissiveTriangles = Array.Empty<EmissiveTriangle>();
        RawTextureArray = Array.Empty<byte>();
        
        FillEmptyArraysMock();
    }

    public void LoadGlbScene(string filename)
    {
        // if another scene is already loaded, we need to clear
        // both cpu and gpu memory 
        // note: we might include additive scenes, I need to think about that
        FreeMeshData();
        
        MeshLoader.LoadAndParseGLBScene(
            filename,
            this,
            Materials.Length
        );

        OnSceneDataChanged();
    }
    
    private void FillEmptyArraysMock()
    {
        // empty buffers would throw errors so
        // we need some dummy data if no shapes used
        if (Materials.Length == 0)
        {
            Materials=[MaterialFactoryHelper.CreateMatte(new Vector3(0.8f))];
            Data.QtyMaterials = 0;
        }

        if (MeshInfos.Length == 0)
        {
            MeshInfos=[new MeshInfo()];
            Data.QtyMeshes = 0;
        }
        
        if (MeshInstances.Length == 0)
        {
            MeshInstances=[new MeshInstance()];
            Data.QtyMeshInstances = 0;
        }

        if (PackedVertices.Length == 0)
        {
            PackedVertices=[new PackedVertex()];
        }

        if (TriangleIndices.Length == 0)
        {
            TriangleIndices=[0, 1, 2, 4];
        }

        if (BLASNodes.Length == 0)
        {
            BLASNodes = [new BVH2Node()];
        }
        
        if (TLASNodes.Length == 0)
        {
            TLASNodes = [new BVH2Node()];
        }

        if (EmissiveTriangles.Length == 0)
        {
            EmissiveTriangles = [new EmissiveTriangle()];   
        }

        if (RawTextureArray.Length == 0)
        {
            RawTextureArray = new byte[TextureLoader.TXT_WIDTH * TextureLoader.TXT_HEIGHT * 4];
            Array.Fill(RawTextureArray, (byte)255);
            TextureLayersCount = 1;
        }
    }
    
    public void Tick(float deltaTime)
    {
        // the scene only can change if previous changes were handled by the renderer
        // to do so it should call ConfirmChangesWereHandled()
        if(SceneChanged)
            return;
        
        _etaSceneChanged += deltaTime;
        
        
        CameraController.Tick(deltaTime);

        CameraChanged = CameraController.MovedThisFrame;
        
        SceneChanged = CameraChanged || HdriChanged;
        
        var viewData = ViewData;

        if (SceneChanged)
        {
            _etaSceneChanged = 0f;
        }

        // wait CAMERA_SETTLE_TIME secs after movement or changes 
        // to settle and allow the path tracer to start
        viewData.viewIsMoving = _etaSceneChanged < CAMERA_SETTLE_TIME ? 1u : 0;
        viewData.RenderMode = (uint)SelectedViewModes.DebugDebugViewMode;
        ViewData = viewData;

        if (CameraChanged)
        {
            // todo encapsulate inside the camera controller
            var config = Config.I();
            var camData =  CameraController.GetData();
            config.cameraPosition = camData.Position;
            config.cameraForward = camData.Forward;
            config.cameraRight = camData.Right;
            config.cameraUp = camData.Up;
            config.cameraFOV = camData.FOV;
            config.camYaw = CameraController.Instance.yaw;
            config.camPitch = CameraController.Instance.pitch;
        }
    }

    public void ConfirmChangesWereHandled()
    {
        SceneChanged = false;
        CameraChanged = false;
        HdriChanged = false;
    }
    
    private void OnHdriChanged()
    {
        Task.Run(() =>
        {
            string hdrImgPath = Config.I().hdriFilePath;
            // Load new image (GPU texture creation happens here)    
            var newHdrImage = new HdrImage(hdrImgPath);

            // Swap under the GPU lock so the render loop never sees 
            // a disposed texture or a half-updated state
            lock (Renderer.gpuLock)
            {
                HdrImage.Dispose();
                HdrImage = newHdrImage;
                SceneChanged = true;
                HdriChanged = true;
                _etaSceneChanged = 0f;
            }
        });
    }

    private void OnSceneDataChanged()
    {
        var data = Data;

        // -- hdri
        float maxIntensity = 20f;
        float v =   Config.I().hdriNormalizedIntensity;
        float mappedIntensity = v < 0.01f ? 0f : (float)  MathF.Pow(v, 5f) * maxIntensity;
        
        data.HdrIntensity = mappedIntensity;
        data.Orientation =  Config.I().hdriOrientation;
        data.HdriDisplayMode =  Config.I().hdriDisplayMode;
        
        // -- linear fog
        float fogIntensity = Config.I().linearFogDensity;
        if (fogIntensity < 0.01f)
        {
            data.linearFogDensity = 0f;
        }
        else
        {
            float meanFreePath = MathF.Pow(1e7f, 1.0f - fogIntensity);
            data.linearFogDensity = 1.0f / meanFreePath;
        }
        
        data.linearFogG = Config.I().linearFogG;
        data.linearFogStartDist = Config.I().linearFogStartDist;
        data.linearFogColor = Config.I().linearFogColor;

        data.fogNoiseScale = 1.0f / Config.I().fogNoiseSize;
        data.fogNoiseMin = 1.0f - Config.I().fogNoiseStrength;
        
        Data = data;
        SceneChanged = true;
    }

    public void Dispose()
    {
        SidePanel.HdriChanged -= OnHdriChanged;
        SidePanel.SceneDataChanged -= OnSceneDataChanged;
        HdrImage.Dispose();
    }
}