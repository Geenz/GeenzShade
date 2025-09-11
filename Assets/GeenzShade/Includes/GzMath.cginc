/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_MATH_INCLUDED
#define GZ_MATH_INCLUDED

// ============================================
// Constants
// ============================================

#define GZ_EPSILON 0.0001

// ============================================
// Common Math Operations
// ============================================

// Square function
half GzSqr(half x)
{
    return x * x;
}

half2 GzSqr(half2 x)
{
    return x * x;
}

half3 GzSqr(half3 x)
{
    return x * x;
}

half4 GzSqr(half4 x)
{
    return x * x;
}

// Power of 5 (common in Fresnel calculations)
half GzPow5(half x)
{
    half x2 = x * x;
    return x2 * x2 * x;
}

half3 GzPow5(half3 x)
{
    half3 x2 = x * x;
    return x2 * x2 * x;
}

// Positive power - avoids NaN from pow(0, 0) or pow(negative, fractional)
half GzPositivePow(half base, half power)
{
    return pow(max(abs(base), half(1e-4)), power);
}

// ============================================
// Vector Component Operations
// ============================================

// Get maximum component of a vector
half GzMax3(half3 v)
{
    return max(max(v.r, v.g), v.b);
}

half GzMax4(half4 v)
{
    return max(max(max(v.r, v.g), v.b), v.a);
}

// Get minimum component of a vector
half GzMin3(half3 v)
{
    return min(min(v.r, v.g), v.b);
}

half GzMin4(half4 v)
{
    return min(min(min(v.r, v.g), v.b), v.a);
}

// Average of vector components
half GzAverage3(half3 v)
{
    return (v.r + v.g + v.b) * 0.3333333333333333h;
}

// Luminance calculation (ITU-R BT.709)
half GzLuminance(half3 color)
{
    return dot(color, half3(0.2126, 0.7152, 0.0722));
}

// ============================================
// Safe Math Operations
// ============================================

// Safe division (avoids divide by zero) - useful for shader safety
half GzSafeDiv(half a, half b)
{
    return a / max(b, GZ_EPSILON);
}

half3 GzSafeDiv(half3 a, half3 b)
{
    return a / max(b, GZ_EPSILON);
}

// Safe normalize (handles zero-length vectors) - useful for shader safety
half3 GzSafeNormalize(half3 v)
{
    half len = length(v);
    return len > GZ_EPSILON ? v / len : half3(0, 0, 1);
}

// Reciprocal with safety
half GzRcp(half x)
{
    return 1.0 / max(x, GZ_EPSILON);
}

// ============================================
// Interpolation and Remapping
// ============================================

// Inverse lerp (get t from lerp result)
half GzInverseLerp(half a, half b, half value)
{
    return saturate((value - a) / (b - a));
}

// Remap value from one range to another
half GzRemap(half value, half oldMin, half oldMax, half newMin, half newMax)
{
    return newMin + (value - oldMin) * (newMax - newMin) / (oldMax - oldMin);
}

// Remap and clamp
half GzRemapClamped(half value, half oldMin, half oldMax, half newMin, half newMax)
{
    return saturate(GzRemap(value, oldMin, oldMax, newMin, newMax));
}

// Smootherstep (Ken Perlin's improved smoothstep) - NOT a built-in
half GzSmootherstep(half edge0, half edge1, half x)
{
    half t = saturate((x - edge0) / (edge1 - edge0));
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// ============================================
// Clamping and Saturation
// ============================================


// ============================================
// Trigonometric Helpers
// ============================================

// Cosine from sine using Pythagorean identity
half GzCosFromSin(half sinTheta)
{
    return sqrt(saturate(1.0 - sinTheta * sinTheta));
}

// Sine from cosine
half GzSinFromCos(half cosTheta)
{
    return sqrt(saturate(1.0 - cosTheta * cosTheta));
}

// ============================================
// Hashing and Noise
// ============================================

// Simple hash function for pseudo-random values
half GzHash(half2 p)
{
    half3 p3 = frac(half3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// ============================================
// Matrix Operations
// ============================================

// Create rotation matrix around Y axis
half3x3 GzRotateY(half angle)
{
    half c = cos(angle);
    half s = sin(angle);
    return half3x3(
        c, 0, s,
        0, 1, 0,
        -s, 0, c
    );
}

// Create TBN matrix from normal
half3x3 GzGetTBN(half3 normal)
{
    half3 tangent = normalize(cross(normal, half3(0, 1, 0)));
    if (length(tangent) < 0.001) 
        tangent = normalize(cross(normal, half3(1, 0, 0)));
    half3 bitangent = cross(normal, tangent);
    return half3x3(tangent, bitangent, normal);
}

#endif // GZ_MATH_INCLUDED