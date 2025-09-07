/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_IRIDESCENCE_INCLUDED
#define GZ_IRIDESCENCE_INCLUDED

#include "GzMath.cginc"  // For GzSqr, GzPow5
#include "GzBRDF.cginc"  // For GzFresnelSchlick

// KHR_materials_iridescence implementation
// Based on official glTF Sample Renderer
// Implements thin-film interference model

// XYZ to Rec709 color space conversion matrix
static const half3x3 XYZ_TO_REC709 = half3x3(
     3.2404542, -0.9692660,  0.0556434,
    -1.5371385,  1.8760108, -0.2040259,
    -0.4985314,  0.0415560,  1.0572252
);


// Evaluation XYZ sensitivity curves in Fourier space (exact official glTF implementation)
half3 GzEvalSensitivity(half OPD, half3 shift)
{
    half phase = 2.0 * UNITY_PI * OPD * 1.0e-9;
    half3 val = half3(5.4856e-13, 4.4201e-13, 5.2481e-13);
    half3 pos = half3(1.6810e+06, 1.7953e+06, 2.2084e+06);
    half3 var = half3(4.3278e+09, 9.3046e+09, 6.6121e+09);

    half3 xyz = val * sqrt(2.0 * UNITY_PI * var) * cos(pos * phase + shift) * exp(-GzSqr(phase) * var);
    xyz.x += 9.7470e-14 * sqrt(2.0 * UNITY_PI * 4.5282e+09) * cos(2.2399e+06 * phase + shift.x) * exp(-4.5282e+09 * GzSqr(phase));
    xyz /= 1.0685e-7;

    half3 srgb = mul(XYZ_TO_REC709, xyz);
    return srgb;
}

// Convert Fresnel F0 to IOR (official glTF implementation)
half3 GzFresnel0ToIor(half3 fresnel0)
{
    half3 sqrtF0 = sqrt(fresnel0);
    return (half3(1.0, 1.0, 1.0) + sqrtF0) / (half3(1.0, 1.0, 1.0) - sqrtF0);
}

// Convert IOR to Fresnel F0 (official glTF implementation)
half3 GzIorToFresnel0(half3 transmittedIor, half incidentIor)
{
    return GzSqr((transmittedIor - half3(incidentIor, incidentIor, incidentIor)) / 
               (transmittedIor + half3(incidentIor, incidentIor, incidentIor)));
}

half GzIorToFresnel0(half transmittedIor, half incidentIor)
{
    return GzSqr((transmittedIor - incidentIor) / (transmittedIor + incidentIor));
}

// Helper to get thickness blend factor for smooth IOR transition
half GzGetThicknessBlendFactor(half thickness, half minThickness, half maxThickness)
{
    // Use a smoother transition range based on the thickness parameters
    half transitionStart = minThickness * 0.3; // 30% of min thickness
    half transitionEnd = minThickness * 0.8;   // 80% of min thickness
    return smoothstep(transitionStart, transitionEnd, thickness);
}

// Calculate wavelength-dependent phase for improved color accuracy
half3 GzGetWavelengthPhase(half opd)
{
    // Representative wavelengths for RGB in nanometers (visible spectrum)
    half3 wavelengths = half3(680.0, 550.0, 440.0); // Red, Green, Blue
    return 2.0 * UNITY_PI * opd / wavelengths;
}

// Main iridescence evaluation function (exact official glTF implementation)
half3 GzEvalIridescence(half outsideIOR, half eta2, half cosTheta1, half thinFilmThickness, half3 baseF0)
{
    half3 I;

    // Force iridescenceIor -> outsideIOR when thinFilmThickness -> 0.0
    half iridescenceIor = lerp(outsideIOR, eta2, smoothstep(0.0, 0.03, thinFilmThickness));
    // Evaluate the cosTheta on the base layer (Snell law)
    half sinTheta2Sq = GzSqr(outsideIOR / iridescenceIor) * (1.0 - GzSqr(cosTheta1));

    // Handle TIR:
    half cosTheta2Sq = 1.0 - sinTheta2Sq;
    if (cosTheta2Sq < 0.0) {
        return half3(1.0, 1.0, 1.0);
    }

    half cosTheta2 = sqrt(cosTheta2Sq);

    // First interface
    half R0 = GzIorToFresnel0(iridescenceIor, outsideIOR);
    half R12 = GzFresnelSchlick(R0, 1.0, cosTheta1);
    half R21 = R12;
    half T121 = 1.0 - R12;
    half phi12 = 0.0;
    if (iridescenceIor < outsideIOR) phi12 = UNITY_PI;
    half phi21 = UNITY_PI - phi12;

    // Second interface
    half3 baseIOR = GzFresnel0ToIor(clamp(baseF0, 0.0, 0.9999)); // guard against 1.0
    half3 R1 = GzIorToFresnel0(baseIOR, iridescenceIor);
    half3 R23 = GzFresnelSchlick(R1, half3(1.0, 1.0, 1.0), cosTheta2);
    half3 phi23 = half3(0.0, 0.0, 0.0);
    if (baseIOR.r < iridescenceIor) phi23.r = UNITY_PI;
    if (baseIOR.g < iridescenceIor) phi23.g = UNITY_PI;
    if (baseIOR.b < iridescenceIor) phi23.b = UNITY_PI;

    // Phase shift
    half OPD = 2.0 * iridescenceIor * thinFilmThickness * cosTheta2;
    half3 phi = half3(phi21, phi21, phi21) + phi23;

    // Compound terms
    half3 R123 = clamp(R12 * R23, 1e-5, 0.9999);
    half3 r123 = sqrt(R123);
    half3 Rs = GzSqr(T121) * R23 / (half3(1.0, 1.0, 1.0) - R123);

    // Reflectance term for m = 0 (DC term amplitude)
    half3 C0 = R12 + Rs;
    I = C0;

    // Reflectance term for m > 0 (pairs of diracs)
    half3 Cm = Rs - T121;
    for (int m = 1; m <= 2; ++m)
    {
        Cm *= r123;
        half3 Sm = 2.0 * GzEvalSensitivity(half(m) * OPD, half(m) * phi);
        I += Cm * Sm;
    }

    // Since out of gamut colors might be produced, negative color values are clamped to 0.
    return max(I, half3(0.0, 0.0, 0.0));
}

// DEPRECATED: These functions are kept for backward compatibility only
// Iridescence is now applied automatically in GzSampleMaterialComplete()

// Simple wrapper for backward compatibility
half3 GzGetIridescentFresnel(half iridescenceFactor, half iridescenceIOR, half iridescenceThickness,
                          half3 baseF0, half NoV)
{
    // For backward compatibility, still calculate iridescence here
    if (iridescenceFactor <= 0.0)
        return baseF0;
    
    half3 iridF0 = GzEvalIridescence(1.0, iridescenceIOR, NoV, iridescenceThickness, baseF0);
    return lerp(baseF0, iridF0, iridescenceFactor);
}

// DEPRECATED: F0/F90 calculation is now centralized in GzMaterialSampling
// This function is kept for backward compatibility only
void GetIridescentFresnelSpecular(half iridescenceFactor, half iridescenceIOR, half iridescenceThickness,
                                  half baseIOR, half specularFactor, half3 specularColorFactor,
                                  half metallic, half3 baseColor, half NoV,
                                  out half3 iridF0, out half3 iridF90)
{
    // For backward compatibility, still provide the calculation
    // In new code, use GzSampleMaterialComplete which handles this automatically
    
    // Calculate base F0
    half iorToF0 = (1.0 - baseIOR) / (1.0 + baseIOR);
    iorToF0 = iorToF0 * iorToF0;
    half3 baseF0 = iorToF0 * specularColorFactor * specularFactor;
    baseF0 = min(baseF0, half3(1.0, 1.0, 1.0));
    
    // Override for metals
    baseF0 = lerp(baseF0, baseColor, metallic);
    half3 baseF90 = lerp(half3(specularFactor, specularFactor, specularFactor), half3(1, 1, 1), metallic);
    
    // Apply iridescence if present
    if (iridescenceFactor > 0.0)
    {
        half3 iridescenceF0 = GzEvalIridescence(1.0, iridescenceIOR, NoV, iridescenceThickness, baseF0);
        iridF0 = lerp(baseF0, iridescenceF0, iridescenceFactor);
        iridF90 = baseF90; // F90 unchanged per glTF spec
    }
    else
    {
        iridF0 = baseF0;
        iridF90 = baseF90;
    }
}

#endif // GZ_IRIDESCENCE_INCLUDED