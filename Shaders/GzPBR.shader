/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

// ============================================
// GeenzShade PBR Shader - Texture Channel Documentation
// ============================================
// 
// Base Color Texture:
//   R: Red color component
//   G: Green color component  
//   B: Blue color component
//   A: Alpha (opacity) for transparency modes
//
// ORM Texture (Occlusion-Roughness-Metallic):
//   R: Ambient occlusion (0 = occluded, 1 = unoccluded)
//   G: Roughness factor (0 = glossy, 1 = rough)
//   B: Metallic factor (0 = dielectric, 1 = metal)
//   A: Unused
//
// Normal Map:
//   Tangent-space normal
//
// Clearcoat Normal Map:
//   Tangent-space normal for clearcoat layer
//
// Emissive Texture:
//   RGB: Emission color (multiplied by EmissiveFactor)
//   A: Unused
//
// Specular Texture:
//   RGB: Specular color tint (for dielectrics)
//   A: Specular strength factor (multiplies SpecularFactor)
//
// Clearcoat/Iridescence Texture:
//   R: Clearcoat intensity factor
//   G: Clearcoat roughness factor
//   B: Iridescence intensity factor  
//   A: Iridescence thickness factor
//
// Sheen Texture:
//   RGB: Sheen color tint
//   A: Sheen roughness factor
//
// Diffuse Transmission Texture (Combined):
//   RGB: Color that modulates transmitted light
//   A: Percentage of diffuse light transmitted through surface (0 = opaque, 1 = fully transmissive)
//
// Note: This combines what the glTF spec defines as two separate textures into one:
//   - diffuseTransmissionTexture (A channel only) 
//   - diffuseTransmissionColorTexture (RGB channels only)
//
// ============================================

Shader "GeenzShade/GzPBR"
{
    Properties
    {
        [Header(Base Textures)]
        _BaseColorTexture ("Base Color (RGB: Color, A: Alpha)", 2D) = "white" {}
        _ORMTexture ("ORM (R: Occlusion, G: Roughness, B: Metallic)", 2D) = "white" {}
        _NormalTexture ("Normal Map (RGB: Tangent Space Normal)", 2D) = "bump" {}
        _ClearcoatNormalTexture ("Clearcoat Normal (RGB: Tangent Space Normal)", 2D) = "bump" {}
        _EmissiveTexture ("Emissive (RGB: Emission Color)", 2D) = "black" {}
        
        [Header(Extension Textures)]
        _SpecularTexture ("Specular (RGB: Color Factor, A: Strength Factor)", 2D) = "white" {}
        _ClearcoatIridescenceTexture ("Clearcoat/Irid (R: Clearcoat, G: CC Rough, B: Irid Factor, A: Irid Thick)", 2D) = "black" {}
        _SheenTexture ("Sheen (RGB: Sheen Color, A: Sheen Roughness)", 2D) = "black" {}
        
        [Header(Base Properties)]
        _Color ("Base Color Factor", Color) = (1,1,1,1)
        _Metallic ("Metallic Factor", Range(0,1)) = 0.0
        _Roughness ("Roughness Factor", Range(0,1)) = 0.5
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _NormalScale ("Normal Scale", Range(0,2)) = 1.0
        _EmissiveFactor ("Emissive Factor", Color) = (0,0,0,1)
        _EmissionStrength ("Emission Strength", Range(0,10)) = 1.0
        
        [Header(IOR)]
        _IOR ("Index of Refraction", Range(1,3)) = 1.5
        
        [Header(Specular Extension)]
        [Toggle(USE_SPECULAR_EXTENSION)] _UseSpecularExtension ("Use Specular Extension", Float) = 0
        _SpecularFactor ("Specular Factor", Range(0,1)) = 1.0
        _SpecularColor ("Specular Color Factor", Color) = (1,1,1,1)
        
        [Header(Clearcoat)]
        [Toggle(USE_CLEARCOAT)] _UseClearcoat ("Use Clearcoat", Float) = 0
        _ClearcoatFactor ("Clearcoat Factor", Range(0,1)) = 0.0
        _ClearcoatRoughness ("Clearcoat Roughness Factor", Range(0,1)) = 0.0
        _ClearcoatNormalScale ("Clearcoat Normal Scale", Range(0,2)) = 1.0
        
        [Header(Sheen)]
        [Toggle(USE_SHEEN)] _UseSheen ("Use Sheen", Float) = 0
        _SheenColor ("Sheen Color Factor", Color) = (0,0,0,1)
        _SheenFactor ("Sheen Factor", Range(0,1)) = 0.0
        _SheenRoughness ("Sheen Roughness Factor", Range(0,1)) = 0.0
        _SheenRimBoost ("Sheen Rim Boost (Artistic)", Range(0,10)) = 1.0
        
        [Header(Iridescence)]
        [Toggle(USE_IRIDESCENCE)] _UseIridescence ("Use Iridescence", Float) = 0
        _IridescenceFactor ("Iridescence Factor", Range(0,1)) = 0.0
        _IridescenceIOR ("Iridescence IOR", Range(1.001,2.0)) = 1.3
        _IridescenceThickness ("Thickness Factor", Range(0,1)) = 1.0
        _IridescenceThicknessMin ("Thickness Min (nm)", Range(50,800)) = 100
        _IridescenceThicknessMax ("Thickness Max (nm)", Range(100,1200)) = 400
        
        [Header(Diffuse Transmission)]
        [Toggle(USE_DIFFUSE_TRANSMISSION)] _UseDiffuseTransmission ("Use Diffuse Transmission", Float) = 0
        _DiffuseTransmissionFactor ("Diffuse Transmission Factor", Range(0,1)) = 0.0
        _DiffuseTransmissionColorFactor ("Diffuse Transmission Color", Color) = (1,1,1,1)
        _DiffuseTransmissionTexture ("Transmission (RGB: Color, A: Factor)", 2D) = "white" {}
        
        [Header(Texture Enable Flags)]
        [Toggle(USE_BASE_COLOR_TEXTURE)] _UseBaseColorTexture ("Use Base Color Texture", Float) = 1
        [Toggle(USE_ORM_TEXTURE)] _UseORMTexture ("Use ORM Texture", Float) = 1
        [Toggle(USE_NORMAL_TEXTURE)] _UseNormalTexture ("Use Normal Texture", Float) = 1
        [Toggle(USE_CLEARCOAT_NORMAL_TEXTURE)] _UseClearcoatNormalTexture ("Use Clearcoat Normal Texture", Float) = 1
        [Toggle(USE_EMISSIVE_TEXTURE)] _UseEmissiveTexture ("Use Emissive Texture", Float) = 0
        [Toggle(USE_SPECULAR_TEXTURE)] _UseSpecularTexture ("Use Specular Texture", Float) = 0
        [Toggle(USE_CLEARCOAT_IRIDESCENCE_TEXTURE)] _UseClearcoatIridescenceTexture ("Use Clearcoat/Iridescence Texture", Float) = 0
        [Toggle(USE_SHEEN_TEXTURE)] _UseSheenTexture ("Use Sheen Texture", Float) = 0
        [Toggle(USE_DIFFUSE_TRANSMISSION_TEXTURE)] _UseDiffuseTransmissionTexture ("Use Diffuse Transmission Texture", Float) = 0
        
        [Header(Advanced Options)]
        [KeywordEnum(Off, On)] _VertexLights ("Vertex Lights", Float) = 1
        [KeywordEnum(Off, On)] _SHDominantLight ("SH Dominant Light", Float) = 1
        [Toggle(USE_ENVIRONMENT_REFLECTION)] _UseEnvironmentReflection ("Use Environment Reflection", Float) = 1
        [Toggle(USE_VRC_LIGHT_VOLUMES)] _UseVRCLightVolumes ("Use VRC Light Volumes", Float) = 1
        _LightIntensityMultiplier ("Light Intensity Multiplier", Range(0.1, 10)) = 3.0
        
        [Header(Rendering Options)]
        [Toggle(USE_SPECULAR_ANTIALIASING)] _UseSpecularAntialiasing ("Use Specular Antialiasing", Float) = 0
        _SpecularAAEdgeFalloff ("Specular AA Edge Falloff", Range(0, 1)) = 0.5
        _SpecularAAEdgeRoughness ("Specular AA Edge Roughness", Range(0, 1)) = 0.3
        _SpecularAACameraDistanceFalloff ("Specular AA Distance Falloff", Range(0, 100)) = 20
        
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _RenderMode ("Render Mode", Float) = 0
        
        [Header(Environment)]
        _ReflectionProbeThreshold ("Reflection Probe Threshold", Range(0,1)) = 0.1
        _SHThreshold ("SH Threshold", Range(0,1)) = 0.1
        _FallbackCubemap ("Fallback Environment", Cube) = "" {}
        _FallbackIntensity ("Fallback Intensity", Range(0,2)) = 1.0
        _FallbackDiffuseMipLevel ("Fallback Diffuse Mip Level", Range(0,10)) = 6.0
        _FallbackMaxMipLevel ("Fallback Max Mip Level", Range(0,10)) = 8.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull [_Cull]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]
        
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }
            
            CGPROGRAM
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
            
            #pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
            #pragma shader_feature_local _VERTEXLIGHTS_OFF _VERTEXLIGHTS_ON
            #pragma shader_feature_local _SHDOMINANTLIGHT_OFF _SHDOMINANTLIGHT_ON
            
            // Include helper files
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "UnityStandardBRDF.cginc"
            #include "AutoLight.cginc"
            
            // Include our modular system
            #include "../Includes/GzProperties.cginc"
            #include "../Includes/GzPassHelpers.cginc"
            #include "../Includes/GzMaterialSampling.cginc"
            #include "../Includes/GzLightGathering.cginc"
            #include "../Includes/GzLayerCalculations.cginc"
            
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
                
                // Add Light Volumes ambient contribution
                #ifdef USE_VRC_LIGHT_VOLUMES
                    indirect.diffuse += GzGetLightVolumeAmbient(i.worldPos, matData.normal);
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
                #ifdef USE_CLEARCOAT
                    if (matData.clearcoatFactor > 0)
                    {
                        half3 clearcoatReflection = reflect(-indirectCtx.viewDir, matData.clearcoatNormal);
                        clearcoatEnvSpecular = GzSampleEnvironment(clearcoatReflection, matData.clearcoatRoughness, i.worldPos);
                    }
                #endif
                
                finalColor += GzEvaluateLayerStackIndirect(matData, indirectCtx, 
                                                          indirect.diffuse, indirect.specular, clearcoatEnvSpecular);
                
                // VRC Light Volumes - use dominant light extraction
                #ifdef USE_VRC_LIGHT_VOLUMES
                    GzLightingContext lvCtx = GzCreateLightVolumeDominantLightContext(i.worldPos, matData.normal);
                    if (lvCtx.lightAtten > 0)
                    {
                        half3 lvLight = GzEvaluateLayerStack(matData, lvCtx);
                        finalColor += lvLight * lvCtx.lightColor;
                    }
                #endif
                
                // Apply fog
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                
                // Premultiply alpha for proper blending
                #ifdef _RENDERMODE_PREMULTIPLIEDALPHA
                    finalColor *= matData.alpha;
                #endif
                
                return half4(finalColor, matData.alpha);
            }
            ENDCG
        }
        
        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            
            CGPROGRAM
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
            
            #pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
            
            // Include helper files
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "UnityStandardBRDF.cginc"
            #include "AutoLight.cginc"
            
            // Include our modular system
            #include "../Includes/GzProperties.cginc"
            #include "../Includes/GzPassHelpers.cginc"
            #include "../Includes/GzMaterialSampling.cginc"
            #include "../Includes/GzLightGathering.cginc"
            #include "../Includes/GzLayerCalculations.cginc"
            
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
                GzMaterialData matData = GzSampleMaterialComplete(i.uv, GzGetTBNAdd(i), viewDir, i.worldPos, facing < 0);
                
                // Alpha test for cutout mode
                #ifdef _RENDERMODE_CUTOUT
                    clip(matData.alpha - _AlphaCutoff);
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
                
                // Apply fog
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                
                // Apply alpha
                half alpha = matData.alpha;
                
                // Premultiply alpha for proper blending
                #ifdef _RENDERMODE_PREMULTIPLIEDALPHA
                    color *= alpha;
                #endif
                
                return half4(color, alpha);
            }
            ENDCG
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            
            #pragma shader_feature_local _RENDERMODE_OPAQUE _RENDERMODE_CUTOUT _RENDERMODE_TRANSPARENT _RENDERMODE_PREMULTIPLIEDALPHA
            #pragma shader_feature_local USE_BASE_COLOR_TEXTURE
            
            #include "UnityCG.cginc"
            #include "../Includes/GzProperties.cginc"
            
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
            ENDCG
        }
        
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert_meta
            #pragma fragment frag_meta
            
            #pragma shader_feature_local USE_BASE_COLOR_TEXTURE
            #pragma shader_feature_local USE_ORM_TEXTURE
            #pragma shader_feature_local USE_EMISSIVE_TEXTURE
            #pragma shader_feature EDITOR_VISUALIZATION
            
            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"
            #include "UnityStandardUtils.cginc"
            #include "../Includes/GzProperties.cginc"
            #include "../Includes/GzPassHelpers.cginc"
            
            struct v2f_meta
            {
                float4 pos      : SV_POSITION;
                float4 uv       : TEXCOORD0;
                #ifdef EDITOR_VISUALIZATION
                    float2 vizUV        : TEXCOORD1;
                    float4 lightCoord   : TEXCOORD2;
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
                o.uv.xy = GzTransformUV(v.uv, _BaseColorTexture_ST);
                o.uv.zw = 0;
                
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
                // Sample base color
                half3 albedo = _Color.rgb;
                #ifdef USE_BASE_COLOR_TEXTURE
                    albedo *= tex2D(_BaseColorTexture, i.uv.xy).rgb;
                #endif
                
                // Sample metallic and roughness from ORM texture
                half metallic = _Metallic;
                half roughness = _Roughness;
                
                #ifdef USE_ORM_TEXTURE
                    half3 orm = tex2D(_ORMTexture, i.uv.xy).rgb;
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
                    emission *= tex2D(_EmissiveTexture, i.uv.xy).rgb;
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
            ENDCG
        }
    }
    
    FallBack "Diffuse"
    CustomEditor "GeenzShade.GzPBRShaderGUI"
}