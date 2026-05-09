
fn hitSphere(ray: RenderRay, sphereIdx: u32) -> RayHit {

    let sphere : SdfSphere = spheres[sphereIdx];
    var hit : RayHit;
    hit.dist = -1.0;

    let oc : vec3<f32> = ray.origin - sphere.position;
    let b = dot(oc, ray.direction);
    let c = dot(oc, oc) - sphere.radius * sphere.radius;
    var h : f32 = b*b - c;
    var t : f32 = 0.0;
    var normal = vec3(0.0, 0.0, 0.0); 
    
    if (h < 0.0) { 
        t = 0.0; 
        normal = vec3(0.0, 0.0, 0.0); 
        return hit; 
    }
    
    h = sqrt(h);
    t = -b - h;
    
    if (t <= 1e-4f){
        t = -b + h;
    } 
    
    
    if (t <= 1e-4f) { 
        normal = vec3(0.0, 0.0, 0.0); 
        return hit; 
    }
    
    let p : vec3<f32> = ray.origin + t * ray.direction;
    normal = normalize(p - sphere.position);
    
    hit.dist = t;
    hit.position = p;
    hit.normal = normal;
    
    hit.matId = sphere.materialId;
    return hit;
}

fn hitBox(ray: RenderRay, boxIdx: u32) -> RayHit {
    let boxShape = boxes[boxIdx];
    var hit: RayHit;
    hit.dist = -1.0;

    // from world to box space
    let ro = ray.origin - boxShape.center;
    let rd = ray.direction;

    // slab method. note: I remember finding a faster implementation time ago.
    // I'd like to replace it when I find it again
    let m = 1.0 / (rd + vec3<f32>(1e-6)); 
    let n = m * ro;
    let k = abs(m) * boxShape.halfExtents;
    
    let t1 = -n - k;
    let t2 = -n + k;

    let tN = max(max(t1.x, t1.y), t1.z); // near entry point
    let tF = min(min(t2.x, t2.y), t2.z); // far exit point

    // if entry is farther than exit, or box is behind camera, no hit
    if (tN > tF || tF < 0.0) {
        return hit;
    }

    hit.dist = tN;
    hit.position = ray.origin + ray.direction * tN;

    // normal calculation
    // by moving the hit position to the center of the box, then moving inwards the faces slighly (the 1e-4)
    // we can infer the face using step, which returns 1 on axes where the hit point was close, 0 in the others
    // then with sign we get the direction
    let p = hit.position - boxShape.center;
    let stepVal = step(abs(boxShape.halfExtents) - vec3<f32>(1e-4), abs(p));
    hit.normal = sign(p) * stepVal;
    
    hit.matId = boxShape.materialId;
    
    return hit;
}

fn analyticalHit(viewRay: RenderRay) -> RayHit {
    
    let qtySpheres = sceneData.qtySpheres;
    var hit : RayHit;
    hit.dist = -1.0;
    var closestDist = INFINITY;
    
    for(var i : u32 = 0; i < qtySpheres; i++)
    {
        let sHit : RayHit  = hitSphere(viewRay, i);
        if(sHit.dist > 0 && sHit.dist < closestDist)
        {
            hit = sHit;
            closestDist = sHit.dist;
        }
    }
    
    let qtyBoxes = sceneData.qtyBoxes;
    for(var i : u32 = 0; i < qtyBoxes; i++)
    {
        let bHit : RayHit  = hitBox(viewRay, i);
        if(bHit.dist > 0 && bHit.dist < closestDist)
        {
            hit = bHit;
            closestDist = bHit.dist;
        }
    }
    
    return hit;
}

    