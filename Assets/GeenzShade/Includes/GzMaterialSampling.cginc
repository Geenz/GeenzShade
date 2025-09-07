/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_MATERIAL_SAMPLING_INCLUDED
#define GZ_MATERIAL_SAMPLING_INCLUDED

#include "GzMaterial.cginc"
#include "GzProperties.cginc"
#include "GzPBRHelpers.cginc"
#include "GzPassHelpers.cginc"  // Contains GzTransformUV
#include "GzIridescence.cginc"  // For evalIridescence function

// ============================================
// Specular Antialiasing
// ============================================

// Apply specular antialiasing to reduce aliasing artifacts
// Increases roughness at grazing angles and with camera distance
half ApplySpecularAntialiasing(half roughness, half NoV, float3 worldPos)
{
    #ifdef USE_SPECULAR_ANTIALIASING
        // Edge falloff: reduce specular at grazing angles
        // As NoV approaches 0 (grazing angle), increase roughness
        half edgeFactor = 1.0 - NoV;
        edgeFactor = pow(edgeFactor, _SpecularAAEdgeFalloff * 4.0); // Falloff control
        half edgeRoughness = lerp(roughness, saturate(roughness + _SpecularAAEdgeRoughness), edgeFactor);
        
        // Camera distance falloff: increase roughness with distance
        // The further the camera, the rougher the surface appears
        float cameraDistance = length(_WorldSpaceCameraPos - worldPos);
        half distanceFactor = saturate(cameraDistance / _SpecularAACameraDistanceFalloff);
        half finalRoughness = lerp(edgeRoughness, 1.0, distanceFactor);
        
        return finalRoughness;
    #else
        return roughness;
    #endif
}

// Sample all material textures and populate GzMaterialData
GzMaterialData GzSampleMaterial(float2 baseUV)
{
    GzMaterialData matData = GzCreateMaterialData();
    
    // Transform UVs for each texture
    float2 uvBaseColor = GzTransformUV(baseUV, _BaseColorTexture_ST);
    float2 uvORM = GzTransformUV(baseUV, _ORMTexture_ST);
    float2 uvEmissive = GzTransformUV(baseUV, _EmissiveTexture_ST);
    float2 uvSpecular = GzTransformUV(baseUV, _SpecularTexture_ST);
    float2 uvClearcoatIrid = GzTransformUV(baseUV, _ClearcoatIridescenceTexture_ST);
    float2 uvSheen = GzTransformUV(baseUV, _SheenTexture_ST);
    
    // Base color
    #ifdef USE_BASE_COLOR_TEXTURE
        half4 baseColorSample = tex2D(_BaseColorTexture, uvBaseColor);
        matData.baseColor = baseColorSample.rgb * _Color.rgb;
        matData.alpha = baseColorSample.a * _Color.a;
    #else
        matData.baseColor = _Color.rgb;
        matData.alpha = _Color.a;
    #endif
    
    // ORM (Occlusion, Roughness, Metallic)
    #ifdef USE_ORM_TEXTURE
        half3 orm = tex2D(_ORMTexture, uvORM).rgb;
        matData.occlusion = lerp(1.0, orm.r, _OcclusionStrength);  // R = Occlusion
        matData.roughness = orm.g * _Roughness;                    // G = Roughness
        matData.metallic = orm.b * _Metallic;                      // B = Metallic
    #else
        matData.metallic = _Metallic;
        matData.roughness = _Roughness;
        matData.occlusion = 1.0;
    #endif
    
    // Emissive
    #ifdef USE_EMISSIVE_TEXTURE
        matData.emissive = tex2D(_EmissiveTexture, uvEmissive).rgb * _EmissiveFactor.rgb;
    #else
        matData.emissive = _EmissiveFactor.rgb;
    #endif
    matData.emissiveStrength = _EmissionStrength;
    
    // Specular extension
    #ifdef USE_SPECULAR_EXTENSION
        #ifdef USE_SPECULAR_TEXTURE
            half4 specularSample = tex2D(_SpecularTexture, uvSpecular);
            matData.specularColor = specularSample.rgb * _SpecularColor.rgb;
            matData.specularFactor = specularSample.a * _SpecularFactor;
        #else
            matData.specularColor = _SpecularColor.rgb;
            matData.specularFactor = _SpecularFactor;
        #endif
    #else
        matData.specularColor = half3(1, 1, 1);
        matData.specularFactor = 1.0;
    #endif
    
    // Clearcoat and Iridescence
    #ifdef USE_CLEARCOAT_IRIDESCENCE_TEXTURE
        half4 clearcoatIridescence = tex2D(_ClearcoatIridescenceTexture, uvClearcoatIrid);
        #ifdef USE_CLEARCOAT
            matData.clearcoatFactor = clearcoatIridescence.r * _ClearcoatFactor;
            matData.clearcoatRoughness = clearcoatIridescence.g * _ClearcoatRoughness;
        #endif
        #ifdef USE_IRIDESCENCE
            matData.iridescenceFactor = clearcoatIridescence.b * _IridescenceFactor;
            half thicknessSample = clearcoatIridescence.a * _IridescenceThickness;
            matData.iridescenceThickness = lerp(_IridescenceThicknessMin, _IridescenceThicknessMax, thicknessSample);
        #endif
    #else
        #ifdef USE_CLEARCOAT
            matData.clearcoatFactor = _ClearcoatFactor;
            matData.clearcoatRoughness = _ClearcoatRoughness;
        #endif
        #ifdef USE_IRIDESCENCE
            matData.iridescenceFactor = _IridescenceFactor;
            matData.iridescenceThickness = lerp(_IridescenceThicknessMin, _IridescenceThicknessMax, _IridescenceThickness);
        #endif
    #endif
    
    matData.iridescenceIOR = _IridescenceIOR;
    
    // Sheen
    #ifdef USE_SHEEN
        #ifdef USE_SHEEN_TEXTURE
            half4 sheenSample = tex2D(_SheenTexture, uvSheen);
            matData.sheenColor = sheenSample.rgb * _SheenColor.rgb;
            matData.sheenRoughness = sheenSample.a * _SheenRoughness;
        #else
            matData.sheenColor = _SheenColor.rgb;
            matData.sheenRoughness = _SheenRoughness;
        #endif
        matData.sheenRimBoost = _SheenRimBoost;  // Artistic parameter (1 = spec compliant)
    #endif
    
    // Diffuse Transmission
    #ifdef USE_DIFFUSE_TRANSMISSION
        float2 uvDiffTrans = GzTransformUV(baseUV, _DiffuseTransmissionTexture_ST);
        
        #ifdef USE_DIFFUSE_TRANSMISSION_TEXTURE
            // Sample combined texture: RGB = color, A = factor
            half4 transmissionSample = tex2D(_DiffuseTransmissionTexture, uvDiffTrans);
            matData.diffuseTransmissionFactor = transmissionSample.a * _DiffuseTransmissionFactor;
            matData.diffuseTransmissionColorFactor = transmissionSample.rgb * _DiffuseTransmissionColorFactor.rgb;
        #else
            matData.diffuseTransmissionFactor = _DiffuseTransmissionFactor;
            matData.diffuseTransmissionColorFactor = _DiffuseTransmissionColorFactor.rgb;
        #endif
    #endif
    
    // Calculate F0 and F90 based on IOR and specular extension
    matData.ior = _IOR;
    half3 baseF0 = GzGetF0FromIOR(_IOR);
    
    // Apply specular extension to F0
    #ifdef USE_SPECULAR_EXTENSION
        // Per glTF spec: F0 = min(baseF0 * specularColor, 1.0) * specularFactor
        matData.f0 = min(baseF0 * matData.specularColor, half3(1,1,1)) * matData.specularFactor;
        matData.f90 = half3(matData.specularFactor, matData.specularFactor, matData.specularFactor);
    #else
        matData.f0 = baseF0;
        matData.f90 = half3(1, 1, 1);
    #endif
    
    // For metals, override F0 with base color
    matData.f0 = lerp(matData.f0, matData.baseColor, matData.metallic);
    matData.f90 = lerp(matData.f90, half3(1, 1, 1), matData.metallic);
    
    return matData;
}

// Apply iridescence to F0 (must be called after normal is set)
void GzApplyIridescenceToF0(inout GzMaterialData matData, half NoV)
{
    #ifdef USE_IRIDESCENCE
    if (matData.iridescenceFactor > 0)
    {
        // For iridescence calculation, we need to pass the correct F0:
        // - For metals: baseColor (already in matData.f0)
        // - For dielectrics with specular: specularFactor * baseF0 (without color)
        // Since we've already applied specular extension, we need to reconstruct
        half3 iridescenceInputF0 = matData.f0;
        
        #ifdef USE_SPECULAR_EXTENSION
        if (matData.metallic < 1.0)
        {
            // For dielectrics with specular extension, per spec we should pass
            // specular_weight * base_f0 to iridescence (not including specularColor)
            // We need to undo the specularColor multiplication
            // Current F0 = min(baseF0 * specularColor, 1) * specularFactor
            // We want: baseF0 * specularFactor
            half3 baseF0 = GzGetF0FromIOR(matData.ior);
            iridescenceInputF0 = baseF0 * matData.specularFactor;
        }
        #endif
        
        // Calculate iridescent F0
        half3 iridF0 = GzEvalIridescence(1.0, matData.iridescenceIOR, NoV, 
                                      matData.iridescenceThickness, iridescenceInputF0);
        
        // Apply iridescence with proper mixing
        #ifdef USE_SPECULAR_EXTENSION
        if (matData.metallic < 1.0)
        {
            // For dielectrics with specular: apply specularColor AFTER iridescence
            // Per spec: the iridescent Fresnel replaces the base Fresnel
            iridF0 = min(iridF0 * matData.specularColor, half3(1,1,1));
        }
        #endif
        
        // Mix base F0 with iridescent F0
        matData.f0 = lerp(matData.f0, iridF0, matData.iridescenceFactor);
        
        // Note: For perfect spec compliance, we'd need different mixing for metals vs dielectrics
        // but this unified approach is visually very close and more efficient
    }
    #endif
}

// Helper to unpack normal with explicit scale control
half3 GzUnpackNormalWithScale(half4 packednormal, half scale)
{
    #if defined(UNITY_NO_DXT5nm)
        // Not DXT5nm format
        half3 normal;
        normal.xy = packednormal.xy * 2 - 1;
        normal.xy *= scale;
        normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
        return normal;
    #else
        // DXT5nm format (AG channels)
        half3 normal;
        normal.xy = packednormal.ag * 2 - 1;
        normal.xy *= scale;
        normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
        return normal;
    #endif
}

// Sample normals and apply to material data
void GzApplyNormalMaps(inout GzMaterialData matData, float2 baseUV, half3x3 tbn)
{
    // Transform UVs for normal textures
    float2 uvNormal = GzTransformUV(baseUV, _NormalTexture_ST);
    float2 uvClearcoatNormal = GzTransformUV(baseUV, _ClearcoatNormalTexture_ST);
    
    // Base normal
    #ifdef USE_NORMAL_TEXTURE
        half4 normalSample = tex2D(_NormalTexture, uvNormal);
        half3 normalTS = GzUnpackNormalWithScale(normalSample, _NormalScale);
        matData.normal = normalize(mul(normalTS, tbn));
    #else
        matData.normal = normalize(tbn[2]); // Use world normal (third row of TBN)
    #endif
    
    // Clearcoat normal
    #ifdef USE_CLEARCOAT
        #ifdef USE_CLEARCOAT_NORMAL_TEXTURE
            half4 clearcoatNormalSample = tex2D(_ClearcoatNormalTexture, uvClearcoatNormal);
            half3 clearcoatNormalTS = GzUnpackNormalWithScale(clearcoatNormalSample, _ClearcoatNormalScale);
            matData.clearcoatNormal = normalize(mul(clearcoatNormalTS, tbn));
        #else
            matData.clearcoatNormal = matData.normal;
        #endif
    #else
        matData.clearcoatNormal = matData.normal;
    #endif
}

// Convenience function to sample everything at once
GzMaterialData GzSampleMaterialComplete(float2 baseUV, half3x3 tbn, half3 viewDir, float3 worldPos, bool isBackFace = false)
{
    GzMaterialData matData = GzSampleMaterial(baseUV);
    GzApplyNormalMaps(matData, baseUV, tbn);
    
    // For back faces, flip the normal to face the viewer
    // This ensures iridescence is calculated correctly
    if (isBackFace)
    {
        matData.normal = -matData.normal;
        matData.clearcoatNormal = -matData.clearcoatNormal;
    }
    
    // Apply iridescence to F0 after normal is set using actual view direction
    half NoV = saturate(dot(matData.normal, viewDir));
    GzApplyIridescenceToF0(matData, NoV);
    
    // Apply specular antialiasing to roughness
    matData.roughness = ApplySpecularAntialiasing(matData.roughness, NoV, worldPos);
    
    // Also apply to clearcoat roughness if clearcoat is enabled
    #ifdef USE_CLEARCOAT
        half clearcoatNoV = saturate(dot(matData.clearcoatNormal, viewDir));
        matData.clearcoatRoughness = ApplySpecularAntialiasing(matData.clearcoatRoughness, clearcoatNoV, worldPos);
    #endif
    
    // Leave the normal flipped for back faces - no need to flip back
    
    return matData;
}

#endif // GZ_MATERIAL_SAMPLING_INCLUDED