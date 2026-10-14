/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

// Requires a depth texture, and callers must fall back gracefully without one.

#ifndef GZ_DEPTH_INCLUDED
#define GZ_DEPTH_INCLUDED

// Stereo-correct screen-space texture sampling (mirrors Mochie's screenspace
// macros so single-pass-instanced VR samples the correct eye).
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    #define GZ_DECLARE_SS(tex)      Texture2DArray tex; SamplerState sampler##tex
    #define GZ_SAMPLE_SS(tex, uv)   tex.SampleLevel(sampler##tex, float3(uv, (float)unity_StereoEyeIndex), 0)
#else
    #define GZ_DECLARE_SS(tex)      Texture2D tex; SamplerState sampler##tex
    #define GZ_SAMPLE_SS(tex, uv)   tex.SampleLevel(sampler##tex, uv, 0)
#endif

GZ_DECLARE_SS(_CameraDepthTexture);   // scene depth (requires a depth texture)

// VRChat mirror detection (Mochie / Common/Utilities.cginc). Screen-space effects
// are skipped in mirrors because the grab/depth there don't match this view.
bool GzIsInMirror()
{
    return unity_CameraProjection[2][0] != 0.0 || unity_CameraProjection[2][1] != 0.0;
}

// Interleaved gradient noise (Jimenez) - procedural per-pixel jitter. Input is the
// pixel coordinate (SV_POSITION.xy).
float GzSSRNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Scene eye-space depth at a ComputeScreenPos coordinate (.xy scaled by .w = clip W).
inline float GzSampleSceneEyeDepth(float4 projPos)
{
    float2 uv = projPos.xy / max(projPos.w, 1e-6);
    return LinearEyeDepth(GZ_SAMPLE_SS(_CameraDepthTexture, uv).r);
}

// partEyeZ is the fragment's own eye depth (COMPUTE_EYEDEPTH).
inline half GzSoftDepthFade(float4 projPos, float partEyeZ, half fadeDistance)
{
    float sceneZ = GzSampleSceneEyeDepth(projPos);
    float diff = sceneZ - partEyeZ;
    return (diff <= 0.0) ? 1.0 : saturate(diff / max(fadeDistance, 1e-4));
}

// Screen-space shadows: needs the _SSShadow* uniforms from GzProperties.cginc.
half GzTraceScreenSpaceShadow(float3 worldPos, half3 lightDir, float noise)
{
    [branch]
    if (GzIsInMirror())
        return 0.0;

    #if UNITY_SINGLE_PASS_STEREO
        half x_min = 0.5 * unity_StereoEyeIndex;
        half x_max = 0.5 + 0.5 * unity_StereoEyeIndex;
    #else
        half x_min = 0.0;
        half x_max = 1.0;
    #endif

    float3 vpos = mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).xyz;
    float3 vdir = normalize(mul((float3x3)UNITY_MATRIX_V, lightDir));

    int steps = max(1, (int)_SSShadowSteps);
    float stepLen = _SSShadowDistance / steps;
    // Jittered start, pushed a step off the surface to avoid self-shadowing.
    float3 p = vpos + vdir * stepLen * (0.5 + 0.5 * noise);

    UNITY_LOOP
    for (int i = 0; i < steps; i++)
    {
        float4 spos = ComputeGrabScreenPos(mul(UNITY_MATRIX_P, float4(p, 1.0)));
        float2 uv = spos.xy / spos.w;

        [branch]
        if (uv.x > x_max || uv.x < x_min || uv.y > 1.0 || uv.y < 0.0)
            break; // ray left the screen -> treat as lit

        float sceneEyeZ = LinearEyeDepth(GZ_SAMPLE_SS(_CameraDepthTexture, uv).r);
        float rayEyeZ = -p.z;
        float diff = rayEyeZ - sceneEyeZ; // >0: ray is behind a depth-buffer surface

        if (diff > _SSShadowBias && diff < _SSShadowThickness)
            return 1.0; // occluder between surface and light

        p += vdir * stepLen;
    }
    return 0.0;
}

#endif // GZ_DEPTH_INCLUDED
