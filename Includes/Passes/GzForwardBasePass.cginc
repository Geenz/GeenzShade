/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// ForwardBase pass body. Shared by every quality-tier SubShader in GzPBR.shader.
// Each SubShader's Pass shell sets GZ_QUALITY_TIER then #includes this file.
// Render state (Tags/Blend/etc) stays in the .shader.

#ifndef GZ_FORWARD_BASE_PASS_INCLUDED
#define GZ_FORWARD_BASE_PASS_INCLUDED

#pragma target 3.0
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fwdbase
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
#pragma shader_feature_local USE_ENVIRONMENT_REFLECTION
#pragma shader_feature_local USE_VRC_LIGHT_VOLUMES
#pragma shader_feature_local USE_SPECULAR_ANTIALIASING
#pragma shader_feature_local USE_SSR

#pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
#pragma shader_feature_local _VERTEXLIGHTS_OFF _VERTEXLIGHTS_ON
#pragma shader_feature_local _SHDOMINANTLIGHT_OFF _SHDOMINANTLIGHT_ON

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

GzVertexOutput vert(GzVertexInput v)
{
    return GzVertexBase(v);
}

half4 frag(GzVertexOutput i, fixed facing : VFACE) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    // Calculate view direction for proper iridescence evaluation
    half3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

    // Sample material data with view direction and facing info
    // This handles normal flipping for back faces internally
    GzMaterialData matData = GzSampleMaterialComplete(i.uv, GzGetTBN(i), viewDir, i.worldPos, facing < 0);

    // Alpha test for cutout mode
    #ifdef _RENDERMODE_CUTOUT
        clip(matData.alpha - _AlphaCutoff);
    #endif

    // Create direct lighting context FIRST to get attenuation
    GzLightingContext dirLightCtx = GzCreateDirectionalLightContext(i, matData.normal);

    // Gather indirect lighting (using attenuation from direct light context)
    half avgEnvLuminance = GzGetAverageEnvironmentLuminance();
    GzIndirectLight indirect = GzGatherIndirectLight(i.worldPos, matData.normal,
                                                     viewDir,
                                                     matData.roughness, matData.occlusion, i.ambientOrLightmapUV, dirLightCtx.lightAtten);

    // Screen-space reflections (Tier 0 only, per Mochie's technique) composited
    // over the probe/cubemap indirect specular. Misses / mirrors / high roughness
    // return 0 alpha and we keep the probe result. i.pos.xy is the pixel coord.
    #ifdef GZ_USE_SSR
        half3 ssrReflDir = reflect(-viewDir, matData.normal);
        float ssrNoise = GzSSRNoise(i.pos.xy);
        float4 ssr = GzGetSSR(i.worldPos, viewDir, ssrReflDir, matData.normal,
                              1.0 - matData.roughness, matData.baseColor, matData.metallic, ssrNoise);
        ssr.rgb *= matData.occlusion;
        indirect.specular = lerp(indirect.specular, ssr.rgb, ssr.a * saturate(_SSRStrength));
    #endif

    // Initialize final color with emissive
    // Apply clearcoat attenuation to emission per glTF spec
    half3 emission = matData.emissive * matData.emissiveStrength;
    emission = GzAttenuateEmissionByClearcoat(emission, matData, viewDir);
    half3 finalColor = emission;

    // Main directional light (already created above)
    if (dirLightCtx.lightAtten > 0)
    {
        half3 directLight = GzEvaluateLayerStack(matData, dirLightCtx);
        finalColor += directLight * dirLightCtx.lightColor;
    }

    // DLM dominant light (reconstructed from lightmap directional data)
    // Only for objects that actually have lightmaps
    #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
        // Check if this object has valid lightmap UVs
        GzLightingContext dlmCtx = GzCreateLightmapDominantLightContext(i.worldPos, i.ambientOrLightmapUV.xy);
            if (dlmCtx.lightAtten > 0)
            {
                GzPopulateLightingVectors(dlmCtx, matData.normal);
                half3 dlmLight = GzEvaluateLayerStack(matData, dlmCtx);
                finalColor += dlmLight * dlmCtx.lightColor;
            }
    #endif

    // SH dominant light (only for dynamic objects without lightmaps)
    #if defined(_SHDOMINANTLIGHT_ON) && !defined(LIGHTMAP_ON)
        GzLightingContext shCtx = GzCreateSHDominantLightContext(i.worldPos, matData.normal);
        if (shCtx.lightAtten > 0)
        {
            half3 shLight = GzEvaluateLayerStack(matData, shCtx);
            finalColor += shLight * shCtx.lightColor;
        }
    #endif

    // Vertex lights
    #ifdef _VERTEXLIGHTS_ON
        finalColor += GzGetVertexLights(i, matData.baseColor, matData.metallic);
    #endif

    // Add indirect lighting
    // Create a view-only context for indirect lighting (no light direction needed)
    GzLightingContext indirectCtx;
    indirectCtx.viewDir = viewDir;
    half3 normalizedNormal = normalize(matData.normal);
    indirectCtx.NoV = saturate(dot(normalizedNormal, indirectCtx.viewDir));

    // Sample clearcoat environment if needed
    half3 clearcoatEnvSpecular = half3(0, 0, 0);
    #ifdef GZ_USE_CLEARCOAT
        if (matData.clearcoatFactor > 0)
        {
            half3 clearcoatReflection = reflect(-indirectCtx.viewDir, matData.clearcoatNormal);
            clearcoatEnvSpecular = GzSampleEnvironment(clearcoatReflection, matData.clearcoatRoughness, i.worldPos);
        }
    #endif

    finalColor += GzEvaluateLayerStackIndirect(matData, indirectCtx,
                                              indirect.diffuse, indirect.specular, clearcoatEnvSpecular);

    // VRC Light Volumes — per-channel directional evaluation
    #ifdef USE_VRC_LIGHT_VOLUMES
        finalColor += GzEvaluateLightVolumes(i.worldPos, matData.normal, viewDir, matData);
    #endif

    // Apply fog (fog coord is stored in eyeVec.w)
    UNITY_APPLY_FOG(i.eyeVec.w, finalColor);

    // Premultiply alpha for proper blending
    #ifdef _RENDERMODE_PREMULTIPLIEDALPHA
        finalColor *= matData.alpha;
    #endif

    return half4(finalColor, matData.alpha);
}

#endif // GZ_FORWARD_BASE_PASS_INCLUDED
