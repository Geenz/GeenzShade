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

#include "UnityCG.cginc"
#include "../GzProperties.cginc"

struct v2f
{
    V2F_SHADOW_CASTER;
    float2 uv : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2f vert(appdata_base v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    o.uv = TRANSFORM_TEX(v.texcoord, _BaseColorTexture);
    return o;
}

float4 frag(v2f i) : SV_Target
{
    #ifdef _RENDERMODE_CUTOUT
        #ifdef USE_BASE_COLOR_TEXTURE
            half alpha = tex2D(_BaseColorTexture, i.uv).a * _Color.a;
        #else
            half alpha = _Color.a;
        #endif
        clip(alpha - _AlphaCutoff);
    #endif

    SHADOW_CASTER_FRAGMENT(i)
}

#endif // GZ_SHADOW_CASTER_PASS_INCLUDED
