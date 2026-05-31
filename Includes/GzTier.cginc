/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_TIER_INCLUDED
#define GZ_TIER_INCLUDED

// ============================================
// Quality Tier Normalizer
// ============================================
//
// GZ_QUALITY_TIER is #defined by each SubShader's pass shell (0 = highest quality,
// higher numbers = cheaper). This file translates the tier number plus the
// per-material USE_* keywords into "effective" GZ_USE_* / GZ_APPROX_* capability
// macros that the rest of the includes consume.
//
// The USE_* keywords stay owned by the material/GUI and are NEVER redefined here.
// Effective capability = (tier permits it) AND (material keyword is on).
//
// Tier map (mirrors the SubShader LOD values in GzPBR.shader):
//   Tier 0 "Ultra"  (LOD 400): exact iridescence & sheen, clearcoat, + SSR
//   Tier 1 "High"   (LOD 300): approx iridescence & sheen, clearcoat
//   Tier 2 "Medium" (LOD 200): approx iridescence & sheen, no clearcoat
//   Tier 3 "Low"    (LOD 100): no iridescence / sheen / clearcoat
//
// Include this BEFORE the modules that read GZ_USE_* / GZ_APPROX_* (i.e. before
// GzMaterialSampling / GzLayerCalculations).

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

#endif // GZ_TIER_INCLUDED
