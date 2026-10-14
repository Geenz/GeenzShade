/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

#ifndef GZ_EMISSION_WAVE_INCLUDED
#define GZ_EMISSION_WAVE_INCLUDED

// Sole AudioLink.cginc includer: include this file instead, never AudioLink.cginc.

#ifdef GZ_EMISSION_WAVE
    #include "Packages/com.llealloo.audiolink/Runtime/Shaders/AudioLink.cginc"
#endif

// Must be called at uniform control flow: it takes screen-space derivatives.
half3 GzApplyEmissionWave(half3 emission, half sdf, half emissiveStrength, float3 worldPos,
                          half3 normal, half3 viewDir)
{
#ifndef GZ_EMISSION_WAVE
    return emission;
#else

    // 0. View-based falloff, before the gradient so AA sees the faded field.
    half fres = saturate(dot(normal, viewDir));
    fres = pow(max(fres, 1.0e-4h), _EmissionWaveFresnelPower);
    fres = clamp(fres * _EmissionWaveFresnelMultiplier, _EmissionWaveFresnelClampMin, _EmissionWaveFresnelClampMax);
    fres = lerp(1.0h, fres, _EmissionWaveFresnel);
    sdf *= saturate(fres);

    // 1. Field gradient, outside every branch: fwidth() is undefined in divergent flow.
    half dSdf = fwidth(sdf);

    // 2/255 floor is two 8-bit code steps, the narrowest edge the field can resolve.
    half w = max(max(_EmissionWaveSoftness, dSdf * _EmissionWaveEdgeSoftness), 2.0h / 255.0h);

    // 2. Object-space travel coordinate, so the wave stays locked to the avatar.
    float3 objPos  = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
    float  spatial = saturate(dot(objPos, _EmissionWaveAxis.xyz) + _EmissionWaveAxis.w);

    // 3. Modulation amount, accumulated per enabled band.
    half3 waveSum = 0;

    // [branch] is required and no AudioLink texel may be read outside this guard.
    [branch] if (AudioLinkIsAvailable())
    {
        half4 bandOn = half4(_EmissionWaveBass, _EmissionWaveLowMid, _EmissionWaveHighMid, _EmissionWaveTreble);
        [unroll] for (uint band = 0; band < 4; band++)
        {
        if (bandOn[band] <= 0.5h) continue;
        half4 bandColor = band == 0 ? _EmissionWaveBassColor
                        : band == 1 ? _EmissionWaveLowMidColor
                        : band == 2 ? _EmissionWaveHighMidColor
                                    : _EmissionWaveTrebleColor;
        half delta = 0;

    #if defined(_EMISSIONWAVE_PULSE)

        // Clamp at 126, not 127, because AudioLinkLerp reads x and x+1.
        float delayPix = clamp(spatial * _EmissionWaveTravel * 127.0, 0.0, 126.0);
        half  amp = (half)AudioLinkLerp(float2(ALPASS_AUDIOLINK.x + delayPix,
                                               ALPASS_AUDIOLINK.y + band)).r;

        amp = saturate(amp * _EmissionWaveGain);
        amp = amp * amp * (3.0h - 2.0h * amp);

        // Silence gate: availability only tests texture size, not whether audio is playing.
        half gate = smoothstep(_EmissionWaveGate, _EmissionWaveGate + 0.05h, amp);

        // Grow is a fraction of 2x the authored spread: 0.5 grows the shape by `spread` px.
        half t = 0.5h - _EmissionWaveGrow * (amp - _EmissionWavePivot);

        half maskNow  = smoothstep(t    - w, t    + w, sdf);   // audio-driven silhouette
        half maskRest = smoothstep(0.5h - w, 0.5h + w, sdf);   // authored silhouette

        // Differencing against the rest mask makes the neutral state exactly zero.
        half annulus = maskNow - maskRest;

        half profile = saturate((sdf - t) / max(0.5h - t, 1.0e-3h));
        profile = profile * profile * (3.0h - 2.0h * profile);   // smoothstep, no pow

        // Rail fade zeroes clamped plateaus, preventing a full-surface strobe.
        half pulseFade = (half)_EmissionWaveEdgeFade;
        half pulseValid = smoothstep(0.0h, pulseFade, sdf)
                        * smoothstep(1.0h, 1.0h - pulseFade, sdf);

        delta = annulus * profile * pulseValid * gate;

    #elif defined(_EMISSIONWAVE_RIPPLE)

        // ALPASS_FILTEREDAUDIOLINK is 16 x 4: x is filter strength (0 heaviest, 15 raw).
        uint smoothIdx = (uint)clamp(_EmissionWaveSmoothing, 0.0, 15.0);
        half amp  = saturate((half)AudioLinkData(ALPASS_FILTEREDAUDIOLINK
                                               + uint2(smoothIdx, band)).r
                             * _EmissionWaveGain);
        half gate = smoothstep(_EmissionWaveGate, _EmissionWaveGate + 0.05h, amp);

        // ChronoTensity index 1 never runs backwards, and integer cycles keep the 20-bit wrap seamless.
        uint  chrono = AudioLinkDecodeDataAsUInt(ALPASS_CHRONOTENSITY + uint2(1, band));
        uint  cycles = (uint)max(1.0, round(_EmissionWaveSpeed * 8.0));
        float travel = float((chrono * cycles) & 0xFFFFFu) * (1.0 / 1048576.0);

        // Spatial phase offset, in ring periods, so the train is not in lockstep across
        // the whole avatar. Up to 4 periods across one unit of the axis.
        travel += spatial * _EmissionWaveTravel * 4.0;

        // fp32 from here down: half quantizes sdf * freq into visible steps on Quest.
        float freq  = _EmissionWaveFrequency;
        float phase = (float)sdf * freq - travel;

        float tri = abs(frac(phase) * 2.0 - 1.0);

        // Duty is also the exact DC mean of ring, which the Nyquist fade below relies on.
        float duty = _EmissionWaveRingWidth;

        // Softness is in triangle units here, not field units.
        float pw   = max((float)dSdf * freq * 2.0, (float)_EmissionWaveSoftness);
        pw = max(pw, 1.0e-5);
        float ring = smoothstep(1.0 - duty - pw, 1.0 - duty + pw, tri);

        // Nyquist fade: lerp to the DC mean (duty) as the ring period nears pixel size.
        ring = lerp(ring, duty, saturate(pw));

        // Rail fade zeroes the clamped 0 and 1 plateaus, so a constant 0 or 1 field adds nothing.
        half fade = (half)_EmissionWaveEdgeFade;
        half fieldValid = smoothstep(0.0h, fade, sdf) * smoothstep(1.0h, 1.0h - fade, sdf);

        // SDF mask over the ripples: (ripple * sdf * mul) * pow(sdf, falloff).
        // max() guards pow against a zero base with a negative exponent.
        half sdfMask = sdf * _EmissionWaveSdfMul * pow(max(sdf, 1.0e-4h), _EmissionWaveSdfFalloff);

        delta = (half)(ring - duty) * sdfMask * fieldValid * gate * amp;

    #endif

        waveSum += saturate(delta) * bandColor.rgb;
        }
    }

    // 4. Additive crest, shared by both modes.
    emission += waveSum
              * fres
              * _EmissionWaveGlow
              * _EmissiveFactor.rgb
              * emissiveStrength;

    return emission;

#endif // GZ_EMISSION_WAVE
}

#endif // GZ_EMISSION_WAVE_INCLUDED
