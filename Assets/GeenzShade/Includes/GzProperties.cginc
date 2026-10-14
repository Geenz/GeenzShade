/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

#ifndef GZ_PROPERTIES_INCLUDED
#define GZ_PROPERTIES_INCLUDED
#include "UnityStandardCore.cginc" // Includes a bunch of standard Unity properties.
#include "GzTextureArrays.cginc"   // GZ_DECLARE_TEX / GZ_SAMPLE_TEX / GZ_TEX_ST macros

// Single source of truth for all shader properties
// Shared between ForwardBase and ForwardAdd passes

// Array variants are shader properties named "<name>Array" (see GzTextureArrays.cginc).

// Base Textures
GZ_DECLARE_TEX(_BaseColorTexture);
float4 GZ_TEX_ST(_BaseColorTexture);
GZ_DECLARE_TEX(_ORMTexture);  // ORM texture (R=Occlusion, G=Roughness, B=Metallic)
float4 GZ_TEX_ST(_ORMTexture);
GZ_DECLARE_TEX(_NormalTexture);
float4 GZ_TEX_ST(_NormalTexture);
GZ_DECLARE_TEX(_EmissiveTexture);
float4 GZ_TEX_ST(_EmissiveTexture);
// Wave SDF is in _EmissiveTexture alpha: Tier 0 ForwardBase is at the 16-sampler cap.

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

// Soft depth fade for transparent surfaces (USE_SOFT_DEPTH). Needs a depth texture.
half _SoftDepthFadeDistance;

// Screen-space shadows from the SH dominant light (USE_SS_SHADOWS). Needs a depth texture.
half _SSShadowSteps;
half _SSShadowDistance;
half _SSShadowThickness;
half _SSShadowBias;
half _SSShadowIntensity;

float4 _EmissionWaveAxis;      // xyz = object-space travel direction, w = origin offset (default 0,1,0,0 = upward)
half   _EmissionWaveFresnel;   // 0 = off, 1 = full V.N falloff on the wave
half   _EmissionWaveFresnelPower;
half   _EmissionWaveFresnelMultiplier;
half   _EmissionWaveFresnelClampMin;
half   _EmissionWaveFresnelClampMax;
// Per AudioLink band: toggle + crest color. All four can run at once; each wave
// uses the SAME controls below, only the color differs.
half   _EmissionWaveBass;
half   _EmissionWaveLowMid;
half   _EmissionWaveHighMid;
half   _EmissionWaveTreble;
half4  _EmissionWaveBassColor;
half4  _EmissionWaveLowMidColor;
half4  _EmissionWaveHighMidColor;
half4  _EmissionWaveTrebleColor;
half   _EmissionWaveGain;
half   _EmissionWaveGate;
half   _EmissionWaveTravel;
half   _EmissionWaveSoftness;      // edge width of the wave itself - the main soft knob
half   _EmissionWaveEdgeSoftness;  // screen-space AA multiplier, a lower bound on the above
half   _EmissionWaveGlow;
half   _EmissionWaveGrow;      // Pulse only
half   _EmissionWavePivot;     // Pulse only
half   _EmissionWaveFrequency; // Ripple only
half   _EmissionWaveRingWidth; // Ripple only
half   _EmissionWaveSpeed;     // Ripple only
half   _EmissionWaveSmoothing; // Ripple only
half   _EmissionWaveEdgeFade;  // Ripple only
half   _EmissionWaveSdfMul;      // Ripple only - SDF mask strength
half   _EmissionWaveSdfFalloff;  // Ripple only - SDF mask power

#endif // GZ_PROPERTIES_INCLUDED