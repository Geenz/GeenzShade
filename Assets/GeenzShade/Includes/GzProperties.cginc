/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_PROPERTIES_INCLUDED
#define GZ_PROPERTIES_INCLUDED
#include "UnityStandardCore.cginc" // Includes a bunch of standard Unity properties.
#include "GzTextureArrays.cginc"   // GZ_DECLARE_TEX / GZ_SAMPLE_TEX / GZ_TEX_ST macros

// Single source of truth for all shader properties
// Shared between ForwardBase and ForwardAdd passes

// ============================================
// Texture Declarations
// ============================================
// Declared via GZ_DECLARE_TEX so they become Texture2DArrays (sampled by a
// per-vertex slice) when USE_TEXTURE_ARRAYS is on. The 2DArray variants are
// separate properties named "<name>Array" in the shader (see GzTextureArrays.cginc).

// Base Textures
GZ_DECLARE_TEX(_BaseColorTexture);
float4 GZ_TEX_ST(_BaseColorTexture);
GZ_DECLARE_TEX(_ORMTexture);  // ORM texture (R=Occlusion, G=Roughness, B=Metallic)
float4 GZ_TEX_ST(_ORMTexture);
GZ_DECLARE_TEX(_NormalTexture);
float4 GZ_TEX_ST(_NormalTexture);
GZ_DECLARE_TEX(_EmissiveTexture);
float4 GZ_TEX_ST(_EmissiveTexture);

// Extension Textures
GZ_DECLARE_TEX(_SpecularTexture);
float4 GZ_TEX_ST(_SpecularTexture);
GZ_DECLARE_TEX(_ClearcoatNormalTexture);
float4 GZ_TEX_ST(_ClearcoatNormalTexture);
GZ_DECLARE_TEX(_ClearcoatIridescenceTexture);
float4 GZ_TEX_ST(_ClearcoatIridescenceTexture);
GZ_DECLARE_TEX(_SheenTexture);
float4 GZ_TEX_ST(_SheenTexture);

// Auxiliary packed data texture (advanced): scalar params not covered by the
// other textures (R: IOR, G: Iridescence IOR, B: Face Cull, A: Sheen Rim Boost).
GZ_DECLARE_TEX(_AuxDataTexture);
float4 GZ_TEX_ST(_AuxDataTexture);
// Aux decode ranges (must match the bake). Used only when USE_AUX_DATA.
half _AuxIORMin, _AuxIORMax;
half _AuxIridescenceIORMin, _AuxIridescenceIORMax;
half _AuxSheenRimMin, _AuxSheenRimMax;
// Second aux page (USE_AUX_THICKNESS): per-variant iridescence thickness range.
// R = thickness min, G = thickness max, normalized into _AuxThicknessRange (nm).
GZ_DECLARE_TEX(_AuxDataTexture2);
float4 GZ_TEX_ST(_AuxDataTexture2);
half _AuxThicknessRangeMin, _AuxThicknessRangeMax;

// ============================================
// Material Properties
// ============================================

// Base Properties
half _Roughness;
half _NormalScale;
half3 _EmissiveFactor;
half _EmissionStrength;
half _AlphaCutoff;

// IOR
half _IOR;

// Specular Extension
half _SpecularFactor;
float4 _SpecularColor;

// Clearcoat
half _ClearcoatFactor;
half _ClearcoatRoughness;
half _ClearcoatNormalScale;

// Sheen
float4 _SheenColor;
half _SheenFactor;
half _SheenRoughness;
half _SheenRimBoost;  // Artistic rim enhancement (1 = glTF compliant, <1 = reduced, >1 = enhanced backscatter)

// Iridescence
half _IridescenceFactor;
half _IridescenceIOR;
half _IridescenceThickness;
half _IridescenceThicknessMin;
half _IridescenceThicknessMax;

// Diffuse Transmission
half _DiffuseTransmissionFactor;
half4 _DiffuseTransmissionColorFactor;
GZ_DECLARE_TEX(_DiffuseTransmissionTexture);
float4 GZ_TEX_ST(_DiffuseTransmissionTexture);

// Environment
half _ReflectionProbeThreshold;
half _SHThreshold;
samplerCUBE _FallbackCubemap;
half _FallbackIntensity;
half _FallbackDiffuseMipLevel;
half _FallbackMaxMipLevel;

// Specular antialiasing
half _SpecularAAEdgeFalloff;
half _SpecularAAEdgeRoughness;
half _SpecularAACameraDistanceFalloff;

// Light intensity multiplier
half _LightIntensityMultiplier;

// Lightmap reflection blend
half _LightmapReflectionBlend;

// Screen-space reflections (Tier 0 only; toggled per-material via USE_SSR).
// _CameraDepthTexture and the GrabPass capture are declared in GzSSR.cginc.
half _SSRStrength;
half _SSRHeight;
half _SSREdgeFade;
half _SSRMaxRoughness;

// ============================================
// Shader Feature Definitions
// ============================================
// These should be defined via #pragma shader_feature_local in the main shader
// Listed here for documentation:
//
// Texture Enable Features:
// - USE_BASE_COLOR_TEXTURE
// - USE_MRO_TEXTURE  
// - USE_NORMAL_TEXTURE
// - USE_CLEARCOAT_NORMAL_TEXTURE
// - USE_EMISSIVE_TEXTURE
// - USE_SPECULAR_TEXTURE
// - USE_CLEARCOAT_IRIDESCENCE_TEXTURE
// - USE_SHEEN_TEXTURE
//
// Extension Features:
// - USE_SPECULAR_EXTENSION
// - USE_CLEARCOAT
// - USE_SHEEN
// - USE_IRIDESCENCE
// - USE_ENVIRONMENT_REFLECTION
// - USE_VRC_LIGHT_VOLUMES
//
// Rendering Features:
// - _VERTEXLIGHTS_ON / _VERTEXLIGHTS_OFF
// - _SHDOMINANTLIGHT_ON / _SHDOMINANTLIGHT_OFF
// - _RENDERMODE_OPAQUE / _RENDERMODE_CUTOUT / _RENDERMODE_TRANSPARENT

#endif // GZ_PROPERTIES_INCLUDED