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


fn RRTAndODTFit(v: vec3<f32>) -> vec3<f32> {
    let a = v * (v + vec3<f32>(0.0245786)) - vec3<f32>(0.000090537);
    let b = v * (vec3<f32>(0.983729) * v + vec3<f32>(0.4329510)) + vec3<f32>(0.238081);
    return a / b;
}

fn ACESFitted(color: vec3<f32>) -> vec3<f32> {
    // sRGB/Rec.709-ish ACES approximation (Narkowicz style)
    let ACESInputMat = mat3x3<f32>(
        vec3<f32>(0.59719, 0.35458, 0.04823),
        vec3<f32>(0.07600, 0.90834, 0.01566),
        vec3<f32>(0.02840, 0.13383, 0.83777)
    );

    let ACESOutputMat = mat3x3<f32>(
        vec3<f32>( 1.60475, -0.53108, -0.07367),
        vec3<f32>(-0.10208,  1.10813, -0.00605),
        vec3<f32>(-0.00327, -0.07276,  1.07602)
    );

    var c = ACESInputMat * color;
    c = RRTAndODTFit(c);
    c = ACESOutputMat * c;

    return clamp(c, vec3<f32>(0.0), vec3<f32>(1.0));
}


@compute @workgroup_size(8, 8)
fn kernel_aces(@builtin(global_invocation_id) id: vec3<u32>) 
{
    let index = getIndexFromId(id);
    if(index < 0) {
        return;
    }
    
    let color = inputColor[index].rgb;
    let result = mix(color, ACESFitted(color), filmData.acesPower);
    outputColor[index] = vec4(result.rgb, 1.0);
}


