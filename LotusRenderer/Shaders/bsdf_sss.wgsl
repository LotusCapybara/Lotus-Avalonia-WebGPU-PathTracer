
// ---- fog

fn hash3(p: vec3<f32>) -> f32 {
    // use large primes on integer coords to avoid periodicity
    let n = dot(floor(p), vec3<f32>(127.1, 311.7, 74.7));
    return fract(sin(n) * 43758.5453123);
}

fn valueNoise3D(p: vec3<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0); // quintic smoothstep

    return mix(
        mix(mix(hash3(i + vec3(0,0,0)), hash3(i + vec3(1,0,0)), u.x),
            mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), u.x), u.y),
        mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), u.x),
            mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), u.x), u.y),
        u.z
    );
}

fn fbm3D(p: vec3<f32>) -> f32 {
    var value = 0.0;
    var amplitude = 0.5;
    var frequency = 1.0;
    for (var i = 0; i < 4; i++) {
        value += amplitude * valueNoise3D(p * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value;
}

// --------------------------


fn henyeyGreenstein(cosTheta: f32, g: f32) -> f32
{
    let g2     = g * g;
    let denom  = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 / (4.0 * PI)) * (1.0 - g2) / (denom * sqrt(denom));
}

// samples direction inside medium
fn sampleHenyeyGreenstein(wo: vec3<f32>, g: f32, rng: ptr<function, u32>) -> vec3<f32> {
    // g = anisotropy (-1 to 1). 
    // 0 = isotropic (scatter in any direction equally)
    // >0 = forward scattering (clouds, skin)
    
    let u = rand01(rng);
    let v = rand01(rng);
    
    var cosTheta: f32;
    if (abs(g) < 0.001) {
        cosTheta = 1.0 - 2.0 * u;
    } else {
        let sqrTerm = (1.0 - g * g) / (1.0 + g - 2.0 * g * u);
        cosTheta = (1.0 + g * g - sqrTerm * sqrTerm) / (2.0 * g);
    }
    
    let sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
    let phi = 2.0 * PI * v;
    
    let basis = buildOrthonormalBasis(wo); 
    let w1 = basis.tangent;
    let w2 = basis.bitangent;
    
    return w1 * sinTheta * cos(phi) + w2 * sinTheta * sin(phi) + wo * cosTheta;
}


fn computeSSSRandomWalk(ray: RenderRay, matr: Material, rng: ptr<function, u32>) -> SSSWalkResult {
    
    var result : SSSWalkResult;
    result.extintedThroughput = v_zero;
    result.totalDistance = 0.0;
    
    var interact: SurfaceInteraction;
    // invalid by default
    interact.distance = -1.0; 
    
    // need to experiment with this value, maybe expose it to ui?
    let ssScale = 1.0;
    
    let avgRadius = (matr.ssRadius.r + matr.ssRadius.g + matr.ssRadius.b) / 3.0;
    let sigma_t = 1.0 / max(avgRadius * ssScale, 0.0001);
    
    var walkingRay = ray;
    var totalDistance : f32 = 0.0;
    
    for(var i = 0u; i < SSS_MAX_STEPS; i++) {
    
        let hit = getHit(walkingRay, -1);
    
        // d = -ln(random) / sigma_t
        let dist = -log(max(rand01(rng), 0.00001)) / sigma_t;
        
        let nextPos = walkingRay.origin + walkingRay.direction * dist;
        
        if (hit.distance > 0.0 && hit.distance < dist) {
            totalDistance += hit.distance;
        
            interact.position = hit.position + walkingRay.direction * EPSILON;
            interact.normal = - hit.normal;
            
            let basis = buildOrthonormalBasis(interact.normal); 
            interact.tangent = basis.tangent;
            interact.bitangent = basis.bitangent;
            interact.distance = 1.0; 
            interact.material = matr; 
            
            interact.wo = walkingRay.direction; 
            result.surfaceInteraction = interact;
            
            // approximation of extintion absorbtion
            let T = exp(-sigma_t * totalDistance);
            let tint = pow(matr.baseColor.rgb, vec3(0.5));
            result.extintedThroughput = T * mix(v_one, tint, matr.ssWeight); 
            result.totalDistance = totalDistance;
             
            return result;
        }
        
        // still inside, scatter
        totalDistance += dist;
        walkingRay.origin = nextPos;
        walkingRay.direction = sampleHenyeyGreenstein(-walkingRay.direction, matr.ssAnisotropy, rng);
    }
    
    // fully absorved
    result.totalDistance = 0.0;
    return result; 
}