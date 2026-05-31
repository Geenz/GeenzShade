/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_LIGHT_GATHERING_INCLUDED
#define GZ_LIGHT_GATHERING_INCLUDED

#include "UnityCG.cginc"
#include "UnityLightingCommon.cginc"
#include "UnityStandardUtils.cginc"
#include "UnityStandardCore.cginc"
#include "UnityImageBasedLighting.cginc"
#include "AutoLight.cginc"
#include "GzLighting.cginc"
#include "GzPassHelpers.cginc"
#include "GzProperties.cginc"
#include "GzSH.cginc"

// VRC Light Volumes support (include early for function availability)
#ifdef USE_VRC_LIGHT_VOLUMES
    #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
#endif

// ============================================
// Lightmap Data Structure
// ============================================

// Extract dominant light direction and color from DLM for full shading
struct GzLightmapData
{
    half3 diffuseColor;
    half3 dominantLightDir;
    half3 dominantLightColor;
    half lightAtten;
};

// Forward declaration
GzLightmapData GzSampleLightmapComplete(float2 lightmapUV);

// ============================================
// Light Context Creation
// ============================================

// Create lighting context for main light in ForwardBase (directional light)
GzLightingContext GzCreateDirectionalLightContext(GzVertexOutput i, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    // Get attenuation (shadows for directional light)
    UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
    
    // Handle shadowmask blending if needed
    #if defined(HANDLE_SHADOWS_BLENDING_IN_GI)
        half bakedAtten = UnitySampleBakedOcclusion(i.ambientOrLightmapUV.xy, i.worldPos);
        float zDist = dot(_WorldSpaceCameraPos - i.worldPos, UNITY_MATRIX_V[2].xyz);
        float fadeDist = UnityComputeShadowFadeDistance(i.worldPos, zDist);
        atten = UnityMixRealtimeAndBakedShadows(atten, bakedAtten, UnityComputeShadowFade(fadeDist));
    #endif
    
    // Check if we should zero out direct light (subtractive mode)
    #if defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
        // In subtractive mode, direct light is handled through lightmap modification
        ctx.lightColor = half3(0, 0, 0);
        ctx.lightDir = half3(0, 1, 0);
        ctx.lightAtten = 0;
    #else
        // Normal direct lighting
        UnityLight mainLight = MainLight();
        ctx.lightColor = mainLight.color * atten * _LightIntensityMultiplier;
        ctx.lightDir = mainLight.dir;
        ctx.lightAtten = atten;
    #endif
    
    ctx.viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
    
    // Populate lighting vectors
    GzPopulateLightingVectors(ctx, normal);
    
    return ctx;
}

// Create lighting context for SH dominant light (ForwardBase)
GzLightingContext GzCreateSHDominantLightContext(float3 worldPos, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    #ifdef _SHDOMINANTLIGHT_ON
        // Get dominant light from SH (functions from GeenzSH.cginc)
        half3 shLightDir = GzGetDominantLightDirFromSH();
        half3 shLightColor = GzGetDominantLightColorFromSH();
        
        if (length(shLightColor) > 0.001)
        {
            ctx.lightDir = shLightDir;
            ctx.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
            ctx.lightColor = shLightColor;
            ctx.lightAtten = 1.0; // SH light has no distance attenuation
            
            // Populate vectors
            GzPopulateLightingVectors(ctx, normal);
        }
    #endif
    
    return ctx;
}

// Create lighting context from reconstructed DLM data
GzLightingContext GzCreateLightmapDominantLightContext(float3 worldPos, float2 lightmapUV)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    #if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
        // Sample the complete lightmap data including directional information
        GzLightmapData lmData = GzSampleLightmapComplete(lightmapUV);
        
        if (lmData.lightAtten > 0.001 && length(lmData.dominantLightColor) > 0.001)
        {
            ctx.lightDir = lmData.dominantLightDir;
            ctx.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
            ctx.lightColor = lmData.dominantLightColor;
            ctx.lightAtten = lmData.lightAtten;
            
            // Note: Don't populate vectors here - let the caller do it with the appropriate normal
        }
    #endif
    
    return ctx;
}


// Create lighting context for point/spot lights (ForwardAdd)
GzLightingContext GzCreateAdditiveLightContext(GzVertexOutputAdd i, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    // Get light direction from vertex shader (IN_LIGHTDIR_FWDADD macro)
    half3 lightDir = half3(i.tangentToWorldAndLightDir[0].w, i.tangentToWorldAndLightDir[1].w, i.tangentToWorldAndLightDir[2].w);
    
    // Get combined distance and shadow attenuation
    UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
    
    // Use Unity's AdditiveLight function
    UnityLight addLight = AdditiveLight(lightDir, atten);
    
    ctx.lightColor = addLight.color * _LightIntensityMultiplier;
    ctx.lightDir = addLight.dir;
    ctx.lightAtten = atten;
    ctx.viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
    
    // Populate lighting vectors
    GzPopulateLightingVectors(ctx, normal);
    
    return ctx;
}

// ============================================
// Vertex Light Gathering (ForwardBase only)
// ============================================

// Get vertex light contribution
half3 GzGetVertexLights(GzVertexOutput i, half3 albedo, half metallic)
{
    #if defined(VERTEXLIGHT_ON) && defined(_VERTEXLIGHTS_ON) && !defined(LIGHTMAP_ON)
        // Only use vertex lights when NOT using lightmaps
        half3 diffuse = albedo * (1.0 - metallic);
        return i.ambientOrLightmapUV.rgb * diffuse;
    #else
        return half3(0, 0, 0);
    #endif
}

// ============================================
// Environment Luminance Calculation
// ============================================

// Calculate average environment luminance from blended reflection probes
half GzGetAverageEnvironmentLuminance()
{
    half mip = _FallbackMaxMipLevel;

    // Sample probe 0 at lowest mip in multiple directions
    half3 avg0 = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,1,0), mip), unity_SpecCube0_HDR);
    avg0 += DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,-1,0), mip), unity_SpecCube0_HDR);
    avg0 += DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,0,1), mip), unity_SpecCube0_HDR);
    half3 avgColor = avg0 / 3.0;

    // Blend with probe 1 using Unity's blend weight so fallback decisions
    // track the actual blended probe result during transitions
    #ifdef UNITY_SPECCUBE_BLENDING
        half blendWeight = unity_SpecCube0_BoxMin.w;
        UNITY_BRANCH
        if (blendWeight < 0.99999)
        {
            half3 avg1 = DecodeHDR(UNITY_SAMPLE_TEXCUBE_SAMPLER_LOD(unity_SpecCube1, unity_SpecCube0, half3(0,1,0), mip), unity_SpecCube1_HDR);
            avg1 += DecodeHDR(UNITY_SAMPLE_TEXCUBE_SAMPLER_LOD(unity_SpecCube1, unity_SpecCube0, half3(0,-1,0), mip), unity_SpecCube1_HDR);
            avg1 += DecodeHDR(UNITY_SAMPLE_TEXCUBE_SAMPLER_LOD(unity_SpecCube1, unity_SpecCube0, half3(0,0,1), mip), unity_SpecCube1_HDR);
            avgColor = lerp(avg1 / 3.0, avgColor, blendWeight);
        }
    #endif

    return dot(avgColor, half3(0.299, 0.587, 0.114));
}

// ============================================
// Lightmap Sampling
// ============================================

// Sample static lightmap and extract lighting data (following Unity's approach)
GzLightmapData GzSampleLightmapComplete(float2 lightmapUV)
{
    GzLightmapData lmData;
    lmData.diffuseColor = half3(0, 0, 0);
    lmData.dominantLightDir = half3(0, 1, 0);
    lmData.dominantLightColor = half3(0, 0, 0);
    lmData.lightAtten = 0;
    
    #ifdef LIGHTMAP_ON
        half4 bakedColorTex = UNITY_SAMPLE_TEX2D(unity_Lightmap, lightmapUV);
        half3 bakedColor = DecodeLightmap(bakedColorTex);
        lmData.diffuseColor = bakedColor;
        
        #ifdef DIRLIGHTMAP_COMBINED
            half4 bakedDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lightmapUV);
            
            // Unity's directional lightmap encoding: direction in RGB as [0,1] range
            // Remap to [-0.5, 0.5] (NOT normalized - length is directionality)
            half3 lightDir = bakedDirTex.xyz - 0.5;
            
            // The w component is the rebalancing coefficient
            // Length of lightDir is the "directionality" 
            lmData.dominantLightDir = normalize(lightDir);
            lmData.lightAtten = length(lightDir); // directionality factor
            
            // Apply rebalancing like Unity does
            lmData.dominantLightColor = bakedColor / max(1e-4h, bakedDirTex.w);
        #else
            // Non-directional lightmap - just diffuse
            lmData.diffuseColor = bakedColor;
        #endif
    #endif
    
    return lmData;
}


// Sample dynamic lightmap (realtime GI)
half3 GzSampleDynamicLightmap(float2 dynamicLightmapUV, half3 worldNormal)
{
    #ifdef DYNAMICLIGHTMAP_ON
        half4 realtimeColorTex = UNITY_SAMPLE_TEX2D(unity_DynamicLightmap, dynamicLightmapUV);
        half3 realtimeColor = DecodeRealtimeLightmap(realtimeColorTex);
        
        #ifdef DIRLIGHTMAP_COMBINED
            half4 realtimeDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_DynamicDirectionality, unity_DynamicLightmap, dynamicLightmapUV);
            realtimeColor = DecodeDirectionalLightmap(realtimeColor, realtimeDirTex, worldNormal);
        #endif
        
        return realtimeColor;
    #else
        return half3(0, 0, 0);
    #endif
}

// ============================================
// Ambient/Indirect Light Gathering
// ============================================

// Get ambient light from SH
half3 GzGetAmbientSH(half3 normal)
{
    return ShadeSH9(half4(normal, 1.0));
}

// Get ambient light with environment-aware fallback
half3 GzGetAmbientWithFallback(half3 normal, half avgEnvLuminance)
{
    half3 sh = GzGetAmbientSH(normal);
    
    #ifdef USE_ENVIRONMENT_REFLECTION
        // Use the cached average environment luminance for comparison
        if (avgEnvLuminance < _SHThreshold)
        {
            half3 fallbackDiffuse = texCUBElod(_FallbackCubemap, half4(normal, _FallbackDiffuseMipLevel)).rgb * _FallbackIntensity;
            half fallbackBlend = saturate((_SHThreshold - avgEnvLuminance) / _SHThreshold);
            sh = lerp(sh, fallbackDiffuse, fallbackBlend);
        }
    #endif
    
    return sh;
}


// Sample environment for any reflection vector (used by clearcoat, etc)
half3 GzSampleEnvironment(half3 reflectionDir, half roughness, float3 worldPos, half occlusion = 1.0)
{
    #ifdef USE_ENVIRONMENT_REFLECTION
        // Set up Unity's GI input data
        UnityGIInput d;
        d.worldPos = worldPos;
        d.probeHDR[0] = unity_SpecCube0_HDR;
        d.probeHDR[1] = unity_SpecCube1_HDR;
        #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
          d.boxMin[0] = unity_SpecCube0_BoxMin;
        #endif
        #ifdef UNITY_SPECCUBE_BOX_PROJECTION
          d.boxMax[0] = unity_SpecCube0_BoxMax;
          d.probePosition[0] = unity_SpecCube0_ProbePosition;
          d.boxMax[1] = unity_SpecCube1_BoxMax;
          d.boxMin[1] = unity_SpecCube1_BoxMin;
          d.probePosition[1] = unity_SpecCube1_ProbePosition;
        #endif
        
        Unity_GlossyEnvironmentData g;
        g.roughness = roughness;
        g.reflUVW = reflectionDir;
        
        // Use Unity's indirect specular function (we'll apply our own occlusion)
        half3 envColor = UnityGI_IndirectSpecular(d, 1.0, g);
        
        // Check if probe is below threshold
        half probeLuminance = dot(envColor, half3(0.299, 0.587, 0.114));
        if (probeLuminance < _ReflectionProbeThreshold)
        {
            // Use fallback cubemap
            half mipLevel = roughness * _FallbackMaxMipLevel;
            half3 fallbackColor = texCUBElod(_FallbackCubemap, half4(reflectionDir, mipLevel)).rgb * _FallbackIntensity;
            
            // Blend based on probe strength
            half fallbackBlend = saturate((_ReflectionProbeThreshold - probeLuminance) / _ReflectionProbeThreshold);
            envColor = lerp(envColor, fallbackColor, fallbackBlend);
        }
        
        return envColor;
    #else
        return half3(0, 0, 0);
    #endif
}

// Apply parallax correction to reflection direction using Unity's built-in function
half3 GzParallaxCorrectReflection(half3 reflectionDir, float3 worldPos)
{
    #if defined(UNITY_SPECCUBE_BOX_PROJECTION)
        // Use Unity's built-in BoxProjectedCubemapDirection function
        return BoxProjectedCubemapDirection(reflectionDir, worldPos, unity_SpecCube0_ProbePosition, unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
    #else
        return reflectionDir;
    #endif
}

// Get indirect specular with environment-aware fallback
// Uses per-pixel blended probe luminance for fallback decisions so transitions
// between probes and fallback are smooth (matches GzSampleEnvironment approach)
half3 GzGetIndirectSpecular(half3 reflectionDir, half roughness, float3 worldPos, half occlusion)
{
    #ifdef USE_ENVIRONMENT_REFLECTION
        // Set up Unity's GI input data (matches Standard's FragmentGI exactly)
        UnityGIInput d;
        d.worldPos = worldPos;
        d.probeHDR[0] = unity_SpecCube0_HDR;
        d.probeHDR[1] = unity_SpecCube1_HDR;
        #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
          d.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
        #endif
        #ifdef UNITY_SPECCUBE_BOX_PROJECTION
          d.boxMax[0] = unity_SpecCube0_BoxMax;
          d.probePosition[0] = unity_SpecCube0_ProbePosition;
          d.boxMax[1] = unity_SpecCube1_BoxMax;
          d.boxMin[1] = unity_SpecCube1_BoxMin;
          d.probePosition[1] = unity_SpecCube1_ProbePosition;
        #endif

        Unity_GlossyEnvironmentData g;
        g.roughness = roughness;
        g.reflUVW = reflectionDir;

        // UnityGI_IndirectSpecular handles probe blending internally via boxMin[0].w
        half3 envSample = UnityGI_IndirectSpecular(d, occlusion, g);

        // Per-pixel fallback: check the actual blended probe result this pixel sees
        half probeLuminance = dot(envSample, half3(0.299, 0.587, 0.114));
        if (probeLuminance < _ReflectionProbeThreshold)
        {
            // Fallback cubemap is a distant environment — use raw reflection direction
            // (no box projection; that's only for local probes)
            half mipLevel = roughness * _FallbackMaxMipLevel;
            half3 fallbackSpecular = texCUBElod(_FallbackCubemap, half4(reflectionDir, mipLevel)).rgb * _FallbackIntensity;
            half fallbackBlend = saturate((_ReflectionProbeThreshold - probeLuminance) / _ReflectionProbeThreshold);
            envSample = lerp(envSample, fallbackSpecular, fallbackBlend);
        }

        return envSample;
    #else
        return half3(0, 0, 0);
    #endif
}

// Calculate horizon occlusion for reflections
half GzHorizonOcclusion(half3 normal, half3 reflectionDir)
{
    half horizon = saturate(1.0 + dot(reflectionDir, normal));
    return horizon * horizon;
}

// Compute specular occlusion from AO
half GzComputeSpecularOcclusion(half NoV, half ao, half roughness)
{
    half horizonFade = saturate(1.0 + NoV);
    horizonFade *= horizonFade;
    
    return saturate(pow(NoV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao) * horizonFade;
}

// ============================================
// Complete Light Gathering
// ============================================

// Structure to hold all gathered indirect lighting
struct GzIndirectLight
{
    half3 diffuse;
    half3 specular;
};



// Gather all indirect lighting for ForwardBase (following Unity's approach)
GzIndirectLight GzGatherIndirectLight(float3 worldPos, half3 normal, half3 viewDir, 
                                      half roughness, half occlusion, float4 lightmapUV, half mainLightAtten)
{
    GzIndirectLight indirect;
    indirect.diffuse = half3(0, 0, 0);
    indirect.specular = half3(0, 0, 0);
    
    // Handle lightmaps like Unity does
    #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
        // Object has lightmaps - no SH
        
        #ifdef LIGHTMAP_ON
            half4 bakedColorTex = UNITY_SAMPLE_TEX2D(unity_Lightmap, lightmapUV.xy);
            half3 bakedColor = DecodeLightmap(bakedColorTex);
            
            #ifdef DIRLIGHTMAP_COMBINED
                half4 bakedDirTex = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, lightmapUV.xy);
                indirect.diffuse += DecodeDirectionalLightmap(bakedColor, bakedDirTex, normal);
                
                // Handle subtractive mixed lighting
                #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
                    // Subtract main light with realtime attenuation from lightmap
                    indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(indirect.diffuse, mainLightAtten, bakedColorTex, normal);
                #endif
            #else
                indirect.diffuse += bakedColor;
                
                // Handle subtractive mixed lighting  
                #if defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)
                    indirect.diffuse = SubtractMainLightWithRealtimeAttenuationFromLightmap(indirect.diffuse, mainLightAtten, bakedColorTex, normal);
                #endif
            #endif
        #endif
        
        #ifdef DYNAMICLIGHTMAP_ON
            indirect.diffuse += GzSampleDynamicLightmap(lightmapUV.zw, normal);
        #endif
    #elif UNITY_SHOULD_SAMPLE_SH
        // Dynamic object - compute SH per-pixel
        indirect.diffuse = ShadeSH9(half4(normal, 1.0));
        
        // Add vertex lights if present
        #ifdef VERTEXLIGHT_ON
            indirect.diffuse += lightmapUV.rgb; // Vertex lights were computed in vertex shader
        #endif
    #endif
    
    // Apply occlusion to indirect diffuse
    indirect.diffuse *= occlusion;
    
    // Get reflection with fallback
    half3 reflectionDir = reflect(-viewDir, normal);
    indirect.specular = GzGetIndirectSpecular(reflectionDir, roughness, worldPos, occlusion);
    
    // Apply horizon occlusion to specular
    half horizon = GzHorizonOcclusion(normal, reflectionDir);
    indirect.specular *= horizon;
    
    // Apply specular occlusion (glTF/Filament formula)
    half NoV = saturate(dot(normal, viewDir));
    half specOcc = GzComputeSpecularOcclusion(NoV, occlusion, roughness);
    indirect.specular *= specOcc;
    
    // Optionally blend indirect specular toward lightmap color on lightmapped surfaces
    // _LightmapReflectionBlend = 0 (default) preserves full reflections (spec-correct)
    // _LightmapReflectionBlend = 1 fully tints reflections by baked color (artistic)
    #ifdef LIGHTMAP_ON
        indirect.specular *= lerp(half3(1,1,1), bakedColor, _LightmapReflectionBlend);
    #endif
    
    return indirect;
}

#endif // GZ_LIGHT_GATHERING_INCLUDED