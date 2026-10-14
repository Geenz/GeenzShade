/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

#ifndef GZ_TIER_INCLUDED
#define GZ_TIER_INCLUDED

// Include before GzMaterialSampling and GzLayerCalculations.

#ifndef GZ_QUALITY_TIER
    #define GZ_QUALITY_TIER 1   // safe default if a shell forgets to set it
#endif

// --- Clearcoat: present at tiers 0-1 ---
#if GZ_QUALITY_TIER <= 1
    #ifdef USE_CLEARCOAT
        #define GZ_USE_CLEARCOAT
    #endif
#endif

// --- Iridescence / Sheen: present at tiers 0-2 ---
#if GZ_QUALITY_TIER <= 2
    #ifdef USE_IRIDESCENCE
        #define GZ_USE_IRIDESCENCE
    #endif
    #ifdef USE_SHEEN
        #define GZ_USE_SHEEN
    #endif
#endif

// --- Approximate iridescence/sheen at tiers 1-2; exact only at tier 0 ---
#if (GZ_QUALITY_TIER >= 1) && (GZ_QUALITY_TIER <= 2)
    #ifdef GZ_USE_IRIDESCENCE
        #define GZ_APPROX_IRIDESCENCE
    #endif
    #ifdef GZ_USE_SHEEN
        #define GZ_APPROX_SHEEN
    #endif
#endif

// --- Screen-space reflections: Tier 0 only (GZ_ENABLE_SSR set by the shell),
//     gated by the per-material USE_SSR toggle and requiring env reflections ---
#if defined(GZ_ENABLE_SSR) && defined(USE_SSR) && defined(USE_ENVIRONMENT_REFLECTION)
    #define GZ_USE_SSR
#endif

// --- Emission wave (AudioLink): deliberately not tier-gated ---
#if defined(_EMISSIONWAVE_PULSE) || defined(_EMISSIONWAVE_RIPPLE)
    #define GZ_EMISSION_WAVE
#endif

// Object toggles are not tier-gated: consumers read USE_OBJECT_TOGGLES directly.

#endif // GZ_TIER_INCLUDED
