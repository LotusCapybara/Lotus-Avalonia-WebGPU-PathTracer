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
@group(1) @binding(1) var<storage, read_write> packedOutput: array<u32>;

@compute @workgroup_size(8, 8)
fn kernel_final_pack(@builtin(global_invocation_id) id: vec3<u32>) 
{
   let index = getIndexFromId(id);
   if(index < 0) {
       return;
   }

   let color = inputColor[index].rgb;
   
   // Reinhard tonemapping 
   let correctedColor = color / (color + vec3(1.0));
   
   // gamma-encoded values for screens. 1/2.2 seems to be standard
   let gammaColor = pow(correctedColor, vec3(1.0 / 2.2));
   
   packedOutput[index] = packColor(vec4<f32>(gammaColor, 1.0));
}