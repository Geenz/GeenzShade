/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_BRDF_INCLUDED
#define GZ_BRDF_INCLUDED

#include "GzMath.cginc"

// ============================================
// Constants
// ============================================

#define GZ_MIN_ROUGHNESS 0.1  // Minimum roughness to prevent HDR bloom and singularities

// ============================================
// Core BRDF Components
// ============================================

// Roughness-dependent Fresnel (based on Fdez-Agüera)
// This is a simplified version without BRDF LUT
half3 GzFresnelSchlick(half3 f0, half3 f90, half cosTheta, half roughness)
{
    // Roughness-dependent Fresnel range
    // On rough surfaces, we interpolate F90 toward F0 to reduce the Fresnel effect
    half3 roughF90 = max(half3(1.0 - roughness, 1.0 - roughness, 1.0 - roughness), f0);
    return f0 + (roughF90 - f0) * GzPow5(saturate(1.0 - cosTheta));
}

// Roughness-dependent Fresnel (scalar overload)
half GzFresnelSchlick(half f0, half f90, half cosTheta, half roughness)
{
    // Roughness-dependent Fresnel range
    half roughF90 = max(1.0 - roughness, f0);
    return f0 + (roughF90 - f0) * GzPow5(saturate(1.0 - cosTheta));
}


// GGX/Trowbridge-Reitz normal distribution
half GzDistributionGGX(half NoH, half roughness)
{
    roughness = max(roughness, GZ_MIN_ROUGHNESS);
    half a = roughness * roughness;
    half a2 = a * a;
    half NoH2 = NoH * NoH;
    
    half denom = NoH2 * (a2 - 1.0) + 1.0;
    return a2 / (UNITY_PI * denom * denom);
}

// Smith G visibility function (optimized)
half GzVisibilitySmithGGX(half NoL, half NoV, half roughness)
{
    // Clamp to prevent numerical issues at grazing angles with normal maps
    // This is a common technique used in Filament and other renderers
    NoL = max(NoL, GZ_EPSILON);
    NoV = max(NoV, GZ_EPSILON);
    
    half a = roughness * roughness;
    half GGXV = NoL * sqrt(NoV * NoV * (1.0 - a) + a);
    half GGXL = NoV * sqrt(NoL * NoL * (1.0 - a) + a);
    return 0.5 / max(GGXV + GGXL, GZ_EPSILON);
}

#endif // GZ_BRDF_INCLUDED