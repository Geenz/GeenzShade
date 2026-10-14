/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

using UnityEditor;
using UnityEngine;

namespace GeenzShade {
    internal class PBRStandardMapConverter : IMapConverter {
        public string DisplayName => "Unity Standard";

        public bool Handles(string shaderName) {
            return shaderName == "Standard";
        }

        public GzMapConversionResult Convert(GzSourceMaterial source, Material target, string outputFolder) {
            GzMapConversionResult result = new GzMapConversionResult();
            Texture mainTexture = source.GetTexture("_MainTex");
            Vector4 mainScaleOffset = source.GetScaleOffset("_MainTex");
            target.SetTexture("_BaseColorTexture", mainTexture);

            if (mainTexture != null) {
                target.SetFloat("_UseBaseColorTexture", 1f);
                SetTiling(target, "_BaseColorTexture", mainScaleOffset);
            }

            ConvertShared(source, target, result);

            Texture metallicMap = source.GetTexture("_MetallicGlossMap");
            Texture occlusionMap = source.GetTexture("_OcclusionMap");
            bool smoothnessInAlbedo = Mathf.RoundToInt(source.GetFloat("_SmoothnessTextureChannel", 0f)) == 1;
            Texture albedoSmoothnessMap = null;
            if (smoothnessInAlbedo) {
                albedoSmoothnessMap = mainTexture;
            }

            if (smoothnessInAlbedo && result.RenderMode != GzPBRShaderGUI.RenderMode.Opaque) {
                result.Warnings.Add("albedo alpha holds smoothness and will act as opacity");
            }

            Vector2Int size = GzMapConversion.LargestSize(metallicMap, occlusionMap, albedoSmoothnessMap);
            Color[] metallicPixels = GzMapConversion.ReadPixels(metallicMap, size.x, size.y);
            Color[] occlusionPixels = GzMapConversion.ReadPixels(occlusionMap, size.x, size.y);
            Color[] albedoPixels = GzMapConversion.ReadPixels(albedoSmoothnessMap, size.x, size.y);

            if (metallicMap != null && metallicPixels == null) {
                result.Warnings.Add("could not read _MetallicGlossMap");
            }

            if (occlusionMap != null && occlusionPixels == null) {
                result.Warnings.Add("could not read _OcclusionMap");
            }

            if (albedoSmoothnessMap != null && albedoPixels == null) {
                result.Warnings.Add("could not read _MainTex");
            }

            Color[] smoothnessPixels = metallicPixels;
            if (smoothnessInAlbedo) {
                smoothnessPixels = albedoPixels;
            }

            bool hasOrm = metallicPixels != null || occlusionPixels != null || albedoPixels != null;
            float glossMapScale = source.GetFloat("_GlossMapScale", 1f);
            Texture2D ormTexture = null;

            if (hasOrm) {
                Color[] ormPixels = new Color[size.x * size.y];

                for (int pixelIndex = 0; pixelIndex < ormPixels.Length; pixelIndex++) {
                    ormPixels[pixelIndex] = PackPixel(metallicPixels, occlusionPixels, smoothnessPixels, pixelIndex, glossMapScale);
                }

                TextureWrapMode wrapMode = GzMapConversion.WrapModeOf(metallicMap, occlusionMap, albedoSmoothnessMap);
                string ormPath = GzMapConversion.OutputPath(target, outputFolder, "ORM");
                ormTexture = GzMapConversion.SavePng(ormPixels, size.x, size.y, ormPath, false, wrapMode, metallicMap, occlusionMap, albedoSmoothnessMap);

                if (ormTexture == null) {
                    Debug.LogError("[GzPBR] " + target.name + ": could not write " + ormPath);
                }
            }

            target.SetTexture("_ORMTexture", ormTexture);

            if (ormTexture != null) {
                target.SetFloat("_UseORMTexture", 1f);
                SetTiling(target, "_ORMTexture", mainScaleOffset);
            }

            float glossiness = source.GetFloat("_Glossiness", 0.5f);

            if (smoothnessInAlbedo && mainTexture == null) {
                glossiness = glossMapScale;
            }

            bool roughnessFromMap = ormTexture != null && smoothnessPixels != null;
            float roughnessFactor = 1f;
            if (!roughnessFromMap) {
                roughnessFactor = 1f - glossiness;
            }

            target.SetFloat("_Roughness", roughnessFactor);

            // Standard declares [Gamma] _Metallic; GzPBR's _Metallic is linear.
            float sourceMetallic = source.GetSourceMetallic(0f);
            bool metallicFromMap = ormTexture != null && metallicPixels != null;
            float metallicFactor = 1f;
            if (!metallicFromMap) {
                metallicFactor = sourceMetallic;

                if (PlayerSettings.colorSpace == ColorSpace.Linear) {
                    metallicFactor = Mathf.GammaToLinearSpace(metallicFactor);
                }
            }

            target.SetFloat("_Metallic", metallicFactor);

            return result;
        }

        // Missing arrays mean the source map is absent. Unmapped channels pack as 1.
        internal static Color PackPixel(Color[] metallicPixels, Color[] occlusionPixels, Color[] smoothnessPixels, int pixelIndex, float glossMapScale) {
            float occlusion = 1f;
            if (occlusionPixels != null) {
                occlusion = occlusionPixels[pixelIndex].g;
            }

            float metallic = 1f;
            if (metallicPixels != null) {
                metallic = metallicPixels[pixelIndex].r;
            }

            float roughness = 1f;
            if (smoothnessPixels != null) {
                roughness = 1f - smoothnessPixels[pixelIndex].a * glossMapScale;
            }

            return new Color(occlusion, Mathf.Clamp01(roughness), metallic, 1f);
        }

        internal static void ConvertShared(GzSourceMaterial source, Material target, GzMapConversionResult result) {
            Vector4 mainScaleOffset = source.GetScaleOffset("_MainTex");
            Texture normalMap = source.GetTexture("_BumpMap");

            target.SetTexture("_NormalTexture", normalMap);

            if (normalMap != null) {
                target.SetFloat("_UseNormalTexture", 1f);
                SetTiling(target, "_NormalTexture", mainScaleOffset);
                target.SetFloat("_NormalScale", Mathf.Clamp(source.GetFloat("_BumpScale", 1f), 0f, 2f));
            }

            bool emissionOn = (target.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
            Texture emissionMap = null;

            if (emissionOn) {
                emissionMap = source.GetTexture("_EmissionMap");

                Color emissionColor = source.GetColor("_EmissionColor", Color.black);
                Color emissionLinear = emissionColor.linear;
                float peak = Mathf.Max(emissionLinear.r, Mathf.Max(emissionLinear.g, emissionLinear.b));
                Color emissiveFactor = emissionColor;
                float emissionStrength = 1f;
                if (peak > 1f) {
                    emissiveFactor = (emissionLinear / peak).gamma;
                    emissiveFactor.a = 1f;
                    emissionStrength = Mathf.Min(peak, 10f);
                }

                target.SetColor("_EmissiveFactor", emissiveFactor);
                target.SetFloat("_EmissionStrength", emissionStrength);
            }

            target.SetTexture("_EmissiveTexture", emissionMap);

            if (emissionMap != null) {
                target.SetFloat("_UseEmissiveTexture", 1f);
                SetTiling(target, "_EmissiveTexture", mainScaleOffset);
            }

            target.SetFloat("_AlphaCutoff", source.GetFloat("_Cutoff", 0.5f));

            int mode = Mathf.RoundToInt(source.GetFloat("_Mode", 0f));
            GzPBRShaderGUI.RenderMode renderMode = GzPBRShaderGUI.RenderMode.Opaque;

            if (mode == 1) {
                renderMode = GzPBRShaderGUI.RenderMode.Cutout;
            } else if (mode == 2) {
                renderMode = GzPBRShaderGUI.RenderMode.Transparent;
            } else if (mode == 3) {
                renderMode = GzPBRShaderGUI.RenderMode.PremultipliedAlpha;
            }

            result.HasRenderMode = true;
            result.RenderMode = renderMode;

            string[] unsupportedMaps = { "_DetailAlbedoMap", "_DetailNormalMap", "_DetailMask", "_ParallaxMap" };

            foreach (string mapName in unsupportedMaps) {
                if (source.GetTexture(mapName) != null) {
                    result.Skipped.Add(mapName);
                }
            }
        }

        // Standard samples every main map with _MainTex_ST.
        internal static void SetTiling(Material target, string slotName, Vector4 scaleOffset) {
            target.SetTextureScale(slotName, new Vector2(scaleOffset.x, scaleOffset.y));
            target.SetTextureOffset(slotName, new Vector2(scaleOffset.z, scaleOffset.w));
        }
    }
}
