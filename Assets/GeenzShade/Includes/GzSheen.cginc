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

// Lambda function helper for more accurate sheen visibility
half GzLambdaSheenNumericHelper(half x, half alphaG)
{
    half oneMinusAlphaSq = (1.0 - alphaG) * (1.0 - alphaG);
    half a = lerp(21.5473, 25.3245, oneMinusAlphaSq);
    half b = lerp(3.82987, 3.32435, oneMinusAlphaSq);
    half c = lerp(0.19823, 0.16801, oneMinusAlphaSq);
    half d = lerp(-1.97760, -1.27393, oneMinusAlphaSq);
    half e = lerp(-4.32054, -4.85967, oneMinusAlphaSq);
    return a / (1.0 + b * pow(x, c)) + d * x + e;
}

// Lambda function for sheen
half GzLambdaSheen(half cosTheta, half alphaG)
{
    if (abs(cosTheta) < 0.5)
    {
        return exp(GzLambdaSheenNumericHelper(cosTheta, alphaG));
    }
    else
    {
        return exp(2.0 * GzLambdaSheenNumericHelper(0.5, alphaG) - GzLambdaSheenNumericHelper(1.0 - cosTheta, alphaG));
    }
}

// More accurate sheen visibility function (from Khronos reference)
half GzV_Sheen(half NoL, half NoV, half sheenRoughness)
{
    sheenRoughness = max(sheenRoughness, 0.0001); // Clamp to prevent division by zero
    half alphaG = sheenRoughness * sheenRoughness;
    
    return saturate(1.0 / ((1.0 + GzLambdaSheen(NoV, alphaG) + GzLambdaSheen(NoL, alphaG)) * 
                           (4.0 * NoV * NoL)));
}

// Calculate sheen BRDF contribution
half3 GzCalculateSheen(GzMaterialData matData, GzLightingContext ctx)
{
    half D = GzD_Charlie(matData.sheenRoughness, ctx.NoH);
    half V = GzV_Sheen(ctx.NoL, ctx.NoV, matData.sheenRoughness);
    
    // Apply sheen factor and rim boost
    return matData.sheenColor * D * V * matData.sheenFactor * matData.sheenRimBoost;
}

// Approximate sheen albedo scaling LUT without texture
// This approximates the Charlie directional albedo
half GzSheenAlbedoScalingLUT(half NoV, half sheenRoughness)
{
    // Approximate the sheen directional albedo
    // Based on empirical fit to Charlie BRDF energy
    // This should return a value that when multiplied by max(sheenColor)
    // gives us the amount to subtract from 1.0 for albedo scaling
    
    // At grazing angles (low NoV), sheen occludes more
    // Rougher sheen spreads energy more, reducing the occlusion effect
    half grazingTerm = saturate(1.0 - NoV);
    half roughnessModulation = 1.0 - sheenRoughness * 0.5; // Rougher = less occlusion
    
    // Simple approximation of the directional albedo
    return grazingTerm * roughnessModulation;
}

// Calculate sheen albedo scaling for energy conservation (direct lighting)
half GzCalculateSheenAlbedoScaling(GzMaterialData matData, GzLightingContext ctx)
{
    // For direct lighting, we need to consider both NoV and NoL
    // per the reference: min(1 - max3(sheenColor) * E(NoV), 1 - max3(sheenColor) * E(NoL))
    half sheenMax = GzMax3(matData.sheenColor) * matData.sheenFactor;
    
    half scalingNoV = 1.0 - sheenMax * GzSheenAlbedoScalingLUT(ctx.NoV, matData.sheenRoughness);
    half scalingNoL = 1.0 - sheenMax * GzSheenAlbedoScalingLUT(ctx.NoL, matData.sheenRoughness);
    
    // Take minimum to be conservative with energy
    return min(scalingNoV, scalingNoL);
}

// Calculate sheen albedo scaling for indirect/IBL
half GzCalculateSheenAlbedoScalingIndirect(GzMaterialData matData, half NoV)
{
    // For IBL, only NoV matters
    half sheenMax = GzMax3(matData.sheenColor) * matData.sheenFactor;
    return 1.0 - sheenMax * GzSheenAlbedoScalingLUT(NoV, matData.sheenRoughness);
}

#endif // GZ_SHEEN_INCLUDED