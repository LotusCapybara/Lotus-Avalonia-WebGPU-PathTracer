
fn getCameraRay(x: u32, y: u32, state: ptr<function, u32>) -> RenderRay {
    var ray : RenderRay;
    ray.origin = cameraData.position;

    let w = f32(viewData.width);
    let h = f32(viewData.height);

    // pixel center
    let px = f32(x) + 0.5;
    let py = f32(y) + 0.5;

    // normalize to [0,1]
    let u =  px / w;
    let v =  py / h;

    // make +Y go up
    let v_up = 1.0 - v;

    // map to NDC [-1, 1]
    let ndcX = u * 2.0 - 1.0;
    let ndcY = v_up * 2.0 - 1.0;

    // projection scale
    let aspect     = w / h;
    let tanHalfFov = tan(0.5 * cameraData.fov * PI / 180.0);

    // final direction (no minus on up; use ndcX/ndcY)
    var dir = cameraData.forward
            - cameraData.right * (ndcX * aspect * tanHalfFov)
            + cameraData.up    * (ndcY * tanHalfFov);
            
    dir.x += randMin1to1(state) * 0.0001;
    dir.y += randMin1to1(state) * 0.0001;
    dir.z += randMin1to1(state) * 0.0001;

    ray.direction = normalize(dir);
    return ray;
}

// 3D direction to 2D UV coordinates
// using this one to sample the hdri
fn getEquirectangularUV(dir: vec3<f32>) -> vec2<f32> 
{
    let offset = sceneData.hdrOrientation / 360.0; 

    // Yaw
    let phi = atan2(dir.z, dir.x);
    let rawU = phi * INV_2PI + 0.5;
    let u = fract(rawU - offset); 

    // (Pitch)  
    let v = 1.0 - acos(-dir.y) * INV_PI; 

    return vec2<f32>(u, v);
}

// -----------------  RNG stuff --------------------

fn rand01(rng: ptr<function, u32>) -> f32 {
    let state = *rng;
    *rng = state * 747796405u + 2891336453u;
    let word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    let result = (word >> 22u) ^ word;
    return f32(result) / 4294967296.0;
}

fn initRng(pixel: vec3<u32>) -> u32 {
    let s0 = (pixel.x * 1973u + 
              viewData.width + 
              pixel.y * 9277u + 
              viewData.height + 
              frameData.rnd * 26699u) 
              | 1u;
    
    var seed = s0;
    seed = (seed ^ 61u) ^ (seed >> 16u);
    seed = seed * 9u;
    seed = seed ^ (seed >> 4u);
    seed = seed * 668265261u;
    seed = seed ^ (seed >> 15u);
    return seed;
}

fn randMin1to1(state: ptr<function, u32>) -> f32 {
    return rand01(state) * 2.0 - 1.0;
}

fn randomUnitVector(state: ptr<function, u32>) -> vec3<f32> {
    let z = rand01(state) * 2.0 - 1.0; // range [-1, 1]
    let a = rand01(state) * TWO_PI;    // Angle around the up-axis
    
    let r = sqrt(1.0 - z * z);
    let x = r * cos(a);
    let y = r * sin(a);
    return vec3<f32>(x, y, z);
}

// -----------------  Vector Transformation --------------------
fn toLocal(v: vec3<f32>, normal: vec3<f32>, tangent: vec3<f32>, bitangent: vec3<f32>) -> vec3<f32> {
    return vec3<f32>(
        dot(v, tangent),
        dot(v, normal),
        dot(v, bitangent)
    );
}

fn toWorld(v: vec3<f32>, normal: vec3<f32>, tangent: vec3<f32>, bitangent: vec3<f32>) -> vec3<f32> {
    return tangent * v.x + normal * v.y + bitangent * v.z;
}

fn buildOrthonormalBasis(n: vec3<f32>) -> Basis {
    var b: Basis;
    b.normal = n;
    
    // Choose a helper vector not parallel to N
    var helper = vec3<f32>(1.0, 0.0, 0.0);
    if (abs(n.x) > 0.99) { helper = vec3<f32>(0.0, 0.0, 1.0); }
    
    b.bitangent = normalize(cross(n, helper));
    b.tangent = cross(b.bitangent, n);
    
    return b;
}

// -----------------  Color Transformation --------------------

// pack float (0-1) to byte (0-255)
// this is used to stage colors for CPU readback
fn packColor(color: vec4<f32>) -> u32 {
    let c = clamp(color, vec4(0.0), vec4(1.0));
    let r = u32(c.x * 255.0);
    let g = u32(c.y * 255.0);
    let b = u32(c.z * 255.0);
    let a = u32(c.w * 255.0);
    // Little Endian packing (R is lowest byte)
    return r | (g << 8u) | (b << 16u) | (a << 24u);
}
// -------------- math and unity functions 

// Standard luminance weights 
fn luminance(color: vec3<f32>) -> f32 {
    return dot(color, vec3<f32>(0.2126, 0.7152, 0.0722));
}

// Schlick Fresnel Weight approximation
// 1.0 when tangent, 0.0 when normal
fn schlickWeight(cosTheta: f32) -> f32 {
    let m = clamp(1.0 - cosTheta, 0.0, 1.0);
    let m2 = m * m;
    return m2 * m2 * m; // pow(m, 5)
}

// Schlick Fresnel
fn F_Schlick(cosTheta: f32, f0: vec3<f32>) -> vec3<f32> {
    return f0 + (vec3(1.0) - f0) * pow(1.0 - cosTheta, 5.0);
}

fn alignToNormal(localDir: vec3<f32>, normal: vec3<f32>) -> vec3<f32> 
{
    var helper = vec3<f32>(1.0, 0.0, 0.0);
    if (abs(normal.x) > 0.99) {
        helper = vec3<f32>(0.0, 0.0, 1.0);
    }

    let tangent = normalize(cross(normal, helper));
    let bitangent = cross(normal, tangent);
    
    return localDir.x * tangent + localDir.y * normal + localDir.z * bitangent;
}

// transforms a x,y coordinate to linear index
// returns -1 if the coordinates are outside the range
fn getIndexFromId(id: vec3<u32>) -> i32 {
    if(id.x >= viewData.width || id.y >= viewData.height) {
        return -1;
    }
    
    let u = f32(id.x) / f32(viewData.width  - 1u);
    let v = f32(id.y) / f32(viewData.height - 1u);
    return i32(id.y * viewData.width + id.x);
}