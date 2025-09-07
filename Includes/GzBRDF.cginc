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

// Fresnel Schlick approximation
half3 GzFresnelSchlick(half3 f0, half3 f90, half cosTheta)
{
    return f0 + (f90 - f0) * GzPow5(saturate(1.0 - cosTheta));
}

// Fresnel Schlick approximation (scalar overload)
half GzFresnelSchlick(half f0, half f90, half cosTheta)
{
    return f0 + (f90 - f0) * GzPow5(saturate(1.0 - cosTheta));
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
    half a = roughness * roughness;
    half GGXV = NoL * sqrt(NoV * NoV * (1.0 - a) + a);
    half GGXL = NoV * sqrt(NoL * NoL * (1.0 - a) + a);
    return 0.5 / max(GGXV + GGXL, 0.00001);
}

#endif // GZ_BRDF_INCLUDED