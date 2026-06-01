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

        [Header(Texture Arrays (Advanced))]
        [Toggle(USE_TEXTURE_ARRAYS)] _UseTextureArrays ("Use Texture Arrays", Float) = 0
        [KeywordEnum(Vertex, MaterialInstance)] _ArrayIndexSource ("Array Index Source", Float) = 0
        _ArraySlice ("Array Slice (Material Instance source)", Float) = 0
        // Parallel Texture2DArray slots used when Use Texture Arrays is on. The
        // per-vertex slice index is read from UV channel 3 (.x). These mirror the
        // 2D input slots one-for-one.
        _BaseColorTextureArray ("Base Color Array (RGB: Color, A: Alpha)", 2DArray) = "" {}
        _ORMTextureArray ("ORM Array (R: Occ, G: Rough, B: Metal)", 2DArray) = "" {}
        _NormalTextureArray ("Normal Array (Tangent Space)", 2DArray) = "" {}
        _EmissiveTextureArray ("Emissive Array (RGB)", 2DArray) = "" {}
        _SpecularTextureArray ("Specular Array (RGB: Color, A: Strength)", 2DArray) = "" {}
        _ClearcoatNormalTextureArray ("Clearcoat Normal Array (Tangent Space)", 2DArray) = "" {}
        _ClearcoatIridescenceTextureArray ("Clearcoat/Irid Array (R: CC, G: CC Rough, B: Irid, A: Thick)", 2DArray) = "" {}
        _SheenTextureArray ("Sheen Array (RGB: Color, A: Roughness)", 2DArray) = "" {}
        _DiffuseTransmissionTextureArray ("Transmission Array (RGB: Color, A: Factor)", 2DArray) = "" {}

        [Header(Auxiliary Data (Advanced))]
        [Toggle(USE_AUX_DATA)] _UseAuxData ("Use Auxiliary Data Texture", Float) = 0
        _AuxDataTexture ("Aux Data (R: IOR, G: Irid IOR, B: Face Cull, A: Sheen Rim)", 2D) = "gray" {}
        _AuxDataTextureArray ("Aux Data Array", 2DArray) = "" {}
        // Decode ranges for the aux channels — must match what the texture was baked with.
        _AuxIORMin ("Aux IOR Min", Float) = 0
        _AuxIORMax ("Aux IOR Max", Float) = 4
        _AuxIridescenceIORMin ("Aux Iridescence IOR Min", Float) = 0
        _AuxIridescenceIORMax ("Aux Iridescence IOR Max", Float) = 4
        _AuxSheenRimMin ("Aux Sheen Rim Min", Float) = 0
        _AuxSheenRimMax ("Aux Sheen Rim Max", Float) = 10
        // Second aux page: per-variant iridescence thickness range (R: min, G: max;
        // B/A reserved). Stored normalized into _AuxThicknessRange (nanometers).
        [Toggle(USE_AUX_THICKNESS)] _UseAuxThickness ("Use Aux Thickness Range", Float) = 0
        _AuxDataTexture2 ("Aux Thickness (R: Thick Min, G: Thick Max)", 2D) = "black" {}
        _AuxDataTexture2Array ("Aux Thickness Array", 2DArray) = "" {}
        _AuxThicknessRangeMin ("Aux Thickness Range Min (nm)", Float) = 0
        _AuxThicknessRangeMax ("Aux Thickness Range Max (nm)", Float) = 2000

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
        [KeywordEnum(Hardware, Texture)] _FaceCullSource ("Face Cull Source", Float) = 0
        // Artist cull intent preserved across the Texture-mode switch (which forces
        // _Cull = Off for double-sided rendering). This is what the baker reads.
        [HideInInspector] _FaceCullBake ("Baked Cull Intent", Float) = 2
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _ZTest ("ZTest", Float) = 4
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _RenderMode ("Render Mode", Float) = 0
        
        [Header(Screen Space Reflections)]
        [Toggle(USE_SSR)] _UseSSR ("Use Screen-Space Reflections (Ultra/LOD0 only)", Float) = 0
        _SSRStrength ("SSR Strength", Range(0,1)) = 1.0
        _SSRHeight ("SSR Ray Height/Step", Range(0.001,2)) = 0.65
        _SSRMaxRoughness ("SSR Max Roughness", Range(0,1)) = 0.3
        _SSREdgeFade ("SSR Edge Fade", Range(0,0.5)) = 0.1

        [Header(Environment)]
        _LightmapReflectionBlend ("Lightmap Reflection Blend", Range(0,1)) = 0
        _ReflectionProbeThreshold ("Reflection Probe Threshold", Range(0,1)) = 0.1
        _SHThreshold ("SH Threshold", Range(0,1)) = 0.1
        _FallbackCubemap ("Fallback Environment", Cube) = "" {}
        _FallbackIntensity ("Fallback Intensity", Range(0,2)) = 1.0
        _FallbackDiffuseMipLevel ("Fallback Diffuse Mip Level", Range(0,10)) = 6.0
        _FallbackMaxMipLevel ("Fallback Max Mip Level", Range(0,10)) = 8.0
    }
    
    // ============================================================
    // Quality-tier SubShaders. Unity selects the first (top-down) whose LOD is
    // <= Shader.globalMaximumLOD / shader.maximumLOD, so they are ordered
    // highest-quality first. Each Pass shell sets GZ_QUALITY_TIER (and, on Tier
    // 0's ForwardBase, GZ_ENABLE_SSR) then includes the shared pass body.
    // Capability per tier is resolved in GzTier.cginc:
    //   Tier 0 (LOD 400): exact iridescence & sheen, clearcoat, + SSR
    //   Tier 1 (LOD 300): approx iridescence & sheen, clearcoat
    //   Tier 2 (LOD 200): approx iridescence & sheen, no clearcoat
    //   Tier 3 (LOD 100): no iridescence / sheen / clearcoat
    // ============================================================

    // ---------- Tier 0 "Ultra" (LOD 400) ----------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        Cull [_Cull]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        // Grab the opaque scene for screen-space reflections (Tier 0 only).
        // Named grab => captured once per frame and shared. Sampled in GzSSR.cginc
        // when the per-material USE_SSR toggle is on. Use the Render Queue field to
        // draw reflective materials after the geometry they should reflect.
        GrabPass { "_GzGrab" }

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 0
            #define GZ_ENABLE_SSR
            #include "../Includes/Passes/GzForwardBasePass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 0
            #include "../Includes/Passes/GzForwardAddPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 0
            #include "../Includes/Passes/GzShadowCasterPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 0
            #include "../Includes/Passes/GzMetaPass.cginc"
            ENDCG
        }
    }

    // ---------- Tier 1 "High" (LOD 300) ----------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull [_Cull]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 1
            #include "../Includes/Passes/GzForwardBasePass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 1
            #include "../Includes/Passes/GzForwardAddPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 1
            #include "../Includes/Passes/GzShadowCasterPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 1
            #include "../Includes/Passes/GzMetaPass.cginc"
            ENDCG
        }
    }

    // ---------- Tier 2 "Medium" (LOD 200) ----------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull [_Cull]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 2
            #include "../Includes/Passes/GzForwardBasePass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 2
            #include "../Includes/Passes/GzForwardAddPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 2
            #include "../Includes/Passes/GzShadowCasterPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 2
            #include "../Includes/Passes/GzMetaPass.cginc"
            ENDCG
        }
    }

    // ---------- Tier 3 "Low" (LOD 100) ----------
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
            #define GZ_QUALITY_TIER 3
            #include "../Includes/Passes/GzForwardBasePass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ForwardAdd"
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 3
            #include "../Includes/Passes/GzForwardAddPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #define GZ_QUALITY_TIER 3
            #include "../Includes/Passes/GzShadowCasterPass.cginc"
            ENDCG
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }

            Cull Off

            CGPROGRAM
            #define GZ_QUALITY_TIER 3
            #include "../Includes/Passes/GzMetaPass.cginc"
            ENDCG
        }
    }

    FallBack "Diffuse"
    CustomEditor "GeenzShade.GzPBRShaderGUI"
}