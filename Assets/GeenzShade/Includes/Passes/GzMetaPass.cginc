/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// Meta (lightmapper) pass body. Identical across all quality tiers (does not
// read GZ_QUALITY_TIER). Cull Off / Tags stay in the .shader Pass shell.

#ifndef GZ_META_PASS_INCLUDED
#define GZ_META_PASS_INCLUDED

#pragma target 3.0
#pragma vertex vert_meta
#pragma fragment frag_meta

#pragma shader_feature_local USE_BASE_COLOR_TEXTURE
#pragma shader_feature_local USE_ORM_TEXTURE
#pragma shader_feature_local USE_EMISSIVE_TEXTURE
#pragma shader_feature_local USE_TEXTURE_ARRAYS
#pragma shader_feature_local _ARRAYINDEXSOURCE_VERTEX _ARRAYINDEXSOURCE_MATERIALINSTANCE
#pragma shader_feature EDITOR_VISUALIZATION

#include "UnityCG.cginc"
#include "UnityMetaPass.cginc"
#include "UnityStandardUtils.cginc"
#include "../GzProperties.cginc"
#include "../GzPassHelpers.cginc"

struct v2f_meta
{
    float4 pos      : SV_POSITION;
    float4 uv       : TEXCOORD0;
    #ifdef EDITOR_VISUALIZATION
        float2 vizUV        : TEXCOORD1;
        float4 lightCoord   : TEXCOORD2;
    #endif
    #ifdef USE_TEXTURE_ARRAYS
        nointerpolation float arraySlice : TEXCOORD3; // per-vertex array slice (flat)
    #endif
};

// Lightmapping albedo calculation - accounts for rough metals
half3 GzLightmappingAlbedo(half3 diffuse, half3 specular, half roughness)
{
    // Rough metals (which have black diffuse) still scatter light
    // Add some of the specular color based on roughness
    half3 res = diffuse;
    res += specular * roughness * 0.5;
    return res;
}

v2f_meta vert_meta(GzVertexInput v)
{
    v2f_meta o;
    o.pos = UnityMetaVertexPosition(v.vertex, v.uv1, v.uv2, unity_LightmapST, unity_DynamicLightmapST);

    // Use same UV transformation as main shader
    o.uv.xy = GzTransformUV(v.uv, GZ_TEX_ST(_BaseColorTexture));
    o.uv.zw = 0;
    #ifdef USE_TEXTURE_ARRAYS
        o.arraySlice = GZ_RESOLVE_SLICE(v);
    #endif

    #ifdef EDITOR_VISUALIZATION
        o.vizUV = 0;
        o.lightCoord = 0;
        if (unity_VisualizationMode == EDITORVIZ_TEXTURE)
            o.vizUV = UnityMetaVizUV(unity_EditorViz_UVIndex, v.uv, v.uv1, v.uv2, unity_EditorViz_Texture_ST);
        else if (unity_VisualizationMode == EDITORVIZ_SHOWLIGHTMASK)
        {
            o.vizUV = v.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
            o.lightCoord = mul(unity_EditorViz_WorldToLight, mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)));
        }
    #endif

    return o;
}

float4 frag_meta(v2f_meta i) : SV_Target
{
    #ifdef USE_TEXTURE_ARRAYS
        float metaSlice = i.arraySlice;
    #else
        float metaSlice = 0;
    #endif

    // Sample base color
    half3 albedo = _Color.rgb;
    #ifdef USE_BASE_COLOR_TEXTURE
        albedo *= GZ_SAMPLE_TEX(_BaseColorTexture, i.uv.xy, metaSlice).rgb;
    #endif

    // Sample metallic and roughness from ORM texture
    half metallic = _Metallic;
    half roughness = _Roughness;

    #ifdef USE_ORM_TEXTURE
        half3 orm = GZ_SAMPLE_TEX(_ORMTexture, i.uv.xy, metaSlice).rgb;
        roughness = orm.g * _Roughness;  // G = Roughness
        metallic = orm.b * _Metallic;    // B = Metallic
    #endif

    // Calculate diffuse and specular from metallic workflow
    // Using Unity's standard functions for consistency with lightmapper
    half3 specColor;
    half oneMinusReflectivity;
    half3 diffColor = DiffuseAndSpecularFromMetallic(albedo, metallic, specColor, oneMinusReflectivity);

    // Sample emission
    half3 emission = _EmissiveFactor.rgb * _EmissionStrength;
    #ifdef USE_EMISSIVE_TEXTURE
        emission *= GZ_SAMPLE_TEX(_EmissiveTexture, i.uv.xy, metaSlice).rgb;
    #endif

    // Setup meta input
    UnityMetaInput metaInput;
    UNITY_INITIALIZE_OUTPUT(UnityMetaInput, metaInput);

    #ifdef EDITOR_VISUALIZATION
        metaInput.Albedo = diffColor;
        metaInput.VizUV = i.vizUV;
        metaInput.LightCoord = i.lightCoord;
    #else
        // For lightmapping, include rough metal contribution
        metaInput.Albedo = GzLightmappingAlbedo(diffColor, specColor, roughness);
    #endif

    metaInput.SpecularColor = specColor;
    metaInput.Emission = emission;

    return UnityMetaFragment(metaInput);
}

#endif // GZ_META_PASS_INCLUDED
