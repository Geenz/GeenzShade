/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_OBJECT_TOGGLES_INCLUDED
#define GZ_OBJECT_TOGGLES_INCLUDED

// Float bit math holds 24 exact bits in fp32 and avoids uint ops, which force target 4.0 (GLES 3.1).

float _GzToggleD0;
float _GzToggleD1;
float _GzToggleD2;
float _GzToggleD3;
float _GzToggleD4;
float _GzToggleD5;
float _GzToggleD6;
float _GzToggleD7;
float _GzToggleD8;
float _GzToggleD9;
float _GzToggleD10;
float _GzToggleD11;
float _GzToggleD12;
float _GzToggleD13;
float _GzToggleD14;
float _GzToggleD15;
float _GzToggleD16;
float _GzToggleD17;
float _GzToggleD18;
float _GzToggleD19;
float _GzToggleD20;
float _GzToggleD21;
float _GzToggleD22;
float _GzToggleD23;

// Returns > 0 when any declaration governing this vertex is disabled.
// Equivalent to (governing & ~enabledBits) != 0, in float form.
inline float GzObjectToggleHidden(float mask)
{
    float states[24] = {
        _GzToggleD0,  _GzToggleD1,  _GzToggleD2,  _GzToggleD3,
        _GzToggleD4,  _GzToggleD5,  _GzToggleD6,  _GzToggleD7,
        _GzToggleD8,  _GzToggleD9,  _GzToggleD10, _GzToggleD11,
        _GzToggleD12, _GzToggleD13, _GzToggleD14, _GzToggleD15,
        _GzToggleD16, _GzToggleD17, _GzToggleD18, _GzToggleD19,
        _GzToggleD20, _GzToggleD21, _GzToggleD22, _GzToggleD23
    };
    float hidden = 0.0;
    [unroll]
    for (int i = 0; i < 24; i++)
    {
        // Bit i of the mask, extracted in float: fmod(floor(mask / 2^i), 2).
        float bitSet = step(1.0, fmod(floor(mask / exp2((float)i)), 2.0));
        // 0.5 threshold tolerates blended / transition-curve state values.
        hidden += bitSet * step(states[i], 0.5);
    }
    return hidden;
}

// Never NaN (undefined on tile GPUs): hidden vertices all collapse to one finite point.
inline void GzApplyObjectToggle(inout float4 clipPos, float mask)
{
    if (GzObjectToggleHidden(mask) > 0.0)
    {
        clipPos = float4(0.0, 0.0, 0.0, 1.0);
    }
}

#endif // GZ_OBJECT_TOGGLES_INCLUDED
