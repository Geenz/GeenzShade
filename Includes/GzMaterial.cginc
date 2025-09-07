/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_MATERIAL_INCLUDED
#define GZ_MATERIAL_INCLUDED

// Material data structure - just data, no assumptions about population
struct GzMaterialData
{
    // Base PBR properties
    half3 baseColor;
    half alpha;
    half metallic;
    half roughness;
    half3 normal;
    half occlusion;
    half3 emissive;
    
    // Derived base properties
    half3 f0;  // Base reflectance
    half3 f90; // Grazing reflectance
    half ior;
    
    // KHR_materials_specular
    half specularFactor;
    half3 specularColor;
    
    // KHR_materials_clearcoat
    half clearcoatFactor;
    half clearcoatRoughness;
    half3 clearcoatNormal;
    
    // KHR_materials_sheen
    half3 sheenColor;
    half sheenRoughness;
    half sheenRimBoost;  // Artistic enhancement (1 = spec compliant)
    
    // KHR_materials_iridescence
    half iridescenceFactor;
    half iridescenceIOR;
    half iridescenceThickness;
    
    // KHR_materials_emissive_strength
    half emissiveStrength;
    
    // KHR_materials_diffuse_transmission (for thin surfaces like leaves, paper)
    half diffuseTransmissionFactor;
    half3 diffuseTransmissionColorFactor;
};

// Initialize material data with defaults
GzMaterialData GzCreateMaterialData()
{
    GzMaterialData data;
    
    data.baseColor = half3(1, 1, 1);
    data.alpha = 1;
    data.metallic = 0;
    data.roughness = 0.5;
    data.normal = half3(0, 0, 1);
    data.occlusion = 1;
    data.emissive = half3(0, 0, 0);
    
    data.f0 = half3(0.04, 0.04, 0.04);
    data.f90 = half3(1, 1, 1);
    data.ior = 1.5;
    
    data.specularFactor = 1;
    data.specularColor = half3(1, 1, 1);
    
    data.clearcoatFactor = 0;
    data.clearcoatRoughness = 0;
    data.clearcoatNormal = half3(0, 0, 1);
    
    data.sheenColor = half3(0, 0, 0);
    data.sheenRoughness = 0;
    data.sheenRimBoost = 1;  // Default to 1 = glTF compliant (no boost)
    
    data.iridescenceFactor = 0;
    data.iridescenceIOR = 1.3;
    data.iridescenceThickness = 100;
    
    data.emissiveStrength = 1;
    
    data.diffuseTransmissionFactor = 0;
    data.diffuseTransmissionColorFactor = half3(1, 1, 1);
    
    return data;
}

// Texture unpacking helpers for specialized formats
void GzUnpackMRO(inout GzMaterialData data, half3 mro)
{
    data.metallic = mro.r;
    data.roughness = mro.g;
    data.occlusion = mro.b;
}

void GzUnpackORM(inout GzMaterialData data, half3 orm)
{
    data.occlusion = orm.r;
    data.roughness = orm.g;
    data.metallic = orm.b;
}

void GzUnpackClearcoatIridescence(inout GzMaterialData data, half4 tex)
{
    data.clearcoatFactor = tex.r;
    data.clearcoatRoughness = tex.g;
    data.iridescenceFactor = tex.b;
    data.iridescenceThickness = tex.a;
}

void GzUnpackSheenColorRoughness(inout GzMaterialData data, half4 tex)
{
    data.sheenColor = tex.rgb;
    data.sheenRoughness = tex.a;
}

#endif // GZ_MATERIAL_INCLUDED