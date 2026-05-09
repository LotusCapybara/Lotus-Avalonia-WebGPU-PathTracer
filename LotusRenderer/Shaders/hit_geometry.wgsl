
// NOTE: BVH traversal is duplicated in hit_geometry_any.wgsl (any-hit variant).              
// The two versions differ in early exit behavior and can't be trivially unified.

// I'm also working in an agnostic BVH C# library for Unity and plain C#.
// I plan to integrate it with this renderer in the future, so a refactor of all BVH related code
// is expected


fn hitTriangle(ray: RenderRay, v0: vec3<f32>, v1: vec3<f32>, v2: vec3<f32>) -> TriangleHit {
    var result: TriangleHit;
    result.hit = false;

    let v0v1 = v1 - v0;
    let v0v2 = v2 - v0;
    let pvec = cross(ray.direction, v0v2);
    let det = dot(v0v1, pvec);

    // CULLING:
    // If det is close to 0, ray is parallel to triangle.
    // If you want backface culling (invisible from behind), check if det < epsilon.
    // For glass/transparent objects, we usually want double-sided (no culling), 
    // so we use abs(det).
    if (abs(det) < 0.000001) { return result; }

    let invDet = 1.0 / det;
    let tvec = ray.origin - v0;
    let u = dot(tvec, pvec) * invDet;

    if (u < 0.0 || u > 1.0) { return result; }

    let qvec = cross(tvec, v0v1);
    let v = dot(ray.direction, qvec) * invDet;

    if (v < 0.0 || u + v > 1.0) { return result; }

    let t = dot(v0v2, qvec) * invDet;

    if (t > 0.001) { // 0.001 = EPSILON to prevent self-intersection acne
        result.hit = true;
        result.t = t;
        result.u = u;
        result.v = v;
    }

    return result;
}


fn hitMesh(viewRay: RenderRay, mIdx: u32, closestDist : f32) -> TriangleHit {
    
    let meshInstance = meshInstances[mIdx];
    let meshInfo = meshInfos[meshInstance.meshInfoId];
    let qtyTriIdx : u32 = meshInfo.qtyTriIdx;
    let tStartOffset = meshInfo.tIndexOffset;
    
    var bestHit : TriangleHit;
    bestHit.hit = false;
    bestHit.t = closestDist;
    
    let blasRoot : BVH2Node  = blas[meshInfo.treeRootIdx];
    
    // Transform ray to mesh-local space for BLAS traversal 
    var localRay : RenderRay;
    localRay.origin    = (meshInstance.inverseTransform * vec4(viewRay.origin, 1.0)).xyz;
    localRay.direction = (meshInstance.inverseTransform * vec4(viewRay.direction, 0.0)).xyz;
    let invRayDir = v_one / localRay.direction;

    // We keep track of the closest WORLD distance we've found so far
    var bestDistWorld = closestDist;
    
    let rayScale = length(localRay.direction);
    var bestDistLocal = closestDist * rayScale;
    localRay.direction = normalize(localRay.direction);
    
    // short stack for non-recursive traversal
    var stack : array<u32, 32>;
    var stackPtr = 0u;
    // pushing root node idx to the stack
    stack[stackPtr] = meshInfo.treeRootIdx;
    stackPtr++;

    // stack traversal loop
    while (stackPtr > 0u) {
        stackPtr--;
        let nodeIdx = stack[stackPtr];
        let node = blas[nodeIdx];
        
        // if ray hits this node
        let distNode = intersectAABB(localRay.origin, invRayDir, node.boundMin, node.boundMax);
        
        if(distNode >= INFINITY || distNode > bestDistLocal) {
            continue;
        }
       
        // if leaf node check triangles
        if(node.triCount > 0) {
            let tStart = node.firstChildIdx + meshInfo.tIndexOffset;
            let tEnd = tStart + (node.triCount * 3u);
            for ( var t : u32 = tStart ; t < tEnd; t += 3u) {
                let idx0 = triIndices[t];
                let idx1 = triIndices[t + 1u];
                let idx2 = triIndices[t + 2u];
                
                let v0 = vertices[idx0];
                let v1 = vertices[idx1];
                let v2 = vertices[idx2];
        
                let hit : TriangleHit = hitTriangle(localRay, v0.pos, v1.pos, v2.pos);

                if(hit.hit) {
                    bestDistLocal = hit.t;
                
                    // We need to transform back the distance of the hit to world distance 
                    // using transformation matrices because scaling is not uniform across axises
                    let localHitPos = localRay.origin + localRay.direction * hit.t;
                    let worldHitPos = (meshInstance.transform * vec4(localHitPos, 1.0)).xyz;
                    let distWorld = distance(viewRay.origin, worldHitPos);
                    
                    if(distWorld < bestDistWorld) {
                        bestDistWorld = distWorld;

                        bestHit.hit = true;
                        bestHit.t = bestDistWorld;
                        
                        // barycentric weight
                        let w = 1.0 - hit.u - hit.v;

                        // vertex UVs
                        let txUV0 = vec2(v0.u, v0.v);
                        let txUV1 = vec2(v1.u, v1.v);
                        let txUV2 = vec2(v2.u, v2.v);
                        
                        // uv interpolation between corners
                        bestHit.uv = (txUV0 * w) + (txUV1 * hit.u) + (txUV2 * hit.v);
                        
                        // normal interpolation (local, then world space)
                        let localNormal = normalize(v0.normal * w + v1.normal * hit.u + v2.normal * hit.v);
                        bestHit.normal = normalize(vec4(localNormal, 0.0) * meshInstance.inverseTransform).xyz;
                        
                        // tangent interpolation
                        let rawTangent = v0.tangent.xyz * w + v1.tangent.xyz * hit.u + v2.tangent.xyz * hit.v;
                        
                        // handedness interpolation from tangent data
                        let signInterp = v0.tangent.w * w + v1.tangent.w * hit.u + v2.tangent.w * hit.v;
                        let tanSign = select(-1.0, 1.0, signInterp >= 0.0);
                        
                        // checking here if the tangent is valid from the data
                        if (dot(rawTangent, rawTangent) > 1e-6) {
                            let localTangent = normalize(rawTangent);
                            // transform to world space (using 0.0 for w because it's a direction)
                            var worldTangent = normalize(meshInstance.transform * vec4<f32>(localTangent, 0.0)).xyz;
                            // Gram-Schmidt Orthogonalization
                            worldTangent = normalize(worldTangent - bestHit.normal * dot(bestHit.normal, worldTangent));
                            bestHit.tangent = vec4<f32>(worldTangent, tanSign);
                        } else {
                            // if data is missing we make it invalid (0.0, not normalized, etc)
                            // I'm checking in gethit if this happens, if so I recreate the tangent using
                            // arbitrary basis creation
                            bestHit.tangent = vec4<f32>(0.0);
                        }
                    }
                }
            }
        }
        // inner node
        else {
            // push both children indices to the stack
            // but only if the ray intersect with them
            let leftIdx = node.firstChildIdx + meshInfo.treeRootIdx; 
            let rightIdx = leftIdx + 1u;
            
            let distL = intersectAABB(localRay.origin, invRayDir, blas[leftIdx].boundMin, blas[leftIdx].boundMax);
            let distR = intersectAABB(localRay.origin, invRayDir, blas[rightIdx].boundMin, blas[rightIdx].boundMax);
    
            let hitL = distL < closestDist;
            let hitR = distR < closestDist;
            
            if (hitL && hitR) {
                // Both hit: traverse BOTH, but optimization order matters.
                // Push the FAR one first (so it stays on stack bottom).
                // Push the NEAR one second (so it gets popped/processed immediately).
                if (distL > distR) {
                    stack[stackPtr] = leftIdx; stackPtr++;
                    stack[stackPtr] = rightIdx; stackPtr++;
                } else {
                    stack[stackPtr] = rightIdx; stackPtr++;
                    stack[stackPtr] = leftIdx; stackPtr++;
                }
            } 
            else if (hitL) {
                stack[stackPtr] = leftIdx; stackPtr++;
            } 
            else if (hitR) {
                stack[stackPtr] = rightIdx; stackPtr++;
            }
        }
    }

    bestHit.matId = meshInfo.matId;

    return bestHit;
}


fn geometryHit(viewRay: RenderRay, meshIgnoreIdx: i32) -> RayHit {

    let qtyMeshes = sceneData.qtyMeshInstances;
    var hit : RayHit;
    hit.dist = -1.0;
    hit.elementId = -1;
    
    var closestDist = INFINITY;
    let invRayDir = v_one / viewRay.direction;
    
    if(sceneData.qtyMeshes <= 0 || sceneData.qtyMeshInstances <= 0) {
        return hit;
    }
    
    // short stack for non-recursive traversal of the TLAS
    var stack : array<u32, 32>;
    var stackPtr = 0u;
    // pushing root node idx to the stack
    stack[stackPtr] = 0u;
    stackPtr++;

    // traversal of the TLAS
    // stack traversal loop
    while (stackPtr > 0u) {
        stackPtr--;
        let nodeIdx = stack[stackPtr];
        let node = tlas[nodeIdx];
        
        // if ray hits this node
        let distNode = intersectAABB(viewRay.origin, invRayDir, node.boundMin, node.boundMax);
        
        if(distNode >= INFINITY || distNode > closestDist) {
            continue;
        }
       
        // if leaf node check mesh instances
        // todo: the triCount field should be renamed to
        // elementCount once I do the generalization of the bvh2 builder
        if(node.triCount > 0) {
            let mStart = node.firstChildIdx;
            let mEnd = mStart + node.triCount;
            
            // leaf tlas node, so we check against each mesh instance in there
            for ( var m : u32 = mStart ; m < mEnd; m++) {
                
                if(meshIgnoreIdx >= 0 && meshIgnoreIdx == i32(m)) {
                    continue;
                }
            
                let meshHit : TriangleHit = hitMesh(viewRay, m, closestDist);
                if(meshHit.hit && meshHit.t < closestDist) {
                    hit.position = viewRay.origin + viewRay.direction * meshHit.t;
                    hit.dist = meshHit.t;
                    hit.normal = meshHit.normal;
                    hit.tangent = meshHit.tangent.xyz;
                    hit.uv = meshHit.uv;
                    hit.matId = meshHit.matId;
                    hit.elementId = i32(m);
        
                    closestDist = meshHit.t;
                }
            }
        }
        // inner node
        else {
            // push both children indices to the stack
            // but only if the ray intersect with them
            let leftIdx = node.firstChildIdx; 
            let rightIdx = leftIdx + 1u;
            
            let distL = intersectAABB(viewRay.origin, invRayDir, tlas[leftIdx].boundMin, tlas[leftIdx].boundMax);
            let distR = intersectAABB(viewRay.origin, invRayDir, tlas[rightIdx].boundMin, tlas[rightIdx].boundMax);
            
            let hitL = distL < closestDist;
            let hitR = distR < closestDist;
            
            if (hitL && hitR) {
                // Both hit: traverse BOTH, but optimization order matters.
                // Push the FAR one first (so it stays on stack bottom).
                // Push the NEAR one second (so it gets popped/processed immediately).
                if (distL > distR) {
                    stack[stackPtr] = leftIdx; stackPtr++;
                    stack[stackPtr] = rightIdx; stackPtr++;
                } else {
                    stack[stackPtr] = rightIdx; stackPtr++;
                    stack[stackPtr] = leftIdx; stackPtr++;
                }
            } 
            else if (hitL) {
                stack[stackPtr] = leftIdx; stackPtr++;
            } 
            else if (hitR) {
                stack[stackPtr] = rightIdx; stackPtr++;
            }
        }
    }
    // end traversal of TLAS
    return hit;
}

