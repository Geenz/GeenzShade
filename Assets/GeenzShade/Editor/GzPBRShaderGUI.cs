/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

using UnityEngine;
using UnityEditor;
using System;
using System.IO;

namespace GeenzShade
{
    public class GzPBRShaderGUI : ShaderGUI
    {
        // Foldout states - UI only, exactly like original
        private static bool advancedRenderModeFoldout = false;
        private static bool clearcoatFoldout = false;
        private static bool iridescenceFoldout = false; 
        private static bool sheenFoldout = false;
        private static bool diffuseTransmissionFoldout = false;
        private static bool lightingFoldout = false;
        private static bool environmentFoldout = false;
        private static bool specularFoldout = false;
        private static bool ssrFoldout = false;
        private static bool qualityTierFoldout = false;
        
        // Light Volumes detection
        private static bool? lightVolumesInstalled = null;
        private const string LIGHT_VOLUMES_PATH = "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc";

        public enum RenderMode
        {
            Opaque,
            Transparent,
            Cutout,
            Additive,
            Multiply,
            PremultipliedAlpha
        }

        public enum CullMode
        {
            Back = 2,
            Front = 1,
            Off = 0
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;

            EditorGUI.BeginChangeCheck();

            DrawRenderMode(materialEditor, properties, material);
            
            EditorGUILayout.Space();
            
            DrawAdvancedRenderMode(materialEditor, properties, material);
            
            EditorGUILayout.Space();
            
            DrawPropertyGroups(materialEditor, properties, material);

            if (EditorGUI.EndChangeCheck())
            {
                UpdateMaterialKeywords(material, properties);
            }
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            var properties = MaterialEditor.GetMaterialProperties(new Material[] { material });
            
            // Auto-configure light volumes based on availability
            var useVRCLightVolumesProp = FindProperty("_UseVRCLightVolumes", properties);
            if (useVRCLightVolumesProp != null)
            {
                bool hasLightVolumes = CheckLightVolumesInstalled();
                useVRCLightVolumesProp.floatValue = hasLightVolumes ? 1.0f : 0.0f;
            }
            
            UpdateMaterialKeywords(material, properties);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            var properties = MaterialEditor.GetMaterialProperties(new Material[] { material });
            UpdateMaterialKeywords(material, properties);
        }

        private void DrawRenderMode(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var renderModeProp = FindProperty("_RenderMode", properties);
            if (renderModeProp == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Render Mode", GUILayout.Width(EditorGUIUtility.labelWidth));

            RenderMode currentMode = (RenderMode)renderModeProp.floatValue;
            RenderMode newMode = (RenderMode)EditorGUILayout.EnumPopup(currentMode);

            if (newMode != currentMode)
            {
                renderModeProp.floatValue = (float)newMode;
                SetupRenderMode(material, newMode);
            }

            EditorGUILayout.EndHorizontal();
            
            // Show alpha cutoff slider when in Cutout mode (EXACTLY like original)
            if (currentMode == RenderMode.Cutout)
            {
                var alphaCutoffProp = FindProperty("_AlphaCutoff", properties);
                if (alphaCutoffProp != null)
                {
                    EditorGUI.indentLevel++;
                    alphaCutoffProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent("Alpha Cutoff", "Threshold for alpha cutout transparency"),
                        alphaCutoffProp.floatValue, 0f, 1f);
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawAdvancedRenderMode(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            advancedRenderModeFoldout = EditorGUILayout.Foldout(advancedRenderModeFoldout, "Advanced Render Mode", true);

            if (advancedRenderModeFoldout)
            {
                EditorGUI.indentLevel++;

                // Face cull source: hardware fixed-function state vs per-variant
                // in-shader culling driven by the Aux Data texture's B channel.
                var cullSrcProp = FindProperty("_FaceCullSource", properties);
                int cullSrc = cullSrcProp != null ? Mathf.RoundToInt(cullSrcProp.floatValue) : 0;
                var cullProp = FindProperty("_Cull", properties);
                var cullBakeProp = FindProperty("_FaceCullBake", properties); // artist intent, preserved
                if (cullSrcProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Face Cull Source", "Hardware: fixed-function Cull state. Texture: render double-sided and cull per-variant in-shader (forward + shadow passes) from the Aux Data texture's B channel, so meshes with different cull modes can share one material. Forces Cull Off."),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    int newCullSrc = EditorGUILayout.Popup(cullSrc, new[] { "Hardware", "Texture" });
                    if (newCullSrc != cullSrc)
                    {
                        // Preserve the cull intent across the switch: _Cull is the render
                        // state (forced Off in Texture mode for double-sided); _FaceCullBake
                        // carries the intent the baker reads.
                        if (newCullSrc == 1)
                        {
                            if (cullBakeProp != null && cullProp != null) cullBakeProp.floatValue = cullProp.floatValue;
                            if (cullProp != null) cullProp.floatValue = (float)CullMode.Off;
                        }
                        else if (cullBakeProp != null && cullProp != null)
                        {
                            cullProp.floatValue = cullBakeProp.floatValue; // restore intent as render state
                        }
                        cullSrcProp.floatValue = newCullSrc;
                        cullSrc = newCullSrc;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (cullSrc == 0 && cullProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Culling", GUILayout.Width(EditorGUIUtility.labelWidth));
                    CullMode currentCull = (CullMode)cullProp.floatValue;
                    CullMode newCull = (CullMode)EditorGUILayout.EnumPopup(currentCull);
                    if (newCull != currentCull)
                    {
                        cullProp.floatValue = (float)newCull;
                        if (cullBakeProp != null) cullBakeProp.floatValue = (float)newCull; // keep intent in sync
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else if (cullSrc == 1)
                {
                    // Render is double-sided (_Cull = Off); this dropdown is the cull
                    // intent the Aux Data Baker writes into the texture's B channel.
                    if (cullBakeProp != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(
                            new GUIContent("Baked Cull", "The cull mode the Aux Data Baker packs into the aux texture's B channel for this material. Rendering stays double-sided; the shader discards the unwanted facing per-region. Re-bake after changing this."),
                            GUILayout.Width(EditorGUIUtility.labelWidth));
                        CullMode curBake = (CullMode)cullBakeProp.floatValue;
                        CullMode newBake = (CullMode)EditorGUILayout.EnumPopup(curBake);
                        if (newBake != curBake) cullBakeProp.floatValue = (float)newBake;
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.HelpBox(
                        "Culling is per-variant from the aux texture's B channel (0=Off, 0.5=Back, 1=Front), read directly — the " +
                        "full Auxiliary Data feature need not be on. Assign an aux texture with cull packed in B (use the Aux Data " +
                        "Baker, or enable Auxiliary Data Texture to set one). The material renders double-sided; cast shadows " +
                        "respect this; lightmap (Meta) baking still renders double-sided. Per-pixel discard disables early-Z, " +
                        "which is why this is an advanced opt-in.", MessageType.Info);
                }

                // Specular Antialiasing
                EditorGUILayout.Space(5);
                var useSpecularAAProp = FindProperty("_UseSpecularAntialiasing", properties);
                if (useSpecularAAProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Specular Antialiasing", "Reduces specular aliasing at grazing angles and distance"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool useSpecularAA = useSpecularAAProp.floatValue > 0;
                    bool newUseSpecularAA = EditorGUILayout.Toggle(useSpecularAA);
                    if (newUseSpecularAA != useSpecularAA)
                    {
                        useSpecularAAProp.floatValue = newUseSpecularAA ? 1 : 0;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (newUseSpecularAA)
                    {
                        EditorGUI.indentLevel++;
                        
                        var edgeFalloffProp = FindProperty("_SpecularAAEdgeFalloff", properties);
                        if (edgeFalloffProp != null)
                        {
                            edgeFalloffProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Edge Falloff", "How quickly roughness increases at grazing angles"),
                                edgeFalloffProp.floatValue, 0f, 1f);
                        }
                        
                        var edgeRoughnessProp = FindProperty("_SpecularAAEdgeRoughness", properties);
                        if (edgeRoughnessProp != null)
                        {
                            edgeRoughnessProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Edge Roughness", "Additional roughness at grazing angles"),
                                edgeRoughnessProp.floatValue, 0f, 1f);
                        }
                        
                        var distanceFalloffProp = FindProperty("_SpecularAACameraDistanceFalloff", properties);
                        if (distanceFalloffProp != null)
                        {
                            distanceFalloffProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Distance Falloff", "Distance at which surface becomes fully rough"),
                                distanceFalloffProp.floatValue, 1f, 100f);
                        }
                        
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUILayout.Space(5);
                
                var srcBlendProp = FindProperty("_SrcBlend", properties);
                if (srcBlendProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Source Blending", GUILayout.Width(EditorGUIUtility.labelWidth));
                    UnityEngine.Rendering.BlendMode currentSrc = (UnityEngine.Rendering.BlendMode)srcBlendProp.floatValue;
                    UnityEngine.Rendering.BlendMode newSrc = (UnityEngine.Rendering.BlendMode)EditorGUILayout.EnumPopup(currentSrc);
                    if (newSrc != currentSrc)
                    {
                        srcBlendProp.floatValue = (float)newSrc;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                var dstBlendProp = FindProperty("_DstBlend", properties);
                if (dstBlendProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Destination Blending", GUILayout.Width(EditorGUIUtility.labelWidth));
                    UnityEngine.Rendering.BlendMode currentDst = (UnityEngine.Rendering.BlendMode)dstBlendProp.floatValue;
                    UnityEngine.Rendering.BlendMode newDst = (UnityEngine.Rendering.BlendMode)EditorGUILayout.EnumPopup(currentDst);
                    if (newDst != currentDst)
                    {
                        dstBlendProp.floatValue = (float)newDst;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                var zWriteProp = FindProperty("_ZWrite", properties);
                if (zWriteProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("ZWrite", "Override depth buffer writing. Off for transparent, On for opaque"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    
                    // Create a custom enum for ZWrite (Off = 0, On = 1) EXACTLY like original
                    string[] zWriteOptions = { "Off", "On" };
                    int currentZWrite = (int)zWriteProp.floatValue;
                    int newZWrite = EditorGUILayout.Popup(currentZWrite, zWriteOptions);
                    if (newZWrite != currentZWrite)
                    {
                        zWriteProp.floatValue = newZWrite;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(5);
                // Render queue override (numeric). -1 = use the shader's default
                // queue. Lets you fine-tune draw order relative to other materials.
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    new GUIContent("Render Queue", "Override the render queue. -1 = use the shader's default."),
                    GUILayout.Width(EditorGUIUtility.labelWidth));
                int newQueue = EditorGUILayout.IntField(material.renderQueue);
                if (newQueue != material.renderQueue)
                    material.renderQueue = newQueue;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                DrawTextureArrays(materialEditor, properties, material);

                EditorGUILayout.Space(5);
                DrawAuxData(materialEditor, properties, material);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawPropertyGroups(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            // Base Properties (Standard)
            DrawBaseProperties(materialEditor, properties, material);
            
            // IOR Properties - AFTER base, BEFORE specular like original
            DrawIORProperties(materialEditor, properties, material);
            
            // Specular Extension Properties
            DrawSpecularExtensionProperties(materialEditor, properties, material);
            
            // Clearcoat Properties
            DrawClearcoatProperties(materialEditor, properties, material);
            
            // Iridescence Properties
            DrawIridescenceProperties(materialEditor, properties, material);
            
            // Sheen Properties
            DrawSheenProperties(materialEditor, properties, material);
            
            // Diffuse Transmission Properties
            DrawDiffuseTransmissionProperties(materialEditor, properties, material);
            
            // Lighting Properties
            DrawLightingProperties(materialEditor, properties, material);
            
            // Environment Properties
            DrawEnvironmentProperties(materialEditor, properties, material);

            // Screen-Space Reflections (Ultra/LOD0 only)
            DrawSSRProperties(materialEditor, properties, material);

            // Quality Tier (shader LOD)
            DrawQualityTier(materialEditor, properties, material);
        }

        private void DrawBaseProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            GUILayout.Label("Standard", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Base Color TEXTURE first, SEPARATE from color
            var baseColorTextureProp = FindTexProperty("_BaseColorTexture", properties);
            if (baseColorTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Base Color Texture (RGB=Color, A=Alpha)", "RGB: Albedo/diffuse color\nAlpha: Transparency/opacity"), baseColorTextureProp);
                if (baseColorTextureProp.textureValue != null)
                {
                    EditorGUI.indentLevel++;
                    materialEditor.TextureScaleOffsetProperty(baseColorTextureProp);
                    EditorGUI.indentLevel--;
                }
            }

            // Base Color COLOR property SEPARATE
            var baseColorProp = FindProperty("_Color", properties);
            if (baseColorProp != null)
            {
                materialEditor.ColorProperty(baseColorProp, "Base Color");
            }

            // ORM Texture
            var ormTextureProp = FindTexProperty("_ORMTexture", properties);
            if (ormTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("ORM Texture (R=Occlusion, G=Roughness, B=Metallic)", "Red: Ambient occlusion (0=occluded, 1=unoccluded)\nGreen: Roughness (0=glossy, 1=rough)\nBlue: Metallic (0=dielectric, 1=metal)"), ormTextureProp);
                if (ormTextureProp.textureValue != null)
                {
                    EditorGUI.indentLevel++;
                    materialEditor.TextureScaleOffsetProperty(ormTextureProp);
                    EditorGUI.indentLevel--;
                }
            }

            // Occlusion FACTOR not Strength
            var occlusionFactorProp = FindProperty("_OcclusionStrength", properties);
            if (occlusionFactorProp != null)
            {
                occlusionFactorProp.floatValue = EditorGUILayout.Slider("Occlusion Factor", occlusionFactorProp.floatValue, 0f, 1f);
            }

            // Roughness FACTOR
            var roughnessFactorProp = FindProperty("_Roughness", properties);
            if (roughnessFactorProp != null)
            {
                roughnessFactorProp.floatValue = EditorGUILayout.Slider("Roughness Factor", roughnessFactorProp.floatValue, 0f, 1f);
            }

            // Metallic FACTOR
            var metallicFactorProp = FindProperty("_Metallic", properties);
            if (metallicFactorProp != null)
            {
                metallicFactorProp.floatValue = EditorGUILayout.Slider("Metallic Factor", metallicFactorProp.floatValue, 0f, 1f);
            }

            var normalMapProp = FindTexProperty("_NormalTexture", properties);
            if (normalMapProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map (Tangent Space)", "Tangent-space normal map. Must be set to 'Normal map' type in texture import settings."), normalMapProp);

                if (normalMapProp.textureValue != null)
                {
                    EditorGUI.indentLevel++;
                    var normalScaleProp = FindProperty("_NormalScale", properties);
                    if (normalScaleProp != null)
                    {
                        normalScaleProp.floatValue = EditorGUILayout.Slider("Normal Scale", normalScaleProp.floatValue, 0f, 1f);
                    }
                    materialEditor.TextureScaleOffsetProperty(normalMapProp);
                    EditorGUI.indentLevel--;
                    CheckNormalMapImportSettings(normalMapProp.textureValue);
                }
            }

            // Emissive TEXTURE separate
            var emissiveTextureProp = FindTexProperty("_EmissiveTexture", properties);
            if (emissiveTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Emissive Texture (RGB=Glow Color)", "RGB: Emission color (multiplied by Emissive Factor)"), emissiveTextureProp);
                if (emissiveTextureProp.textureValue != null)
                {
                    EditorGUI.indentLevel++;
                    materialEditor.TextureScaleOffsetProperty(emissiveTextureProp);
                    EditorGUI.indentLevel--;
                }
            }

            // Emissive FACTOR color separate
            var emissiveFactorProp = FindProperty("_EmissiveFactor", properties);
            if (emissiveFactorProp != null)
            {
                materialEditor.ColorProperty(emissiveFactorProp, "Emissive Factor");
            }

            // Emission Strength with 0-10 range
            var emissionStrengthProp = FindProperty("_EmissionStrength", properties);
            if (emissionStrengthProp != null)
            {
                emissionStrengthProp.floatValue = EditorGUILayout.Slider("Emission Strength", emissionStrengthProp.floatValue, 0f, 10f);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawIORProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var iorProp = FindProperty("_IOR", properties);
            if (iorProp != null)
            {
                GUILayout.Label("IOR", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                
                iorProp.floatValue = EditorGUILayout.Slider(
                    new GUIContent("Index of Refraction", "Controls the strength of reflections at glancing angles"),
                    iorProp.floatValue, 1f, 3f);
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawClearcoatProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useClearcoatProp = FindProperty("_UseClearcoat", properties);
            if (useClearcoatProp == null) return;

            clearcoatFoldout = EditorGUILayout.Foldout(clearcoatFoldout, "Clearcoat", true);
            
            if (clearcoatFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enable Clearcoat", GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useClearcoat = useClearcoatProp.floatValue > 0.5f;
                bool newUseClearcoat = EditorGUILayout.Toggle(useClearcoat);
                if (newUseClearcoat != useClearcoat)
                {
                    useClearcoatProp.floatValue = newUseClearcoat ? 1.0f : 0.0f;
                }
                EditorGUILayout.EndHorizontal();
                
                if (newUseClearcoat)
                {
                    EditorGUI.indentLevel++;
                    
                    var clearcoatFactorProp = FindProperty("_ClearcoatFactor", properties);
                    if (clearcoatFactorProp != null)
                    {
                        clearcoatFactorProp.floatValue = EditorGUILayout.Slider("Clearcoat Factor", clearcoatFactorProp.floatValue, 0f, 1f);
                    }
                    
                    var clearcoatRoughnessProp = FindProperty("_ClearcoatRoughness", properties);
                    if (clearcoatRoughnessProp != null)
                    {
                        clearcoatRoughnessProp.floatValue = EditorGUILayout.Slider("Clearcoat Roughness", clearcoatRoughnessProp.floatValue, 0f, 1f);
                    }
                    
                    var clearcoatNormalTextureProp = FindTexProperty("_ClearcoatNormalTexture", properties);
                    if (clearcoatNormalTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat Normal (Tangent Space)", "Tangent-space normal map for clearcoat layer only"), clearcoatNormalTextureProp);
                        
                        if (clearcoatNormalTextureProp.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            var clearcoatNormalScaleProp = FindProperty("_ClearcoatNormalScale", properties);
                            if (clearcoatNormalScaleProp != null)
                            {
                                clearcoatNormalScaleProp.floatValue = EditorGUILayout.Slider("Clearcoat Normal Scale", clearcoatNormalScaleProp.floatValue, 0f, 1f);
                            }
                            materialEditor.TextureScaleOffsetProperty(clearcoatNormalTextureProp);
                            EditorGUI.indentLevel--;
                        }
                    }
                    
                    var clearcoatIridescenceTextureProp = FindTexProperty("_ClearcoatIridescenceTexture", properties);
                    if (clearcoatIridescenceTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat/Iridescence (R=Clear, G=Rough, B=Irid, A=Thick)", "Red: Clearcoat intensity (0=none, 1=full)\nGreen: Clearcoat roughness (0=glossy, 1=rough)\nBlue: Iridescence intensity (0=none, 1=full)\nAlpha: Iridescence thickness (0=min, 1=max)"), clearcoatIridescenceTextureProp);
                        if (clearcoatIridescenceTextureProp.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(clearcoatIridescenceTextureProp);
                            EditorGUI.indentLevel--;
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawIridescenceProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useIridescenceProp = FindProperty("_UseIridescence", properties);
            if (useIridescenceProp == null) return;

            iridescenceFoldout = EditorGUILayout.Foldout(iridescenceFoldout, "Iridescence", true);
            
            if (iridescenceFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enable Iridescence", GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useIridescence = useIridescenceProp.floatValue > 0.5f;
                bool newUseIridescence = EditorGUILayout.Toggle(useIridescence);
                if (newUseIridescence != useIridescence)
                {
                    useIridescenceProp.floatValue = newUseIridescence ? 1.0f : 0.0f;
                }
                EditorGUILayout.EndHorizontal();
                
                if (newUseIridescence)
                {
                    EditorGUI.indentLevel++;
                    
                    // Clearcoat/Iridescence Texture FIRST in iridescence too!
                    var clearcoatIridescenceTextureProp = FindTexProperty("_ClearcoatIridescenceTexture", properties);
                    if (clearcoatIridescenceTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat/Iridescence (R=Clear, G=Rough, B=Irid, A=Thick)", "Red: Clearcoat intensity (0=none, 1=full)\nGreen: Clearcoat roughness (0=glossy, 1=rough)\nBlue: Iridescence intensity (0=none, 1=full)\nAlpha: Iridescence thickness (0=min, 1=max)"), clearcoatIridescenceTextureProp);
                        if (clearcoatIridescenceTextureProp.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(clearcoatIridescenceTextureProp);
                            EditorGUI.indentLevel--;
                        }
                    }
                    
                    var iridescenceFactorProp = FindProperty("_IridescenceFactor", properties);
                    if (iridescenceFactorProp != null)
                    {
                        iridescenceFactorProp.floatValue = EditorGUILayout.Slider("Iridescence Factor", iridescenceFactorProp.floatValue, 0f, 1f);
                    }
                    
                    // IridescenceIOR not IridescenceIor!
                    var iridescenceIORProp = FindProperty("_IridescenceIOR", properties);
                    if (iridescenceIORProp != null)
                    {
                        iridescenceIORProp.floatValue = EditorGUILayout.Slider(
                            new GUIContent("Iridescence IOR", "Index of refraction for the thin film"),
                            iridescenceIORProp.floatValue, 1f, 3f);
                    }
                    
                    var iridescenceThicknessProp = FindProperty("_IridescenceThickness", properties);
                    if (iridescenceThicknessProp != null)
                    {
                        iridescenceThicknessProp.floatValue = EditorGUILayout.Slider(
                            new GUIContent("Thickness Factor", "Controls the overall thickness scaling"),
                            iridescenceThicknessProp.floatValue, 0f, 1f);
                    }
                    
                    var iridescenceThicknessMinProp = FindProperty("_IridescenceThicknessMin", properties);
                    if (iridescenceThicknessMinProp != null)
                    {
                        iridescenceThicknessMinProp.floatValue = EditorGUILayout.FloatField(
                            new GUIContent("Thickness Min (nm)", "Minimum thin film thickness in nanometers"),
                            iridescenceThicknessMinProp.floatValue);
                    }
                    
                    var iridescenceThicknessMaxProp = FindProperty("_IridescenceThicknessMax", properties);
                    if (iridescenceThicknessMaxProp != null)
                    {
                        iridescenceThicknessMaxProp.floatValue = EditorGUILayout.FloatField(
                            new GUIContent("Thickness Max (nm)", "Maximum thin film thickness in nanometers"),
                            iridescenceThicknessMaxProp.floatValue);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSheenProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useSheenProp = FindProperty("_UseSheen", properties);
            if (useSheenProp == null) return;

            sheenFoldout = EditorGUILayout.Foldout(sheenFoldout, "Sheen", true);
            
            if (sheenFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enable Sheen", GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useSheen = useSheenProp.floatValue > 0.5f;
                bool newUseSheen = EditorGUILayout.Toggle(useSheen);
                if (newUseSheen != useSheen)
                {
                    useSheenProp.floatValue = newUseSheen ? 1.0f : 0.0f;
                }
                EditorGUILayout.EndHorizontal();
                
                if (newUseSheen)
                {
                    EditorGUI.indentLevel++;
                    
                    var sheenTextureProp = FindTexProperty("_SheenTexture", properties);
                    if (sheenTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Sheen Texture (RGB=Color, A=Roughness)", "RGB: Sheen color tint\nAlpha: Sheen roughness (0=smooth, 1=rough)"), sheenTextureProp);
                        if (sheenTextureProp.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(sheenTextureProp);
                            EditorGUI.indentLevel--;
                        }
                    }
                    
                    var sheenColorProp = FindProperty("_SheenColor", properties);
                    if (sheenColorProp != null)
                    {
                        materialEditor.ColorProperty(sheenColorProp, "Sheen Color");
                    }
                    
                    var sheenFactorProp = FindProperty("_SheenFactor", properties);
                    if (sheenFactorProp != null)
                    {
                        sheenFactorProp.floatValue = EditorGUILayout.Slider("Sheen Factor", sheenFactorProp.floatValue, 0f, 1f);
                    }
                    
                    var sheenRoughnessProp = FindProperty("_SheenRoughness", properties);
                    if (sheenRoughnessProp != null)
                    {
                        sheenRoughnessProp.floatValue = EditorGUILayout.Slider("Sheen Roughness", sheenRoughnessProp.floatValue, 0.0001f, 1f);
                    }
                    
                    var sheenRimBoostProp = FindProperty("_SheenRimBoost", properties);
                    if (sheenRimBoostProp != null)
                    {
                        sheenRimBoostProp.floatValue = EditorGUILayout.Slider(
                            new GUIContent("Sheen Rim Boost", "Artistic enhancement for rim/backlight. 1 = glTF compliant (default), >1 = enhanced backscatter"),
                            sheenRimBoostProp.floatValue, 0f, 10f);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawDiffuseTransmissionProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            diffuseTransmissionFoldout = EditorGUILayout.Foldout(diffuseTransmissionFoldout, "Diffuse Transmission (Thin Surfaces)", true);
            
            if (diffuseTransmissionFoldout)
            {
                EditorGUI.indentLevel++;
                
                // Info box about double-sided requirement
                EditorGUILayout.HelpBox("Diffuse transmission simulates light passing through thin surfaces like leaves or paper. " +
                                      "For best results, set Cull Mode to 'Off' (double-sided) in Rendering Options.", MessageType.Info);
                
                var useDiffuseTransmissionProp = FindProperty("_UseDiffuseTransmission", properties);
                if (useDiffuseTransmissionProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Enable Diffuse Transmission", GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool useDiffuseTransmission = useDiffuseTransmissionProp.floatValue > 0.5f;
                    bool newUseDiffuseTransmission = EditorGUILayout.Toggle(useDiffuseTransmission);
                    if (newUseDiffuseTransmission != useDiffuseTransmission)
                    {
                        useDiffuseTransmissionProp.floatValue = newUseDiffuseTransmission ? 1.0f : 0.0f;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (useDiffuseTransmission)
                    {
                        EditorGUI.indentLevel++;
                        
                        var diffuseTransmissionFactorProp = FindProperty("_DiffuseTransmissionFactor", properties);
                        if (diffuseTransmissionFactorProp != null)
                        {
                            diffuseTransmissionFactorProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Transmission Factor", "Percentage of light that passes through the surface"),
                                diffuseTransmissionFactorProp.floatValue, 0f, 1f);
                        }
                        
                        var diffuseTransmissionTextureProp = FindTexProperty("_DiffuseTransmissionTexture", properties);
                        if (diffuseTransmissionTextureProp != null)
                        {
                            materialEditor.TexturePropertySingleLine(new GUIContent("Transmission Texture (RGB=Color, A=Amount)", "RGB: Color of transmitted light\nAlpha: Transmission amount (0=opaque, 1=fully transmissive)\nNote: Combines glTF diffuseTransmissionTexture and diffuseTransmissionColorTexture"), diffuseTransmissionTextureProp);
                            if (diffuseTransmissionTextureProp.textureValue != null)
                            {
                                EditorGUI.indentLevel++;
                                materialEditor.TextureScaleOffsetProperty(diffuseTransmissionTextureProp);
                                EditorGUI.indentLevel--;
                            }
                        }
                        
                        var diffuseTransmissionColorFactorProp = FindProperty("_DiffuseTransmissionColorFactor", properties);
                        if (diffuseTransmissionColorFactorProp != null)
                        {
                            materialEditor.ColorProperty(diffuseTransmissionColorFactorProp, "Transmission Color");
                        }
                        
                        
                        EditorGUI.indentLevel--;
                    }
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private bool GameObjectIsStaticOrLightmapped(MaterialEditor materialEditor)
        {
            foreach (var target in materialEditor.targets)
            {
                Material mat = target as Material;
                if (mat != null && (mat.globalIlluminationFlags & MaterialGlobalIlluminationFlags.BakedEmissive) != 0)
                    return true;
            }
            // Check if any selected renderer is lightmap static
            if (Selection.activeGameObject != null)
            {
                var renderer = Selection.activeGameObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    return GameObjectUtility.AreStaticEditorFlagsSet(Selection.activeGameObject, StaticEditorFlags.ContributeGI);
                }
            }
            return false;
        }

        private bool CheckLightVolumesInstalled()
        {
            if (lightVolumesInstalled == null)
            {
                lightVolumesInstalled = File.Exists(Path.Combine(Application.dataPath.Replace("/Assets", ""), LIGHT_VOLUMES_PATH));
            }
            return lightVolumesInstalled.Value;
        }
        
        private void DrawLightingProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            lightingFoldout = EditorGUILayout.Foldout(lightingFoldout, "Lighting", true);
            
            if (lightingFoldout)
            {
                EditorGUI.indentLevel++;
                
                // Check for VRC Light Volumes
                bool hasLightVolumes = CheckLightVolumesInstalled();
                
                // VRC Light Volumes toggle
                var useVRCLightVolumesProp = FindProperty("_UseVRCLightVolumes", properties);
                if (useVRCLightVolumesProp != null)
                {
                    if (!hasLightVolumes)
                    {
                        EditorGUILayout.HelpBox(
                            "VRC Light Volumes are not installed. The shader may fail to compile without them.\n" +
                            "Click the button below for installation instructions.",
                            MessageType.Error);
                        
                        if (GUILayout.Button("Open Installation Instructions"))
                        {
                            Application.OpenURL("https://github.com/REDSIM/VRCLightVolumes?tab=readme-ov-file#Installation-through-VRChat-Creator-Companion");
                        }
                        
                        EditorGUILayout.Space();
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("VRC Light Volumes detected and ready to use!", MessageType.Info);
                    }
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Use VRC Light Volumes", "Enable support for VRChat Light Volumes system"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    
                    bool useVRCLightVolumes = useVRCLightVolumesProp.floatValue > 0.5f;
                    bool newUseVRCLightVolumes = EditorGUILayout.Toggle(useVRCLightVolumes);
                    
                    if (newUseVRCLightVolumes != useVRCLightVolumes)
                    {
                        useVRCLightVolumesProp.floatValue = newUseVRCLightVolumes ? 1.0f : 0.0f;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.Space();
                }
                
                var vertexLightsProp = FindProperty("_VertexLights", properties);
                if (vertexLightsProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Vertex Lights", "Enable vertex light support for additional point lights"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool vertexLights = vertexLightsProp.floatValue > 0.5f;
                    bool newVertexLights = EditorGUILayout.Toggle(vertexLights);
                    if (newVertexLights != vertexLights)
                    {
                        vertexLightsProp.floatValue = newVertexLights ? 1.0f : 0.0f;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                var shDominantLightProp = FindProperty("_SHDominantLight", properties);
                if (shDominantLightProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("SH Dominant Light", "Extract dominant light direction from spherical harmonics"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool shDominant = shDominantLightProp.floatValue > 0.5f;
                    bool newSHDominant = EditorGUILayout.Toggle(shDominant);
                    if (newSHDominant != shDominant)
                    {
                        shDominantLightProp.floatValue = newSHDominant ? 1.0f : 0.0f;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // Light Intensity Multiplier
                var lightIntensityMultiplierProp = FindProperty("_LightIntensityMultiplier", properties);
                if (lightIntensityMultiplierProp != null)
                {
                    EditorGUILayout.Space();
                    lightIntensityMultiplierProp.floatValue = EditorGUILayout.Slider(
                        new GUIContent("Light Intensity Multiplier", "Global multiplier for all light sources"),
                        lightIntensityMultiplierProp.floatValue, 0.1f, 10f);
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawEnvironmentProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            environmentFoldout = EditorGUILayout.Foldout(environmentFoldout, "Environment / Fallback", true);
            
            if (environmentFoldout)
            {
                EditorGUI.indentLevel++;
                
                var useEnvironmentReflectionProp = FindProperty("_UseEnvironmentReflection", properties);
                if (useEnvironmentReflectionProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Environment Reflection", "Enable reflection probe sampling"),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool useEnvReflection = useEnvironmentReflectionProp.floatValue > 0.5f;
                    bool newUseEnvReflection = EditorGUILayout.Toggle(useEnvReflection);
                    if (newUseEnvReflection != useEnvReflection)
                    {
                        useEnvironmentReflectionProp.floatValue = newUseEnvReflection ? 1.0f : 0.0f;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (newUseEnvReflection)
                    {
                        EditorGUI.indentLevel++;
                        
                        var reflectionProbeThresholdProp = FindProperty("_ReflectionProbeThreshold", properties);
                        if (reflectionProbeThresholdProp != null)
                        {
                            reflectionProbeThresholdProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Reflection Probe Threshold", "Minimum probe intensity to use fallback"),
                                reflectionProbeThresholdProp.floatValue, 0f, 1f);
                        }
                        
                        var shThresholdProp = FindProperty("_SHThreshold", properties);
                        if (shThresholdProp != null)
                        {
                            shThresholdProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("SH Threshold", "SH intensity threshold to use fallback"),
                                shThresholdProp.floatValue, 0f, 1f);
                        }
                        
                        EditorGUI.indentLevel--;
                    }
                }
                
                // Lightmap Reflection Blend — only relevant for static/lightmapped objects
                if ((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.RealtimeEmissive) == 0
                    || material.globalIlluminationFlags == MaterialGlobalIlluminationFlags.BakedEmissive
                    || GameObjectIsStaticOrLightmapped(materialEditor))
                {
                    var lightmapReflectionBlendProp = FindProperty("_LightmapReflectionBlend", properties);
                    if (lightmapReflectionBlendProp != null)
                    {
                        lightmapReflectionBlendProp.floatValue = EditorGUILayout.Slider(
                            new GUIContent("Lightmap Reflection Blend",
                                "Blends indirect specular toward baked lightmap color. 0 = full reflections (spec-correct), 1 = fully tinted by lightmap. Only affects static/lightmapped objects."),
                            lightmapReflectionBlendProp.floatValue, 0f, 1f);
                    }
                }

                EditorGUILayout.Space();

                var fallbackCubemapProp = FindProperty("_FallbackCubemap", properties);
                if (fallbackCubemapProp != null)
                {
                    materialEditor.TexturePropertySingleLine(
                        new GUIContent("Fallback Cubemap (HDR Environment)", "HDR cubemap used when reflection probes are weak or missing. Provides ambient lighting and reflections."),
                        fallbackCubemapProp);
                    
                    if (fallbackCubemapProp.textureValue != null)
                    {
                        EditorGUI.indentLevel++;
                        
                        var fallbackIntensityProp = FindProperty("_FallbackIntensity", properties);
                        if (fallbackIntensityProp != null)
                        {
                            fallbackIntensityProp.floatValue = EditorGUILayout.Slider(
                                "Fallback Intensity", fallbackIntensityProp.floatValue, 0f, 2f);
                        }
                        
                        var fallbackDiffuseMipLevelProp = FindProperty("_FallbackDiffuseMipLevel", properties);
                        if (fallbackDiffuseMipLevelProp != null)
                        {
                            fallbackDiffuseMipLevelProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Diffuse Mip Level", "Mip level for diffuse lighting from fallback"),
                                fallbackDiffuseMipLevelProp.floatValue, 0f, 10f);
                        }
                        
                        var fallbackMaxMipLevelProp = FindProperty("_FallbackMaxMipLevel", properties);
                        if (fallbackMaxMipLevelProp != null)
                        {
                            fallbackMaxMipLevelProp.floatValue = EditorGUILayout.Slider(
                                new GUIContent("Max Mip Level", "Maximum mip level for fallback reflections"),
                                fallbackMaxMipLevelProp.floatValue, 0f, 10f);
                        }
                        
                        EditorGUI.indentLevel--;
                    }
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSpecularExtensionProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useSpecularExtensionProp = FindProperty("_UseSpecularExtension", properties);
            if (useSpecularExtensionProp == null) return;

            specularFoldout = EditorGUILayout.Foldout(specularFoldout, "Specular Extension", true);
            
            if (specularFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enable Specular", GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useSpecular = useSpecularExtensionProp.floatValue > 0.5f;
                bool newUseSpecular = EditorGUILayout.Toggle(useSpecular);
                if (newUseSpecular != useSpecular)
                {
                    useSpecularExtensionProp.floatValue = newUseSpecular ? 1.0f : 0.0f;
                }
                EditorGUILayout.EndHorizontal();
                
                if (newUseSpecular)
                {
                    EditorGUI.indentLevel++;
                    
                    var specularTextureProp = FindTexProperty("_SpecularTexture", properties);
                    if (specularTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Specular Texture (RGB=Color, A=Strength)", "RGB: Specular color tint (for dielectrics)\nAlpha: Specular strength multiplier"), specularTextureProp);
                        if (specularTextureProp.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(specularTextureProp);
                            EditorGUI.indentLevel--;
                        }
                    }
                    
                    var specularFactorProp = FindProperty("_SpecularFactor", properties);
                    if (specularFactorProp != null)
                    {
                        specularFactorProp.floatValue = EditorGUILayout.Slider("Specular Factor", specularFactorProp.floatValue, 0f, 1f);
                    }
                    
                    var specularColorProp = FindProperty("_SpecularColor", properties);
                    if (specularColorProp != null)
                    {
                        materialEditor.ColorProperty(specularColorProp, "Specular Color");
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSlider(MaterialProperty[] properties, string name, string label, float min, float max)
        {
            var p = FindProperty(name, properties);
            if (p != null)
                p.floatValue = EditorGUILayout.Slider(label, p.floatValue, min, max);
        }

        private void DrawAuxData(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useAuxProp = FindProperty("_UseAuxData", properties);
            if (useAuxProp == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Auxiliary Data Texture", "Packs scalar params not covered by the other textures (R: IOR, G: Iridescence IOR, B: Face Cull, A: Sheen Rim Boost) into one texture, so parameter variants can batch under a single material. Replaces those values while on (sheen factor is omitted — it equals scaling the sheen colour)."),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            bool useAux = useAuxProp.floatValue > 0.5f;
            bool newUseAux = EditorGUILayout.Toggle(useAux);
            if (newUseAux != useAux)
                useAuxProp.floatValue = newUseAux ? 1.0f : 0.0f;
            EditorGUILayout.EndHorizontal();

            if (newUseAux)
            {
                EditorGUI.indentLevel++;
                var tex = FindTexProperty("_AuxDataTexture", properties);
                if (tex != null)
                {
                    materialEditor.TexturePropertySingleLine(
                        new GUIContent("Aux Data (R:IOR G:IridIOR B:FaceCull A:SheenRim)",
                            "R: IOR (0-4). G: Iridescence IOR (0-4). B: Face Cull (0=off, 0.5=back, 1=front; requires Face Cull Source = Texture). A: Sheen Rim Boost (0-10)."), tex);
                    if (tex.textureValue != null)
                    {
                        EditorGUI.indentLevel++;
                        materialEditor.TextureScaleOffsetProperty(tex);
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUILayout.HelpBox(
                    "Channels: R=IOR, G=Iridescence IOR, B=Face Cull (0=off, 0.5=back, 1=front), A=Sheen Rim Boost — decoded " +
                    "per-vertex/instance slice when Texture Arrays are on. Iridescence and sheen channels only apply when those " +
                    "features are on; B only acts when Face Cull Source = Texture. Sheen factor is omitted (equals scaling the " +
                    "sheen colour).", MessageType.None);

                EditorGUILayout.LabelField("Decode Ranges (Min, Max)", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Must match the ranges the aux texture was baked with. The Aux Data Baker writes these for you. Keep Iridescence IOR Min ≥ 1 — a decoded value near 0 produces white iridescence artifacts.", MessageType.None);
                DrawRangeProp(properties, "_AuxIORMin", "_AuxIORMax", "IOR");
                DrawRangeProp(properties, "_AuxIridescenceIORMin", "_AuxIridescenceIORMax", "Iridescence IOR");
                DrawRangeProp(properties, "_AuxSheenRimMin", "_AuxSheenRimMax", "Sheen Rim");

                EditorGUI.indentLevel--;
            }

            // Second aux page: per-variant iridescence thickness range (independent toggle).
            var useThickProp = FindProperty("_UseAuxThickness", properties);
            if (useThickProp != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    new GUIContent("Aux Thickness Range", "Per-variant iridescence thin-film thickness range, packed into a second aux texture (R=min, G=max). The per-region position within the range still comes from the Clearcoat/Iridescence A channel. Only acts when Iridescence is on."),
                    GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useThick = useThickProp.floatValue > 0.5f;
                bool newUseThick = EditorGUILayout.Toggle(useThick);
                if (newUseThick != useThick)
                    useThickProp.floatValue = newUseThick ? 1.0f : 0.0f;
                EditorGUILayout.EndHorizontal();

                if (newUseThick)
                {
                    EditorGUI.indentLevel++;
                    var tex2 = FindTexProperty("_AuxDataTexture2", properties);
                    if (tex2 != null)
                    {
                        materialEditor.TexturePropertySingleLine(
                            new GUIContent("Aux Thickness (R:Min G:Max)",
                                "R: iridescence thickness min, G: thickness max — normalized into the storage range below (nm)."), tex2);
                        if (tex2.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(tex2);
                            EditorGUI.indentLevel--;
                        }
                    }
                    DrawRangeProp(properties, "_AuxThicknessRangeMin", "_AuxThicknessRangeMax", "Storage Range (nm)");
                    EditorGUILayout.HelpBox(
                        "Storage range (nm) the R/G channels normalize into — must match the bake. The per-region position " +
                        "within [min, max] still comes from the Clearcoat/Iridescence texture's A channel.", MessageType.None);
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawRangeProp(MaterialProperty[] properties, string minName, string maxName, string label)
        {
            var pmin = FindProperty(minName, properties);
            var pmax = FindProperty(maxName, properties);
            if (pmin == null || pmax == null) return;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            pmin.floatValue = EditorGUILayout.FloatField(pmin.floatValue);
            pmax.floatValue = EditorGUILayout.FloatField(pmax.floatValue);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureArrays(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useArraysProp = FindProperty("_UseTextureArrays", properties);
            if (useArraysProp == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Texture Arrays", "Switch all input textures to Texture2DArrays sampled by a per-vertex slice (UV3.x). The standard texture slots above bind to the array versions while this is on."),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            bool useArrays = useArraysProp.floatValue > 0.5f;
            bool newUseArrays = EditorGUILayout.Toggle(useArrays);
            if (newUseArrays != useArrays)
                useArraysProp.floatValue = newUseArrays ? 1.0f : 0.0f;
            EditorGUILayout.EndHorizontal();

            if (newUseArrays)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox(
                    "Input textures are now Texture2DArrays — the texture slots in the sections above accept array assets.",
                    MessageType.Info);

                // Index Source: where the array layer index comes from.
                var sourceProp = FindProperty("_ArrayIndexSource", properties);
                int src = 0;
                if (sourceProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Index Source", "Vertex: per-vertex layer from UV3.x (merged meshes). Material Instance: a per-material/per-instance slice value (also overridable via MaterialPropertyBlock for instanced batching)."),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    src = Mathf.RoundToInt(sourceProp.floatValue);
                    int newSrc = EditorGUILayout.Popup(src, new[] { "Vertex", "Material Instance" });
                    if (newSrc != src) { sourceProp.floatValue = newSrc; src = newSrc; }
                    EditorGUILayout.EndHorizontal();
                }

                if (src == 1)
                {
                    var sliceProp = FindProperty("_ArraySlice", properties);
                    if (sliceProp != null)
                    {
                        int maxSlice = 0;
                        var arr = FindProperty("_BaseColorTextureArray", properties);
                        if (arr != null && arr.textureValue is Texture2DArray ta) maxSlice = Mathf.Max(0, ta.depth - 1);
                        int v = Mathf.RoundToInt(sliceProp.floatValue);
                        v = (maxSlice > 0)
                            ? EditorGUILayout.IntSlider("Array Slice", Mathf.Clamp(v, 0, maxSlice), 0, maxSlice)
                            : EditorGUILayout.IntField("Array Slice", Mathf.Max(0, v));
                        sliceProp.floatValue = v;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Vertex source: write each vertex's slice index into UV channel 3 (.x). With no per-vertex data " +
                        "every vertex reads 0, so use Material Instance to preview other layers.", MessageType.None);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawSSRProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useSSRProp = FindProperty("_UseSSR", properties);
            if (useSSRProp == null) return;

            ssrFoldout = EditorGUILayout.Foldout(ssrFoldout, "Screen-Space Reflections (Ultra / LOD0 only)", true);
            if (!ssrFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Screen-space reflections (adapted from Mochie's shaders) only run on the Ultra tier (LOD 400). " +
                "They require the camera to render a depth texture — in VRChat that means a realtime shadow-casting " +
                "directional light, or a depth-enabled camera. Use the Render Queue field (Advanced Render Mode) to draw " +
                "reflective materials after the geometry they should reflect. SSR is skipped in mirrors and for rough surfaces.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Enable SSR", "Per-material toggle. Only effective on the Ultra (LOD 400) tier."),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            bool useSSR = useSSRProp.floatValue > 0.5f;
            bool newUseSSR = EditorGUILayout.Toggle(useSSR);
            if (newUseSSR != useSSR)
                useSSRProp.floatValue = newUseSSR ? 1.0f : 0.0f;
            EditorGUILayout.EndHorizontal();

            if (newUseSSR)
            {
                EditorGUI.indentLevel++;
                DrawSlider(properties, "_SSRStrength", "Strength", 0f, 1f);
                DrawSlider(properties, "_SSRMaxRoughness", "Max Roughness", 0f, 1f);
                DrawSlider(properties, "_SSRHeight", "Ray Height / Step", 0.001f, 2f);
                DrawSlider(properties, "_SSREdgeFade", "Edge Fade", 0f, 0.5f);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private static int LODToTierIndex(int lod)
        {
            if (lod >= 400) return 0;
            if (lod >= 300) return 1;
            if (lod >= 200) return 2;
            return 3;
        }

        private void DrawQualityTier(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            qualityTierFoldout = EditorGUILayout.Foldout(qualityTierFoldout, "Quality Tier (Shader LOD)", true);
            if (!qualityTierFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Quality tiers use Unity shader LOD. This is GLOBAL per shader asset — changing it affects ALL materials " +
                "using GzPBR, not just this one. Treat it as an authoring/preview control; for builds, drive " +
                "Shader.globalMaximumLOD or shader.maximumLOD from script.\n\n" +
                "Ultra (400): exact iridescence/sheen, clearcoat\n" +
                "High (300): approx iridescence/sheen, clearcoat\n" +
                "Medium (200): approx iridescence/sheen, no clearcoat\n" +
                "Low (100): no iridescence / sheen / clearcoat",
                MessageType.Info);

            Shader shader = material.shader;
            if (shader == null) { EditorGUI.indentLevel--; return; }

            int tierIndex = LODToTierIndex(shader.maximumLOD);
            string[] tierNames = { "Ultra (400)", "High (300)", "Medium (200)", "Low (100)" };
            int newTierIndex = EditorGUILayout.Popup(
                new GUIContent("Max Tier (global)", "Sets shader.maximumLOD for ALL GzPBR materials in this project session."),
                tierIndex, tierNames);
            if (newTierIndex != tierIndex)
            {
                int[] lodValues = { 400, 300, 200, 100 };
                shader.maximumLOD = lodValues[newTierIndex];
                EditorUtility.SetDirty(shader);
            }

            EditorGUI.indentLevel--;
        }

        private void SetupRenderMode(Material material, RenderMode mode)
        {
            // Clear all render mode keywords first
            material.DisableKeyword("_RENDERMODE_OPAQUE");
            material.DisableKeyword("_RENDERMODE_CUTOUT");
            material.DisableKeyword("_RENDERMODE_TRANSPARENT");
            material.DisableKeyword("_RENDERMODE_PREMULTIPLIEDALPHA");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            
            switch (mode)
            {
                case RenderMode.Opaque:
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.EnableKeyword("_RENDERMODE_OPAQUE");
                    material.renderQueue = -1;
                    break;

                case RenderMode.Transparent:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_RENDERMODE_TRANSPARENT");
                    material.renderQueue = 3000;
                    break;

                case RenderMode.Cutout:
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_RENDERMODE_CUTOUT");
                    material.renderQueue = 2450;
                    break;

                case RenderMode.Additive:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_RENDERMODE_TRANSPARENT");
                    material.renderQueue = 3000;
                    break;

                case RenderMode.Multiply:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.DstColor);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.EnableKeyword("_RENDERMODE_TRANSPARENT");
                    material.renderQueue = 3000;
                    break;

                case RenderMode.PremultipliedAlpha:
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.EnableKeyword("_RENDERMODE_PREMULTIPLIEDALPHA");
                    material.renderQueue = 3000;
                    break;
            }

            material.SetFloat("_RenderMode", (float)mode);
        }

        private void UpdateMaterialKeywords(Material material, MaterialProperty[] properties)
        {
            if (material == null || properties == null) return;

            // Update render mode keywords
            var renderModeProp = FindProperty("_RenderMode", properties);
            if (renderModeProp != null)
            {
                RenderMode renderMode = (RenderMode)renderModeProp.floatValue;
                
                // Clear all render mode keywords first
                material.DisableKeyword("_RENDERMODE_OPAQUE");
                material.DisableKeyword("_RENDERMODE_CUTOUT");
                material.DisableKeyword("_RENDERMODE_TRANSPARENT");
                material.DisableKeyword("_RENDERMODE_PREMULTIPLIEDALPHA");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                
                // Set the appropriate render mode keyword
                switch (renderMode)
                {
                    case RenderMode.Opaque:
                        material.EnableKeyword("_RENDERMODE_OPAQUE");
                        break;
                    case RenderMode.Cutout:
                        material.EnableKeyword("_RENDERMODE_CUTOUT");
                        material.EnableKeyword("_ALPHATEST_ON");
                        break;
                    case RenderMode.Transparent:
                    case RenderMode.Additive:
                    case RenderMode.Multiply:
                        material.EnableKeyword("_RENDERMODE_TRANSPARENT");
                        material.EnableKeyword("_ALPHABLEND_ON");
                        break;
                    case RenderMode.PremultipliedAlpha:
                        material.EnableKeyword("_RENDERMODE_PREMULTIPLIEDALPHA");
                        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        break;
                }
            }

            // Update texture keywords
            UpdateTextureKeyword(material, properties, "_BaseColorTexture", "USE_BASE_COLOR_TEXTURE");
            UpdateTextureKeyword(material, properties, "_ORMTexture", "USE_ORM_TEXTURE");
            UpdateTextureKeyword(material, properties, "_NormalTexture", "USE_NORMAL_TEXTURE");
            UpdateTextureKeyword(material, properties, "_EmissiveTexture", "USE_EMISSIVE_TEXTURE");
            UpdateTextureKeyword(material, properties, "_SpecularTexture", "USE_SPECULAR_TEXTURE");
            UpdateTextureKeyword(material, properties, "_ClearcoatNormalTexture", "USE_CLEARCOAT_NORMAL_TEXTURE");
            UpdateTextureKeyword(material, properties, "_ClearcoatIridescenceTexture", "USE_CLEARCOAT_IRIDESCENCE_TEXTURE");
            UpdateTextureKeyword(material, properties, "_SheenTexture", "USE_SHEEN_TEXTURE");
            UpdateTextureKeyword(material, properties, "_DiffuseTransmissionTexture", "USE_DIFFUSE_TRANSMISSION_TEXTURE");
            UpdateTextureKeyword(material, properties, "_DiffuseTransmissionColorTexture", "USE_DIFFUSE_TRANSMISSION_COLOR_TEXTURE");

            // Update feature keywords
            UpdateFeatureKeyword(material, properties, "_UseClearcoat", "USE_CLEARCOAT");
            UpdateFeatureKeyword(material, properties, "_UseIridescence", "USE_IRIDESCENCE");
            UpdateFeatureKeyword(material, properties, "_UseSheen", "USE_SHEEN");
            UpdateFeatureKeyword(material, properties, "_UseSpecularExtension", "USE_SPECULAR_EXTENSION");
            UpdateFeatureKeyword(material, properties, "_UseDiffuseTransmission", "USE_DIFFUSE_TRANSMISSION");
            UpdateFeatureKeyword(material, properties, "_UseEnvironmentReflection", "USE_ENVIRONMENT_REFLECTION");
            UpdateFeatureKeyword(material, properties, "_UseSpecularAntialiasing", "USE_SPECULAR_ANTIALIASING");
            UpdateFeatureKeyword(material, properties, "_UseVRCLightVolumes", "USE_VRC_LIGHT_VOLUMES");
            UpdateFeatureKeyword(material, properties, "_UseSSR", "USE_SSR");
            UpdateFeatureKeyword(material, properties, "_UseTextureArrays", "USE_TEXTURE_ARRAYS");
            UpdateFeatureKeyword(material, properties, "_ArrayIndexSource", "_ARRAYINDEXSOURCE_MATERIALINSTANCE");
            UpdateFeatureKeywordInverse(material, properties, "_ArrayIndexSource", "_ARRAYINDEXSOURCE_VERTEX");
            UpdateFeatureKeyword(material, properties, "_UseAuxData", "USE_AUX_DATA");
            UpdateFeatureKeyword(material, properties, "_UseAuxThickness", "USE_AUX_THICKNESS");
            UpdateFeatureKeyword(material, properties, "_FaceCullSource", "_FACECULLSOURCE_TEXTURE");
            UpdateFeatureKeywordInverse(material, properties, "_FaceCullSource", "_FACECULLSOURCE_HARDWARE");
            
            // Lighting keywords
            UpdateFeatureKeyword(material, properties, "_VertexLights", "VERTEXLIGHT_ON");
            UpdateFeatureKeyword(material, properties, "_VertexLights", "_VERTEXLIGHTS_ON");
            UpdateFeatureKeywordInverse(material, properties, "_VertexLights", "_VERTEXLIGHTS_OFF");
            
            UpdateFeatureKeyword(material, properties, "_SHDominantLight", "_SHDOMINANTLIGHT_ON");
            UpdateFeatureKeywordInverse(material, properties, "_SHDominantLight", "_SHDOMINANTLIGHT_OFF");
        }

        private void UpdateTextureKeyword(Material material, MaterialProperty[] properties, string propertyName, string keyword)
        {
            // When texture arrays are on, the assignment lives on the array variant,
            // so detect "has texture" from whichever slot is currently active.
            var prop = FindTexProperty(propertyName, properties);
            if (prop != null && prop.type == MaterialProperty.PropType.Texture)
            {
                // Check if there's a corresponding toggle property
                string toggleName = propertyName.Replace("Texture", "").Insert(0, "_Use") + "Texture";
                var toggleProp = FindProperty(toggleName, properties);
                
                bool shouldEnable = prop.textureValue != null;
                
                // If there's a toggle, also check its value
                if (toggleProp != null && toggleProp.type == MaterialProperty.PropType.Float)
                {
                    shouldEnable = shouldEnable && (toggleProp.floatValue > 0);
                }
                
                if (shouldEnable)
                    material.EnableKeyword(keyword);
                else
                    material.DisableKeyword(keyword);
            }
        }

        private void UpdateFeatureKeyword(Material material, MaterialProperty[] properties, string propertyName, string keyword)
        {
            var prop = FindProperty(propertyName, properties);
            if (prop != null)
            {
                if (prop.floatValue > 0.5f)
                    material.EnableKeyword(keyword);
                else
                    material.DisableKeyword(keyword);
            }
        }

        private void UpdateFeatureKeywordInverse(Material material, MaterialProperty[] properties, string propertyName, string keyword)
        {
            var prop = FindProperty(propertyName, properties);
            if (prop != null)
            {
                if (prop.floatValue > 0.5f)
                    material.DisableKeyword(keyword);
                else
                    material.EnableKeyword(keyword);
            }
        }

        private new MaterialProperty FindProperty(string name, MaterialProperty[] properties, bool mandatory = false)
        {
            foreach (var prop in properties)
            {
                if (prop.name == name)
                    return prop;
            }

            if (mandatory)
                throw new System.Exception($"Material property {name} not found");

            return null;
        }

        // Returns the array-variant texture property (name + "Array") when texture
        // arrays are enabled, otherwise the standard 2D property. Lets the normal
        // texture slots bind to the Texture2DArray in place when arrays are on.
        private MaterialProperty FindTexProperty(string name2D, MaterialProperty[] properties)
        {
            var useArrays = FindProperty("_UseTextureArrays", properties);
            bool arrays = useArrays != null && useArrays.floatValue > 0.5f;
            return FindProperty(arrays ? name2D + "Array" : name2D, properties);
        }

        private void CheckNormalMapImportSettings(Texture texture)
        {
            if (texture == null) return;

            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                EditorGUILayout.HelpBox("Normal map texture is not set to 'Normal map' type. This may cause incorrect rendering.", MessageType.Warning);
                if (GUILayout.Button("Fix Normal Map Settings"))
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
            }
        }
    }
}