struct LightSample {
    Le           : vec3<f32>,
    wi           : vec3<f32>,
    dist         : f32,
    pdfSolidAngle: f32,
    valid        : bool,
}

fn doNEE(surfInt : SurfaceInteraction, throughput : vec3<f32>, rng: ptr<function, u32>) -> vec3<f32> {     
    var result = vec3<f32>(0.0);
    
    // Strategy 1: Emissive meshes
    let meshSample = sampleNEE_EmissiveMesh(surfInt, rng);
    if (meshSample.isValid) {
      result += meshSample.Li;
    }
    
    // todo: add concrete lights NEE. Don't forget to weight both strategies properly
    
    return result * throughput;
}

fn sampleNEE_EmissiveMesh(surfInt : SurfaceInteraction, rng: ptr<function, u32>) -> NeeSample {
    var smpl: NeeSample;
    smpl.isValid = false;

    let ls = sampleEmissiveTriangle(surfInt.position, rng);
    if (!ls.valid) { 
        return smpl;
    }

    if (dot(surfInt.normal, ls.wi) <= 0.0) { 
        return smpl;
    }

    let eval = BSDF_Eval_Mixture(surfInt, ls.wi);
    if (eval.pdf <= 0.0) { 
        return smpl; 
    }

    let misWeight = ls.pdfSolidAngle / (ls.pdfSolidAngle + eval.pdf);

    smpl.wi      = ls.wi;
    smpl.pdf     = ls.pdfSolidAngle;
    smpl.Li      = ls.Le * eval.f * misWeight / ls.pdfSolidAngle;
    smpl.isValid = true;
    return smpl;
}

// Uses sampleEmissiveTriangle then applies HG phase function instead of BSDF
// Called from the fog scatter block in the integrator
fn doVolumetricNEE(
    scatterPos  : vec3<f32>,
    incomingDir : vec3<f32>,
    fogG        : f32,
    throughput  : vec3<f32>,
    rng         : ptr<function, u32>
) -> vec3<f32> {
    let ls = sampleEmissiveTriangle(scatterPos, rng);
    if (!ls.valid) { return vec3(0.0); }

    // no NdotL_surface check — fog scatters in all directions unlike surfaces
    let cosTheta = dot(-incomingDir, ls.wi);
    let phase    = henyeyGreenstein(cosTheta, fogG);
    
    // a bit of a hard coded magic number clamped contribution, to avoid big fireflies in fog
    // and too noisy values
    let contribution = ls.Le * phase / ls.pdfSolidAngle;
    let maxComponent = max(contribution.x, max(contribution.y, contribution.z));                               
    let clampedContribution = select(contribution, contribution * (VOL_CONTRIB_CLAMP / maxComponent), maxComponent > 10.0);
    return throughput * clampedContribution;
}

fn sampleEmissiveTriangle(fromPos: vec3<f32>, rng: ptr<function, u32>) -> LightSample {
    var s: LightSample;
    s.valid = false;

    let qtyEmissive = i32(sceneData.qtyEmissiveTriangles);
    if (qtyEmissive <= 0) { return s; }

    let triIdx = min(u32(rand01(rng) * f32(qtyEmissive)), u32(qtyEmissive - 1));
    let emTri = emissiveTriangles[triIdx];

    let inst = meshInstances[emTri.meshInstanceIdx];
    let idx0 = triIndices[emTri.triIndexOffset + 0u];
    let idx1 = triIndices[emTri.triIndexOffset + 1u];
    let idx2 = triIndices[emTri.triIndexOffset + 2u];

    let v0 = (inst.transform * vec4(vertices[idx0].pos, 1.0)).xyz;
    let v1 = (inst.transform * vec4(vertices[idx1].pos, 1.0)).xyz;
    let v2 = (inst.transform * vec4(vertices[idx2].pos, 1.0)).xyz;

    var u1 = rand01(rng);
    var u2 = rand01(rng);
    if (u1 + u2 > 1.0) { u1 = 1.0 - u1; u2 = 1.0 - u2; }
    let w = 1.0 - u1 - u2;

    let lightPos    = v0 * w + v1 * u1 + v2 * u2;
    let lightNormal = normalize(cross(v1 - v0, v2 - v0));

    let toLight = lightPos - fromPos;
    let dist    = length(toLight);
    let wi      = toLight / dist;

    let NdotL_light = dot(lightNormal, -wi);
    if (NdotL_light < 0.1) { return s; }

    var shadowRay: RenderRay;
    shadowRay.origin    = fromPos + wi * 0.001;
    shadowRay.direction = wi;
    if (getHitAny(shadowRay, dist - 0.002)) { return s; }

    let uv = emTri.uv0 * w + emTri.uv1 * u1 + emTri.uv2 * u2;
    let meshInfo = meshInfos[inst.meshInfoId];
    let mate = evalMaterial(meshInfo.matId, uv);

    let Le = mate.emissionColor * mate.emissionStrength;
    if (all(Le < vec3(0.001))) { return s; }

    let pdfArea       = 1.0 / (f32(qtyEmissive) * emTri.area);
    let pdfSolidAngle = pdfArea * dist * dist / NdotL_light;

    s.Le            = Le;
    s.wi            = wi;
    s.dist          = dist;
    s.pdfSolidAngle = pdfSolidAngle;
    s.valid         = true;
    return s;
}