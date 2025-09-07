/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_SHEEN_INCLUDED
#define GZ_SHEEN_INCLUDED

#include "GzMath.cginc"

// ============================================
// Constants
// ============================================

#define GZ_MIN_SIN2H 0.0078125  // Prevent zero in log for Charlie distribution (1/128)

// ============================================
// Sheen BRDF Components
// ============================================

// Charlie sheen distribution
half GzD_Charlie(half roughness, half NoH)
{
    // Estevez and Kulla 2017, "Production Friendly Microfacet Sheen BRDF"
    half invAlpha = 1.0 / roughness;
    half cos2h = NoH * NoH;
    half sin2h = max(1.0 - cos2h, GZ_MIN_SIN2H);
    
    return (2.0 + invAlpha) * pow(sin2h, invAlpha * 0.5) / (2.0 * UNITY_PI);
}

// Ashikhmin sheen visibility function
half GzV_Ashikhmin(half NoL, half NoV)
{
    return 1.0 / (4.0 * (NoL + NoV - NoL * NoV));
}

// Calculate sheen BRDF contribution
half3 GzCalculateSheen(GzMaterialData matData, GzLightingContext ctx)
{
    half D = GzD_Charlie(matData.sheenRoughness, ctx.NoH);
    half V = GzV_Ashikhmin(ctx.NoL, ctx.NoV);
    
    // Apply rim boost if enabled
    return matData.sheenColor * D * V * matData.sheenRimBoost;
}

// Calculate sheen albedo scaling for energy conservation
half GzCalculateSheenAlbedoScaling(GzMaterialData matData, GzLightingContext ctx)
{
    // Per glTF spec: albedo_scaling = 1.0 - max3(sheenColor) * E(NoV)
    // E(NoV) is the directional albedo
    half E = GzPow5(1.0 - ctx.NoV);
    return saturate(1.0 - GzMax3(matData.sheenColor) * E);
}

#endif // GZ_SHEEN_INCLUDED