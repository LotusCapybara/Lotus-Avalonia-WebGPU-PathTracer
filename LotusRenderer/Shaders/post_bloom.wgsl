// #include "constants.wgsl"
// #include "types.wgsl"
// #include "utility.wgsl"

struct BloomStepParams
{
    srcWidth  : f32,
    srcHeight : f32,
    dstWidth  : f32,
    dstHeight : f32,
    lowWeight : f32,
}

// group 0: uniforms ,standard around the engine
@group(0) @binding(0) var<uniform> viewData   : ViewData;
@group(0) @binding(1) var<uniform> cameraData : CameraData;
@group(0) @binding(2) var<uniform> sceneData  : SceneData;
@group(0) @binding(3) var<uniform> frameData  : FrameData;
@group(0) @binding(4) var<uniform> filmData   : FilmData;

// group 1: buffers for this kernel
@group(1) @binding(0) var<storage, read_write> inputColor: array<vec4<f32>>;
@group(1) @binding(1) var<storage, read_write> outputColor: array<vec4<f32>>;

@group(2) @binding(0) var<uniform> bloomStep   : BloomStepParams;
@group(2) @binding(1) var textSrc   : texture_2d<f32>;
// when upsampling we need 2 reading textures
@group(2) @binding(2) var textSrc2   : texture_2d<f32>; 
@group(2) @binding(3) var textDst  : texture_storage_2d<rgba16float, write>;
@group(2) @binding(4) var textSampler : sampler;


@compute @workgroup_size(8, 8)
fn kernel_bloom_prefilter(@builtin(global_invocation_id) id: vec3<u32>) 
{
    let index = getIndexFromId(id);
    if(index < 0) {
        return;
    }

    // capping max hdr value so it won't go crazy on brightness
    // in fireflies or similar hot spots
    let hdr = min(vec3<f32>(BLOOM_HDR_CAP), inputColor[index].rgb); 
    let thr = filmData.bloomThreshold;
    let knee = thr * filmData.bloomSoftKnee;

    let l = luminance(hdr);
    // soft curve (l - thr + knee)
    let softOp = l - thr + knee;
    let softClamp = clamp(softOp, 0.0, 2.0 * knee);
    let softCurve = (softClamp * softClamp) / (4.0 * knee + 1E-5);

    let contrib = max(softCurve, l - thr) / max(l, 1E-5);

    let bloom = hdr * contrib;

    textureStore(textDst, id.xy, vec4<f32>(bloom, 1.0));
}


@compute @workgroup_size(8, 8)
fn kernel_bloom_apply(@builtin(global_invocation_id) id: vec3<u32>) 
{
    let index = getIndexFromId(id);
    if(index < 0) {
        return;
    }
    
    let uv = vec2<f32>(
        (f32(id.x) + 0.5) / f32(viewData.width),
        (f32(id.y) + 0.5) / f32(viewData.height)
    );
    
    let base = inputColor[index];
    
    let bloomSample = textureSampleLevel(textSrc, textSampler, uv, 0.0);
    let bloomIntensity = filmData.bloomIntensity;
    let finalColor = base + bloomIntensity * bloomSample;
    
    outputColor[index] = vec4<f32>(finalColor.rgb, 1.0);
}

@compute @workgroup_size(8, 8)
fn kernel_down_sample(@builtin(global_invocation_id) id: vec3<u32>) 
{
    if (id.x >= u32(bloomStep.dstWidth) || id.y >= u32(bloomStep.dstHeight)) {
        return;
    }

    let dstSize = vec2<f32>(bloomStep.dstWidth, bloomStep.dstHeight);
    let uv = (vec2<f32>(vec2<u32>(id.xy)) + vec2(0.5, 0.5)) / dstSize;
    let color = textureSampleLevel(textSrc, textSampler, uv, 0.0);

    textureStore(
        textDst,
        vec2<i32>(i32(id.x), i32(id.y)),
        color
    );
}

@compute @workgroup_size(8, 8)
fn kernel_blur(@builtin(global_invocation_id) id: vec3<u32>) 
{
    if (id.x >= u32(bloomStep.dstWidth) || id.y >= u32(bloomStep.dstHeight)) {
        return;
    }
    
    let srcSize   = vec2<f32>(bloomStep.srcWidth, bloomStep.srcHeight);
    let texelSize = 1.0 / srcSize;
    let uvCenter  = (vec2<f32>(vec2<u32>(id.xy)) + vec2(0.5, 0.5)) / srcSize;

    var sum      = vec3<f32>(0.0);
    var tapCount = 0.0;

    let radius    = filmData.bloomRadius; // in texels
    let radiusSqr = radius * radius;
    let maxRadius = i32(ceil(radius));

    for (var oy: i32 = -maxRadius; oy <= maxRadius; oy = oy + 1) {
        for (var ox: i32 = -maxRadius; ox <= maxRadius; ox = ox + 1) {
            let offset = vec2<f32>(f32(ox), f32(oy));

            let r2 = dot(offset, offset);
            if (r2 > radiusSqr) {
                continue;
            }

            var sampleUV = uvCenter + offset * texelSize; 
            sampleUV = clamp(sampleUV, vec2(0.0, 0.0), vec2(1.0, 1.0));

            let s = textureSampleLevel(textSrc, textSampler, sampleUV, 0.0).rgb;
            sum      += s;
            tapCount += 1.0;
        }
    }

    let blurred = sum / max(tapCount, 1.0); // average
    textureStore(
        textDst,
        vec2<i32>(i32(id.x), i32(id.y)),
        vec4<f32>(blurred, 1.0)
    );
}

@compute @workgroup_size(8, 8)
fn kernel_up_sample(@builtin(global_invocation_id) id: vec3<u32>) 
{
    if (id.x >= u32(bloomStep.dstWidth) || id.y >= u32(bloomStep.dstHeight)) {
        return;
    }

    let dstSize = vec2<f32>(bloomStep.dstWidth, bloomStep.dstHeight);
    let uv = (vec2<f32>(vec2<u32>(id.xy)) + vec2(0.5, 0.5)) / dstSize;
    
    let lowSample = textureSampleLevel(textSrc, textSampler, uv, 0.0);
    let hiSample = textureSampleLevel(textSrc2, textSampler, uv, 0.0);
    let color = hiSample + lowSample * bloomStep.lowWeight;

    textureStore(
        textDst,
        vec2<i32>(i32(id.x), i32(id.y)),
        color
    );
}