// Returns distance to box (tMin). Returns negative if miss
// 0.0 inside the box, > 0 dist to box, < 0 no hit
fn intersectAABB(rayOrigin: vec3<f32>, invRayDir: vec3<f32>, boxMin: vec3<f32>, boxMax: vec3<f32>) -> f32 {
    
    let t0 = (boxMin - rayOrigin) * invRayDir;
    let t1 = (boxMax - rayOrigin) * invRayDir;
    let tmin_v = min(t0, t1);
    let tmax_v = max(t0, t1);

    let tmin = max(max(tmin_v.x, tmin_v.y), tmin_v.z);
    let tmax = min(min(tmax_v.x, tmax_v.y), tmax_v.z);

    if (tmax >= tmin && tmax > 0.0) {
        return max(tmin, 0.0);
    }
    
    return INFINITY; // Miss
}

fn evalMaterial(matId : u32, uv: vec2<f32>) -> Material{

    var material = materials[matId];
        
    if (material.textIdBaseColor >= 0) {
        let layer = material.textIdBaseColor;
        let texColor = textureSampleLevel(sceneTextures, envSampler, uv, layer, 0.0);
        material.baseColor = material.baseColor * texColor;
    }
    
    if (material.textIdRoughness >= 0) {
        let layer = material.textIdRoughness;
        let texRoughness = textureSampleLevel(sceneTextures, envSampler, uv, layer, 0.0);
        material.roughness = material.roughness * texRoughness.g;
    }
    
    if (material.textIdMetallic >= 0) {
        let layer = material.textIdMetallic;
        let texMetallic = textureSampleLevel(sceneTextures, envSampler, uv, layer, 0.0);
        material.metallic = material.metallic * texMetallic.b;
    }
    
    if (material.textIdEmission >= 0) {
        let layer = material.textIdEmission;
        let texEmission = textureSampleLevel(sceneTextures, envSampler, uv, layer, 0.0);
        material.emissionColor = material.emissionColor * texEmission.rgb;
    }
    
    return material;
}