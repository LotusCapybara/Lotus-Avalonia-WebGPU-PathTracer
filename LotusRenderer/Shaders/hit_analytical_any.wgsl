


fn analyticalHit_any(viewRay: RenderRay, maxDist :f32 ) -> bool {
    
    let qtySpheres = sceneData.qtySpheres;
    let tMin = EPSILON;
    
    for(var i : u32 = 0; i < qtySpheres; i++)
    {
        let sHit : RayHit  = hitSphere(viewRay, i);
        if(sHit.dist > EPSILON && sHit.dist < maxDist ) {
            return true;
        }
    }
    
    let qtyBoxes = sceneData.qtyBoxes;
    for(var i : u32 = 0; i < qtyBoxes; i++)
    {
        let bHit : RayHit  = hitBox(viewRay, i);
        if(bHit.dist > EPSILON && bHit.dist < maxDist) {
            return true;
        }
    }
    
    return false;
}

    