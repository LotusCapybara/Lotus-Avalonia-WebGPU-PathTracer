// todo 
// add some isotropic GGX? add common GGX to profile quality and time 
// against vndf?

// the whole idea of this is that we sample ggx (a direction that's
// the half fector of the view directions and the light directions) but 
// only using the visible microfacet (that's why VNDF - visible normal distribution function)
// so it's similar to common GGX but uses a spherical cap or a warped disk
// in the space of the view vector (in this case a warped disk)
// (stretched by the aniso factors) and only considers microfacets what would be 
// visible by the view ray
// the theory is that by sampling H that matches the distribution of visible GGX 
// we have better convergence and lower noise 
// based on Heitz (https://jcgt.org/published/0007/04/01/)
fn sampleGGX_VNDF(wo: vec3<f32>, alphaX: f32, alphaY: f32, rng: ptr<function, u32>) -> vec3<f32> 
{
    // transforming the view direction to the hemisphere configuration
    let Vh = normalize(vec3<f32>(alphaX * wo.x, wo.y, alphaY * wo.z));

    // orthonormal basis
    let lensq = Vh.x * Vh.x + Vh.z * Vh.z;
    var T1 = vec3<f32>(1.0, 0.0, 0.0); 
    if (lensq > 0.0) {
        T1 = vec3<f32>(-Vh.z, 0.0, Vh.x) * inverseSqrt(lensq);
    }
    let T2 = cross(Vh, T1);
    
    let u1 = rand01(rng);
    let u2 = rand01(rng);

    //  parameterization of the projected area
    let r = sqrt(u1);
    let phi = 2.0 * PI * u2;
    let t1 = r * cos(phi);
    var t2 = r * sin(phi);
    
    // warp depending on Vh.y (visible normals)
    let s = 0.5 * (1.0 + Vh.y);
    t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;
    
    // reprojection onto hemisphere
    let Nh = t1 * T1 + t2 * T2 + sqrt( max(0.0, 1.0 - t1 * t1 - t2 * t2) ) * Vh;
    let Ne = normalize(vec3(alphaX * Nh.x, max(0.0, Nh.y), alphaY * Nh.z));
    
    return Ne; 
}

fn evalGGX(wo: vec3<f32>, wi: vec3<f32>, roughness: f32, f0: vec3<f32>) -> BsdfEval
{
    var eval : BsdfEval;

    // isotropic alpha
    var alpha = roughness * roughness;
    alpha = clamp(alpha, 0.001, 1.0);

    let H = normalize(wo + wi);
    let NdotL = wi.y;
    let NdotV = wo.y;
    var NdotH = H.y;
    var VdotH = dot(wo, H);

    if (NdotL <= 0.0 || NdotV <= 0.0) { 
        eval.f = vec3(0.0);
        eval.pdf = 0.0;
        return eval; 
    }
    
    NdotH = max(NdotH, 0.0);
    VdotH = max(VdotH, 1e-4);

    // isotropic distribution
    let D = D_GGX(NdotH, alpha);
    // Vis = G / (4*N.L*N.V)
    // (geometry term / divisor)
    let Vis = V_SmithGGXCorrelated(NdotV, NdotL, alpha); 
    let F = F_Schlick(VdotH, f0);

    // brdf value
    eval.f = D * F * Vis; 

    // this is the standard pdf 
    // it should remain valid for also for
    // vndf sampling
    //eval.pdf = D * H.y / (4.0 * VdotH);
    
    let a2 = alpha * alpha;
    let lambdaV = 0.5 * (sqrt((a2 + (1.0 - a2) * NdotV * NdotV) / (NdotV * NdotV)) - 1.0);
    let G1_V = 1.0 / (1.0 + lambdaV); 
    
    // (D * G1(v)) / (4 * NdotV)
    eval.pdf = (D * G1_V) / (4.0 * NdotV);
    
    return eval;
}

// note: I need to revisit the refraction, I added it on a whimp porting code from other renderers I made in the 
// past, but I think it has some issues here and there
fn evalMicrofacetRefraction(
    lWo: vec3<f32>,
    lWi: vec3<f32>,
    roughness: f32,
    eta: f32,                 // eta = eta_i / eta_t (same as used in refract)
    baseColor: vec3<f32>,
    F: f32,                   // scalar Fresnel value for this H
    H: vec3<f32>
) -> BsdfEval {
    var eval: BsdfEval;
    eval.f   = v_zero;
    eval.pdf = 0.0;

    let NdotV = lWo.y;
    let NdotL = lWi.y;

    // transmission should go "down" in local space (towards the inside)
    if (NdotV * NdotL >= 0.0) {
        return eval;
    }

    let LdotH = dot(lWi, H);
    let VdotH = dot(lWo, H);
    let NdotH = H.y;

    if (abs(LdotH) < 1e-6 || VdotH <= 0.0 || NdotH <= 0.0) {
        return eval;
    }

    // NDF
    let alpha = max(roughness * roughness, 0.001);
    let D = D_GGX(NdotH, alpha);

    // Geometry terms
    let G1v = smithG1_GGX(abs(NdotV), alpha);
    let G1l = smithG1_GGX(abs(NdotL), alpha);
    let G2  = G1v * G1l;

    // Jacobian from half-vector to refracted direction
    let denom  = LdotH + VdotH * eta;
    let denom2 = denom * denom;

    if (denom2 <= 1e-8) {
        return eval;
    }

    let eta2     = eta * eta;
    let jacobian = abs(LdotH) / denom2;

    eval.pdf = G1v * max(VdotH, 0.0) * D * jacobian / max(abs(NdotV), 1e-6);

    let tint = pow(baseColor, vec3<f32>(0.5));

    // BTDF value
    eval.f = tint
           * (1.0 - F)           // transmission side of Fresnel
           * D * G2
           * abs(VdotH) * jacobian * eta2
           / max(abs(NdotL * NdotV), 1e-6);

    return eval;
}


fn evalClearcoat( coatRoughness :f32, lWo :vec3<f32>, lWi :vec3<f32>, H : vec3<f32>) -> BsdfEval {
    var eval: BsdfEval;
    eval.f   = v_zero;
    eval.pdf = 0.0;
    
    if (lWi.y <= 0.0) {
        return eval;
    }

    let VDotH = dot(lWo, H);

    let F = mix(0.04, 1.0, schlickWeight(VDotH));
    let D = GTR1(H.y, coatRoughness);
    let G = smithG1_GGX(lWo.y, 0.25) * smithG1_GGX(lWi.y, 0.25);
    let jacobian = 1.0 / (4.0 * VDotH);

    eval.pdf = D * H.y * jacobian;
    eval.f = vec3(F) * D * G;
    return eval;
}