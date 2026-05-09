


fn hitMesh_any(viewRay: RenderRay, mIdx: u32, maxDistance : f32) -> bool {
    
    let meshInstance = meshInstances[mIdx];
    let meshInfo = meshInfos[meshInstance.meshInfoId];
    let qtyTriIdx : u32 = meshInfo.qtyTriIdx;
    let tStartOffset = meshInfo.tIndexOffset;
    
    let blasRoot : BVH2Node  = blas[meshInfo.treeRootIdx];
    
    var localRay : RenderRay;
    localRay.origin    = (meshInstance.inverseTransform * vec4(viewRay.origin, 1.0)).xyz;
    localRay.direction = (meshInstance.inverseTransform * vec4(viewRay.direction, 0.0)).xyz;
    let invRayDir = v_one / localRay.direction;
    
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
        
        if(distNode >= INFINITY || distNode > maxDistance) {
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
                
                if(hit.hit && hit.t < maxDistance) {
                    return true;
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
    
            if (distL < INFINITY && distL >= 0) {
                stack[stackPtr] = leftIdx; 
                stackPtr++;
            }

            if (distR < INFINITY && distR >= 0) {
                stack[stackPtr] = rightIdx; 
                stackPtr++;
            }
        }
    }

    return false;
}


fn geometryHit_any(viewRay: RenderRay, maxDistance : f32) -> bool {

    let qtyMeshes = sceneData.qtyMeshInstances;
    let invRayDir = v_one / viewRay.direction;
    
    if(sceneData.qtyMeshes <= 0 || sceneData.qtyMeshInstances <= 0) {
        return false;
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
        
        if(distNode >= INFINITY || distNode > maxDistance) {
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
                let hit = hitMesh_any(viewRay, m, maxDistance);
                // no need to check maxDistance because
                // that's done inside hitMesh_any
                if(hit) {
                    return true;
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
            
            if (distL < INFINITY && distL >= 0) {
                stack[stackPtr] = leftIdx; 
                stackPtr++;
            }

            if (distR < INFINITY && distR >= 0) {
                stack[stackPtr] = rightIdx; 
                stackPtr++;
            }
        }
        
            
       
    }
    // end traversal of TLAS
    return false;
}

