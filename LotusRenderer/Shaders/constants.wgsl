const PI: f32 = 3.141592653589793;
const TWO_PI : f32 = 6.28318530718;
const INV_2PI = 0.1591549430918953358;
const INV_PI  = 0.3183098861837906715;
const EPSILON = 0.0000000000000000000001;
const INFINITY = 99999999999999999999.0;

const v_one = vec3<f32>(1.0, 1.0, 1.0);
const v_zero = vec3<f32>(0.0, 0.0, 0.0);

// hit
const MAX_HIT_DISTANCE: f32  = 1e12;
const TANGENT_VALIDITY_THRESHOLD: f32  = 0.1;

// bounces
const MAX_BOUNCES: u32  = 10u;
const MAX_DIFFUSE_DEPTH: u32  = 5u;
const MAX_SPECULAR_DEPTH: u32  = 10u;

// russian roulette
const RR_MIN_BOUNCES: u32  = 3u;
const RR_PROB_MIN: f32  = 0.05;
const RR_PROB_MAX: f32  = 0.95;

// tweaks
const FIREFLY_CLAMP: f32  = 20.0;

// sss
const SSS_MAX_STEPS: u32  = 32u;

// nee
const VOL_CONTRIB_CLAMP: f32  = 10.0;

// bsdf
const DIELECTRIC_F0: vec3<f32> = vec3<f32>(0.04);

// EViewDataRenderMode (in types, struct ViewData.renderMode: u32 )
const MODE_PATH_TRACER         :u32 = 0u;
const MODE_DEBUG_RANDOM_COLOR  :u32 = 1u;
const MODE_DEBUG_COLOR_BUFFER  :u32 = 2u;
const MODE_DEBUG_NORMAL_BUFFER :u32 = 3u;
const MODE_DEBUG_BVH_DEPTH     :u32 = 4u;
const MODE_DEBUG_HDRI_ONLY     :u32 = 5u;
const MODE_DEBUG_DEPTH_BUFFER  :u32 = 6u;
const MODE_DEBUG_ZEBRA_SPACING :u32 = 7u;
const MODE_DEBUG_GEOMETRY_ID   :u32 = 8u;
const MODE_DEBUG_UV_COORDINATES :u32 = 9u;
const MODE_DEBUG_BARYCENTRICS  :u32 = 10u;
const MODE_DEBUG_ROUGHNESS     :u32 = 11u;
const MODE_DEBUG_METALLIC      :u32 = 12u;
const MODE_DEBUG_EMISSIVE      :u32 = 13u;
const MODE_DEBUG_ALPHA      :u32 = 14u;

// 0: Opaque, 1: Blend, 2: Mask in/off
const ALPHA_MODE_OPAQUE :u32 = 0u;
const ALPHA_MODE_BLEND  :u32 = 1u;
const ALPHA_MODE_MASK   :u32 = 2u;

const HDRI_MODE_SHOW_IMAGE   :u32 = 0u;
const HDRI_MODE_HIDE_IMAGE   :u32 = 1u;

const LOBE_INDEX_DIFFUSE     :i32 = 0;
const LOBE_INDEX_DIELECTRIC  :i32 = 1;
const LOBE_INDEX_METALLIC    :i32 = 2;
const LOBE_INDEX_TRANMISSION :i32 = 3;
const LOBE_INDEX_CLEAR_COAT  :i32 = 4;

// -------------- bloom

// radius is simple left, right, top down amount of 
// pixel iterations per level of downsampling
const BLOOM_RADIUS : i32 = 4;
// weight of blur per level / radius displacement
var<private> BLOOM_LEVEL_WEIGHTS : array<f32, 9> = array<f32, 9>(
    0.05, 0.09, 0.12, 0.15, 0.18, 0.15, 0.12, 0.09, 0.05
);
const BLOOM_HDR_CAP: f32 = 20.0;

// --- fog
const FOG_MAX_BOUNCES : u32 = 10;
const FOG_EPSILON: f32 = 1e-4;

// todo: this could be exposed to the UI
const MAX_FOG_DISTANCE: f32  = 1e5;



          