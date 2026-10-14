/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

using UnityEngine;

namespace GeenzShade {
    internal class PBRSpecularMapConverter : IMapConverter {
        private const float DielectricSpecular = 0.04f;
        private const float Epsilon = 1e-6f;

        public string DisplayName => "Unity Standard (Specular setup)";

        public bool Handles(string shaderName) {
            return shaderName == "Standard (Specular setup)";
        }

        public GzMapConversionResult Convert(GzSourceMaterial source, Material target, string outputFolder) {
            GzMapConversionResult result = new GzMapConversionResult();
            PBRStandardMapConverter.ConvertShared(source, target, result);

            Texture mainTexture = source.GetTexture("_MainTex");
            Texture specularMap = source.GetTexture("_SpecGlossMap");
            Texture occlusionMap = source.GetTexture("_OcclusionMap");
            Color colorFactor = source.GetSourceColor(Color.white);
            Color specularColor = source.GetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f, 1f));
            float glossiness = source.GetFloat("_Glossiness", 0.5f);
            float glossMapScale = source.GetFloat("_GlossMapScale", 1f);
            bool smoothnessFromAlbedo = Mathf.RoundToInt(source.GetFloat("_SmoothnessTextureChannel", 0f)) == 1;
            Vector2Int size = GzMapConversion.LargestSize(mainTexture, specularMap, occlusionMap);
            Color[] diffusePixels = GzMapConversion.ReadPixels(mainTexture, size.x, size.y);
            Color[] specularPixels = GzMapConversion.ReadPixels(specularMap, size.x, size.y);
            Color[] occlusionPixels = GzMapConversion.ReadPixels(occlusionMap, size.x, size.y);

            if (mainTexture != null && diffusePixels == null) {
                result.Warnings.Add("could not read _MainTex");
            }

            if (specularMap != null && specularPixels == null) {
                result.Warnings.Add("could not read _SpecGlossMap");
            }

            if (occlusionMap != null && occlusionPixels == null) {
                result.Warnings.Add("could not read _OcclusionMap");
            }

            // Standard samples the default white texture when no albedo is assigned.
            if (smoothnessFromAlbedo && mainTexture == null) {
                glossiness = glossMapScale;
            }

            bool hasTextures = diffusePixels != null || specularPixels != null || occlusionPixels != null;
            Texture2D baseColorTexture = null;
            Texture2D ormTexture = null;

            if (hasTextures) {
                Color colorLinear = colorFactor.linear;
                Color specularLinear = specularColor.linear;
                Color[] baseColorPixels = new Color[size.x * size.y];
                Color[] ormPixels = new Color[size.x * size.y];

                for (int pixelIndex = 0; pixelIndex < baseColorPixels.Length; pixelIndex++) {
                    Color diffuse = colorLinear;
                    float alpha = 1f;
                    if (diffusePixels != null) {
                        diffuse = diffusePixels[pixelIndex] * colorLinear;

                        if (!smoothnessFromAlbedo) {
                            alpha = diffusePixels[pixelIndex].a;
                        }
                    }

                    Color specular = specularLinear;

                    if (specularPixels != null) {
                        specular = specularPixels[pixelIndex];
                    }

                    float gloss = glossiness;

                    if (smoothnessFromAlbedo && diffusePixels != null) {
                        gloss = diffusePixels[pixelIndex].a * glossMapScale;
                    } else if (!smoothnessFromAlbedo && specularPixels != null) {
                        gloss = specularPixels[pixelIndex].a * glossMapScale;
                    }

                    float occlusion = 1f;
                    if (occlusionPixels != null) {
                        occlusion = occlusionPixels[pixelIndex].g;
                    }

                    SolvePixel(diffuse, specular, out Color baseColorLinear, out float metallic);
                    baseColorPixels[pixelIndex] = new Color(baseColorLinear.r, baseColorLinear.g, baseColorLinear.b, alpha);
                    ormPixels[pixelIndex] = new Color(occlusion, Mathf.Clamp01(1f - gloss), metallic, 1f);
                }

                TextureWrapMode wrapMode = GzMapConversion.WrapModeOf(mainTexture, specularMap, occlusionMap);
                string baseColorPath = GzMapConversion.OutputPath(target, outputFolder, "BaseColor");
                string ormPath = GzMapConversion.OutputPath(target, outputFolder, "ORM");
                baseColorTexture = GzMapConversion.SavePng(baseColorPixels, size.x, size.y, baseColorPath, true, wrapMode, mainTexture, specularMap, occlusionMap);
                ormTexture = GzMapConversion.SavePng(ormPixels, size.x, size.y, ormPath, false, wrapMode, mainTexture, specularMap, occlusionMap);

                if (baseColorTexture == null) {
                    Debug.LogError("[GzPBR] " + target.name + ": could not write " + baseColorPath);
                }

                if (ormTexture == null) {
                    Debug.LogError("[GzPBR] " + target.name + ": could not write " + ormPath);
                }
            }

            if (baseColorTexture != null && ormTexture != null) {
                Vector4 mainScaleOffset = source.GetScaleOffset("_MainTex");
                target.SetTexture("_BaseColorTexture", baseColorTexture);
                target.SetTexture("_ORMTexture", ormTexture);
                target.SetFloat("_UseBaseColorTexture", 1f);
                target.SetFloat("_UseORMTexture", 1f);
                PBRStandardMapConverter.SetTiling(target, "_BaseColorTexture", mainScaleOffset);
                PBRStandardMapConverter.SetTiling(target, "_ORMTexture", mainScaleOffset);
                target.SetColor("_Color", new Color(1f, 1f, 1f, colorFactor.a));
                target.SetFloat("_Metallic", 1f);
                target.SetFloat("_Roughness", 1f);

                return result;
            }

            target.SetTexture("_BaseColorTexture", null);
            target.SetTexture("_ORMTexture", null);
            SolvePixel(colorFactor.linear, specularColor.linear, out Color scalarBaseLinear, out float scalarMetallic);
            Color scalarBase = scalarBaseLinear.gamma;
            scalarBase.a = colorFactor.a;
            target.SetColor("_Color", scalarBase);
            target.SetFloat("_Metallic", scalarMetallic);
            target.SetFloat("_Roughness", 1f - glossiness);

            return result;
        }

        // Babylon.js glTF exporter spec-gloss to metal-rough conversion, in linear space.
        internal static void SolvePixel(Color diffuseLinear, Color specularLinear, out Color baseColorLinear, out float metallic) {
            float oneMinusSpecularStrength = 1f - Mathf.Max(specularLinear.r, Mathf.Max(specularLinear.g, specularLinear.b));
            metallic = SolveMetallic(Brightness(diffuseLinear), Brightness(specularLinear), oneMinusSpecularStrength);

            float mainScale = oneMinusSpecularStrength / (1f - DielectricSpecular) / Mathf.Max(1f - metallic, Epsilon);
            baseColorLinear = new Color(
                SolveChannel(diffuseLinear.r, specularLinear.r, mainScale, metallic),
                SolveChannel(diffuseLinear.g, specularLinear.g, mainScale, metallic),
                SolveChannel(diffuseLinear.b, specularLinear.b, mainScale, metallic),
                1f);
        }

        private static float SolveChannel(float diffuse, float specular, float mainScale, float metallic) {
            float mainBase = diffuse * mainScale;
            float metallicBase = (specular - DielectricSpecular * (1f - metallic)) / Mathf.Max(metallic, Epsilon);
            float blend = metallic * metallic;

            return Mathf.Clamp01(mainBase * (1f - blend) + metallicBase * blend);
        }

        private static float SolveMetallic(float diffuse, float specular, float oneMinusSpecularStrength) {
            if (specular < DielectricSpecular) {
                return 0f;
            }

            float quadraticTerm = DielectricSpecular;
            float linearTerm = diffuse * oneMinusSpecularStrength / (1f - DielectricSpecular) + specular - 2f * DielectricSpecular;
            float constantTerm = DielectricSpecular - specular;
            float discriminant = linearTerm * linearTerm - 4f * quadraticTerm * constantTerm;

            return Mathf.Clamp01((-linearTerm + Mathf.Sqrt(discriminant)) / (2f * quadraticTerm));
        }

        private static float Brightness(Color color) {
            return Mathf.Sqrt(0.299f * color.r * color.r + 0.587f * color.g * color.g + 0.114f * color.b * color.b);
        }
    }
}
