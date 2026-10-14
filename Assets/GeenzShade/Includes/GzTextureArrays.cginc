/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

#ifndef GZ_TEXTURE_ARRAYS_INCLUDED
#define GZ_TEXTURE_ARRAYS_INCLUDED

// Array variants are separate properties named <name>Array, e.g. _BaseColorTextureArray.

#ifdef USE_TEXTURE_ARRAYS

    #if defined(USE_SHARED_SAMPLER) && defined(UNITY_SEPARATE_TEXTURE_SAMPLER)
        #if defined(USE_BASE_COLOR_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_BaseColorTextureArray
        #elif defined(USE_ORM_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_ORMTextureArray
        #elif defined(USE_NORMAL_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_NormalTextureArray
        #endif
    #endif

    #ifdef GZ_SHARED_SAMPLER
        SamplerState GZ_SHARED_SAMPLER;
        #define GZ_DECLARE_TEX(name)            UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name##Array)
        #define GZ_SAMPLE_TEX(name, uv, slice)  name##Array.Sample(GZ_SHARED_SAMPLER, float3((uv), (slice)))
    #else
        #define GZ_DECLARE_TEX(name)            UNITY_DECLARE_TEX2DARRAY(name##Array)
        #define GZ_SAMPLE_TEX(name, uv, slice)  UNITY_SAMPLE_TEX2DARRAY(name##Array, float3((uv), (slice)))
    #endif
    #define GZ_TEX_ST(name)                 name##Array##_ST

#else

    #if defined(USE_SHARED_SAMPLER) && defined(UNITY_SEPARATE_TEXTURE_SAMPLER)
        #if defined(USE_BASE_COLOR_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_BaseColorTexture
        #elif defined(USE_ORM_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_ORMTexture
        #elif defined(USE_NORMAL_TEXTURE)
            #define GZ_SHARED_SAMPLER sampler_NormalTexture
        #endif
    #endif

    #ifdef GZ_SHARED_SAMPLER
        SamplerState GZ_SHARED_SAMPLER;
        #define GZ_DECLARE_TEX(name)            UNITY_DECLARE_TEX2D_NOSAMPLER(name)
        #define GZ_SAMPLE_TEX(name, uv, slice)  name.Sample(GZ_SHARED_SAMPLER, (uv))
    #else
        #define GZ_DECLARE_TEX(name)            sampler2D name
        #define GZ_SAMPLE_TEX(name, uv, slice)  tex2D(name, (uv))
    #endif
    #define GZ_TEX_ST(name)                 name##_ST

#endif

#ifdef USE_TEXTURE_ARRAYS
    // _ARRAYINDEXSOURCE_* picks per-vertex UV3.x or the instanced _ArraySlice.
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
