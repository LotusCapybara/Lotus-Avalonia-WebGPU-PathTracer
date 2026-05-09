// #include "constants.wgsl"
// #include "types.wgsl"
// #include "utility.wgsl"
// #include "bsdf.wgsl"

// ---- group 0: uniforms
@group(0) @binding(0) 
var<uniform> viewData : ViewData;

@group(0) @binding(1) 
var<uniform> cameraData : CameraData;

@group(0) @binding(2) 
var<uniform> sceneData : SceneData;

@group(0) @binding(3) 
var<uniform> frameData : FrameData;

@group(0) @binding(4) 
var<uniform> filmData : FilmData;


// ---- group 1: scene buffers
@group(1) @binding(0) 
var<storage, read> materials : array<Material>;

@group(1) @binding(1) 
var<storage, read> spheres: array<SdfSphere>;

@group(1) @binding(2) 
var<storage, read> boxes: array<SdfBox>;

@group(1) @binding(3) 
var<storage, read> meshInfos: array<MeshInfo>;

@group(1) @binding(4) 
var<storage, read> meshInstances: array<MeshInstance>;

@group(1) @binding(5) 
var<storage, read> vertices: array<PackedVertex>;

@group(1) @binding(6) 
var<storage, read> triIndices: array<u32>;

@group(1) @binding(7) 
var<storage, read> blas: array<BVH2Node>;

@group(1) @binding(8) 
var<storage, read> tlas: array<BVH2Node>;

@group(1) @binding(9) 
var<storage, read> emissiveTriangles: array<EmissiveTriangle>;


// ---- group 2: target textures
@group(2) @binding(0) 
var<storage, read_write> output: array<u32>;

@group(2) @binding(1) 
var<storage, read_write> accumulation: array<vec4<f32>>;

// ---- group 3: textures
@group(3) @binding(0) 
var envTexture: texture_2d<f32>;

@group(3) @binding(1) 
var envSampler: sampler;

@group(3) @binding(2) 
var sceneTextures : texture_2d_array<f32>;

// #include "hit.wgsl"
// #include "hit_any.wgsl"

struct DebugBSDFData {
    material   : Material,
    wi : vec3<f32>,
    bsdfEval   : BsdfEval
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3<u32>) 
{
    if(id.x >= viewData.width || id.y >= viewData.height) {
        return;
    }
    
    let index = id.y * viewData.width + id.x;
    var rngState = initRng(id);
    
    // these are not used on every debug mode, but in many, so we 
    // build them anyways
    var viewRay = getCameraRay(id.x, id.y, &rngState);
    var surfInt : SurfaceInteraction = getHit(viewRay, -1);
    var material = surfInt.material;

    var finalColor = vec3<f32>(0.1, 0.1, 0.1);
    
    
    if(viewData.renderMode == MODE_PATH_TRACER) {
        // skip, this is done by a different kernel
    }
    else if(viewData.renderMode == MODE_DEBUG_RANDOM_COLOR) {
        finalColor.r = rand01(&rngState);
        finalColor.g = rand01(&rngState);
        finalColor.b = rand01(&rngState);
    }
    else if(viewData.renderMode == MODE_DEBUG_COLOR_BUFFER) {
        if(surfInt.distance > 0) {
            finalColor = material.baseColor.rgb;
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_NORMAL_BUFFER) {
        if(surfInt.distance > 0) {
            finalColor = surfInt.normal;
        }     
    }
    else if(viewData.renderMode == MODE_DEBUG_BVH_DEPTH) {
                 
    }
    else if(viewData.renderMode == MODE_DEBUG_HDRI_ONLY) {
         finalColor = getHdriValue(viewRay.direction);
    }
    else if(viewData.renderMode == MODE_DEBUG_DEPTH_BUFFER) {
        if(surfInt.distance > 0) {
            let brightness = 5.0 / (surfInt.distance + 0.1); 
            finalColor = vec3(brightness);
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_ZEBRA_SPACING) {
        if(surfInt.distance > 0) {
            let pattern = fract(surfInt.distance); 
            finalColor = vec3(pattern);
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_GEOMETRY_ID) {
            if(surfInt.distance > 0) {
                // todo
            }
        }
    else if(viewData.renderMode == MODE_DEBUG_UV_COORDINATES) {
        if(surfInt.distance > 0) {
            // todo
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_BARYCENTRICS) {
        if(surfInt.distance > 0) {
            // todo
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_ROUGHNESS) {
        if(surfInt.distance > 0) {
            finalColor = v_one * material.roughness;
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_METALLIC) {
        if(surfInt.distance > 0) {
            finalColor = v_one * material.metallic;
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_EMISSIVE) {
        if(surfInt.distance > 0) {
            finalColor = material.emissionColor *  material.emissionStrength;
        }
    }
    else if(viewData.renderMode == MODE_DEBUG_ALPHA) {
        if(surfInt.distance > 0) {
            finalColor = vec3(material.baseColor.a);
        }
    }
    
    output[index] = packColor(vec4<f32>(finalColor, 1.0));
}

fn getBsdfData(viewRay :RenderRay, surfInt: SurfaceInteraction, rngState: ptr<function, u32>) -> DebugBSDFData {
    let bsdf = BSDF_Sample(surfInt, rngState);

    var data : DebugBSDFData;
    data.material = surfInt.material;
    data.wi = bsdf.wi;
    data.bsdfEval = BSDF_Eval_Mixture(surfInt, data.wi); 
    return data;
}