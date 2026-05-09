// #include "hit_utils.wgsl"
// #include "hit_analytical.wgsl"
// #include "hit_geometry.wgsl"

fn getHit(viewRay : RenderRay, meshIgnoreIdx: i32) -> SurfaceInteraction {

    var hit : RayHit = analyticalHit(viewRay);
    hit.uv = vec2(0.0, 0.0);
    hit.elementId = -1;

    var bestGeomHit : RayHit = geometryHit(viewRay, meshIgnoreIdx);

    if(bestGeomHit.dist > 0) {
        if( (hit.dist > 0 && bestGeomHit.dist < hit.dist) || hit.dist < 0) {
            hit = bestGeomHit;
        }
    }
    
    var surfInt : SurfaceInteraction = SurfaceInteraction(); 
    surfInt.position = hit.position;
    surfInt.distance = hit.dist;
    surfInt.normal = normalize(hit.normal);
    surfInt.wo = - viewRay.direction;
    surfInt.uv = hit.uv;
    surfInt.elementId = hit.elementId;

    surfInt.material = evalMaterial(hit.matId, hit.uv);
    
    // if the tangents are null or wrong, we build ortho ones
    if(dot(hit.tangent, hit.tangent) < TANGENT_VALIDITY_THRESHOLD)
    {
        let basis = buildOrthonormalBasis(surfInt.normal); 
        surfInt.tangent = basis.tangent;
        surfInt.bitangent = basis.bitangent;
    }
    // these should come from the geometry if they are valid
    else
    {
        let rawTangent = hit.tangent.xyz;
            // Orthogonalize
            var t = rawTangent - surfInt.normal * dot(surfInt.normal, rawTangent);
            
            // SAFETY: If tangent vanishes (was parallel to normal), fall back to arbitrary basis
            if (dot(t, t) < 1e-6) {
                 let basis = buildOrthonormalBasis(surfInt.normal); 
                 surfInt.tangent = basis.tangent;
                 surfInt.bitangent = basis.bitangent;
            } else {
                 surfInt.tangent = normalize(t);
                 let sigma = 1.0; 
                 surfInt.bitangent = normalize(cross(surfInt.normal, surfInt.tangent) * sigma);
            }
    }
    
    if(surfInt.material.textIdNormal >= 0) {
        let layer = surfInt.material.textIdNormal;
        let rawNormal = textureSampleLevel(sceneTextures, envSampler, surfInt.uv, layer, 0.0).rgb;
        var mapNormal = rawNormal * 2.0 - 1.0;
        let strength = surfInt.material.normalScale;
        mapNormal = normalize(vec3(mapNormal.xy * strength, mapNormal.z));
        
        let perturbedNormal = normalize(
                    surfInt.tangent.xyz * mapNormal.x + 
                    surfInt.bitangent   * mapNormal.y + 
                    surfInt.normal      * mapNormal.z
                );
                
        surfInt.normal = perturbedNormal;
    }
    
    surfInt.isEntering = dot(surfInt.wo, surfInt.normal) > 0.0;
    
    return surfInt;
}