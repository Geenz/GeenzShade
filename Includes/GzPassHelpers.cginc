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
    float2 uv1 : TEXCOORD1;     // Always include UV1 for lightmaps (matches Unity Standard)
    float2 uv2 : TEXCOORD2;     // Always include UV2 for dynamic lightmaps
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct GzVertexOutput
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 eyeVec : TEXCOORD1;             // eyeVec.xyz | fogCoord
    
    // TBN matrix stored as rows (consistent with ForwardAdd)
    float4 tangentToWorld[3] : TEXCOORD2;  // [3x3:tangentToWorld | 1x3:unused/packed data]
    half4 ambientOrLightmapUV : TEXCOORD5; // SH ambient or Lightmap UVs
    UNITY_LIGHTING_COORDS(6,7)
    float4 screenPos : TEXCOORD8;          // Screen position for depth fade
    float3 worldPos : TEXCOORD9;           // World position
    
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// ForwardAdd specific output (more compact)
struct GzVertexOutputAdd
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 eyeVec : TEXCOORD1;             // eyeVec.xyz | fogCoord
    
    // TBN matrix + light direction in W component (exactly like Unity)
    float4 tangentToWorldAndLightDir[3] : TEXCOORD2;    // [3x3:tangentToWorld | 1x3:lightDir] - mirrors Unity Standard.
    float3 worldPos : TEXCOORD5;
    UNITY_LIGHTING_COORDS(6, 7)
    float4 screenPos : TEXCOORD8;          // Screen position for depth fade
    
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// ============================================
// Vertex Shader Helpers
// ============================================

// Build TBN matrix and output vertex data for ForwardBase
GzVertexOutput GzVertexBase(GzVertexInput v)
{
    GzVertexOutput o;
    UNITY_INITIALIZE_OUTPUT(GzVertexOutput, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    
    float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv; // Transform will be applied based on which texture is being sampled
    o.worldPos = posWorld.xyz;
    o.eyeVec.xyz = normalize(posWorld.xyz - _WorldSpaceCameraPos);
    
    // Build TBN matrix
    half3 wNormal = UnityObjectToWorldNormal(v.normal);
    half3 wTangent = UnityObjectToWorldDir(v.tangent.xyz);
    half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
    half3 wBitangent = cross(wNormal, wTangent) * tangentSign;
    
    // Store TBN matrix rows (consistent with ForwardAdd)
    o.tangentToWorld[0] = float4(wTangent, 0);
    o.tangentToWorld[1] = float4(wBitangent, 0);
    o.tangentToWorld[2] = float4(wNormal, 0);
    
    // Ambient or lightmap UVs
    o.ambientOrLightmapUV = 0;
    #ifdef LIGHTMAP_ON
        o.ambientOrLightmapUV.xy = v.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
    #elif UNITY_SHOULD_SAMPLE_SH
        #ifdef VERTEXLIGHT_ON
            // Approximated illumination from non-important point lights (keep vertex lights!)
            o.ambientOrLightmapUV.rgb = Shade4PointLights(
                unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                unity_LightColor[0].rgb, unity_LightColor[1].rgb, 
                unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                unity_4LightAtten0, o.worldPos, wNormal
            );
        #endif
        // NO SH HERE - we compute SH per-pixel in fragment shader
    #endif
    #ifdef DYNAMICLIGHTMAP_ON
        o.ambientOrLightmapUV.zw = v.uv2 * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    
    // We need this for shadow receiving (exactly like Unity Standard)
    UNITY_TRANSFER_LIGHTING(o, v.uv1);
    
    // Screen position for depth fade (with proper W component for depth)
    o.screenPos = ComputeScreenPos(o.pos);
    COMPUTE_EYEDEPTH(o.screenPos.w);
    
    UNITY_TRANSFER_FOG_COMBINED_WITH_EYE_VEC(o, o.pos);
    
    return o;
}

// Build TBN matrix and output vertex data for ForwardAdd
GzVertexOutputAdd GzVertexAdd(GzVertexInput v)
{
    GzVertexOutputAdd o;
    UNITY_INITIALIZE_OUTPUT(GzVertexOutputAdd, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    
    float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    o.worldPos = posWorld.xyz;
    o.eyeVec.xyz = normalize(posWorld.xyz - _WorldSpaceCameraPos);
    
    // Build TBN matrix
    half3 wNormal = UnityObjectToWorldNormal(v.normal);
    half3 wTangent = UnityObjectToWorldDir(v.tangent.xyz);
    half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
    half3 wBitangent = cross(wNormal, wTangent) * tangentSign;
    
    // Calculate light direction (exactly like Unity Standard shader)
    float3 lightDir = _WorldSpaceLightPos0.xyz - o.worldPos * _WorldSpaceLightPos0.w;
    #ifndef USING_DIRECTIONAL_LIGHT
        lightDir = normalize(lightDir);
    #endif
    
    // Store TBN matrix rows like Unity (tangentToWorld[0/1/2])
    o.tangentToWorldAndLightDir[0] = float4(wTangent, lightDir.x);
    o.tangentToWorldAndLightDir[1] = float4(wBitangent, lightDir.y);
    o.tangentToWorldAndLightDir[2] = float4(wNormal, lightDir.z);
    
    // We need this for shadow receiving and lighting (exactly like Unity Standard)
    UNITY_TRANSFER_LIGHTING(o, v.uv1);
    
    // Screen position for depth fade (with proper W component for depth)
    o.screenPos = ComputeScreenPos(o.pos);
    COMPUTE_EYEDEPTH(o.screenPos.w);
    
    // Fog coordinates combined with eye vector
    UNITY_TRANSFER_FOG_COMBINED_WITH_EYE_VEC(o, o.pos);
    
    return o;
}

// ============================================
// TBN and Normal Helpers
// ============================================

// Extract TBN matrix from vertex output
half3x3 GzGetTBN(GzVertexOutput i)
{
    return half3x3(
        i.tangentToWorld[0].xyz, // tangent (row 0)
        i.tangentToWorld[1].xyz, // bitangent (row 1)
        i.tangentToWorld[2].xyz  // normal (row 2)
    );
}

// Extract TBN matrix from ForwardAdd vertex output
half3x3 GzGetTBNAdd(GzVertexOutputAdd i)
{
    return half3x3(
        i.tangentToWorldAndLightDir[0].xyz, // tangent (row 0)
        i.tangentToWorldAndLightDir[1].xyz, // bitangent (row 1)
        i.tangentToWorldAndLightDir[2].xyz  // normal (row 2)
    );
}

// Get world normal from vertex output (no normal map)
half3 GzGetWorldNormal(GzVertexOutput i)
{
    return normalize(i.tangentToWorld[2].xyz);
}

// Get world normal from ForwardAdd output
half3 GzGetWorldNormalAdd(GzVertexOutputAdd i)
{
    return normalize(i.tangentToWorldAndLightDir[2].xyz);
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