/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_PBR_HELPERS_INCLUDED
#define GZ_PBR_HELPERS_INCLUDED

#include "GzMath.cginc"
#include "GzMaterial.cginc"
#include "GzIridescence.cginc"

// ============================================
// F0 Calculation Helpers
// ============================================

// Calculate F0 from IOR
half3 GzGetF0FromIOR(half ior)
{
    half f0 = (ior - 1.0) / (ior + 1.0);
    f0 *= f0;
    return half3(f0, f0, f0);
}

// Standard metallic workflow F0 (no extensions)
half3 GzGetF0Metallic(half3 baseColor, half metallic, half ior)
{
    half3 dielectricF0 = GzGetF0FromIOR(ior);
    return lerp(dielectricF0, baseColor, metallic);
}

// KHR_materials_specular workflow F0 (with optional IOR)
half3 GzGetF0Specular(half3 baseColor, half metallic, half3 specularColor, half specularFactor, half ior)
{
    // Start with IOR-based F0 or default 0.04
    half3 baseF0 = GzGetF0FromIOR(ior);
    
    // Apply specular extension: F0 is scaled by specularColor and specularFactor
    // Per spec: dielectric_f0 = min(baseF0 * specularColor, 1.0) * specularFactor
    half3 dielectricF0 = min(baseF0 * specularColor, half3(1,1,1)) * specularFactor;
    
    // For metals, F0 is the base color
    return lerp(dielectricF0, baseColor, metallic);
}

// Get F90 for specular extension
half3 GzGetF90Specular(half metallic, half specularFactor)
{
    // Per spec: dielectric_f90 = specularFactor
    // Metals always have F90 = 1
    return lerp(half3(specularFactor, specularFactor, specularFactor), half3(1, 1, 1), metallic);
}

// ============================================
// Albedo Calculations
// ============================================

// Get diffuse albedo (non-metals only)
half3 GzGetAlbedo(half3 baseColor, half metallic)
{
    return baseColor * (1.0 - metallic);
}

// Get specular color for the specular BRDF term
half3 GzGetSpecularColor(half3 baseColor, half metallic, half3 dielectricSpecColor, half specFactor)
{
    // For dielectrics: use specular color * factor
    // For metals: use base color (specular extension doesn't affect metals)
    return lerp(dielectricSpecColor * specFactor, baseColor, metallic);
}

// ============================================
// Roughness Processing
// ============================================

// Clamp roughness for punctual lights to avoid singularities
half GzClampRoughness(half roughness, bool isDirectLighting)
{
    return isDirectLighting ? max(roughness, 0.01) : roughness;
}

// Remap roughness for perceptual linearity (optional)
half GzRemapRoughness(half roughness)
{
    return roughness * roughness;
}

// ============================================
// Iridescence Helpers
// ============================================

// Apply iridescence modification to F0
// Note: This requires the GzEvalIridescence function from GzIridescence.cginc
half3 GzApplyIridescence(half3 baseF0, half iridescenceFactor, half iridescenceIOR, 
                         half iridescenceThickness, half NoV)
{
    #ifdef USE_IRIDESCENCE
        // GzEvalIridescence should be defined in GzIridescence.cginc
        half3 iridF0 = GzEvalIridescence(1.0, iridescenceIOR, NoV, iridescenceThickness, baseF0);
        return lerp(baseF0, iridF0, iridescenceFactor);
    #else
        return baseF0;
    #endif
}

// ============================================
// Material Property Derivation
// ============================================

// Populate F0 and F90 based on material configuration
void GzDeriveF0F90(inout GzMaterialData data)
{
    #ifdef USE_SPECULAR_EXTENSION
        // When specular extension is used, apply it on top of IOR
        data.f0 = GzGetF0Specular(data.baseColor, data.metallic, 
                                  data.specularColor, data.specularFactor, data.ior);
        data.f90 = GzGetF90Specular(data.metallic, data.specularFactor);
    #else
        // Standard metallic workflow with IOR
        data.f0 = GzGetF0Metallic(data.baseColor, data.metallic, data.ior);
        data.f90 = half3(1, 1, 1);
    #endif
}

// ============================================
// Energy Conservation Helpers
// ============================================

// GzMax3 is now in GzMath.cginc

// Sheen albedo scaling for energy conservation
half GzSheenAlbedoScaling(half3 sheenColor, half NoV)
{
    // Per glTF spec: albedo_scaling = 1.0 - max3(sheenColor) * E(NoV)
    // E(NoV) is the directional albedo, approximated here
    half maxSheenColor = GzMax3(sheenColor);
    half E = GzPow5(1.0 - NoV); // Simplified directional albedo
    return saturate(1.0 - maxSheenColor * E);
}

// ============================================
// Texture Thickness Mapping
// ============================================

// Map normalized thickness to physical thickness for iridescence
half GzMapIridescenceThickness(half normalizedThickness, half thicknessMin, half thicknessMax)
{
    return lerp(thicknessMin, thicknessMax, normalizedThickness);
}

// ============================================
// Validation Helpers
// ============================================

// Clamp F0 to valid range
half3 GzClampF0(half3 f0)
{
    return min(f0, half3(1.0, 1.0, 1.0));
}

// Validate material properties
void GzValidateMaterial(inout GzMaterialData data)
{
    data.metallic = saturate(data.metallic);
    data.roughness = saturate(data.roughness);
    data.occlusion = saturate(data.occlusion);
    data.alpha = saturate(data.alpha);
    data.f0 = GzClampF0(data.f0);
    data.clearcoatFactor = saturate(data.clearcoatFactor);
    data.clearcoatRoughness = saturate(data.clearcoatRoughness);
    data.iridescenceFactor = saturate(data.iridescenceFactor);
    data.specularFactor = saturate(data.specularFactor);
}

#endif // GZ_PBR_HELPERS_INCLUDED