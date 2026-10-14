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
using System.Collections.Generic;

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
        private static bool softDepthFoldout = false;
        private static bool ssShadowFoldout = false;
        private static bool qualityTierFoldout = false;
        private static bool complexityFoldout = false;
        private static int mapConverterIndex = 0;
        private static Material mapConverterMaterial = null;
        private static string mapConverterPreviousShader = null;

        // Light Volumes detection
        private static bool? lightVolumesInstalled = null;
        private const string LIGHT_VOLUMES_PATH = "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc";
        private static bool emissionWaveFoldout = false;
        private static bool? audioLinkInstalled = null;
        private const string AUDIOLINK_PATH = "Packages/com.llealloo.audiolink/Runtime/Shaders/AudioLink.cginc";
        private static bool? ltcgiInstalled = null;
        private const string LTCGI_PATH = "Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc";

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
            
            DrawComplexitySummary(materialEditor, material);

            EditorGUILayout.Space();

            DrawAdvancedRenderMode(materialEditor, properties, material);
            
            EditorGUILayout.Space();
            
            DrawPropertyGroups(materialEditor, properties, material);

            if (EditorGUI.EndChangeCheck())
            {
                UpdateMaterialKeywords(material, properties);
            }
        }

        private void DrawComplexitySummary(MaterialEditor materialEditor, Material material) {
            List<GzComplexityEstimate.Row> rows = GzComplexityEstimate.GetRows(material, materialEditor);
            int rowIndex = 0;

            EditorGUILayout.LabelField("Complexity", EditorStyles.boldLabel);

            while (rowIndex < rows.Count) {
                int subshaderIndex = rows[rowIndex].SubshaderIndex;
                string tierLabel = rows[rowIndex].TierLabel;
                bool measuring = false;
                int vertexAlu = -1;
                int fragmentAlu = -1;
                int samplers = -1;
                int questUnits = -1;
                bool questFailed = false;

                while (rowIndex < rows.Count && rows[rowIndex].SubshaderIndex == subshaderIndex) {
                    GzComplexityEstimate.Row row = rows[rowIndex];
                    rowIndex++;

                    if (!row.Measured) {
                        measuring = true;
                    }

                    if (row.PcFailure == null) {
                        if (row.PassName == "ForwardBase") {
                            vertexAlu = Math.Max(vertexAlu, row.VertexAlu);
                            fragmentAlu = Math.Max(fragmentAlu, row.FragmentAlu);
                        }

                        samplers = Math.Max(samplers, row.FragmentSamplers);
                    }

                    questUnits = Math.Max(questUnits, row.QuestTextureUnits);

                    if (row.QuestFailure != null) {
                        questFailed = true;
                    }
                }

                string tierText = tierLabel + ": measuring...";
                if (!measuring) {
                    string questText = "Quest compile error";
                    if (!questFailed) {
                        questText = "Quest ALU ~PS " + GzComplexityEstimate.FormatCount(fragmentAlu)
                            + ", " + GzComplexityEstimate.FormatLimit(questUnits) + " texture units";
                    }

                    tierText = tierLabel + ": PC ALU VS " + GzComplexityEstimate.FormatCount(vertexAlu) + " / PS " + GzComplexityEstimate.FormatCount(fragmentAlu)
                        + ", " + GzComplexityEstimate.FormatLimit(samplers) + " samplers"
                        + " | " + questText;
                }

                EditorGUILayout.LabelField(tierText, EditorStyles.wordWrappedLabel);
            }

            foreach (string issue in GzComplexityEstimate.DescribeIssues(rows, material)) {
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }

            complexityFoldout = EditorGUILayout.Foldout(complexityFoldout, "Complexity Breakdown", true);

            if (complexityFoldout) {
                EditorGUI.indentLevel++;

                foreach (GzComplexityEstimate.Row row in rows) {
                    EditorGUILayout.LabelField(GzComplexityEstimate.DescribeRow(row), EditorStyles.wordWrappedLabel);
                }

                EditorGUI.indentLevel--;
            }
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            if (oldShader != null) {
                material.SetOverrideTag("GzPreviousShader", oldShader.name);
                material.SetOverrideTag(GzSourceMaterial.SourceColorTag, "");
                material.SetOverrideTag(GzSourceMaterial.SourceMetallicTag, "");
                IMapConverter converter = GzMapConversion.Find(oldShader.name);

                if (converter != null) {
                    ApplyMapConversion(material, converter);
                }
            }

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

        private void ApplyMapConversion(Material material, IMapConverter converter) {
            GzMapConversionResult result;
            string outputFolder = GzMapConversion.OutputFolder(material);

            using (GzSourceMaterial source = new GzSourceMaterial(material)) {
                result = converter.Convert(source, material, outputFolder);
            }

            if (result.HasRenderMode) {
                SetupRenderMode(material, result.RenderMode);
            }

            if (result.Skipped.Count > 0) {
                Debug.LogWarning("[GzPBR] " + material.name + ": not converted (no GzPBR slot): " + string.Join(", ", result.Skipped));
            }

            foreach (string warning in result.Warnings) {
                Debug.LogWarning("[GzPBR] " + material.name + ": " + warning);
            }
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
                        // _Cull is forced Off in Texture mode, so _FaceCullBake carries the cull intent.
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
                        "Culling is per-variant from the aux texture's B channel (0=Off, 0.5=Back, 1=Front), read directly - the " +
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

                var useDitheredShadowsProp = FindProperty("_UseDitheredShadows", properties);

                if (useDitheredShadowsProp != null) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Dithered Shadows", "Transparent and Premultiplied modes cast shadows dithered by opacity."),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool useDitheredShadows = useDitheredShadowsProp.floatValue > 0;
                    bool newUseDitheredShadows = EditorGUILayout.Toggle(useDitheredShadows);

                    if (newUseDitheredShadows != useDitheredShadows) {
                        float ditheredShadowsValue = 0f;
                        if (newUseDitheredShadows) {
                            ditheredShadowsValue = 1f;
                        }

                        useDitheredShadowsProp.floatValue = ditheredShadowsValue;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                var useSharedSamplerProp = FindProperty("_UseSharedSampler", properties);

                if (useSharedSamplerProp != null) {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(
                        new GUIContent("Shared Texture Sampler", "Material textures use the first enabled texture's wrap and filter settings (Base Color, then ORM, then Normal). Frees samplers on PC."),
                        GUILayout.Width(EditorGUIUtility.labelWidth));
                    bool useSharedSampler = useSharedSamplerProp.floatValue > 0;
                    bool newUseSharedSampler = EditorGUILayout.Toggle(useSharedSampler);

                    if (newUseSharedSampler != useSharedSampler) {
                        float sharedSamplerValue = 0f;
                        if (newUseSharedSampler) {
                            sharedSamplerValue = 1f;
                        }

                        useSharedSamplerProp.floatValue = sharedSamplerValue;
                    }

                    EditorGUILayout.EndHorizontal();
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
                materialEditor.EnableInstancingField();
                materialEditor.DoubleSidedGIField();
                materialEditor.LightmapEmissionFlagsProperty(0, true, true);

                EditorGUILayout.Space(5);
                DrawTextureArrays(materialEditor, properties, material);

                EditorGUILayout.Space(5);
                DrawAuxData(materialEditor, properties, material);

                EditorGUILayout.Space(5);
                DrawMapConversion(material);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawMapConversion(Material material) {
            string previousShader = material.GetTag("GzPreviousShader", false);

            if (mapConverterMaterial != material || mapConverterPreviousShader != previousShader) {
                mapConverterMaterial = material;
                mapConverterPreviousShader = previousShader;
                mapConverterIndex = 0;

                for (int index = 0; index < GzMapConversion.Converters.Count; index++) {
                    if (GzMapConversion.Converters[index].Handles(previousShader)) {
                        mapConverterIndex = index;
                    }
                }
            }

            string[] converterNames = new string[GzMapConversion.Converters.Count];

            for (int index = 0; index < converterNames.Length; index++) {
                converterNames[index] = GzMapConversion.Converters[index].DisplayName;
            }

            mapConverterIndex = EditorGUILayout.Popup(
                new GUIContent("Convert Maps From", "Source shader whose maps are repacked into GzPBR slots. Source maps are never modified; new maps are written to Assets/GeenzShade/Converted."),
                mapConverterIndex, converterNames);

            if (GUILayout.Button("Convert Maps")) {
                Undo.RecordObject(material, "Convert Maps");
                ApplyMapConversion(material, GzMapConversion.Converters[mapConverterIndex]);
                ValidateMaterial(material);
                GUIUtility.ExitGUI();
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
            DrawSoftDepthProperties(materialEditor, properties, material);
            DrawScreenSpaceShadowProperties(materialEditor, properties, material);

            // Emission wave (AudioLink)
            DrawEmissionWaveProperties(materialEditor, properties, material);

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

        private bool CheckAudioLinkInstalled()
        {
            if (audioLinkInstalled == null)
            {
                audioLinkInstalled = File.Exists(Path.Combine(Application.dataPath.Replace("/Assets", ""), AUDIOLINK_PATH));
            }
            return audioLinkInstalled.Value;
        }

        private bool CheckLTCGIInstalled() {
            if (ltcgiInstalled == null) {
                ltcgiInstalled = File.Exists(Path.Combine(Application.dataPath.Replace("/Assets", ""), LTCGI_PATH));
            }

            return ltcgiInstalled.Value;
        }

        /// <summary>Forces the mode Off without AudioLink, whose #include cannot resolve.</summary>
        private void DrawEmissionWaveProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var modeProp = FindProperty("_EmissionWave", properties);
            if (modeProp == null) return;

            emissionWaveFoldout = EditorGUILayout.Foldout(emissionWaveFoldout, "Emission Wave (AudioLink)", true);
            if (!emissionWaveFoldout) return;

            EditorGUI.indentLevel++;

            if (!CheckAudioLinkInstalled())
            {
                EditorGUILayout.HelpBox(
                    "AudioLink is not installed in this project, so the emission wave cannot compile.",
                    MessageType.Info);
                if (modeProp.floatValue > 0.5f) modeProp.floatValue = 0f;
                // The float alone is not enough: an imported material keeps its Pulse/Ripple keyword.
                material.DisableKeyword("_EMISSIONWAVE_PULSE");
                material.DisableKeyword("_EMISSIONWAVE_RIPPLE");
                material.EnableKeyword("_EMISSIONWAVE_OFF");
                EditorGUI.indentLevel--;
                return;
            }

            int mode = Mathf.RoundToInt(modeProp.floatValue);
            int newMode = EditorGUILayout.Popup("Wave Mode", mode, new[] { "Off", "Pulse", "Ripple" });
            if (newMode != mode)
            {
                modeProp.floatValue = newMode;
                material.DisableKeyword("_EMISSIONWAVE_OFF");
                material.DisableKeyword("_EMISSIONWAVE_PULSE");
                material.DisableKeyword("_EMISSIONWAVE_RIPPLE");
                material.EnableKeyword(
                    newMode == 1 ? "_EMISSIONWAVE_PULSE" :
                    newMode == 2 ? "_EMISSIONWAVE_RIPPLE" : "_EMISSIONWAVE_OFF");
            }

            if (newMode == 0)
            {
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUI.indentLevel++;

            // The shader always reads the field from _EmissiveTexture's alpha. This slot is
            // just the pack source; it is never sampled, so it costs no sampler register.
            var sdfTexProp = FindProperty("_EmissionWaveSdfTex", properties);
            var emissiveTexProp = FindProperty("_EmissiveTexture", properties);
            if (sdfTexProp != null)
                materialEditor.TexturePropertySingleLine(new GUIContent("Wave SDF"), sdfTexProp);

            using (new EditorGUI.DisabledScope(
                sdfTexProp == null || sdfTexProp.textureValue == null ||
                emissiveTexProp == null || emissiveTexProp.textureValue == null))
            {
                if (GUILayout.Button("Pack SDF Into Emissive Alpha"))
                {
                    var packed = PackSdfIntoEmissiveAlpha(
                        emissiveTexProp.textureValue as Texture2D,
                        sdfTexProp.textureValue as Texture2D);
                    if (packed != null) emissiveTexProp.textureValue = packed;
                }
            }

            if (GzSdfBridge.Available)
            {
                using (new EditorGUI.DisabledScope(
                    emissiveTexProp == null || emissiveTexProp.textureValue == null ||
                    materialEditor.targets.Length > 1))
                {
                    if (GUILayout.Button(new GUIContent("Generate SDF From Emissive (GeenzTools)",
                            "Generates the field from the emissive map's luminance, stitches UV " +
                            "seams using the mesh this material renders on, packs it into the " +
                            "emissive alpha, and assigns the result.")))
                    {
                        var generated = GzSdfBridge.GenerateAndPack(
                            emissiveTexProp.textureValue as Texture2D, material);
                        if (generated != null) emissiveTexProp.textureValue = generated;
                    }
                }
            }

            DrawBandRow(properties, materialEditor, "_EmissionWaveBass", "_EmissionWaveBassColor", "Bass");
            DrawBandRow(properties, materialEditor, "_EmissionWaveLowMid", "_EmissionWaveLowMidColor", "Low Mid");
            DrawBandRow(properties, materialEditor, "_EmissionWaveHighMid", "_EmissionWaveHighMidColor", "High Mid");
            DrawBandRow(properties, materialEditor, "_EmissionWaveTreble", "_EmissionWaveTrebleColor", "Treble");

            DrawFloat(properties, "_EmissionWaveGain", "Audio Gain");
            DrawFloat(properties, "_EmissionWaveGate", "Silence Gate");

            var axisProp = FindProperty("_EmissionWaveAxis", properties);
            if (axisProp != null)
                axisProp.vectorValue = EditorGUILayout.Vector4Field("Travel Axis (XYZ dir, W origin)", axisProp.vectorValue);

            DrawFloat(properties, "_EmissionWaveTravel", "Travel Amount");
            DrawFloat(properties, "_EmissionWaveSoftness", "Softness");
            DrawFloat(properties, "_EmissionWaveEdgeSoftness", "Screen AA (px)");
            DrawFloat(properties, "_EmissionWaveEdgeFade", "Field Edge Fade");
            DrawFloat(properties, "_EmissionWaveGlow", "Crest Glow");
            DrawSlider(properties, "_EmissionWaveFresnel", "View Falloff", 0f, 1f);
            DrawFloat(properties, "_EmissionWaveFresnelPower", "View Falloff Power");
            DrawFloat(properties, "_EmissionWaveFresnelMultiplier", "View Falloff Multiplier");
            DrawFloat(properties, "_EmissionWaveFresnelClampMin", "View Falloff Clamp Min");
            DrawFloat(properties, "_EmissionWaveFresnelClampMax", "View Falloff Clamp Max");

            if (newMode == 1)
            {
                DrawFloat(properties, "_EmissionWaveGrow", "Pulse Grow");
                DrawFloat(properties, "_EmissionWavePivot", "Pulse Pivot");
            }
            else
            {
                DrawFloat(properties, "_EmissionWaveFrequency", "Ripple Frequency");
                DrawFloat(properties, "_EmissionWaveRingWidth", "Ripple Ring Width");
                DrawFloat(properties, "_EmissionWaveSpeed", "Ripple Speed");
                DrawFloat(properties, "_EmissionWaveSdfMul", "Ripple SDF Multiplier");
                DrawFloat(properties, "_EmissionWaveSdfFalloff", "Ripple SDF Falloff");
                DrawSlider(properties, "_EmissionWaveSmoothing", "Ripple Audio Smoothing", 0f, 15f);
            }

            EditorGUI.indentLevel -= 2;
        }

        private void DrawFloat(MaterialProperty[] properties, string name, string label)
        {
            var p = FindProperty(name, properties);
            if (p != null)
                p.floatValue = EditorGUILayout.FloatField(label, p.floatValue);
        }

        private void DrawBandRow(MaterialProperty[] properties, MaterialEditor materialEditor,
            string toggleName, string colorName, string label)
        {
            var toggle = FindProperty(toggleName, properties);
            var color = FindProperty(colorName, properties);
            EditorGUILayout.BeginHorizontal();
            if (toggle != null)
                toggle.floatValue = EditorGUILayout.ToggleLeft(label, toggle.floatValue > 0.5f, GUILayout.Width(90)) ? 1f : 0f;
            using (new EditorGUI.DisabledScope(toggle != null && toggle.floatValue <= 0.5f))
            {
                if (color != null)
                    color.colorValue = EditorGUILayout.ColorField(color.colorValue);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static Texture2D PackSdfIntoEmissiveAlpha(Texture2D emissive, Texture2D sdf)
        {
            if (emissive == null || sdf == null) return null;

            var emissivePath = AssetDatabase.GetAssetPath(emissive);
            if (string.IsNullOrEmpty(emissivePath))
            {
                Debug.LogError("[GzPBR] The emissive texture is not a project asset; cannot pack.");
                return null;
            }

            int w = emissive.width;
            int h = emissive.height;
            var rgb = ReadThrough(emissive, w, h, ShouldRegamma(emissive));
            // The field is data, never gamma-encoded, so it is read raw.
            var field = ReadThrough(sdf, w, h, false);
            if (rgb == null || field == null) return null;

            var packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var outPixels = new Color32[w * h];
            for (int i = 0; i < outPixels.Length; i++)
            {
                outPixels[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(rgb[i].r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(rgb[i].g * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(rgb[i].b * 255f), 0, 255),
                    // The SDF generator replicates the field into all four channels, so any
                    // channel works; alpha matches what a standalone field texture uses.
                    (byte)Mathf.Clamp(Mathf.RoundToInt(field[i].a * 255f), 0, 255));
            }
            packed.SetPixels32(outPixels);
            packed.Apply(false, false);

            return SaveWithSdfPng(packed, emissivePath, emissive.wrapMode, overwrite: false);
        }

        /// <summary>Saves the packed texture as _WithSDF.png and destroys it.</summary>
        private static Texture2D SaveWithSdfPng(Texture2D packed, string emissivePath, TextureWrapMode wrapMode, bool overwrite)
        {
            int w = packed.width, h = packed.height;
            var dir = Path.GetDirectoryName(emissivePath).Replace('\\', '/');
            var baseName = Path.GetFileNameWithoutExtension(emissivePath);
            if (baseName.EndsWith("_WithSDF")) baseName = baseName.Substring(0, baseName.Length - "_WithSDF".Length);
            var outPath = $"{dir}/{baseName}_WithSDF.png";
            if (!overwrite) outPath = AssetDatabase.GenerateUniqueAssetPath(outPath);
            File.WriteAllBytes(outPath, packed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (importer != null)
            {
                var src = AssetImporter.GetAtPath(emissivePath) as TextureImporter;
                importer.textureType = TextureImporterType.Default;
                // RGB is emission colour, so sRGB stays ON - the opposite of a standalone
                // field texture. Alpha is never gamma-encoded either way.
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = src == null || src.mipmapEnabled;
                importer.wrapMode = wrapMode;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 32, 8192);
                // CompressedHQ is BC7, not BC3, whose alpha block error stair-steps the SDF edge.
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.crunchedCompression = false;
                importer.SaveAndReimport();
            }

            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            EditorGUIUtility.PingObject(result);
            return result;
        }

        /// <summary>Reflection bridge to GeenzTools' SdfGenerator, an optional dependency.</summary>
        private static class GzSdfBridge
        {
            private static bool searched;
            private static System.Reflection.MethodInfo generate;
            private static Type channelType;

            public static bool Available
            {
                get
                {
                    if (!searched)
                    {
                        searched = true;
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            var t = asm.GetType("GeenzTools.Core.SdfGenerator");
                            if (t == null) continue;
                            generate = t.GetMethod("Generate");
                            channelType = asm.GetType("GeenzTools.Core.SdfSourceChannel");
                            break;
                        }
                        if (generate == null || channelType == null || generate.GetParameters().Length < 9)
                            generate = null;
                    }
                    return generate != null;
                }
            }

            public static Texture2D GenerateAndPack(Texture2D emissive, Material material)
            {
                if (!Available || emissive == null) return null;
                var emissivePath = AssetDatabase.GetAssetPath(emissive);
                if (string.IsNullOrEmpty(emissivePath))
                {
                    Debug.LogError("[GzPBR] The emissive texture is not a project asset; cannot generate.");
                    return null;
                }

                var mesh = FindMeshUsing(material);
                float spread = Mathf.Max(8f, emissive.width / 64f);
                Texture2D generated = null;
                try
                {
                    EditorUtility.DisplayProgressBar("GzPBR", "Generating SDF...", 0.5f);
                    // Luminance, never Auto: an already-packed emissive carries the previous
                    // field in alpha, and Auto would feed it back into itself.
                    generated = (Texture2D)generate.Invoke(null, new object[]
                    {
                        emissive, Enum.ToObject(channelType, 2), 0.5f, spread,
                        false, 0, emissive, mesh != null, mesh
                    });
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
                if (generated == null)
                {
                    Debug.LogError("[GzPBR] SDF generation failed; see previous errors.");
                    return null;
                }

                var saved = SaveWithSdfPng(generated, emissivePath, emissive.wrapMode, overwrite: true);
                Debug.Log(mesh != null
                    ? $"[GzPBR] SDF generated from '{emissive.name}', seams stitched via '{mesh.name}' (spread {spread:0} px)."
                    : $"[GzPBR] SDF generated from '{emissive.name}'. No renderer in the open scenes uses this material, so no seam stitching.");
                return saved;
            }

            private static Mesh FindMeshUsing(Material material)
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    if (mats == null) continue;
                    bool uses = false;
                    foreach (var m in mats)
                        if (m == material) { uses = true; break; }
                    if (!uses) continue;
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) return smr.sharedMesh;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
                }
                return null;
            }
        }

        /// <summary>Reads through a temporary RT so compressed or unreadable sources work.</summary>
        internal static Color[] ReadThrough(Texture2D source, int w, int h, bool regamma)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                readback = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply(false, false);
                var pixels = readback.GetPixels();

                // Sampling sRGB in a Linear project returns decoded values, so re-encode them.
                if (regamma)
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var g = pixels[i].gamma;
                        pixels[i] = new Color(g.r, g.g, g.b, pixels[i].a);
                    }
                }
                return pixels;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GzPBR] Could not read '{source.name}': {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static bool ShouldRegamma(Texture2D source)
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear) return false;
            var path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null && importer.sRGBTexture;
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

                var ltcgiProp = FindProperty("_LTCGI", properties);

                if (ltcgiProp != null) {
                    if (!CheckLTCGIInstalled()) {
                        EditorGUILayout.HelpBox("LTCGI is not installed in this project, so it stays off.", MessageType.Info);

                        if (ltcgiProp.floatValue > 0.5f) {
                            ltcgiProp.floatValue = 0f;
                        }

                        material.DisableKeyword("_LTCGI_WORLD");
                        material.DisableKeyword("_LTCGI_AVATAR");
                        material.EnableKeyword("_LTCGI_OFF");

                        if (GUILayout.Button("Open LTCGI Installation Instructions")) {
                            Application.OpenURL("https://ltcgi.dev/Getting%20Started/Installation");
                        }
                    } else {
                        int ltcgiMode = Mathf.RoundToInt(ltcgiProp.floatValue);
                        int newLtcgiMode = EditorGUILayout.Popup("LTCGI Mode", ltcgiMode, new[] { "Off", "World", "Avatar" });

                        if (newLtcgiMode != ltcgiMode) {
                            ltcgiProp.floatValue = newLtcgiMode;
                        }

                        if (newLtcgiMode != 0) {
                            EditorGUILayout.HelpBox("LTCGI adds a sampler and several textures. Keep Shared Texture Sampler on for PC Tier 0. Quest may exceed its texture limit.", MessageType.Warning);

                            if (material.enableInstancing) {
                                EditorGUILayout.HelpBox("LTCGI gives each lit renderer its own property block, so these renderers will not GPU-instance.", MessageType.Warning);
                            }
                        }
                    }

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
                
                // Lightmap Reflection Blend - only relevant for static/lightmapped objects
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
                new GUIContent("Auxiliary Data Texture", "Packs scalar params not covered by the other textures (R: IOR, G: Iridescence IOR, B: Face Cull, A: Sheen Rim Boost) into one texture, so parameter variants can batch under a single material. Replaces those values while on (sheen factor is omitted - it equals scaling the sheen colour)."),
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
                    "Channels: R=IOR, G=Iridescence IOR, B=Face Cull (0=off, 0.5=back, 1=front), A=Sheen Rim Boost - decoded " +
                    "per-vertex/instance slice when Texture Arrays are on. Iridescence and sheen channels only apply when those " +
                    "features are on; B only acts when Face Cull Source = Texture. Sheen factor is omitted (equals scaling the " +
                    "sheen colour).", MessageType.None);

                EditorGUILayout.LabelField("Decode Ranges (Min, Max)", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Must match the ranges the aux texture was baked with. The Aux Data Baker writes these for you. Keep Iridescence IOR Min >= 1 - a decoded value near 0 produces white iridescence artifacts.", MessageType.None);
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
                                "R: iridescence thickness min, G: thickness max - normalized into the storage range below (nm)."), tex2);
                        if (tex2.textureValue != null)
                        {
                            EditorGUI.indentLevel++;
                            materialEditor.TextureScaleOffsetProperty(tex2);
                            EditorGUI.indentLevel--;
                        }
                    }
                    DrawRangeProp(properties, "_AuxThicknessRangeMin", "_AuxThicknessRangeMax", "Storage Range (nm)");
                    EditorGUILayout.HelpBox(
                        "Storage range (nm) the R/G channels normalize into - must match the bake. The per-region position " +
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
                    "Input textures are now Texture2DArrays - the texture slots in the sections above accept array assets.",
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
                "They require the camera to render a depth texture - in VRChat that means a realtime shadow-casting " +
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

        private void DrawSoftDepthProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useProp = FindProperty("_UseSoftDepth", properties);
            if (useProp == null) return;

            softDepthFoldout = EditorGUILayout.Foldout(softDepthFoldout, "Soft Depth Fade (transparent)", true);
            if (!softDepthFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Eases alpha out as a transparent surface approaches the opaque geometry behind it, softening hard " +
                "intersection seams. Only acts in Transparent / Premultiplied render modes. Requires the camera to render a " +
                "depth texture (in VRChat: a realtime shadow-casting directional light, or a depth-enabled camera); where none " +
                "is available the surface renders fully.\n\n" +
                "Heads-up: this really only behaves on static / non-skinned meshes. On rigged meshes the depth prepass and the " +
                "forward pass disagree about where the skinned verts are, so the fade gets confused - a Unity quirk, not " +
                "something the shader can fix. Keep it to non-rigged geometry.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Enable Soft Depth", "Fade transparent surfaces near opaque geometry behind them."),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            bool use = useProp.floatValue > 0.5f;
            bool newUse = EditorGUILayout.Toggle(use);
            if (newUse != use)
                useProp.floatValue = newUse ? 1.0f : 0.0f;
            EditorGUILayout.EndHorizontal();

            if (newUse)
            {
                EditorGUI.indentLevel++;
                DrawSlider(properties, "_SoftDepthFadeDistance", "Fade Distance", 0.001f, 5f);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawScreenSpaceShadowProperties(MaterialEditor materialEditor, MaterialProperty[] properties, Material material)
        {
            var useProp = FindProperty("_UseScreenSpaceShadows", properties);
            if (useProp == null) return;

            ssShadowFoldout = EditorGUILayout.Foldout(ssShadowFoldout, "Screen-Space Shadows (SH dominant light)", true);
            if (!ssShadowFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Casts a screen-space shadow from the dominant light direction derived from the scene's spherical harmonics " +
                "(the same SH dominant light the shader already evaluates). Marches the depth buffer toward that direction and " +
                "darkens the SH light where occluded. Requires a camera depth texture (in VRChat: a realtime shadow-casting " +
                "directional light, or a depth-enabled camera). Only affects the SH dominant light, on dynamic (non-lightmapped) " +
                "objects.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Enable SS Shadows", "Screen-space shadow from the SH dominant light direction."),
                GUILayout.Width(EditorGUIUtility.labelWidth));
            bool use = useProp.floatValue > 0.5f;
            bool newUse = EditorGUILayout.Toggle(use);
            if (newUse != use)
                useProp.floatValue = newUse ? 1.0f : 0.0f;
            EditorGUILayout.EndHorizontal();

            if (newUse)
            {
                EditorGUI.indentLevel++;
                DrawSlider(properties, "_SSShadowIntensity", "Intensity", 0f, 1f);
                DrawSlider(properties, "_SSShadowDistance", "March Distance", 0.05f, 5f);
                DrawSlider(properties, "_SSShadowSteps", "Steps", 4f, 64f);
                DrawSlider(properties, "_SSShadowThickness", "Thickness", 0.01f, 2f);
                DrawSlider(properties, "_SSShadowBias", "Bias", 0f, 0.5f);
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
                "Quality tiers use Unity shader LOD. This is GLOBAL per shader asset - changing it affects ALL materials " +
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

            // Resync on Undo/Redo, which restores the float but not keyword state.
            var emissionWaveProp = FindProperty("_EmissionWave", properties);
            if (emissionWaveProp != null)
            {
                int waveMode = Mathf.RoundToInt(emissionWaveProp.floatValue);
                if (!CheckAudioLinkInstalled()) waveMode = 0;
                material.DisableKeyword("_EMISSIONWAVE_OFF");
                material.DisableKeyword("_EMISSIONWAVE_PULSE");
                material.DisableKeyword("_EMISSIONWAVE_RIPPLE");
                material.EnableKeyword(
                    waveMode == 1 ? "_EMISSIONWAVE_PULSE" :
                    waveMode == 2 ? "_EMISSIONWAVE_RIPPLE" : "_EMISSIONWAVE_OFF");
            }

            var ltcgiProp = FindProperty("_LTCGI", properties);

            if (ltcgiProp != null) {
                int ltcgiMode = Mathf.RoundToInt(ltcgiProp.floatValue);

                if (!CheckLTCGIInstalled()) {
                    ltcgiMode = 0;
                }

                material.DisableKeyword("_LTCGI_OFF");
                material.DisableKeyword("_LTCGI_WORLD");
                material.DisableKeyword("_LTCGI_AVATAR");
                string ltcgiKeyword = "_LTCGI_OFF";
                if (ltcgiMode == 1) {
                    ltcgiKeyword = "_LTCGI_WORLD";
                } else if (ltcgiMode == 2) {
                    ltcgiKeyword = "_LTCGI_AVATAR";
                }

                material.EnableKeyword(ltcgiKeyword);
            }

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
            UpdateFeatureKeyword(material, properties, "_UseDitheredShadows", "USE_DITHERED_SHADOWS");
            UpdateFeatureKeyword(material, properties, "_UseSharedSampler", "USE_SHARED_SAMPLER");
            UpdateFeatureKeyword(material, properties, "_UseVRCLightVolumes", "USE_VRC_LIGHT_VOLUMES");
            UpdateFeatureKeyword(material, properties, "_UseSSR", "USE_SSR");
            UpdateFeatureKeyword(material, properties, "_UseSoftDepth", "USE_SOFT_DEPTH");
            UpdateFeatureKeyword(material, properties, "_UseScreenSpaceShadows", "USE_SS_SHADOWS");
            UpdateFeatureKeyword(material, properties, "_UseTextureArrays", "USE_TEXTURE_ARRAYS");
            UpdateFeatureKeyword(material, properties, "_ArrayIndexSource", "_ARRAYINDEXSOURCE_MATERIALINSTANCE");
            UpdateFeatureKeywordInverse(material, properties, "_ArrayIndexSource", "_ARRAYINDEXSOURCE_VERTEX");
            UpdateFeatureKeyword(material, properties, "_UseAuxData", "USE_AUX_DATA");
            UpdateFeatureKeyword(material, properties, "_UseAuxThickness", "USE_AUX_THICKNESS");
            // Baker-driven (GeenzTools merged-mesh visibility); resynced here so an
            // inspector pass cannot clobber the keyword off a baked material.
            UpdateFeatureKeyword(material, properties, "_ObjectToggles", "USE_OBJECT_TOGGLES");
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