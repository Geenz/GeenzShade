/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_TEXTURE_ARRAYS_INCLUDED
#define GZ_TEXTURE_ARRAYS_INCLUDED

// ============================================================================
// Texture Array Support (advanced)
//
// When USE_TEXTURE_ARRAYS is enabled, every 2D input texture is switched to a
// Texture2DArray and sampled with a per-vertex slice index (carried in UV3.x).
// This lets many meshes be merged into one object/material where each vertex
// selects which texture set to sample.
//
// A Unity texture property's type is fixed (2D vs 2DArray), so the array
// variants are SEPARATE properties named "<name>Array" (e.g. _BaseColorTexture
// -> _BaseColorTextureArray). These macros select the right property + sampler
// per variant so call sites stay identical:
//
//   GZ_DECLARE_TEX(_BaseColorTexture);                       // declaration
//   float4 GZ_TEX_ST(_BaseColorTexture);                     // tiling/offset
//   half4 c = GZ_SAMPLE_TEX(_BaseColorTexture, uv, slice);   // sample
//
// In 2D mode the slice argument is ignored.
// ============================================================================

#ifdef USE_TEXTURE_ARRAYS

    #define GZ_DECLARE_TEX(name)            UNITY_DECLARE_TEX2DARRAY(name##Array)
    #define GZ_SAMPLE_TEX(name, uv, slice)  UNITY_SAMPLE_TEX2DARRAY(name##Array, float3((uv), (slice)))
    #define GZ_TEX_ST(name)                 name##Array##_ST

#else

    #define GZ_DECLARE_TEX(name)            sampler2D name
    #define GZ_SAMPLE_TEX(name, uv, slice)  tex2D(name, (uv))
    #define GZ_TEX_ST(name)                 name##_ST

#endif

#ifdef USE_TEXTURE_ARRAYS
    // Where the array layer index comes from (static per-material choice, selected
    // by the _ARRAYINDEXSOURCE_* shader_feature so only the chosen path compiles):
    //   _ARRAYINDEXSOURCE_VERTEX         -> per-vertex (UV3.x)   [merged meshes]
    //   _ARRAYINDEXSOURCE_MATERIALINSTANCE -> material / per-instance (_ArraySlice)
    // _ArraySlice is an instanced property: set it on the material (the default
    // for every instance) or override per-instance via a MaterialPropertyBlock
    // for GPU-instanced batching.
    UNITY_INSTANCING_BUFFER_START(GzArrayProps)
        UNITY_DEFINE_INSTANCED_PROP(float, _ArraySlice)
    UNITY_INSTANCING_BUFFER_END(GzArrayProps)

    // Resolve the slice for a vertex input `v` (has texArraySlice when arrays on).
    // In MaterialInstance mode, call after UNITY_SETUP_INSTANCE_ID(v).
    #if defined(_ARRAYINDEXSOURCE_MATERIALINSTANCE)
        #define GZ_RESOLVE_SLICE(v) UNITY_ACCESS_INSTANCED_PROP(GzArrayProps, _ArraySlice)
    #else
        #define GZ_RESOLVE_SLICE(v) ((v).texArraySlice.x)
    #endif
#endif

#endif // GZ_TEXTURE_ARRAYS_INCLUDED
