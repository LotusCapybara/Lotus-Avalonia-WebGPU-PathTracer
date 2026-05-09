// #include "constants.wgsl"
// #include "types.wgsl"
// #include "utility.wgsl"
// #include "bsdf.wgsl"
// #include "nee.wgsl"

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

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3<u32>) 
{
    if(id.x >= viewData.width || id.y >= viewData.height) 
    {
        return;
    }

    var rngState = initRng(id);
    var viewRay = getCameraRay(id.x, id.y, &rngState);

    var hasBounced = false; 
    var radiance = v_zero;
    var throughput = v_one;
    var meshIdToIgnore : i32 = -1;
    
    var depthTotal : u32 = 0;
    var depthSpecular : u32 = 0;
    var depthDiffuse : u32 = 0;
        
    // fog
    let fogDensity = sceneData.linearFogDensity;    
    let fogStart   = sceneData.linearFogStartDist; 
    
    // IOR stack
    // supports up to 4 stacked ior values
    var iorStack : array<f32, 4>;
    iorStack[0] = 1.0;
    var stackPtr : u32 = 1u;
    
    var surfaceBounces: u32 = 0;
    var mediumInteractions: u32 = 0; 
    
    var eval :BsdfEval; 
    var prevWasDelta = false;
    
    loop
    {
        if(surfaceBounces >= MAX_BOUNCES){
            break;
        }
    
        var surfInt : SurfaceInteraction = getHit(viewRay, meshIdToIgnore);
        let hitDistance = select(surfInt.distance, MAX_HIT_DISTANCE, surfInt.distance <= 0.0);      
        
        if (fogDensity > 0.0 && mediumInteractions < FOG_MAX_BOUNCES) 
        {
            let effectiveFogStart = select(fogStart, 0.0, hasBounced);
            
            if (hitDistance > effectiveFogStart)
            {
                let fogColor = clamp(sceneData.linearFogColor, vec3(0.0), vec3(1.0)); 
                let fogG     = sceneData.linearFogG;
    
                // length of the ray that is INSIDE the fog volume
                let fogEnd = min(hitDistance, MAX_FOG_DISTANCE);
                let validPathLength = fogEnd - effectiveFogStart;
    
                // free flight distance with base density
                let r_vol = rand01(&rngState);
                let distLocal = -log(max(FOG_EPSILON, 1.0 - r_vol)) / fogDensity;

                if (distLocal < validPathLength) {

                    let totalDist = effectiveFogStart + distLocal;
                    let scatterPos = viewRay.origin + viewRay.direction * totalDist;

                    // accept/reject based on noise
                    // honestly, I'm not too sure about this implementation, and the noise is not
                    // too visible in the render, I might re implement or just get rid of this
                    let noiseVal = fbm3D(scatterPos * sceneData.fogNoiseScale);
                    let acceptProb = mix(sceneData.fogNoiseMin, 1.0, noiseVal);
                    if (rand01(&rngState) < acceptProb) {
                        viewRay.origin = scatterPos;
                        radiance += doVolumetricNEE(viewRay.origin, viewRay.direction, fogG, throughput, &rngState);
                        throughput *= fogColor;
                        viewRay.direction = sampleHenyeyGreenstein(viewRay.direction, fogG, &rngState);
                        hasBounced = true;
                        meshIdToIgnore = -1;
                    }
                    else {
                        // scatter rejected, instead of stalling the ray I move it forward
                        viewRay.origin = scatterPos + viewRay.direction * FOG_EPSILON;
                    }
                    
                    mediumInteractions++;
                    continue;
                }
            }
        }
        
        // this means it's a escape ray
        if(surfInt.distance <= 0.0)
        {
            let ambientColor = getHdriValue(viewRay.direction);
            
            // if you need a furnace test, you can do here:
            // ambientColor = v_one;
            // sceneData.hdrIntensity = 1.0;
            
            // If we have NOT bounced (Direct eye, or through clear glass), use normal color.
            // If we HAVE bounced (Diffuse/Specular reflection), use High Intensity.
            if (hasBounced) {
                radiance += ambientColor * throughput * sceneData.hdrIntensity;
            } else {
            
                if(sceneData.hdriDisplayMode == HDRI_MODE_SHOW_IMAGE) {
                    radiance += ambientColor * throughput;
                } 
                else if(sceneData.hdriDisplayMode == HDRI_MODE_HIDE_IMAGE)  {
                    radiance += vec3(0.001);
                }
            }
             
            break;
        }
        
        let material : Material = surfInt.material;
        
        if (material.blendType == ALPHA_MODE_BLEND && material.transmissionWeight <= 0.0) {
            let alpha = clamp(material.baseColor.a, 0.0, 1.0);
        
            if (alpha < 1.0) {
                let r = rand01(&rngState);
                if (r > alpha) {
                    // Ray passes through this surface
                    viewRay.origin = surfInt.position + viewRay.direction * EPSILON;
                    meshIdToIgnore = surfInt.elementId;
                    continue;
                }
            }
        }

        meshIdToIgnore = -1;
        
        
        if (material.emissionStrength > 0.0)
        {
            // Primary ray: add directly so they look "as-is" in the camera
            if (surfaceBounces == 0u) {
                radiance += throughput * material.emissionStrength * material.emissionColor;
            }
            // After surface bounce: NEE samples for emission, so we have to use MIS to avoid duplication
            else
            {
                let Le = material.emissionColor * material.emissionStrength;
                    
                let NdotL_light = abs(dot(surfInt.normal, -viewRay.direction));
                let avgTriArea = sceneData.totalEmissiveArea / f32(sceneData.qtyEmissiveTriangles);
                let pdfNEE = surfInt.distance * surfInt.distance / 
                             (sceneData.totalEmissiveArea * max(NdotL_light, 0.1));
                
                let misWeight = eval.pdf / (eval.pdf + pdfNEE);
                radiance += throughput * Le * misWeight;
            }
        }
        
        if (viewData.viewIsMoving == 1u) {
            radiance = fastSunLight(surfInt);
            radiance += doNEE(surfInt, throughput, &rngState);
            break;
        }
        
        // eta calculation, using ior stack
        let iorCurrent = iorStack[stackPtr - 1u];
        let iorHit     = material.ior;
        if (surfInt.isEntering) {
            surfInt.eta = iorCurrent / iorHit;
        } else {
            let iorTarget = select(1.0, iorStack[stackPtr - 2u], stackPtr > 1u);
            surfInt.eta = iorHit / iorTarget;
        }
        
        
        // NEE Direct Lighting
        if(!prevWasDelta) {
            radiance += doNEE(surfInt, throughput, &rngState);
        }
        
        
        // BSDF  evaluation and path sample
        let smpl = BSDF_Sample(surfInt, &rngState);
        
        // invalid lobe / path
        if (smpl.lobeIndex < 0) {
            break;
        }
        
        eval = BSDF_Eval_Lobe(surfInt, smpl.wi, smpl.lobeIndex); 
        
        // invalid path, debug magenta.
        // (this should not happen, but I'm leaving it here because early code was causing this issue)
        // (can be removed later)
        if(eval.pdf < 0.0){
            radiance = vec3(100.0, 0.0, 1.0);
            break;
        }
        
        if(eval.pdf < EPSILON) {
            break;
        }
        
        throughput *= (eval.f / eval.pdf);
        depthTotal++;
        prevWasDelta = false;
        
        if(smpl.lobeIndex == LOBE_INDEX_DIFFUSE) {
            depthDiffuse++;
            if(depthDiffuse >= MAX_DIFFUSE_DEPTH) {
                break;
            }
        }
        else if(smpl.lobeIndex <= LOBE_INDEX_METALLIC)
        {
            prevWasDelta = true;
            depthSpecular++;
            if(depthSpecular >= MAX_SPECULAR_DEPTH) {
                break;
            }
        }
        
        if (surfaceBounces >= RR_MIN_BOUNCES) {
            // Use luminance or max component
            let t = max(throughput.r, max(throughput.g, throughput.b));
        
            // Choose continuation probability
            // Clamp to avoid killing almost everything (p_min) and avoid p==1
            let p = clamp(t, RR_PROB_MIN, RR_PROB_MAX);
        
            let r = rand01(&rngState); // your RNG
            if (r > p) {
                break;            // path dies
            }
        
            // Survives: compensate for the probability of survival
            throughput /= p;
        }
        
        surfaceBounces++;
        hasBounced = true;
        viewRay.direction = smpl.wi;
        viewRay.origin = surfInt.position + viewRay.direction * EPSILON;
        
        if(material.ssWeight > 0.0) {
            // both SSS random walk checks and logic
            // I know, these are mutually exclusive so a single condition would work
            // but put it like this for readability
            let hasTransmissiveSSS = material.transmissionWeight > 0.0; 
            let hasOpaqueSSS       = material.transmissionWeight == 0.0; 
            
            // this inner random walk happens when a refraction happened and the ray
            // actually went inside the medium (ie foggy things)
            if (hasTransmissiveSSS && !eval.wasReflection) {
                let walkResult = computeSSSRandomWalk(viewRay, material, &rngState);
                
                // fully absorbed
                if (walkResult.totalDistance <= 0.0) {
                    break; 
                }
            
                throughput *= walkResult.extintedThroughput;
                viewRay.origin    = walkResult.surfaceInteraction.position;
                viewRay.direction = walkResult.surfaceInteraction.wo;
            }
            
            // this is a shallow medium simulation, typical shallow SSS for things like skin
            if (hasOpaqueSSS && eval.wasReflection) {
                var mediumRay = viewRay;
                // we push the ray down a bit so it's "inside" the medium
                mediumRay.origin = surfInt.position - surfInt.normal * EPSILON; 
                // we point it inside, since the event was a reflection
                mediumRay.direction = -surfInt.wo; 
            
                let walkResult = computeSSSRandomWalk(mediumRay, material, &rngState);
            
                // fully absorbed
                if (walkResult.totalDistance <= 0.0) {
                    break;
                }
            
                throughput *= walkResult.extintedThroughput;
                viewRay.direction = smpl.wi;
                viewRay.origin    = surfInt.position + viewRay.direction * EPSILON;
            }
        }
        
        if(!eval.wasReflection) {
            if (surfInt.isEntering) {
                if (stackPtr < 4u) {
                    iorStack[stackPtr] = iorHit;
                    stackPtr++;
                }
            } else {
                if (stackPtr > 1u) {
                    stackPtr--;
                }
            }
        }
        
        // if the hit we just evaluated went inside the surface
        // we do random walk until it exits
        if(!eval.wasReflection && material.ssWeight > 0) {
            var mediumRay = viewRay;
            // just pushing the origin a bit more further inside
            mediumRay.origin = mediumRay.origin + mediumRay.direction * 0.01;
        
            let walkResult = computeSSSRandomWalk(mediumRay, material, &rngState);
            
            // if the ray didn't exit or travel enough to exit, then it got fully absorved
            if (walkResult.totalDistance <= 0) {
                break;
            }

            // throughput gets reduced  by the internal interactions, but we need to
            // divide by ssWeight to keep it unbiased
            throughput *=  walkResult.extintedThroughput / material.ssWeight;
            
            viewRay.origin = walkResult.surfaceInteraction.position;
            viewRay.direction = walkResult.surfaceInteraction.wo;
        }
    }
    
    // Clamp Fireflies
    // 10.0 or 20.0 is usually enough for a single sample
    // I think this could be a user parameter, blender does it
    radiance = clamp(radiance, vec3(0.0), vec3(FIREFLY_CLAMP));
   
    // todo: these lines should be part of another
    // compute pass, with post processing, color grading
    // etc
    let u = f32(id.x) / f32(viewData.width  - 1u);
    let v = f32(id.y) / f32(viewData.height - 1u);
    let index = id.y * viewData.width + id.x;
    
    var finalColor = radiance;
    
    if (frameData.frameNumber > 0u) {
        let iterationWeight = 1.0 / (f32(frameData.frameNumber) + 1.0);
        let oldAverage = accumulation[index].rgb;
        finalColor = oldAverage * (1.0 - iterationWeight) + radiance * iterationWeight;
    }
    
    accumulation[index] = vec4<f32>(finalColor, 1.0);
}
