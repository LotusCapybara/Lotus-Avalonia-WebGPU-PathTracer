// #include "bsdf_functions.wgsl"
// #include "bsdf_lambert.wgsl"
// #include "bsdf_ggx.wgsl"
// #include "bsdf_sss.wgsl"

fn calculateLobes(matr: Material, lWo: vec3<f32>) -> BsdfLobes 
{
    var lobes: BsdfLobes;
    
    // This way of calculate lobes is inspired by GSL-PathTracer by KnightCrawler25
    // (https://github.com/knightcrawler25/GLSL-PathTracer/tree/master)
    // I like the way it's organized and I feel it clean. I know there are other ways to make this
    // but I'll roll with this one for now
    
    lobes.wDielectric = (1.0 - matr.metallic) * (1.0 - matr.transmissionWeight);
    lobes.wMetal = matr.metallic;
    lobes.wTransmission = (1.0 - matr.metallic) * matr.transmissionWeight;

    let NdotV = lWo.y;
    let slickWt = schlickWeight(NdotV);
    let specColor = mix(DIELECTRIC_F0, vec3(1.0), slickWt);
    let specLum = luminance(specColor);
    let baseLum = luminance(matr.baseColor.rgb);
    
    lobes.prDiffuse = lobes.wDielectric * baseLum;
    // Usually standard F0 is 0.04 for plastic/glass
    lobes.prDielectric = lobes.wDielectric * 0.04;
    
    // Mix between Albedo (F0) and White (F90)
    let metalColor = mix(matr.baseColor.rgb, vec3(1.0), slickWt);
    lobes.prMetallic = lobes.wMetal * luminance(metalColor);
    
    // Usually assumes full throughput (white), or tinted by baseColor
    lobes.prTransmission = lobes.wTransmission * baseLum; // Or just wTrans??
    
     // Clear coat sampling weight scaled down (0.25) to avoid over-sampling this lobe
    lobes.prClearCoat = matr.coatWeight * 0.25;
                 
    lobes.prTotal = lobes.prDiffuse + lobes.prDielectric 
                + lobes.prMetallic + lobes.prTransmission 
                + lobes.prClearCoat;
                    
    let invSum = select(0.0, 1.0 / lobes.prTotal, lobes.prTotal > 0.0);
    
    lobes.prDiffuse *= invSum;
    lobes.prDielectric *= invSum;
    lobes.prMetallic *= invSum;
    lobes.prTransmission *= invSum;
    lobes.prClearCoat *= invSum;
    
    lobes.rangeDiffuse      = lobes.prDiffuse;
    lobes.rangeDielectric   = lobes.rangeDiffuse + lobes.prDielectric;
    lobes.rangeMetallic     = lobes.rangeDielectric + lobes.prMetallic;
    lobes.rangeTransmission = lobes.rangeMetallic + lobes.prTransmission;
    lobes.rangeClearCoat    = lobes.rangeTransmission + lobes.prClearCoat;
    
    return lobes;
}

fn BSDF_Sample(surfInt : SurfaceInteraction, rng: ptr<function, u32>) -> BsdfSample
{
    let lWo = toLocal(surfInt.wo, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    let material = surfInt.material;
    let lobes = calculateLobes(material, lWo);
    
    var chosenLobeIdx = -1;
    var lWi = v_zero;
   
    // Selecting the lobe direction based on the probabilities
    let rnd = rand01(rng);
    
    if (rnd < lobes.rangeDiffuse) {
        chosenLobeIdx = LOBE_INDEX_DIFFUSE;
        lWi = randomCosineWeightedHemisphere(rng);
    }
    
    // any of both speculars are reflected in same way
    // (dielectric or metallic reflections)
    else if (rnd < lobes.rangeMetallic) {
        if(rnd < lobes.rangeDielectric) {
            chosenLobeIdx = LOBE_INDEX_DIELECTRIC;
        } else {
            chosenLobeIdx = LOBE_INDEX_METALLIC;
        }
   
        let ax = max(0.001, material.roughness * material.roughness);
        let ay = ax; // todo: apply aniso scaling for each of these
        var H = sampleGGX_VNDF(lWo, ax, ay, rng);
        if(H.y < 0.0) {
            H = -H;
        }
        lWi = normalize(reflect(-lWo, H));
    } 
    else if (rnd < lobes.rangeTransmission) {
        chosenLobeIdx = LOBE_INDEX_TRANMISSION;
      
        // specular transmission is sampled like normal specular, but with
        // a probability of being refracted instead of reflected, based on the
        // fresnel (with ior)
        let ax = max(0.001, material.roughness * material.roughness);
        let ay = ax; // todo: apply aniso scaling for each of these
        let lWoUp = vec3(lWo.x, abs(lWo.y), lWo.z);
        var H = sampleGGX_VNDF(lWo, ax, ay, rng);

        let F = dielectricFresnelSchlick(dot(lWo, H), material.ior);

        // < F = reflection, usually more reflection happens
        // near the edges (normals pointing away)
        if(rand01(rng) < F) {
            lWi =  normalize(reflect(-lWo, H));
        } 
        else {
            // refraction
            lWi = normalize(refract(-lWo, H, surfInt.eta));
        }
    } 
    else if (rnd < lobes.rangeClearCoat){ 
        chosenLobeIdx = LOBE_INDEX_CLEAR_COAT;
        
        let mappedCoatA = clearcoatA(material.coatRoughness);
        var H = sampleGTR1(mappedCoatA, rng);
        if(H.y < 0.0) {
            H = -H;
        }
        lWi = normalize(reflect(-lWo, H));
    }

    
    
    var smpl : BsdfSample;
    smpl.wi = toWorld(lWi, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    smpl.lobeIndex = chosenLobeIdx;
    
    return smpl;
}

fn BSDF_Eval_Lobe(surfInt : SurfaceInteraction, wi :vec3<f32>, lobeIndex : i32) -> BsdfEval
{
    let lWi = toLocal(wi, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    let lWo = toLocal(surfInt.wo, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    let NdotL = lWi.y;
    let NdotV = lWo.y;
    let isReflection = (NdotL * NdotV) > 0.0;

    var eval : BsdfEval;
    eval.f = vec3<f32>(0.0, 0.0, 0.0);
    eval.pdf = 0.0;
    
    let material = surfInt.material;
    let lobes = calculateLobes(material, lWo);
    
    // ray goes "out" the surface (reflection)
    if (isReflection) {
        if (lobeIndex == 0) {
            let evalDiffuse = evalLambert(material, lWi);
            eval.f += evalDiffuse.f * lobes.wDielectric;
            eval.pdf += evalDiffuse.pdf * lobes.prDiffuse;
        }
        
        if (lobeIndex == 1) {
            // F0 for dielectric is around 0.04, this will give the white-ish
            // specular highlights that's good for plastics and that, 
            // so I could just hardcode it to 0.04 and that's what many engines do,
            // however a more physic-ish approach is derive this from the material IOR, also
            // maybe more expensive but ¯\_(ツ)_/¯
            // another alternative is to add specular parameter to the materials
            // but I think right now artists can just use ior to tweak this        
            let ior = max(material.ior, 1.0001);    
            let r0 = (ior - 1.0) / (ior + 1.0);
            let F0 = vec3<f32>(r0 * r0);

            let specEval = evalGGX(lWo, lWi, material.roughness, F0);
            eval.f += specEval.f * lobes.wDielectric;
            eval.pdf += specEval.pdf * lobes.prDielectric;
        }

        if (lobeIndex == 2) {
            // F0 for metal is BaseColor, this will give the tinted specular highlights
            // basically the light reflects in different wave sizes per color, absorbing 
            // some more than others, creating the color specular (that's how metals behave)
            let metalEval = evalGGX(lWo, lWi, material.roughness, material.baseColor.rgb);
            eval.f += metalEval.f * lobes.wMetal;
            eval.pdf += metalEval.pdf * lobes.prMetallic;
        }
    

    
        if (lobeIndex == 4) {
            let H = normalize(lWo + lWi);
            let mappedCoatA = clearcoatA(material.coatRoughness);
            let coatEval = evalClearcoat(mappedCoatA, lWo, lWi, H);
        
            eval.f += coatEval.f * material.coatWeight;
            eval.pdf += coatEval.pdf * lobes.prClearCoat;

        }
    }
    
    // there are transmission lobe chances (either reflection or refraction)
    if(lobeIndex == 3) {
        var H : vec3<f32>;
        if (isReflection) {
            H = normalize(lWi + lWo);
        }
        else {
            H = normalize(lWi + lWo * surfInt.eta);
        }

        if (H.y < 0.0) {
             H = -H;
        }

        let VDotH = abs(dot(lWo, H));
        // Dielectric fresnel (achromatic / float)
        let F = dielectricFresnelSchlick(VDotH, surfInt.eta);

        // just like dielectric reflection but with transmission weight factors
        if (isReflection) {
            let specEval = evalGGX(lWo, lWi, material.roughness, vec3(0.04));
            eval.f += specEval.f * lobes.wTransmission;
            eval.pdf += specEval.pdf * lobes.prTransmission * F;
        } 
        else {
            // Simplified glass transmission approximation
            // TODO: implement proper refractive transmission with Fresnel-weighted BTDF
            let tint = pow(material.baseColor.rgb, vec3<f32>(0.5)); // “artist” tint

            eval.f  += tint * lobes.wTransmission;
            eval.pdf += lobes.prTransmission * (1.0 - F);
        }
    }

    eval.f *= abs(NdotL);
    eval.wasReflection = isReflection;
    
    return eval;
}


fn BSDF_Eval_Mixture(surfInt : SurfaceInteraction, wi :vec3<f32>) -> BsdfEval
{
    let lWi = toLocal(wi, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    let lWo = toLocal(surfInt.wo, surfInt.normal, surfInt.tangent, surfInt.bitangent);
    let NdotL = lWi.y;
    let NdotV = lWo.y;
    let isReflection = (NdotL * NdotV) > 0.0;

    var eval : BsdfEval;
    eval.f = vec3<f32>(0.0, 0.0, 0.0);
    eval.pdf = 0.0;
    
    let material = surfInt.material;
    let lobes = calculateLobes(material, lWo);
    
    // ray goes "out" the surface (reflection)
    if (isReflection) {
        if (lobes.prDiffuse > 0.0) {
            let evalDiffuse = evalLambert(material, lWi);
            eval.f += evalDiffuse.f * lobes.wDielectric;
            eval.pdf += evalDiffuse.pdf * lobes.prDiffuse;
        }
        
        if (lobes.prDielectric > 0.0) {
            // F0 for dielectric is around 0.04, this will give the white-ish
            // specular highlights that's good for plastics and that, 
            // so I could just hardcode it to0.04 and that's what many engines do,
            // however a more physic-ish approach is derive this from the material IOR, also
            // maybe more expensive but ¯\_(ツ)_/¯
            // another alternative is to add specular parameter to the materials
            // but I think right now artists can just use ior to tweak this        
            let ior = max(material.ior, 1.0001);    
            let r0 = (ior - 1.0) / (ior + 1.0);
            let F0 = vec3<f32>(r0 * r0);

            let specEval = evalGGX(lWo, lWi, material.roughness, F0);
            eval.f += specEval.f * lobes.wDielectric;
            eval.pdf += specEval.pdf * lobes.prDielectric;
        }

        if (lobes.prMetallic > 0.0) {
            // F0 for metal is BaseColor, this will give the tinted specular highlights
            // basically the light reflects in different wave sizes per color, absorbing 
            // some more than others, creating the color specular (that's how metals behave)
            let metalEval = evalGGX(lWo, lWi, material.roughness, material.baseColor.rgb);
            eval.f += metalEval.f * lobes.wMetal;
            eval.pdf += metalEval.pdf * lobes.prMetallic;
        }
    

    
        if (isReflection && lobes.prClearCoat > 0.0) {
            let H = normalize(lWo + lWi);
            let mappedCoatA = clearcoatA(material.coatRoughness);
            let coatEval = evalClearcoat(mappedCoatA, lWo, lWi, H);
        
            eval.f += coatEval.f * material.coatWeight;
            eval.pdf += coatEval.pdf * lobes.prClearCoat;

        }
    }
    
    // there are transmission lobe chances (either reflection or refraction)
    if(lobes.prTransmission > 0.0) {
        var H : vec3<f32>;
        if (isReflection) {
            H = normalize(lWi + lWo);
        }
        else {
            H = normalize(lWi + lWo * surfInt.eta);
        }

        if (H.y < 0.0) {
             H = -H;
        }

        let VDotH = abs(dot(lWo, H));
        // Dielectric fresnel (achromatic / float)
        let F = dielectricFresnelSchlick(VDotH, surfInt.eta);

        // just like dielectric reflection but with transmission weight factors
        if (isReflection) {
            let specEval = evalGGX(lWo, lWi, material.roughness, vec3(0.04));
            eval.f += specEval.f * lobes.wTransmission;
            eval.pdf += specEval.pdf * lobes.prTransmission * F;
        } 
        else {
            // Simplified glass transmission approximation
            // TODO: implement proper refractive transmission with Fresnel-weighted BTDF
            let tint = pow(material.baseColor.rgb, vec3<f32>(0.5)); // “artist” tint

            eval.f  += tint * lobes.wTransmission;
            eval.pdf += lobes.prTransmission * (1.0 - F);
        }
    }

    eval.f *= abs(NdotL);
    eval.wasReflection = isReflection;
    
    return eval;
}

// this is used when there is a no Path tracer mode,
// typically when moving the camera around or changing the scene properties
fn fastSunLight(surfInt: SurfaceInteraction) -> vec3<f32> {
    
    let sunDirection = vec3(-0.2, -1.0, 0.1);
    let L = normalize(-sunDirection);
    var NdotL = max(dot(surfInt.normal, L), 0.0);
    
    if (NdotL <= 0.0) {
        return vec3<f32>(0.0);
    }

    var albedo = surfInt.material.baseColor.rgb; 
    if(surfInt.material.emissionStrength > 0.0) {
        albedo = surfInt.material.emissionStrength * surfInt.material.emissionColor;
        NdotL = 1.0;
    }
    
    let lightColor = v_one * 1.0;
    return albedo * lightColor * NdotL;
}