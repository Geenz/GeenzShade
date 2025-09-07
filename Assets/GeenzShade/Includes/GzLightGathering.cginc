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
// Shadow and Attenuation Helpers
// ============================================

// Get shadow attenuation for ForwardBase
half GzGetShadowAttenuationBase(GzVertexOutput i)
{
    return SHADOW_ATTENUATION(i);
}

// Get light attenuation for ForwardAdd
half GzGetLightAttenuationAdd(GzVertexOutputAdd i, float3 worldPos)
{
    UNITY_LIGHT_ATTENUATION(atten, i, worldPos);
    return atten;
}

// ============================================
// Light Context Creation
// ============================================

// Create lighting context for main light in ForwardBase (can be directional, point, or spot)
GzLightingContext GzCreateDirectionalLightContext(GzVertexOutput i, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    // Check if light exists
    if (any(_LightColor0.rgb))
    {
        // Determine light type from w component:
        // w = 0: directional light (xyz is direction)
        // w = 1: point/spot light (xyz is position)
        if (_WorldSpaceLightPos0.w == 0)
        {
            // Directional light - xyz is the light direction
            ctx.lightDir = normalize(_WorldSpaceLightPos0.xyz);
        }
        else
        {
            // Point or spot light - calculate direction from position
            ctx.lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
        }
        
        ctx.viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
        ctx.lightColor = _LightColor0.rgb;
        ctx.lightAtten = GzGetShadowAttenuationBase(i) * _LightIntensityMultiplier;
        
        // Populate vectors
        GzPopulateLightingVectors(ctx, normal);
    }
    
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

// Create lighting context for VRC Light Volumes dominant light
GzLightingContext GzCreateLightVolumeDominantLightContext(float3 worldPos, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    #ifdef USE_VRC_LIGHT_VOLUMES
        if (LightVolumesEnabled())
        {
            // Get the SH coefficients from Light Volumes
            float3 lvL0, lvL1r, lvL1g, lvL1b;
            LightVolumeSH(worldPos, lvL0, lvL1r, lvL1g, lvL1b);
            
            // Extract dominant direction from L1 coefficients
            // Light Volumes adds all L1 components together to get the dominant direction
            // (see LightVolumeSpecularDominant in LightVolumes.cginc)
            half3 lightDir = normalize(lvL1r + lvL1g + lvL1b);
            
            if (length(lightDir) > 0.001)
            {
                ctx.lightDir = lightDir;
                ctx.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                ctx.lightColor = lvL0;
                ctx.lightAtten = 1.0; // Light volumes have no distance attenuation
                
                // Populate vectors
                GzPopulateLightingVectors(ctx, normal);
            }
        }
    #endif
    
    return ctx;
}

// Create lighting context for point/spot lights (ForwardAdd)
GzLightingContext GzCreateAdditiveLightContext(GzVertexOutputAdd i, half3 normal)
{
    GzLightingContext ctx = GzCreateLightingContext();
    
    // Determine light type and direction
    #ifndef USING_DIRECTIONAL_LIGHT
        ctx.lightDir = normalize(_WorldSpaceLightPos0.xyz - i.worldPos);
    #else
        ctx.lightDir = _WorldSpaceLightPos0.xyz;
    #endif
    
    ctx.viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
    ctx.lightColor = _LightColor0.rgb;
    ctx.lightAtten = GzGetLightAttenuationAdd(i, i.worldPos) * _LightIntensityMultiplier;
    
    // Populate vectors
    GzPopulateLightingVectors(ctx, normal);
    
    return ctx;
}

// ============================================
// Vertex Light Gathering (ForwardBase only)
// ============================================

// Get vertex light contribution
half3 GzGetVertexLights(GzVertexOutput i, half3 albedo, half metallic)
{
    #if defined(VERTEXLIGHT_ON) && defined(_VERTEXLIGHTS_ON)
        half3 diffuse = albedo * (1.0 - metallic);
        return i.vertexLightColor * diffuse;
    #else
        return half3(0, 0, 0);
    #endif
}

// ============================================
// Environment Luminance Calculation
// ============================================

// Calculate average environment luminance from reflection probe
half GzGetAverageEnvironmentLuminance()
{
    // Sample lowest mip level at multiple directions for average - use _FallbackMaxMipLevel like original
    half3 up = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,1,0), _FallbackMaxMipLevel), unity_SpecCube0_HDR);
    half3 down = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,-1,0), _FallbackMaxMipLevel), unity_SpecCube0_HDR);
    half3 center = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, half3(0,0,1), _FallbackMaxMipLevel), unity_SpecCube0_HDR);
    
    // Calculate average and convert to luminance
    half3 avgColor = (up + down + center) / 3.0;
    return dot(avgColor, half3(0.299, 0.587, 0.114));
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

// Get reflection probe sample using Unity Standard shader method with box projection
half3 GzSampleReflectionProbe(half3 reflectionDir, half roughness, float3 worldPos)
{
    // Create glossy environment data like Unity Standard shader
    Unity_GlossyEnvironmentData glossIn;
    glossIn.roughness = roughness; // This is perceptualRoughness
    
    #ifdef UNITY_SPECCUBE_BOX_PROJECTION
        // Store original for second probe
        half3 originalReflUVW = reflectionDir;
        // Apply box projection for first probe
        glossIn.reflUVW = BoxProjectedCubemapDirection(reflectionDir, worldPos, unity_SpecCube0_ProbePosition, unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
    #else
        glossIn.reflUVW = reflectionDir;
    #endif
    
    // Use Unity's built-in function which handles roughness remapping internally
    half3 env0 = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, glossIn);
    
    #if UNITY_SPECCUBE_BLENDING
        const float kBlendFactor = 0.99999;
        float blendLerp = unity_SpecCube0_BoxMin.w;
        if (blendLerp < kBlendFactor)
        {
            #ifdef UNITY_SPECCUBE_BOX_PROJECTION
                // Apply box projection for second probe
                glossIn.reflUVW = BoxProjectedCubemapDirection(originalReflUVW, worldPos, unity_SpecCube1_ProbePosition, unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax);
            #endif
            
            half3 env1 = Unity_GlossyEnvironment(UNITY_PASS_TEXCUBE_SAMPLER(unity_SpecCube1, unity_SpecCube0), unity_SpecCube1_HDR, glossIn);
            env0 = lerp(env1, env0, blendLerp);
        }
    #endif
    
    return env0;
}

// Sample environment for any reflection vector (used by clearcoat, etc)
half3 GzSampleEnvironment(half3 reflectionDir, half roughness, float3 worldPos)
{
    #ifdef USE_ENVIRONMENT_REFLECTION
        // Try reflection probes first
        half3 envColor = GzSampleReflectionProbe(reflectionDir, roughness, worldPos);
        
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
half3 GzGetIndirectSpecular(half3 reflectionDir, half roughness, float3 worldPos,
                            half avgEnvLuminance)
{
    #ifdef USE_ENVIRONMENT_REFLECTION
        // Sample reflection probe with box projection handled internally
        half3 envSample = GzSampleReflectionProbe(reflectionDir, roughness, worldPos);
        
        // Use the cached average environment luminance for consistent fallback decisions
        if (avgEnvLuminance < _ReflectionProbeThreshold)
        {
            // Apply box projection to fallback cubemap as well
            half3 fallbackDir = GzParallaxCorrectReflection(reflectionDir, worldPos);
            half mipLevel = roughness * _FallbackMaxMipLevel;
            half3 fallbackSpecular = texCUBElod(_FallbackCubemap, half4(fallbackDir, mipLevel)).rgb * _FallbackIntensity;
            half fallbackBlend = saturate((_ReflectionProbeThreshold - avgEnvLuminance) / _ReflectionProbeThreshold);
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


// Get Light Volumes ambient diffuse contribution
#ifdef USE_VRC_LIGHT_VOLUMES
half3 GzGetLightVolumeAmbient(float3 worldPos, half3 normal)
{
    if (LightVolumesEnabled())
    {
        // Get the SH coefficients from Light Volumes
        float3 lvL0, lvL1r, lvL1g, lvL1b;
        LightVolumeSH(worldPos, lvL0, lvL1r, lvL1g, lvL1b);
        
        // Evaluate SH manually like we do elsewhere
        // L0 is the ambient term, L1 provides directional variation
        half3 result;
        result.r = lvL0.r + dot(lvL1r, normal);
        result.g = lvL0.g + dot(lvL1g, normal);
        result.b = lvL0.b + dot(lvL1b, normal);
        
        return max(0, result);
    }
    
    return half3(0, 0, 0);
}
#endif

// Gather all indirect lighting for ForwardBase
GzIndirectLight GzGatherIndirectLight(float3 worldPos, half3 normal, half3 viewDir, 
                                      half roughness, half occlusion)
{
    GzIndirectLight indirect;
    
    // Calculate average environment luminance once for all fallback decisions
    half avgEnvLuminance = GzGetAverageEnvironmentLuminance();
    
    // Get ambient diffuse with fallback (SH or fallback cubemap)
    indirect.diffuse = GzGetAmbientWithFallback(normal, avgEnvLuminance);
    
    // Get reflection with fallback
    half3 reflectionDir = reflect(-viewDir, normal);
    indirect.specular = GzGetIndirectSpecular(reflectionDir, roughness, worldPos, avgEnvLuminance);
    
    // Apply horizon occlusion to specular
    half horizon = GzHorizonOcclusion(normal, reflectionDir);
    indirect.specular *= horizon;
    
    // Apply specular occlusion
    half NoV = saturate(dot(normal, viewDir));
    half specOcc = GzComputeSpecularOcclusion(NoV, occlusion, roughness);
    indirect.specular *= specOcc;
    
    return indirect;
}

#endif // GZ_LIGHT_GATHERING_INCLUDED