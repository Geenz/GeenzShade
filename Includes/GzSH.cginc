/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_SH_INCLUDED
#define GZ_SH_INCLUDED

// Unity's SH coefficients
// unity_SHAr, unity_SHAg, unity_SHAb - DC and linear terms
// unity_SHBr, unity_SHBg, unity_SHBb - quadratic terms  
// unity_SHC - final quadratic term

// Evaluate SH for a given normal direction
half3 GzEvaluateSH(half3 normal)
{
    // L0 (DC term) and L1 (linear terms)
    half4 vA = half4(normal, 1.0);
    
    half3 x1;
    x1.r = dot(unity_SHAr, vA);
    x1.g = dot(unity_SHAg, vA);
    x1.b = dot(unity_SHAb, vA);
    
    // L2 (quadratic terms)
    half4 vB = normal.xyzz * normal.yzzx;
    half3 x2;
    x2.r = dot(unity_SHBr, vB);
    x2.g = dot(unity_SHBg, vB);
    x2.b = dot(unity_SHBb, vB);
    
    // Final quadratic term
    half vC = normal.x * normal.x - normal.y * normal.y;
    half3 x3 = unity_SHC.rgb * vC;
    
    return max(0, x1 + x2 + x3);
}

// Get dominant light direction from SH
// This extracts the dipole (L1) component which represents directional info
half3 GzGetDominantLightDirFromSH()
{
    // The linear (L1) coefficients encode the dominant direction
    // L1 coefficients are stored in xyz components
    half3 dir;
    dir.x = unity_SHAr.x;
    dir.y = unity_SHAg.y; 
    dir.z = unity_SHAb.z;
    
    // Unity stores SH in a specific way:
    // The dipole moment gives us the average light direction
    // We need to extract it properly from the L1 band
    half3 L1_R = half3(unity_SHAr.x, unity_SHAr.y, unity_SHAr.z);
    half3 L1_G = half3(unity_SHAg.x, unity_SHAg.y, unity_SHAg.z);
    half3 L1_B = half3(unity_SHAb.x, unity_SHAb.y, unity_SHAb.z);
    
    // Average the color channels for a more stable direction
    dir = (L1_R + L1_G + L1_B) / 3.0;
    
    // The direction points toward the light
    return normalize(dir);
}

// Get dominant light intensity from SH L1 coefficients
// This extracts the actual directional light component intensity
half3 GzGetDominantLightColorFromSH()
{
    // The L1 coefficients represent the dipole moment of the lighting
    // The magnitude of these coefficients gives us the directional light intensity
    half3 L1_R = half3(unity_SHAr.x, unity_SHAr.y, unity_SHAr.z);
    half3 L1_G = half3(unity_SHAg.x, unity_SHAg.y, unity_SHAg.z);
    half3 L1_B = half3(unity_SHAb.x, unity_SHAb.y, unity_SHAb.z);
    
    // The magnitude of each color channel's L1 vector gives the intensity
    half intensityR = length(L1_R);
    half intensityG = length(L1_G);
    half intensityB = length(L1_B);
    
    // Scale by the SH normalization factor for L1 band
    // L1 basis functions have normalization factor of sqrt(3/(4*pi))
    const half L1_normalization = 0.488603; // sqrt(3/(4*pi))
    
    // Convert from SH coefficients to actual light intensity
    // Unity's SH encoding includes a factor we need to account for
    half3 lightColor = half3(intensityR, intensityG, intensityB) / L1_normalization;
    
    // Unity's SH is pre-scaled, apply appropriate factor
    return lightColor * 2.958; // Empirical factor for Unity's SH encoding
}

// Evaluate SH with contrast adjustment (useful for specular)
half3 GzEvaluateSHContrast(half3 normal, half contrast)
{
    half3 sh = GzEvaluateSH(normal);
    
    // Increase contrast around the average
    half3 avgSH = GzEvaluateSH(half3(0, 1, 0)); // Sample "up" as average
    sh = avgSH + (sh - avgSH) * contrast;
    
    return max(0, sh);
}

// Get SH irradiance for diffuse (includes the convolution)
half3 GzGetSHIrradiance(half3 normal)
{
    // Standard SH evaluation is already convolved for diffuse
    return GzEvaluateSH(normal);
}

// Approximate specular SH by sharpening the distribution
half3 GzGetSHSpecularApprox(half3 reflectionDir, half roughness)
{
    // Use contrast to approximate specular lobe sharpening
    // Lower roughness = higher contrast
    half contrast = lerp(4.0, 1.0, roughness);
    return GzEvaluateSHContrast(reflectionDir, contrast);
}

// Extract the L0 (ambient) component
half3 GzGetSHAmbient()
{
    return half3(unity_SHAr.w, unity_SHAg.w, unity_SHAb.w);
}

// Get average SH intensity by sampling multiple directions
half GzGetAverageSHIntensity()
{
    // Sample SH in 6 cardinal directions to get average intensity
    half3 sampleDirs[6] = {
        half3(1, 0, 0),   // +X
        half3(-1, 0, 0),  // -X  
        half3(0, 1, 0),   // +Y
        half3(0, -1, 0),  // -Y
        half3(0, 0, 1),   // +Z
        half3(0, 0, -1)   // -Z
    };
    
    half totalIntensity = 0.0;
    for (int i = 0; i < 6; i++)
    {
        half3 shColor = GzGetSHIrradiance(sampleDirs[i]);
        totalIntensity += dot(shColor, half3(0.299, 0.587, 0.114)); // Luminance
    }
    
    return totalIntensity / 6.0;
}

// Evaluate SH for a set of directions and return weighted average
// Useful for area lights or soft shadows
half3 GzEvaluateSHCone(half3 direction, half angle, int samples)
{
    half3 result = 0;
    half weight = 0;
    
    // Simple stratified sampling in a cone
    for (int i = 0; i < samples; i++)
    {
        half theta = angle * sqrt((i + 0.5) / samples);
        half phi = i * 2.399963; // Golden angle
        
        half3 sampleDir;
        sampleDir.x = sin(theta) * cos(phi);
        sampleDir.y = sin(theta) * sin(phi);
        sampleDir.z = cos(theta);
        
        // Transform to world space aligned with direction
        half3 tangent = normalize(cross(direction, half3(0, 1, 0)));
        if (length(tangent) < 0.001) tangent = normalize(cross(direction, half3(1, 0, 0)));
        half3 bitangent = cross(direction, tangent);
        
        half3 worldDir = sampleDir.x * tangent + sampleDir.y * bitangent + sampleDir.z * direction;
        
        half w = cos(theta);
        result += GzEvaluateSH(worldDir) * w;
        weight += w;
    }
    
    return result / weight;
}

#endif // GZ_SH_INCLUDED