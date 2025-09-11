/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_PROPERTIES_INCLUDED
#define GZ_PROPERTIES_INCLUDED
#include "UnityStandardCore.cginc" // Includes a bunch of standard Unity properties.

// Single source of truth for all shader properties
// Shared between ForwardBase and ForwardAdd passes

// ============================================
// Texture Declarations
// ============================================

// Base Textures
sampler2D _BaseColorTexture;
float4 _BaseColorTexture_ST;
sampler2D _ORMTexture;  // ORM texture (R=Occlusion, G=Roughness, B=Metallic)
float4 _ORMTexture_ST;
sampler2D _NormalTexture;
float4 _NormalTexture_ST;
sampler2D _EmissiveTexture;
float4 _EmissiveTexture_ST;

// Extension Textures
sampler2D _SpecularTexture;
float4 _SpecularTexture_ST;
sampler2D _ClearcoatNormalTexture;
float4 _ClearcoatNormalTexture_ST;
sampler2D _ClearcoatIridescenceTexture;
float4 _ClearcoatIridescenceTexture_ST;
sampler2D _SheenTexture;
float4 _SheenTexture_ST;

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
sampler2D _DiffuseTransmissionTexture;
float4 _DiffuseTransmissionTexture_ST;

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

// ============================================
// System Textures
// ============================================

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

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