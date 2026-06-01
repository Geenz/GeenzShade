/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// ForwardAdd pass body. Shared by every quality-tier SubShader in GzPBR.shader.
// Render state (Blend One One / ZWrite Off / Tags) stays in the .shader Pass shell.

#ifndef GZ_FORWARD_ADD_PASS_INCLUDED
#define GZ_FORWARD_ADD_PASS_INCLUDED

#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fwdadd_fullshadows
#pragma multi_compile_fog
#pragma multi_compile_instancing

// Shader features
#pragma shader_feature_local USE_BASE_COLOR_TEXTURE
#pragma shader_feature_local USE_ORM_TEXTURE
#pragma shader_feature_local USE_NORMAL_TEXTURE
#pragma shader_feature_local USE_CLEARCOAT_NORMAL_TEXTURE
#pragma shader_feature_local USE_EMISSIVE_TEXTURE
#pragma shader_feature_local USE_SPECULAR_TEXTURE
#pragma shader_feature_local USE_CLEARCOAT_IRIDESCENCE_TEXTURE
#pragma shader_feature_local USE_SHEEN_TEXTURE

#pragma shader_feature_local USE_SPECULAR_EXTENSION
#pragma shader_feature_local USE_CLEARCOAT
#pragma shader_feature_local USE_SHEEN
#pragma shader_feature_local USE_IRIDESCENCE
#pragma shader_feature_local USE_DIFFUSE_TRANSMISSION
#pragma shader_feature_local USE_DIFFUSE_TRANSMISSION_TEXTURE
#pragma shader_feature_local USE_SPECULAR_ANTIALIASING
#pragma shader_feature_local USE_TEXTURE_ARRAYS
#pragma shader_feature_local _ARRAYINDEXSOURCE_VERTEX _ARRAYINDEXSOURCE_MATERIALINSTANCE
#pragma shader_feature_local USE_AUX_DATA
#pragma shader_feature_local USE_AUX_THICKNESS
#pragma shader_feature_local _FACECULLSOURCE_HARDWARE _FACECULLSOURCE_TEXTURE

#pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA

// Include helper files
#include "UnityCG.cginc"
#include "UnityLightingCommon.cginc"
#include "UnityStandardBRDF.cginc"
#include "AutoLight.cginc"

// Include our modular system
#include "../GzTier.cginc"
#include "../GzProperties.cginc"
#include "../GzPassHelpers.cginc"
#include "../GzMaterialSampling.cginc"
#include "../GzLightGathering.cginc"
#include "../GzLayerCalculations.cginc"

GzVertexOutputAdd vert(GzVertexInput v)
{
    return GzVertexAdd(v);
}

half4 frag(GzVertexOutputAdd i, fixed facing : VFACE) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    // Calculate view direction for proper iridescence evaluation
    half3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

    // Sample material data with view direction and facing info
    #ifdef USE_TEXTURE_ARRAYS
        GzMaterialData matData = GzSampleMaterialComplete(i.uv, GzGetTBNAdd(i), viewDir, i.worldPos, facing < 0, i.arraySlice);
    #else
        GzMaterialData matData = GzSampleMaterialComplete(i.uv, GzGetTBNAdd(i), viewDir, i.worldPos, facing < 0);
    #endif

    // Alpha test for cutout mode
    #ifdef _RENDERMODE_CUTOUT
        clip(matData.alpha - _AlphaCutoff);
    #endif

    // Texture-driven face culling (matches ForwardBase) — reads the aux B channel
    // directly, independent of the Aux Data feature.
    #if defined(_FACECULLSOURCE_TEXTURE)
        #ifdef USE_TEXTURE_ARRAYS
            float gzCullSlice = i.arraySlice;
        #else
            float gzCullSlice = 0;
        #endif
        float2 gzCullUV = i.uv * GZ_TEX_ST(_AuxDataTexture).xy + GZ_TEX_ST(_AuxDataTexture).zw;
        half gzCullMode = round(GZ_SAMPLE_TEX(_AuxDataTexture, gzCullUV, gzCullSlice).b * 2.0);
        if (gzCullMode > 0.5 && ((gzCullMode >= 1.5) == (facing >= 0))) discard;
    #endif

    // Create lighting context for additive light
    GzLightingContext ctx = GzCreateAdditiveLightContext(i, matData.normal);

    // Calculate lighting
    half3 color = half3(0, 0, 0);
    if (ctx.lightAtten > 0)
    {
        half3 lightResult = GzEvaluateLayerStack(matData, ctx);
        color = lightResult * ctx.lightColor;
    }

    // Apply fog (fog coord is stored in eyeVec.w)
    UNITY_APPLY_FOG_COLOR(i.eyeVec.w, color, half4(0,0,0,0));

    // Apply alpha
    half alpha = matData.alpha;

    // Premultiply alpha for proper blending
    #ifdef _RENDERMODE_PREMULTIPLIEDALPHA
        color *= alpha;
    #endif

    return half4(color, alpha);
}

#endif // GZ_FORWARD_ADD_PASS_INCLUDED
