/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_LAYER_CALCULATIONS_INCLUDED
#define GZ_LAYER_CALCULATIONS_INCLUDED

#include "GzMath.cginc"
#include "GzMaterial.cginc"
#include "GzPBRHelpers.cginc"
#include "GzSheen.cginc"
#include "GzLighting.cginc"  // For GzLightingContext (includes this file back with guards)
// Forward declarations
half3 GzCalculateDiffuseWithTransmission(GzMaterialData matData, GzLightingContext ctx);

// ============================================
// Base Material Layer Calculations
// ============================================

// Calculate diffuse component (Lambertian)
half3 GzCalculateDiffuse(GzMaterialData matData, GzLightingContext ctx)
{
    // Get diffuse albedo (non-metals only)
    half3 diffuseAlbedo = GzGetAlbedo(matData.baseColor, matData.metallic);
    
    // Simple Lambertian diffuse
    return diffuseAlbedo * UNITY_INV_PI;
}

// Calculate specular component (GGX)
half3 GzCalculateSpecular(GzMaterialData matData, GzLightingContext ctx, half3 F)
{
    // Clamp roughness for direct lighting
    half roughness = max(matData.roughness, 0.001);
    
    // GGX Distribution
    half D = GzDistributionGGX(ctx.NoH, roughness);
    
    // Smith Visibility
    half V = GzVisibilitySmithGGX(ctx.NoL, ctx.NoV, roughness);
    
    // Specular BRDF = F * D * V
    return F * D * V;
}

// Calculate base material components separately for proper sheen layering
void GzCalculateBaseBRDFComponents(GzMaterialData matData, GzLightingContext ctx, half3 F0, half3 F90,
                                   out half3 diffuse, out half3 specular)
{
    // Calculate Fresnel using roughness-dependent version
    half3 F = GzFresnelSchlick(F0, F90, ctx.VoH, matData.roughness);
    
    // Calculate specular
    specular = GzCalculateSpecular(matData, ctx, F);
    
    // Calculate diffuse with energy conservation
    half3 kS = F;  // Specular contribution
    half3 kD = (1.0 - kS) * (1.0 - matData.metallic);  // Diffuse contribution
    
    // Calculate diffuse component (BRDF or BTDF based on hemisphere)
    // Per glTF spec: mix(diffuse_brdf, diffuse_btdf, diffuseTransmission)
    diffuse = kD * GzCalculateDiffuseWithTransmission(matData, ctx);
}

// Calculate complete base material BRDF
half3 GzCalculateBaseBRDF(GzMaterialData matData, GzLightingContext ctx, half3 F0, half3 F90)
{
    half3 diffuse, specular;
    GzCalculateBaseBRDFComponents(matData, ctx, F0, F90, diffuse, specular);
    return diffuse + specular;
}

// ============================================
// Iridescence Layer Calculations
// ============================================

// DEPRECATED: Iridescence is now applied in GzMaterialSampling
// This function is kept for backward compatibility only
half3 GzCalculateIridescence(GzMaterialData matData, GzLightingContext ctx, half3 baseF0)
{
    // Iridescence is now pre-calculated in material sampling
    // Return the material's F0 which already includes iridescence
    return matData.f0;
}

// ============================================
// Sheen Layer Calculations (functions moved to GzSheen.cginc)
// ============================================
// Note: Sheen is now properly applied by scaling base color before diffuse calculation,
// not by scaling the final BRDF. See GzEvaluateLayerStack for the correct implementation.

// ============================================
// Clearcoat Layer Calculations
// ============================================

// Calculate clearcoat BRDF with proper clearcoat normal
// Per glTF spec: clearcoat_brdf = D(α) * G(α) / (4 * abs(VdotNc) * abs(LdotNc))
// Note: NoL is NOT included in the BRDF, it's applied during layering
half3 GzCalculateClearcoat(GzMaterialData matData, GzLightingContext ctx)
{
#ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0)
    {
        // Fixed F0 for clearcoat (IOR = 1.5, F0 = ((1-1.5)/(1+1.5))^2 = 0.04)
        half3 clearcoatF0 = half3(0.04, 0.04, 0.04);
        half3 clearcoatF90 = half3(1, 1, 1);
        
        // Use pre-computed clearcoat dot products from context
        // Fresnel for clearcoat BRDF using roughness-dependent version
        half3 F = GzFresnelSchlick(clearcoatF0, clearcoatF90, ctx.ccVoH, matData.clearcoatRoughness);
        
        // Clamp clearcoat roughness and square it for alpha
        // Per spec: α = clearcoatRoughness^2
        half roughness = max(matData.clearcoatRoughness, 0.01);
        
        // GGX Distribution with clearcoat normal
        half D = GzDistributionGGX(ctx.ccNoH, roughness);
        
        // Smith Visibility with clearcoat normal
        half V = GzVisibilitySmithGGX(ctx.ccNoL, ctx.ccNoV, roughness);
        
        // Return BRDF without NoL (NoL is applied during layering)
        return F * D * V;
    }
#endif
    
    return half3(0, 0, 0);
}

// Calculate clearcoat Fresnel for layer mixing (uses clearcoat normal)
half3 GzCalculateClearcoatFresnel(GzMaterialData matData, half3 viewDir)
{
#ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0)
    {
        // Use clearcoat normal for fresnel calculation
        half ccNoV = saturate(dot(matData.clearcoatNormal, viewDir));
        half3 clearcoatF0 = half3(0.04, 0.04, 0.04);
        half3 clearcoatF90 = half3(1, 1, 1);
        return GzFresnelSchlick(clearcoatF0, clearcoatF90, ccNoV, matData.clearcoatRoughness) * matData.clearcoatFactor;
    }
#endif
    
    return half3(0, 0, 0);
}

// Calculate clearcoat attenuation for emission
// Per spec: coated_emission = emission * (1 - clearcoat * clearcoat_fresnel)
half3 GzAttenuateEmissionByClearcoat(half3 emission, GzMaterialData matData, half3 viewDir)
{
#ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0)
    {
        // Calculate clearcoat Fresnel
        half ccNoV = saturate(dot(matData.clearcoatNormal, viewDir));
        half clearcoatFresnel = 0.04 + (1.0 - 0.04) * GzPow5(1.0 - ccNoV);
        
        // Apply attenuation: emission * (1 - clearcoat * clearcoat_fresnel)
        half attenuation = 1.0 - (matData.clearcoatFactor * clearcoatFresnel);
        return emission * attenuation;
    }
#endif
    
    return emission;
}

// Apply clearcoat layer on top of base
half3 GzApplyClearcoatLayer(half3 baseBRDF, GzMaterialData matData, GzLightingContext ctx)
{
#ifdef USE_CLEARCOAT
    half3 clearcoatBRDF = GzCalculateClearcoat(matData, ctx);
    half3 clearcoatFresnel = GzCalculateClearcoatFresnel(matData, ctx.NoV);
    
    // Layer mixing: base * (1 - clearcoatFresnel) + clearcoat
    return baseBRDF * (1.0 - clearcoatFresnel) + clearcoatBRDF;
#else
    return baseBRDF;
#endif
}

// ============================================
// Diffuse Transmission Calculations (KHR_materials_diffuse_transmission)
// ============================================

// Calculate diffuse BSDF with transmission
// Following Khronos reference implementation - returns diffuse without NoL
half3 GzCalculateDiffuseWithTransmission(GzMaterialData matData, GzLightingContext ctx)
{
    half3 diffuseAlbedo = GzGetAlbedo(matData.baseColor, matData.metallic);
    
    // Regular diffuse BRDF (uses regular NoL, applied later)
    half3 f_diffuse = diffuseAlbedo * UNITY_INV_PI;
    
#ifdef USE_DIFFUSE_TRANSMISSION
    // Transmission diffuse BTDF (uses abs(NoL), but NoL applied later)
    // Per Khronos: diffuseTransmissionColorFactor modulates the diffuse
    half3 f_diffuse_transmission = matData.diffuseTransmissionColorFactor * diffuseAlbedo * UNITY_INV_PI;
    
    // Mix regular diffuse with transmission diffuse
    f_diffuse = lerp(f_diffuse, f_diffuse_transmission, matData.diffuseTransmissionFactor);
#endif
    
    return f_diffuse;
}

// ============================================
// Complete Layer Stack Evaluation
// ============================================

// Evaluate all layers in correct order for direct lighting
half3 GzEvaluateLayerStack(GzMaterialData matData, GzLightingContext ctx)
{
    // Populate clearcoat vectors (always needed for clearcoat layer)
#ifdef USE_CLEARCOAT
    GzPopulateClearcoatVectors(ctx, matData.clearcoatNormal);
#endif
    
    // Use pre-calculated F0 from material data (already includes iridescence)
    // This is calculated once in GzSampleMaterialComplete
    half3 F0 = matData.f0;
    half3 F90 = matData.f90;
    
    // Step 3: Apply sheen albedo scaling to base color if needed
    half3 scaledBaseColor = matData.baseColor;
#ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0)
    {
        half albedoScaling = GzCalculateSheenAlbedoScaling(matData, ctx);
        // Scale the base color for diffuse calculation
        scaledBaseColor = matData.baseColor * albedoScaling;
    }
#endif
    
    // Calculate base BRDF with potentially scaled base color
    // We need to temporarily modify the material data
    half3 originalBaseColor = matData.baseColor;
    matData.baseColor = scaledBaseColor;
    half3 baseBRDF = GzCalculateBaseBRDF(matData, ctx, F0, F90);
    matData.baseColor = originalBaseColor; // Restore original
    
    // Step 4: Add sheen layer on top
#ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0 && ctx.NoL > 0)
    {
        half3 sheenBRDF = GzCalculateSheen(matData, ctx);
        baseBRDF = baseBRDF + sheenBRDF;
    }
#endif
    
    // Step 4: Apply NoL
#ifdef USE_DIFFUSE_TRANSMISSION
    // Following Khronos: mix between NoL and abs(NoL) based on transmission
    // Regular BRDF uses NoL, transmission BTDF uses abs(NoL)
    half lightDot = lerp(ctx.NoL, abs(ctx.NoL), matData.diffuseTransmissionFactor);
    baseBRDF = baseBRDF * lightDot;
#else
    baseBRDF = baseBRDF * ctx.NoL;
#endif
    
    // Step 5: Apply clearcoat layer on top (if present)
#ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0)
    {
        // Calculate clearcoat BRDF (without NoL)
        half3 clearcoatBRDF = GzCalculateClearcoat(matData, ctx);
        
        // Calculate clearcoat Fresnel for layering using pre-computed ccNoV
        // Per spec: clearcoat_fresnel = 0.04 + (1 - 0.04) * (1 - abs(VdotNc))^5
        half clearcoatFresnel = 0.04 + (1.0 - 0.04) * GzPow5(1.0 - ctx.ccNoV);
        
        // Apply clearcoat layering per glTF spec:
        // coated_material = mix(material, clearcoat_brdf, clearcoat * clearcoat_fresnel)
        // Since baseBRDF already has NoL applied and clearcoatBRDF needs ccNoL:
        // baseBRDF = baseBRDF * (1 - clearcoat * fresnel) + clearcoatBRDF * ccNoL * clearcoat * fresnel
        half clearcoatWeight = matData.clearcoatFactor * clearcoatFresnel;
        
        // Attenuate base material and add clearcoat contribution
        baseBRDF = baseBRDF * (1.0 - clearcoatWeight) + clearcoatBRDF * ctx.ccNoL * clearcoatWeight;
    }
#endif
    
    return baseBRDF;
}

// ============================================
// Indirect/Environment Layer Calculations
// ============================================

// Calculate base indirect response
half3 GzCalculateBaseIndirect(GzMaterialData matData, GzLightingContext ctx, 
                              half3 indirectDiffuse, half3 indirectSpecular)
{
    // Use pre-calculated F0 from material data (already includes iridescence)
    half3 F0 = matData.f0;
    half3 F90 = matData.f90;
    
    // Environment Fresnel using roughness-dependent Fresnel (Fdez-Agüera)
    half3 F = GzFresnelSchlick(F0, F90, ctx.NoV, matData.roughness);
    
    // Energy conservation
    half3 kS = F;
    half3 kD = (1.0 - kS) * (1.0 - matData.metallic);
    
    // Get base color (will be scaled by sheen in the main evaluation)
    half3 albedo = GzGetAlbedo(matData.baseColor, matData.metallic);
    
    // Combine diffuse and specular indirect
    half3 diffuse = indirectDiffuse * albedo * kD;
    half3 specular = indirectSpecular * F;
    
    return diffuse + specular;
}

// Calculate sheen indirect contribution
half3 GzCalculateSheenIndirect(GzMaterialData matData, GzLightingContext ctx, half3 envDiffuse, half3 envSpecular)
{
#ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0)
    {
        // For sheen IBL, we need to approximate the Charlie BRDF response
        // Without a Charlie-filtered environment map, we blend between diffuse and specular
        // based on sheen roughness
        
        // Calculate sheen BRDF response for environment lighting
        // Charlie distribution has a different energy profile than GGX
        // It's broader and more diffuse-like, especially at high roughness
        
        // Use sheen roughness to interpolate between specular and diffuse environment
        // This approximates the Charlie-filtered environment response
        half sheenMix = matData.sheenRoughness;
        half3 sheenEnv = lerp(envSpecular, envDiffuse, sheenMix);
        
        // The Charlie BRDF LUT would normally be applied here
        // For now, use a simple approximation
        half brdf = 1.0 - sheenMix * 0.5; // Rough approximation of Charlie BRDF integral
        
        // Apply sheen factor and rim boost (artistic control)
        return matData.sheenColor * sheenEnv * brdf * matData.sheenFactor * matData.sheenRimBoost;
    }
#endif
    
    return half3(0, 0, 0);
}

// Calculate clearcoat indirect contribution
// clearcoatEnvSpecular should be pre-sampled using clearcoat reflection vector
half3 GzCalculateClearcoatIndirect(GzMaterialData matData, half3 viewDir, half3 clearcoatEnvSpecular)
{
#ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0)
    {
        // Calculate fresnel with clearcoat normal using roughness-dependent Fresnel
        half ccNoV = saturate(dot(matData.clearcoatNormal, viewDir));
        half3 clearcoatF0 = half3(0.04, 0.04, 0.04);
        half3 clearcoatF90 = half3(1, 1, 1);
        half3 F = GzFresnelSchlick(clearcoatF0, clearcoatF90, ccNoV, matData.clearcoatRoughness);
        
        return clearcoatEnvSpecular * F * matData.clearcoatFactor;
    }
#endif
    
    return half3(0, 0, 0);
}

// Evaluate all layers for indirect lighting
// Now takes clearcoatEnvSpecular as a parameter instead of worldPos
half3 GzEvaluateLayerStackIndirect(GzMaterialData matData, GzLightingContext ctx,
                                   half3 indirectDiffuse, half3 indirectSpecular, half3 clearcoatEnvSpecular)
{
    // Apply sheen albedo scaling to base color if needed
    half3 originalBaseColor = matData.baseColor;
#ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0)
    {
        half albedoScaling = GzCalculateSheenAlbedoScalingIndirect(matData, ctx.NoV);
        matData.baseColor = matData.baseColor * albedoScaling;
    }
#endif
    
    // Base indirect with potentially scaled base color
    half3 result = GzCalculateBaseIndirect(matData, ctx, indirectDiffuse, indirectSpecular);
    matData.baseColor = originalBaseColor; // Restore
    
    // Apply occlusion
    result *= matData.occlusion;
    
// Add sheen indirect on top
#ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0)
    {
        half3 sheenIndirect = GzCalculateSheenIndirect(matData, ctx, indirectDiffuse, indirectSpecular);
        result = result + sheenIndirect * matData.occlusion;
    }
#endif

// Clearcoat indirect with pre-sampled environment
#ifdef USE_CLEARCOAT
    half3 clearcoatIndirect = GzCalculateClearcoatIndirect(matData, ctx.viewDir, clearcoatEnvSpecular);
    half3 clearcoatFresnel = GzCalculateClearcoatFresnel(matData, ctx.viewDir);
    result = result * (1.0 - clearcoatFresnel) + clearcoatIndirect;
#else
    // When clearcoat is not enabled, we still need to accept the parameter
    // but we don't use it - this prevents shader compilation errors
    // The compiler should optimize this out
#endif
    
    return result;
}

// Overload without clearcoat parameter for compatibility
half3 GzEvaluateLayerStackIndirect(GzMaterialData matData, GzLightingContext ctx,
                                   half3 indirectDiffuse, half3 indirectSpecular)
{
    return GzEvaluateLayerStackIndirect(matData, ctx, indirectDiffuse, indirectSpecular, half3(0,0,0));
}

#endif // GZ_LAYER_CALCULATIONS_INCLUDED