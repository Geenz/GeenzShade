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
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_shadowcaster
#pragma multi_compile_instancing

#pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
#pragma shader_feature_local USE_BASE_COLOR_TEXTURE
#pragma shader_feature_local USE_TEXTURE_ARRAYS
#pragma shader_feature_local _ARRAYINDEXSOURCE_VERTEX _ARRAYINDEXSOURCE_MATERIALINSTANCE
#pragma shader_feature_local USE_AUX_DATA
#pragma shader_feature_local _FACECULLSOURCE_HARDWARE _FACECULLSOURCE_TEXTURE

#include "UnityCG.cginc"
#include "../GzProperties.cginc"

// Custom input (appdata_base has no UV3) so cutout can read the per-vertex slice.
struct GzShadowInput
{
    float4 vertex   : POSITION;
    float3 normal   : NORMAL;
    float2 texcoord : TEXCOORD0;
    #ifdef USE_TEXTURE_ARRAYS
        float2 texArraySlice : TEXCOORD3; // .x = per-vertex array slice index
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
    o.uv = v.texcoord * GZ_TEX_ST(_BaseColorTexture).xy + GZ_TEX_ST(_BaseColorTexture).zw;
    #ifdef USE_TEXTURE_ARRAYS
        o.arraySlice = GZ_RESOLVE_SLICE(v);
    #endif
    return o;
}

float4 frag(v2f i, fixed facing : VFACE) : SV_Target
{
    // Per-vertex/instance array slice (used by cutout and texture face culling).
    #if defined(_RENDERMODE_CUTOUT) || defined(_FACECULLSOURCE_TEXTURE)
        #ifdef USE_TEXTURE_ARRAYS
            float shadowSlice = i.arraySlice;
        #else
            float shadowSlice = 0;
        #endif
    #endif

    #ifdef _RENDERMODE_CUTOUT
        #ifdef USE_BASE_COLOR_TEXTURE
            half alpha = GZ_SAMPLE_TEX(_BaseColorTexture, i.uv, shadowSlice).a * _Color.a;
        #else
            half alpha = _Color.a;
        #endif
        clip(alpha - _AlphaCutoff);
    #endif

    // Texture-driven face culling in the shadow pass (matches the forward passes)
    // so cast shadows respect per-variant culling, independent of the Aux Data
    // feature. Cull mode is per-variant, so sampling at the base UV is fine.
    #if defined(_FACECULLSOURCE_TEXTURE)
        half auxCull = round(GZ_SAMPLE_TEX(_AuxDataTexture, i.uv, shadowSlice).b * 2.0);
        if (auxCull > 0.5 && ((auxCull >= 1.5) == (facing >= 0))) discard;
    #endif

    SHADOW_CASTER_FRAGMENT(i)
}

#endif // GZ_SHADOW_CASTER_PASS_INCLUDED
