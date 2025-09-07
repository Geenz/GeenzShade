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

// Calculate complete base material BRDF
half3 GzCalculateBaseBRDF(GzMaterialData matData, GzLightingContext ctx, half3 F0, half3 F90)
{
    // Calculate Fresnel
    half3 F = GzFresnelSchlick(F0, F90, ctx.VoH);
    
    // Calculate specular
    half3 specular = GzCalculateSpecular(matData, ctx, F);
    
    // Calculate diffuse with energy conservation
    half3 kS = F;  // Specular contribution
    half3 kD = (1.0 - kS) * (1.0 - matData.metallic);  // Diffuse contribution
    
    // Calculate diffuse component (BRDF or BTDF based on hemisphere)
    // Per glTF spec: mix(diffuse_brdf, diffuse_btdf, diffuseTransmission)
    half3 diffuse = kD * GzCalculateDiffuseWithTransmission(matData, ctx);
    
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

// Apply sheen layer to base BRDF
half3 GzApplySheenLayer(half3 baseBRDF, GzMaterialData matData, GzLightingContext ctx)
{
    #ifdef USE_SHEEN
        half3 sheenBRDF = GzCalculateSheen(matData, ctx);
        half albedoScaling = GzCalculateSheenAlbedoScaling(matData, ctx);
        return baseBRDF * albedoScaling + sheenBRDF;
    #else
        return baseBRDF;
    #endif
}

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
            // Fresnel for clearcoat BRDF
            half3 F = GzFresnelSchlick(clearcoatF0, clearcoatF90, ctx.ccVoH);
            
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
            return GzFresnelSchlick(clearcoatF0, half3(1, 1, 1), ccNoV) * matData.clearcoatFactor;
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
// Per glTF spec: mix(diffuse_brdf, diffuse_btdf, diffuseTransmission)
// Returns the diffuse BSDF without NoL multiplication (that's applied in the caller)
half3 GzCalculateDiffuseWithTransmission(GzMaterialData matData, GzLightingContext ctx)
{
    half3 diffuseAlbedo = GzGetAlbedo(matData.baseColor, matData.metallic);
    
    // Per glTF spec:
    // diffuse_brdf returns (1/π) * baseColor when view and light on same hemisphere, 0 otherwise
    // diffuse_btdf returns (1/π) * diffuseTransmissionColor when on opposite hemispheres, 0 otherwise
    // Final result: mix(diffuse_brdf, diffuse_btdf, diffuseTransmission)
    
    half3 diffuseBRDF = half3(0, 0, 0);
    half3 diffuseBTDF = half3(0, 0, 0);
    
    // Calculate BRDF contribution (same hemisphere)
    if (ctx.NoL > 0)
    {
        diffuseBRDF = diffuseAlbedo * UNITY_INV_PI;
    }
    
    // Calculate BTDF contribution (opposite hemisphere)
    #ifdef USE_DIFFUSE_TRANSMISSION
    if (ctx.NoL < 0)
    {
        // Per spec: diffuseTransmissionColor = baseColor * diffuseTransmissionColorFactor
        half3 diffuseTransmissionColor = diffuseAlbedo * matData.diffuseTransmissionColorFactor;
        diffuseBTDF = diffuseTransmissionColor * UNITY_INV_PI;
    }
    
    // Mix BRDF and BTDF according to transmission factor
    // glTF spec: mix(diffuse_brdf, diffuse_btdf, diffuseTransmission)
    // This is: (1 - diffuseTransmission) * diffuse_brdf + diffuseTransmission * diffuse_btdf
    return (1.0 - matData.diffuseTransmissionFactor) * diffuseBRDF + matData.diffuseTransmissionFactor * diffuseBTDF;
    #else
    // No transmission - just return BRDF
    return diffuseBRDF;
    #endif
}

// ============================================
// Complete Layer Stack Evaluation
// ============================================

// Evaluate all layers in correct order for direct lighting
half3 GzEvaluateLayerStack(GzMaterialData matData, GzLightingContext ctx)
{
    // Populate clearcoat vectors if clearcoat normal differs from base
    #ifdef USE_CLEARCOAT
    if (matData.clearcoatFactor > 0 && any(matData.clearcoatNormal != matData.normal))
    {
        GzPopulateClearcoatVectors(ctx, matData.clearcoatNormal);
    }
    #endif
    
    // Use pre-calculated F0 from material data (already includes iridescence)
    // This is calculated once in GzSampleMaterialComplete
    half3 F0 = matData.f0;
    half3 F90 = matData.f90;
    
    // Calculate base BRDF with pre-calculated F0 (WITHOUT NoL multiplication)
    // This already includes diffuse transmission per glTF spec
    half3 baseBRDF = GzCalculateBaseBRDF(matData, ctx, F0, F90);
    
    // Step 3: Apply sheen layer (if present) and NoL
    #ifdef USE_SHEEN
    if (GzMax3(matData.sheenColor) > 0 && ctx.NoL > 0)
    {
        // Apply sheen layer using Gz implementation
        half3 sheenBRDF = GzCalculateSheen(matData, ctx);
        half albedoScaling = GzCalculateSheenAlbedoScaling(matData, ctx);
        baseBRDF = (baseBRDF * albedoScaling + sheenBRDF) * ctx.NoL;
    }
    else 
    {
        // No sheen or backface - apply NoL with transmission consideration
        #ifdef USE_DIFFUSE_TRANSMISSION
        // Blend between NoL and abs(NoL) based on transmission factor
        // When factor = 0: pure BRDF (use NoL)
        // When factor = 1: pure BTDF on backside (use abs(NoL))
        half absNoL = abs(ctx.NoL);
        half lightDot = lerp(ctx.NoL, absNoL, matData.diffuseTransmissionFactor);
        baseBRDF = baseBRDF * lightDot;
        #else
        baseBRDF = baseBRDF * ctx.NoL;
        #endif
    }
    #else
    // No sheen feature, apply NoL to base
    #ifdef USE_DIFFUSE_TRANSMISSION
    // Blend between NoL and abs(NoL) based on transmission factor
    half absNoL = abs(ctx.NoL);
    half lightDot = lerp(ctx.NoL, absNoL, matData.diffuseTransmissionFactor);
    baseBRDF = baseBRDF * lightDot;
    #else
    baseBRDF = baseBRDF * ctx.NoL;
    #endif
    #endif
    
    // Step 4: Apply clearcoat layer on top (if present)
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
    
    // Environment Fresnel using Schlick approximation
    half3 F = GzFresnelSchlick(F0, F90, ctx.NoV);
    
    // Energy conservation
    half3 kS = F;
    half3 kD = (1.0 - kS) * (1.0 - matData.metallic);
    
    // Combine diffuse and specular indirect
    half3 diffuse = indirectDiffuse * GzGetAlbedo(matData.baseColor, matData.metallic) * kD;
    
    // For specular: the environment map is already pre-convolved for roughness
    // We just need to apply the Fresnel term which includes F0
    half3 specular = indirectSpecular * F;
    
    return diffuse + specular;
}

// Calculate sheen indirect contribution
half3 GzCalculateSheenIndirect(GzMaterialData matData, GzLightingContext ctx, half3 envDiffuse)
{
    #ifdef USE_SHEEN
        if (GzMax3(matData.sheenColor) > 0)
        {
            // Simplified environment sheen - use diffuse approximation
            return matData.sheenColor * envDiffuse * (1.0 - matData.sheenRoughness);
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
            // Calculate fresnel with clearcoat normal
            half ccNoV = saturate(dot(matData.clearcoatNormal, viewDir));
            half3 clearcoatF0 = half3(0.04, 0.04, 0.04);
            half3 F = GzFresnelSchlick(clearcoatF0, half3(1, 1, 1), ccNoV);
            
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
    // Base indirect
    half3 result = GzCalculateBaseIndirect(matData, ctx, indirectDiffuse, indirectSpecular);
    
    // Apply occlusion
    result *= matData.occlusion;
    
    // Sheen indirect
    #ifdef USE_SHEEN
        half3 sheenIndirect = GzCalculateSheenIndirect(matData, ctx, indirectDiffuse);
        half albedoScaling = GzCalculateSheenAlbedoScaling(matData, ctx);
        result = result * albedoScaling + sheenIndirect * matData.occlusion;
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