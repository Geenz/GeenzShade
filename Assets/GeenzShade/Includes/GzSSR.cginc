/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// Adapted from Mochie's Unity Shaders (StandardSSR.cginc), used under the MIT
// License, Copyright (c) 2020 MochiesCode:
//   https://github.com/MochiMochiBoboService/Mochies-Unity-Shaders
//
// The original screen-space reflection technique was created by
// error.mdl, Toocanzs, and Xiexe, and reworked by Mochie.
//
// MIT License notice for the adapted portions:
//   Permission is hereby granted, free of charge, to any person obtaining a
//   copy of this software and associated documentation files (the "Software"),
//   to deal in the Software without restriction... The above copyright notice
//   and this permission notice shall be included in all copies or substantial
//   portions of the Software. (Full text: see Mochie's LICENSE.)
//
// Noise is procedural interleaved gradient noise, not the original's blue-noise texture.

#ifndef GZ_SSR_INCLUDED
#define GZ_SSR_INCLUDED

// Shared stereo screen-space macros (GZ_DECLARE_SS/GZ_SAMPLE_SS) and the
// _CameraDepthTexture declaration live here so SSR and soft depth fade agree.
#include "GzDepth.cginc"

// Only compiled when GzTier.cginc enables SSR (Tier 0 + per-material USE_SSR +
// environment reflections on).
#ifdef GZ_USE_SSR

GZ_DECLARE_SS(_GzGrab);               // GrabPass capture of the opaque scene
float4 _GzGrab_TexelSize;

// Box blur of the grab capture for rougher reflections (Mochie GetBlurredGrabPass).
float3 GzBlurredGrab(float2 texelSize, float2 uvs, float dim)
{
    float2 pixSize = 2.0 / texelSize;
    float dimFloored = floor(dim);
    float center = floor(dim * 0.5);
    float3 refTotal = float3(0, 0, 0);
    for (int i = 0; i < dimFloored; i++)
    {
        for (int j = 0; j < dimFloored; j++)
        {
            refTotal += GZ_SAMPLE_SS(_GzGrab, float2(uvs.x + pixSize.x * (i - center),
                                                     uvs.y + pixSize.y * (j - center))).rgb;
        }
    }
    return refTotal / (dimFloored * dimFloored);
}

// View-space ray march against the depth buffer (Mochie ReflectRay).
// Returns the view-space hit position in .xyz (0 = miss).
float4 GzReflectRay(float3 reflectedRay, float3 rayDir, float lRad, float sRad, float step, float noise, const int maxIterations)
{
    #if UNITY_SINGLE_PASS_STEREO
        half x_min = 0.5 * unity_StereoEyeIndex;
        half x_max = 0.5 + 0.5 * unity_StereoEyeIndex;
    #else
        half x_min = 0.0;
        half x_max = 1.0;
    #endif

    reflectedRay = mul(UNITY_MATRIX_V, float4(reflectedRay, 1));
    rayDir = mul(UNITY_MATRIX_V, float4(rayDir, 0));
    int direction = 1;
    float3 finalPos = float3(0, 0, 0);

    [loop]
    for (int i = 0; i < maxIterations; i++)
    {
        float4 spos = ComputeGrabScreenPos(mul(UNITY_MATRIX_P, float4(reflectedRay, 1)));
        float2 uvDepth = spos.xy / spos.w;

        [branch]
        if (uvDepth.x > x_max || uvDepth.x < x_min || uvDepth.y > 1 || uvDepth.y < 0)
            break;

        float rawDepth = GZ_SAMPLE_SS(_CameraDepthTexture, uvDepth).r;
        float linearDepth = Linear01Depth(rawDepth);
        float sampleDepth = -reflectedRay.z;
        float realDepth = linearDepth * _ProjectionParams.z;
        float depthDifference = abs(sampleDepth - realDepth);

        if (depthDifference < lRad)
        {
            if (direction == 1)
            {
                if (sampleDepth > (realDepth - sRad))
                {
                    if (sampleDepth < (realDepth + sRad))
                    {
                        finalPos = reflectedRay;
                        break;
                    }
                    direction = -1;
                    step = step * 0.1;
                }
            }
            else
            {
                if (sampleDepth < (realDepth + sRad))
                {
                    direction = 1;
                    step = step * 0.1;
                }
            }
        }
        reflectedRay = reflectedRay + direction * step * rayDir;
        step += step * (0.025 + 0.005 * noise);
        lRad += lRad * (0.025 + 0.005 * noise);
        sRad += sRad * (0.025 + 0.005 * noise);
    }
    return float4(finalPos, 0);
}

// Compute the screen-space reflection colour + alpha (Mochie GetSSR).
// smoothness = 1 - roughness. noise is from GzSSRNoise(pixelCoord).
float4 GzGetSSR(float3 wPos, float3 viewDir, float3 rayDir, half3 faceNormal,
                float smoothness, float3 albedo, float metallic, float noise)
{
    float FdotR = dot(faceNormal, rayDir);
    float roughness = 1.0 - smoothness;

    [branch]
    if (GzIsInMirror() || FdotR < 0 || roughness > _SSRMaxRoughness)
        return 0;

    // Offset the ray start off the surface to avoid self-intersection, jittered
    // by noise to break up banding (Mochie's _SSRHeight scheme).
    float3 reflectedRay = wPos + (_SSRHeight * _SSRHeight / FdotR + noise * _SSRHeight) * rayDir;
    float4 finalPos = GzReflectRay(reflectedRay, rayDir, _SSRHeight, 0.02, _SSRHeight, noise, 50);
    finalPos.w = 1;

    if (!any(finalPos.xyz))
        return 0;

    float4 uvs = UNITY_PROJ_COORD(ComputeGrabScreenPos(mul(UNITY_MATRIX_P, finalPos)));
    uvs.xy = uvs.xy / uvs.w;

    #if UNITY_SINGLE_PASS_STEREO || defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
        float xfade = 1;
    #else
        float xfade = smoothstep(0, _SSREdgeFade, uvs.x) * smoothstep(1, 1 - _SSREdgeFade, uvs.x);
    #endif
    float yfade = smoothstep(0, _SSREdgeFade, uvs.y) * smoothstep(1, 1 - _SSREdgeFade, uvs.y);
    float smoothFade = smoothstep(_SSRMaxRoughness, _SSRMaxRoughness * 0.33, roughness);
    float reflectionAlpha = xfade * yfade * smoothFade;

    float4 reflection = 0;
    if (reflectionAlpha > 0)
    {
        float blurFac = max(1, min(12, 12 * (-2) * (smoothness - 1)));
        reflection.rgb = GzBlurredGrab(_GzGrab_TexelSize.zw, uvs.xy, blurFac);
        // Tint reflections by base colour for metals (dielectrics stay neutral).
        reflection.rgb = lerp(reflection.rgb, reflection.rgb * albedo, smoothstep(0, 1.75, metallic));
        reflection.a = reflectionAlpha;
    }

    return max(0, reflection);
}

#endif // GZ_USE_SSR
#endif // GZ_SSR_INCLUDED
