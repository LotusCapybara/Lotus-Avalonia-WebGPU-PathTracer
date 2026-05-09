

// Z-up direction cosine weighted
fn randomCosineWeightedHemisphere(rng: ptr<function, u32>) -> vec3<f32> 
{
    let r1 = rand01(rng);
    let r2 = rand01(rng);
    
    let phi = TWO_PI * r1; // polar coords
    let radius_on_disk = sqrt(r2);
    
    let x = radius_on_disk * cos(phi);
    let z = radius_on_disk * sin(phi);
    let y = sqrt(max(0.0, 1.0 - r2)); 
    
    return vec3<f32>(x, y, z);
}


// GGX Distribution (D)
fn D_GGX(NdotH: f32, alpha: f32) -> f32 {
    let a2 = alpha * alpha;
    let d = (NdotH * a2 - NdotH) * NdotH + 1.0;
    return a2 / (PI * d * d);
}

// Smith Geometry (V = G / (4 * N.V * N.L))
fn V_SmithGGXCorrelated(NdotV: f32, NdotL: f32, alpha: f32) -> f32 {
    let a2 = alpha * alpha;
    let ggxV = NdotL * sqrt(NdotV * NdotV * (1.0 - a2) + a2);
    let ggxL = NdotV * sqrt(NdotL * NdotL * (1.0 - a2) + a2);
    return 0.5 / (ggxV + ggxL);
}

fn dielectricFresnelSchlick(cosTheta: f32, eta: f32) -> f32 {
    let r0 = (1.0 - eta) / (1.0 + eta);
    let F0 = r0 * r0;
    let m  = clamp(1.0 - cosTheta, 0.0, 1.0);
    return F0 + (1.0 - F0) * m * m * m * m * m;
}

fn smithG1_GGX(NdotX: f32, alpha: f32) -> f32 {
    if (NdotX <= 0.0) {
        return 0.0;
    }

    let a2   = alpha * alpha;
    let n2   = NdotX * NdotX;
    let tan2 = (1.0 - n2) / max(n2, 1e-6);

    let root = 1.0 + a2 * tan2;
    let lambda = 0.5 * (-1.0 + sqrt(max(root, 0.0)));

    // Smith masking-shadowing G1 = 1 / (1 + lambda)
    return 1.0 / (1.0 + lambda);
}

// --- GTR ----
// GTR is a Disney Generalization of GGX (trowbridge-reitz) with exponent 2
// GTR2 is the "normal" ggx, for pbr specular
// GTR1 has a sharper tail, usually used for clear coat, like in this code

// Distribution D factor for GTR1
fn GTR1(NdotH :f32, a : f32) -> f32 {
    if (a >= 1.0) {
        return INV_PI;  
    }
        
    let a2 = a * a;
    let t = 1.0 + (a2 - 1.0) * NdotH * NdotH;
    return (a2 - 1.0) / (PI * log(a2) * t);
}

// Samples a Half microfacet direction with GTR1 (so it's shaper tail than "normal" ggx)
fn sampleGTR1(roughness :f32, rng: ptr<function, u32>) -> vec3<f32> {
    let a = max(0.001, roughness);
    let a2 = a * a;

    let r1 = rand01(rng);
    let r2 = rand01(rng);
    let phi = r1 * TWO_PI;

    let cosTheta = sqrt((1.0 - pow(a2, 1.0 - r2)) / (1.0 - a2));
    let sinTheta = clamp(sqrt(1.0 - (cosTheta * cosTheta)), 0.0, 1.0);
    let sinPhi = sin(phi);
    let cosPhi = cos(phi);

    return vec3(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
}

// disney doesn't feed [0..1] coatRoughness into the sampleGTR1 directly
// the 0 to 1 range is only for artists/ui convenciene, so they map it 
// to something that gives very low values so it's a sharp direction sampling
fn clearcoatA(coatRoughness: f32) -> f32 {
    let r =  clamp(coatRoughness, 0.0, 1.0);
    // I think disney maps it to something really really subtle
    // like 0.001 to 0.1, I just bumped the higher value so it's more
    // noticeable
    return mix(0.005, 0.2, r);
}

fn getHdriValue(direction :vec3<f32>) -> vec3<f32> {
    let hdrUV = getEquirectangularUV(normalize(direction));
    var ambientColor = textureSampleLevel(envTexture, envSampler, hdrUV, 0.0).rgb;
    return clamp(ambientColor, vec3(0.0), vec3(30.0));
}