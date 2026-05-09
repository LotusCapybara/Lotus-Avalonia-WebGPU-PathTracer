// #include "constants.wgsl"
// #include "types.wgsl"
// #include "utility.wgsl"

// group 0: uniforms ,standard around the engine
@group(0) @binding(0) var<uniform> viewData   : ViewData;
@group(0) @binding(1) var<uniform> cameraData : CameraData;
@group(0) @binding(2) var<uniform> sceneData  : SceneData;
@group(0) @binding(3) var<uniform> frameData  : FrameData;
@group(0) @binding(4) var<uniform> filmData   : FilmData;

// group 1: buffers for this kernel
@group(1) @binding(0) var<storage, read_write> inputColor: array<vec4<f32>>;
@group(1) @binding(1) var<storage, read_write> outputColor: array<vec4<f32>>;

@compute @workgroup_size(8, 8)
fn kernel_exposure(@builtin(global_invocation_id) id: vec3<u32>) 
{
    let index = getIndexFromId(id);
    if(index < 0) {
        return;
    }
    
    let color = inputColor[index];
    let ev = clamp(filmData.cameraExposure, -20.0, 20.0);
    let exposureScale = exp2(ev);
    let result = color * exposureScale;
    outputColor[index] = result;
}


