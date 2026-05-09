

fn sampleLambert(wo: vec3<f32>, material: Material, rng: ptr<function, u32>) -> vec3<f32> {
    return randomCosineWeightedHemisphere(rng);
}

fn evalLambert(material: Material, wi: vec3<f32>) -> BsdfEval {
    var eval : BsdfEval;
    
    let NdotL = max( wi.y, 0.0);
    eval.f = material.baseColor.rgb * INV_PI;
    eval.pdf = NdotL * INV_PI; 
    
    return eval;
}
