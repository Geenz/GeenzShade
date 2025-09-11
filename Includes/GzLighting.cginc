/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_LIGHTING_INCLUDED
#define GZ_LIGHTING_INCLUDED

#include "GzMath.cginc"
#include "GzBRDF.cginc"
#include "GzMaterial.cginc"
#include "GzPBRHelpers.cginc"

// ============================================
// Lighting Context Structure
// ============================================

struct GzLightingContext
{
    // Light properties
    half3 lightDir;
    half3 lightColor;
    half lightAtten;
    
    // View properties
    half3 viewDir;
    
    // Computed vectors
    half3 halfVec;
    
    // Dot products (pre-calculated for performance)
    half NoL;
    half NoV;
    half NoH;
    half VoH;
    half LoV;
    half LoH;
    
    // Backface lighting (for transmission if implemented)
    half NoL_back;
    
    // Clearcoat dot products (when clearcoat normal differs from base normal)
    #ifdef USE_CLEARCOAT
    half ccNoL;
    half ccNoV;
    half ccNoH;
    half ccVoH;
    #endif
    
    // Ambient/indirect
    half3 ambientLight;  // Can be SH data or flat color
};

// ============================================
// Context Creation and Population
// ============================================

// Create an empty lighting context
GzLightingContext GzCreateLightingContext()
{
    GzLightingContext ctx;
    
    ctx.lightDir = half3(0, 1, 0);
    ctx.lightColor = half3(0, 0, 0);
    ctx.lightAtten = 0;
    ctx.viewDir = half3(0, 0, 1);
    ctx.halfVec = half3(0, 0.707, 0.707);
    ctx.ambientLight = half3(0, 0, 0);
    
    ctx.NoL = 0;
    ctx.NoV = 0;
    ctx.NoH = 0;
    ctx.VoH = 0;
    ctx.LoV = 0;
    ctx.LoH = 0;
    ctx.NoL_back = 0;
    
    #ifdef USE_CLEARCOAT
    ctx.ccNoL = 0;
    ctx.ccNoV = 0;
    ctx.ccNoH = 0;
    ctx.ccVoH = 0;
    #endif
    
    return ctx;
}

// Populate lighting vectors and dot products
void GzPopulateLightingVectors(inout GzLightingContext ctx, half3 normal)
{
    ctx.halfVec = normalize(ctx.lightDir + ctx.viewDir);
    ctx.NoL = saturate(dot(normal, ctx.lightDir));
    ctx.NoV = saturate(dot(normal, ctx.viewDir));
    ctx.NoH = saturate(dot(normal, ctx.halfVec));
    ctx.VoH = saturate(dot(ctx.viewDir, ctx.halfVec));
    ctx.LoV = dot(ctx.lightDir, ctx.viewDir);
    ctx.LoH = saturate(dot(ctx.lightDir, ctx.halfVec));
    ctx.NoL_back = saturate(dot(-normal, ctx.lightDir));
    
    // Clearcoat values default to base values (will be overridden if clearcoat normal differs)
    #ifdef USE_CLEARCOAT
    ctx.ccNoL = ctx.NoL;
    ctx.ccNoV = ctx.NoV;
    ctx.ccNoH = ctx.NoH;
    ctx.ccVoH = ctx.VoH;
    #endif
}

// Populate clearcoat-specific dot products when clearcoat normal differs
#ifdef USE_CLEARCOAT
void GzPopulateClearcoatVectors(inout GzLightingContext ctx, half3 clearcoatNormal)
{
    ctx.ccNoL = saturate(dot(clearcoatNormal, ctx.lightDir));
    ctx.ccNoV = saturate(dot(clearcoatNormal, ctx.viewDir));
    ctx.ccNoH = saturate(dot(clearcoatNormal, ctx.halfVec));
    ctx.ccVoH = ctx.VoH; // VoH doesn't change with normal
}
#endif

// Core BRDF components are now in GzBRDF.cginc

#endif // GZ_LIGHTING_INCLUDED