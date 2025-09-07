/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * 
 * Copyright (c) 2025 Geenz
 */

using UnityEngine;
using UnityEditor;
using System;

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
        private static bool depthFadeFoldout = false;
        private static bool specularFoldout = false;

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

                var cullProp = FindProperty("_Cull", properties);
                if (cullProp != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Culling", GUILayout.Width(EditorGUIUtility.labelWidth));
                    CullMode currentCull = (CullMode)cullProp.floatValue;
                    CullMode newCull = (CullMode)EditorGUILayout.EnumPopup(currentCull);
                    if (newCull != currentCull)
                    {
                        cullProp.floatValue = (float)newCull;
                    }
                    EditorGUILayout.EndHorizontal();
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
            
            // Depth Fade Properties
            DrawDepthFadeProperties(materialEditor, properties, material);
        }

        private void DrawBaseProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            GUILayout.Label("Standard", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Base Color TEXTURE first, SEPARATE from color
            var baseColorTextureProp = FindProperty("_BaseColorTexture", properties);
            if (baseColorTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Base Color Texture (RGB=Color, A=Alpha)", "RGB: Albedo/diffuse color\nAlpha: Transparency/opacity"), baseColorTextureProp);
            }

            // Base Color COLOR property SEPARATE
            var baseColorProp = FindProperty("_Color", properties);
            if (baseColorProp != null)
            {
                materialEditor.ColorProperty(baseColorProp, "Base Color");
            }

            // ORM Texture
            var ormTextureProp = FindProperty("_ORMTexture", properties);
            if (ormTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("ORM Texture (R=Occlusion, G=Roughness, B=Metallic)", "Red: Ambient occlusion (0=occluded, 1=unoccluded)\nGreen: Roughness (0=glossy, 1=rough)\nBlue: Metallic (0=dielectric, 1=metal)"), ormTextureProp);
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

            var normalMapProp = FindProperty("_NormalTexture", properties);
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
                    EditorGUI.indentLevel--;
                    CheckNormalMapImportSettings(normalMapProp.textureValue);
                }
            }

            // Emissive TEXTURE separate
            var emissiveTextureProp = FindProperty("_EmissiveTexture", properties);
            if (emissiveTextureProp != null)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Emissive Texture (RGB=Glow Color)", "RGB: Emission color (multiplied by Emissive Factor)"), emissiveTextureProp);
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
                    
                    var clearcoatNormalTextureProp = FindProperty("_ClearcoatNormalTexture", properties);
                    if (clearcoatNormalTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat Normal (Tangent Space)", "Tangent-space normal map for clearcoat layer only"), clearcoatNormalTextureProp);
                        
                        if (clearcoatNormalTextureProp.textureValue != null)
                        {
                            var clearcoatNormalScaleProp = FindProperty("_ClearcoatNormalScale", properties);
                            if (clearcoatNormalScaleProp != null)
                            {
                                EditorGUI.indentLevel++;
                                clearcoatNormalScaleProp.floatValue = EditorGUILayout.Slider("Clearcoat Normal Scale", clearcoatNormalScaleProp.floatValue, 0f, 1f);
                                EditorGUI.indentLevel--;
                            }
                        }
                    }
                    
                    var clearcoatIridescenceTextureProp = FindProperty("_ClearcoatIridescenceTexture", properties);
                    if (clearcoatIridescenceTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat/Iridescence (R=Clear, G=Rough, B=Irid, A=Thick)", "Red: Clearcoat intensity (0=none, 1=full)\nGreen: Clearcoat roughness (0=glossy, 1=rough)\nBlue: Iridescence intensity (0=none, 1=full)\nAlpha: Iridescence thickness (0=min, 1=max)"), clearcoatIridescenceTextureProp);
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
                    var clearcoatIridescenceTextureProp = FindProperty("_ClearcoatIridescenceTexture", properties);
                    if (clearcoatIridescenceTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Clearcoat/Iridescence (R=Clear, G=Rough, B=Irid, A=Thick)", "Red: Clearcoat intensity (0=none, 1=full)\nGreen: Clearcoat roughness (0=glossy, 1=rough)\nBlue: Iridescence intensity (0=none, 1=full)\nAlpha: Iridescence thickness (0=min, 1=max)"), clearcoatIridescenceTextureProp);
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
                    
                    var sheenTextureProp = FindProperty("_SheenTexture", properties);
                    if (sheenTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Sheen Texture (RGB=Color, A=Roughness)", "RGB: Sheen color tint\nAlpha: Sheen roughness (0=smooth, 1=rough)"), sheenTextureProp);
                    }
                    
                    var sheenColorProp = FindProperty("_SheenColor", properties);
                    if (sheenColorProp != null)
                    {
                        materialEditor.ColorProperty(sheenColorProp, "Sheen Color");
                    }
                    
                    var sheenRoughnessProp = FindProperty("_SheenRoughness", properties);
                    if (sheenRoughnessProp != null)
                    {
                        sheenRoughnessProp.floatValue = EditorGUILayout.Slider("Sheen Roughness", sheenRoughnessProp.floatValue, 0f, 1f);
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
                        
                        var diffuseTransmissionTextureProp = FindProperty("_DiffuseTransmissionTexture", properties);
                        if (diffuseTransmissionTextureProp != null)
                        {
                            materialEditor.TexturePropertySingleLine(new GUIContent("Transmission Texture (RGB=Color, A=Amount)", "RGB: Color of transmitted light\nAlpha: Transmission amount (0=opaque, 1=fully transmissive)\nNote: Combines glTF diffuseTransmissionTexture and diffuseTransmissionColorTexture"), diffuseTransmissionTextureProp);
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

        private void DrawLightingProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            lightingFoldout = EditorGUILayout.Foldout(lightingFoldout, "Lighting", true);
            
            if (lightingFoldout)
            {
                EditorGUI.indentLevel++;
                
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

        private void DrawDepthFadeProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            // Only show depth fade for non-opaque render modes
            var renderModeProp = FindProperty("_RenderMode", properties);
            if (renderModeProp == null) return;
            
            RenderMode currentMode = (RenderMode)renderModeProp.floatValue;
            if (currentMode == RenderMode.Opaque || currentMode == RenderMode.Cutout) return;
            
            var useDepthFadeProp = FindProperty("_UseDepthFade", properties);
            if (useDepthFadeProp == null) return;

            depthFadeFoldout = EditorGUILayout.Foldout(depthFadeFoldout, "Depth Fade", true);
            
            if (depthFadeFoldout)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Enable Depth Fade", GUILayout.Width(EditorGUIUtility.labelWidth));
                bool useDepthFade = useDepthFadeProp.floatValue > 0.5f;
                bool newUseDepthFade = EditorGUILayout.Toggle(useDepthFade);
                if (newUseDepthFade != useDepthFade)
                {
                    useDepthFadeProp.floatValue = newUseDepthFade ? 1.0f : 0.0f;
                }
                EditorGUILayout.EndHorizontal();
                
                if (newUseDepthFade)
                {
                    EditorGUI.indentLevel++;
                    
                    var depthFadeProp = FindProperty("_DepthFade", properties);
                    if (depthFadeProp != null)
                    {
                        depthFadeProp.floatValue = EditorGUILayout.FloatField("Depth Fade Distance", depthFadeProp.floatValue);
                    }
                    
                    var depthFadePowerProp = FindProperty("_DepthFadePower", properties);
                    if (depthFadePowerProp != null)
                    {
                        depthFadePowerProp.floatValue = EditorGUILayout.Slider("Depth Fade Power", depthFadePowerProp.floatValue, 0.1f, 10f);
                    }
                    
                    var depthFadeOffsetProp = FindProperty("_DepthFadeOffset", properties);
                    if (depthFadeOffsetProp != null)
                    {
                        depthFadeOffsetProp.floatValue = EditorGUILayout.FloatField("Depth Fade Offset", depthFadeOffsetProp.floatValue);
                    }
                    
                    var debugDepthFadeProp = FindProperty("_DebugDepthFade", properties);
                    if (debugDepthFadeProp != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Debug Depth Fade", GUILayout.Width(EditorGUIUtility.labelWidth));
                        bool debugDepthFade = debugDepthFadeProp.floatValue > 0.5f;
                        bool newDebugDepthFade = EditorGUILayout.Toggle(debugDepthFade);
                        if (newDebugDepthFade != debugDepthFade)
                        {
                            debugDepthFadeProp.floatValue = newDebugDepthFade ? 1.0f : 0.0f;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    
                    EditorGUI.indentLevel--;
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
                    
                    var specularTextureProp = FindProperty("_SpecularTexture", properties);
                    if (specularTextureProp != null)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Specular Texture (RGB=Color, A=Strength)", "RGB: Specular color tint (for dielectrics)\nAlpha: Specular strength multiplier"), specularTextureProp);
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
            
            // Lighting keywords
            UpdateFeatureKeyword(material, properties, "_VertexLights", "VERTEXLIGHT_ON");
            UpdateFeatureKeyword(material, properties, "_VertexLights", "_VERTEXLIGHTS_ON");
            UpdateFeatureKeywordInverse(material, properties, "_VertexLights", "_VERTEXLIGHTS_OFF");
            
            UpdateFeatureKeyword(material, properties, "_SHDominantLight", "_SHDOMINANTLIGHT_ON");
            UpdateFeatureKeywordInverse(material, properties, "_SHDominantLight", "_SHDOMINANTLIGHT_OFF");
            
            // Debug keywords
            UpdateFeatureKeyword(material, properties, "_DebugDepthFade", "DEBUG_DEPTH_FADE");
        }

        private void UpdateTextureKeyword(Material material, MaterialProperty[] properties, string propertyName, string keyword)
        {
            var prop = FindProperty(propertyName, properties);
            if (prop != null && prop.type == MaterialProperty.PropType.Texture)
            {
                if (prop.textureValue != null)
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