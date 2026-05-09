// ---------- types for buffers and uniforms
// all these types come from the cpu
// they are also defined at StructTypes.cs
// and they should all be aligned to 16 bytes



struct ViewData 
{ 
    width : u32,
    height : u32,
    
    // for debugging and other things, not used on every kernel
    // enum values in constant.wgsl
    renderMode: u32,
    
    // could be packed with other flags 
    viewIsMoving : u32, 
}

struct FilmData 
{
    screenWidth  : u32,
    screenHeight : u32,

    cameraExposure : f32,
    bloomThreshold : f32,
    bloomSoftKnee  : f32,
    bloomIntensity : f32,
    bloomRadius    : f32,
    bloomSpreadPower : f32,
    acesPower      : f32,
}

struct SdfSphere
{
    position: vec3<f32>,
    radius : f32, 
    
    materialId : u32,
    _pad1 : f32,
    _pad2 : f32,
    _pad3 : f32,
}

struct SdfBox
{
    center: vec3<f32>,
    _pad1 : f32, 
    halfExtents: vec3<f32>,
    materialId : u32, 
}

struct SceneData
{
    // 16
    qtyMaterials: u32,
    qtySpheres: u32,
    qtyBoxes  : u32,
    hdrIntensity : f32,
    
    // 16
    hdriDisplayMode  : u32,
    hdrOrientation   : f32,
    qtyMeshes        : u32,
    qtyMeshInstances : u32,
    
    // 16
    qtyEmissiveTriangles : u32,
    totalVertices    : u32,
    totalTriIdxs     : u32,
    totalEmissiveArea : f32,

    // 16
    // need to confirm this, but apparently in wgsl vec3 NEEDS to start aligned to 16b
    linearFogColor   : vec3<f32>, 
    linearFogDensity : f32,
    
    // 16
    linearFogG         : f32,
    linearFogStartDist : f32,
    fogNoiseScale      : f32,
    fogNoiseMin        : f32,
}

struct FrameData         
{ 
    frameNumber : u32,
    rnd         : u32,
    _pad1 : f32,
    _pad2 : f32,
}

struct PackedVertex {
    pos    : vec3<f32>,
    u      : f32,
    normal : vec3<f32>,
    v      : f32,
    tangent : vec4<f32>,
}

struct MeshInfo {

    boundMin    : vec3<f32>,
    tIndexOffset : u32,
    
    boundMax    : vec3<f32>,
    qtyVertices : u32,
    
    qtyTriIdx   : u32, 
    matId       : u32,
    qtyBVHNodes : u32,
    treeRootIdx : u32,
}

struct MeshInstance {
    transform : mat4x4<f32>,
    inverseTransform : mat4x4<f32>,
    meshInfoId : u32,
    _pad1 : f32,
    _pad2 : f32,
    _pad3 : f32,
}

struct EmissiveTriangle
{
    meshInstanceIdx: u32,
    triIndexOffset: u32,
    area: f32,
    _padding1: f32,
    uv0 : vec2<f32>,
    uv1 : vec2<f32>,
    uv2 : vec2<f32>,
    _padding2: f32,
    _padding3: f32,
}


// Data of the Camera that's rendering
struct CameraData         // 64 bytes
{ 
    position : vec3<f32>,   // 
    fov : f32,              // 16
    forward : vec3<f32>,    // 
    frameCount : u32,       // 16
    right : vec3<f32>,      // 
    aspectRatio : f32,      // 16
    up : vec3<f32>,         // 
    _pad1 : f32,            // 16
}



struct BVH2Node {
    boundMin : vec3<f32>,
    triCount  : u32,
    
    boundMax : vec3<f32>,
    firstChildIdx  : u32,
}



// ------------ internal GPU only types
struct Basis {
    tangent: vec3<f32>,   // X
    normal: vec3<f32>,    // Y (Up)
    bitangent: vec3<f32>, // Z
};


struct RenderRay            // 24 bytes
{
    origin: vec3<f32>,     // 12
    direction: vec3<f32>,  // 12
}

struct BsdfEval {
    f : vec3<f32>,
    pdf : f32,
    wasReflection : bool,
}

struct BsdfSample {
    wi : vec3<f32>,
    lobeIndex : i32,
}


// small fast to iterate on possible hits
struct RayHit
{
    position : vec3<f32>,
    dist : f32,
    normal   : vec3<f32>,
    tangent  : vec3<f32>,
    matId    : u32,
    uv       : vec2<f32>,
    elementId : i32,
}

// once the final hit is confirmed, it becomes
// this type instead of RayHit
struct SurfaceInteraction
{
    position  : vec3<f32>,
    distance  : f32,
    normal    : vec3<f32>,
    wo        : vec3<f32>,
    uv        : vec2<f32>,
    tangent   : vec3<f32>,
    bitangent : vec3<f32>,
    material  : Material,
    isEntering : bool,
    eta        : f32,
    elementId  : i32,
}

struct SSSWalkResult {
    surfaceInteraction : SurfaceInteraction,
    totalDistance      : f32,
    extintedThroughput : vec3<f32>
}

struct Material {
    baseColor : vec4<f32>,
    
    metallic : f32,
    roughness : f32,
    ior : f32,
    transmissionWeight : f32,
    
    ssRadius : vec3<f32>,
    ssWeight : f32,
    
    ssScale : f32,
    ssAnisotropy : f32,
    _pad1 : f32,
    _pad2 : f32,
    
    specularTint : vec3<f32>,
    anisotropy : f32,
    
    coatWeight    : f32,
    coatRoughness : f32,
    coatIOR       : f32,
    matIndex      : u32,   // index in array
    
    coatTint : vec3<f32>,
    sheenWeight : f32,
    
    sheenTint : vec3<f32>,
    sheenRoughness : f32,
    
    emissionColor : vec3<f32>,
    emissionStrength : f32,
    
    textIdBaseColor : i32,
    textIdRoughness : i32,
    textIdMetallic : i32,
    textIdEmission : i32,
    
    textIdNormal : i32,
    normalScale : f32,
    textIdTransmission : i32,
    // 0: Opaque, 1: Blend, 2: Mask in/off
    blendType : u32,
}        

struct BsdfLobes {
    // Weights (0.0 - 1.0)
    wDielectric: f32,
    wMetal: f32, 
    wTransmission: f32,
    
    prDiffuse: f32,
    prDielectric: f32,
    prMetallic: f32,
    prTransmission: f32,
    prClearCoat: f32,
    
    rangeDiffuse: f32,
    rangeDielectric: f32,
    rangeMetallic: f32,
    rangeTransmission: f32,
    rangeClearCoat: f32,
    
    prTotal: f32,
};


struct TriangleHit {
    hit: bool,
    t: f32,
    u: f32,
    v: f32,
    normal : vec3<f32>,
    matId  : u32,
    uv     : vec2<f32>,
    tangent : vec4<f32>,
}

struct NeeSample {
    wi: vec3<f32>,   // world dir from surface to env
    Li: vec3<f32>,   // env radiance along -wi
    pdf: f32,        // pdf in solid angle
    isValid: bool
};