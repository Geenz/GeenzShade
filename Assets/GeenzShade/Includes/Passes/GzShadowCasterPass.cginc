/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// ShadowCaster pass body. Identical across all quality tiers (does not read
// GZ_QUALITY_TIER). Render state / Tags stay in the .shader Pass shell.

#ifndef GZ_SHADOW_CASTER_PASS_INCLUDED
#define GZ_SHADOW_CASTER_PASS_INCLUDED

#pragma target 3.0
// NOTE: #pragma vertex/fragment live in the .shader pass shells (SPS patcher).
#pragma multi_compile_shadowcaster
#pragma multi_compile_instancing

#pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
#pragma shader_feature_local USE_BASE_COLOR_TEXTURE
#pragma shader_feature_local USE_TEXTURE_ARRAYS
#pragma shader_feature_local _ARRAYINDEXSOURCE_VERTEX _ARRAYINDEXSOURCE_MATERIALINSTANCE
#pragma shader_feature_local USE_OBJECT_TOGGLES
#pragma shader_feature_local USE_AUX_DATA
#pragma shader_feature_local _FACECULLSOURCE_HARDWARE _FACECULLSOURCE_TEXTURE

#pragma shader_feature_local USE_DITHERED_SHADOWS

#if defined(USE_DITHERED_SHADOWS) && (defined(_RENDERMODE_TRANSPARENT) || defined(_RENDERMODE_PREMULTIPLIEDALPHA))
    #define GZ_DITHERED_SHADOWS
#endif

#include "UnityCG.cginc"
#include "../GzProperties.cginc"
#include "../GzObjectToggles.cginc"

// Custom input (appdata_base has no UV3) so cutout can read the per-vertex slice.
struct GzShadowInput
{
    float4 vertex   : POSITION;
    float3 normal   : NORMAL;
    float2 texcoord : TEXCOORD0;
    #if defined(USE_TEXTURE_ARRAYS) || defined(USE_OBJECT_TOGGLES)
        float2 texArraySlice : TEXCOORD3; // .x = per-vertex array slice index, .y = governing toggle bitmask
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    V2F_SHADOW_CASTER;
    float2 uv : TEXCOORD1;
    #ifdef USE_TEXTURE_ARRAYS
        nointerpolation float arraySlice : TEXCOORD2; // per-vertex array slice (flat)
    #endif
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vert(GzShadowInput v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    #ifdef USE_OBJECT_TOGGLES
        // o.pos exists in both V2F_SHADOW_CASTER layouts, so collapsing it is enough
        GzApplyObjectToggle(o.pos, v.texArraySlice.y);
    #endif
    o.uv = v.texcoord * GZ_TEX_ST(_BaseColorTexture).xy + GZ_TEX_ST(_BaseColorTexture).zw;
    #ifdef USE_TEXTURE_ARRAYS
        o.arraySlice = GZ_RESOLVE_SLICE(v);
    #endif
    return o;
}

float4 frag(v2f i, bool facing : SV_IsFrontFace) : SV_Target
{
    // Per-vertex/instance array slice (used by cutout, dithered shadows and texture face culling).
    #if defined(_RENDERMODE_CUTOUT) || defined(GZ_DITHERED_SHADOWS) || defined(_FACECULLSOURCE_TEXTURE)
        #ifdef USE_TEXTURE_ARRAYS
            float shadowSlice = i.arraySlice;
        #else
            float shadowSlice = 0;
        #endif
    #endif

    #if defined(_RENDERMODE_CUTOUT) || defined(GZ_DITHERED_SHADOWS)
        #ifdef USE_BASE_COLOR_TEXTURE
            half alpha = GZ_SAMPLE_TEX(_BaseColorTexture, i.uv, shadowSlice).a * _Color.a;
        #else
            half alpha = _Color.a;
        #endif
    #endif

    #ifdef _RENDERMODE_CUTOUT
        clip(alpha - _AlphaCutoff);
    #endif

    #ifdef GZ_DITHERED_SHADOWS
        const half bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        float2 cell = fmod(floor(i.pos.xy), 4.0);
        clip(alpha - (bayer[(int)(cell.y * 4.0 + cell.x)] + 0.5) / 16.0);
    #endif

    // Base UV is fine for face culling here because cull mode is constant per variant.
    #if defined(_FACECULLSOURCE_TEXTURE)
        half auxCull = round(GZ_SAMPLE_TEX(_AuxDataTexture, i.uv, shadowSlice).b * 2.0);
        if (auxCull > 0.5 && ((auxCull >= 1.5) == facing)) discard;
    #endif

    SHADOW_CASTER_FRAGMENT(i)
}

#endif // GZ_SHADOW_CASTER_PASS_INCLUDED
