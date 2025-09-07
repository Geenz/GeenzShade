/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

#ifndef GZ_PASS_HELPERS_INCLUDED
#define GZ_PASS_HELPERS_INCLUDED

#include "UnityCG.cginc"
#include "UnityLightingCommon.cginc"
#include "AutoLight.cginc"

// ============================================
// Unified Vertex Data Structures
// ============================================

struct GzVertexInput
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 uv : TEXCOORD0;
};

struct GzVertexOutput
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    
    // TBN matrix stored as 3 vectors
    half3 tspace0 : TEXCOORD2; // tangent.x, bitangent.x, normal.x
    half3 tspace1 : TEXCOORD3; // tangent.y, bitangent.y, normal.y
    half3 tspace2 : TEXCOORD4; // tangent.z, bitangent.z, normal.z
    
    // Lighting and shadows
    UNITY_FOG_COORDS(5)
    SHADOW_COORDS(6)
    float4 screenPos : TEXCOORD7;
    
    #ifdef VERTEXLIGHT_ON
        half3 vertexLightColor : TEXCOORD8;
    #endif
};

// ForwardAdd specific output (more compact)
struct GzVertexOutputAdd
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    
    // TBN matrix
    half3 tspace0 : TEXCOORD2;
    half3 tspace1 : TEXCOORD3;
    half3 tspace2 : TEXCOORD4;
    
    LIGHTING_COORDS(5, 6)
    float4 screenPos : TEXCOORD7;
    UNITY_FOG_COORDS(8)
};

// ============================================
// Vertex Shader Helpers
// ============================================

// Build TBN matrix and output vertex data for ForwardBase
GzVertexOutput GzVertexBase(GzVertexInput v)
{
    GzVertexOutput o;
    UNITY_INITIALIZE_OUTPUT(GzVertexOutput, o);
    
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv; // Transform will be applied based on which texture is being sampled
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    
    // Build TBN matrix
    half3 wNormal = UnityObjectToWorldNormal(v.normal);
    half3 wTangent = UnityObjectToWorldDir(v.tangent.xyz);
    half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
    half3 wBitangent = cross(wNormal, wTangent) * tangentSign;
    
    // Store TBN in tspace vectors
    o.tspace0 = half3(wTangent.x, wBitangent.x, wNormal.x);
    o.tspace1 = half3(wTangent.y, wBitangent.y, wNormal.y);
    o.tspace2 = half3(wTangent.z, wBitangent.z, wNormal.z);
    
    // Compute vertex lights
    #if defined(VERTEXLIGHT_ON) && defined(_VERTEXLIGHTS_ON)
        o.vertexLightColor = Shade4PointLights(
            unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
            unity_LightColor[0].rgb, unity_LightColor[1].rgb, 
            unity_LightColor[2].rgb, unity_LightColor[3].rgb,
            unity_4LightAtten0, o.worldPos, wNormal
        );
    #endif
    
    // Transfer shadow and fog coords
    TRANSFER_SHADOW(o);
    UNITY_TRANSFER_FOG(o, o.pos);
    
    // Screen position for depth fade (with proper W component for depth)
    o.screenPos = ComputeScreenPos(o.pos);
    COMPUTE_EYEDEPTH(o.screenPos.w);
    
    return o;
}

// Build TBN matrix and output vertex data for ForwardAdd
GzVertexOutputAdd GzVertexAdd(GzVertexInput v)
{
    GzVertexOutputAdd o;
    UNITY_INITIALIZE_OUTPUT(GzVertexOutputAdd, o);
    
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    
    // Build TBN matrix
    half3 wNormal = UnityObjectToWorldNormal(v.normal);
    half3 wTangent = UnityObjectToWorldDir(v.tangent.xyz);
    half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
    half3 wBitangent = cross(wNormal, wTangent) * tangentSign;
    
    // Store TBN in tspace vectors
    o.tspace0 = half3(wTangent.x, wBitangent.x, wNormal.x);
    o.tspace1 = half3(wTangent.y, wBitangent.y, wNormal.y);
    o.tspace2 = half3(wTangent.z, wBitangent.z, wNormal.z);
    
    // Transfer lighting coords
    TRANSFER_VERTEX_TO_FRAGMENT(o);
    
    // Screen position for depth fade (with proper W component for depth)
    o.screenPos = ComputeScreenPos(o.pos);
    COMPUTE_EYEDEPTH(o.screenPos.w);
    
    // Fog coordinates
    UNITY_TRANSFER_FOG(o, o.pos);
    
    return o;
}

// ============================================
// TBN and Normal Helpers
// ============================================

// Extract TBN matrix from vertex output
half3x3 GzGetTBN(GzVertexOutput i)
{
    return half3x3(
        half3(i.tspace0.x, i.tspace1.x, i.tspace2.x), // tangent
        half3(i.tspace0.y, i.tspace1.y, i.tspace2.y), // bitangent
        half3(i.tspace0.z, i.tspace1.z, i.tspace2.z)  // normal
    );
}

// Extract TBN matrix from ForwardAdd vertex output
half3x3 GzGetTBNAdd(GzVertexOutputAdd i)
{
    return half3x3(
        half3(i.tspace0.x, i.tspace1.x, i.tspace2.x), // tangent
        half3(i.tspace0.y, i.tspace1.y, i.tspace2.y), // bitangent
        half3(i.tspace0.z, i.tspace1.z, i.tspace2.z)  // normal
    );
}

// Get world normal from vertex output (no normal map)
half3 GzGetWorldNormal(GzVertexOutput i)
{
    return normalize(half3(i.tspace0.z, i.tspace1.z, i.tspace2.z));
}

// Get world normal from ForwardAdd output
half3 GzGetWorldNormalAdd(GzVertexOutputAdd i)
{
    return normalize(half3(i.tspace0.z, i.tspace1.z, i.tspace2.z));
}

// Sample normal map and transform to world space
half3 GzSampleNormalMap(sampler2D normalMap, float2 uv, half scale, half3x3 TBN)
{
    half4 normalSample = tex2D(normalMap, uv);
    
    // Unpack normal with scale
    #if defined(UNITY_NO_DXT5nm)
        half3 normalTS;
        normalTS.xy = normalSample.xy * 2 - 1;
        normalTS.xy *= scale;
        normalTS.z = sqrt(1 - saturate(dot(normalTS.xy, normalTS.xy)));
    #else
        half3 normalTS;
        normalTS.xy = normalSample.ag * 2 - 1;
        normalTS.xy *= scale;
        normalTS.z = sqrt(1 - saturate(dot(normalTS.xy, normalTS.xy)));
    #endif
    
    return normalize(mul(normalTS, TBN));
}

// ============================================
// Final Output Helpers
// ============================================

// Apply final output with transparency
half4 GzFinalOutput(half3 color, half alpha)
{
    #ifdef _RENDERMODE_TRANSPARENT
        return half4(color, alpha);
    #else
        return half4(color, 1.0);
    #endif
}

// Apply final output for additive pass
half4 GzFinalOutputAdd(half3 color)
{
    return half4(color, 0);
}

// ============================================
// Utility Functions
// ============================================

// Transform UV with texture ST
float2 GzTransformUV(float2 uv, float4 st)
{
    return uv * st.xy + st.zw;
}

// Alpha test for cutout mode
void GzAlphaTest(half alpha, half cutoff)
{
    #ifdef _RENDERMODE_CUTOUT
        clip(alpha - cutoff);
    #endif
}

#endif // GZ_PASS_HELPERS_INCLUDED