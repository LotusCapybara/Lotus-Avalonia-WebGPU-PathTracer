// #include "hit_analytical_any.wgsl"
// #include "hit_geometry_any.wgsl"

fn getHitAny(viewRay : RenderRay, maxDist : f32) -> bool {

    var hit : bool = analyticalHit_any(viewRay, maxDist);
    
    if(hit) {
        return true;
    }
    
    
    return geometryHit_any(viewRay, maxDist);
}