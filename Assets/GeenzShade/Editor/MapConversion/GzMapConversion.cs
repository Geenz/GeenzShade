/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GeenzShade {
    internal static class GzMapConversion {
        public static readonly List<IMapConverter> Converters = new List<IMapConverter> {
            new PBRStandardMapConverter(),
            new PBRSpecularMapConverter()
        };

        public static IMapConverter Find(string shaderName) {
            foreach (IMapConverter converter in Converters) {
                if (converter.Handles(shaderName)) {
                    return converter;
                }
            }

            return null;
        }

        // The GUID suffix keeps two materials with the same name from overwriting each other.
        public static string OutputFolder(Material material) {
            string suffix = Mathf.Abs(material.GetInstanceID()).ToString();
            string assetPath = AssetDatabase.GetAssetPath(material);

            if (!string.IsNullOrEmpty(assetPath)) {
                suffix = AssetDatabase.AssetPathToGUID(assetPath).Substring(0, 8);
            }

            return "Assets/GeenzShade/Converted/" + SafeName(material.name) + "_" + suffix;
        }

        public static string OutputPath(Material material, string outputFolder, string suffix) {
            return outputFolder + "/" + SafeName(material.name) + "_" + suffix + ".png";
        }

        public static Vector2Int LargestSize(params Texture[] textures) {
            int width = 0;
            int height = 0;

            foreach (Texture texture in textures) {
                if (texture != null) {
                    width = Mathf.Max(width, texture.width);
                    height = Mathf.Max(height, texture.height);
                }
            }

            return new Vector2Int(width, height);
        }

        public static TextureWrapMode WrapModeOf(params Texture[] textures) {
            foreach (Texture texture in textures) {
                if (texture != null) {
                    return texture.wrapMode;
                }
            }

            return TextureWrapMode.Repeat;
        }

        // Linear values as the shader samples them; null for non-Texture2D sources.
        public static Color[] ReadPixels(Texture texture, int width, int height) {
            Texture2D texture2D = texture as Texture2D;

            if (texture2D == null || width <= 0 || height <= 0) {
                return null;
            }

            return GzPBRShaderGUI.ReadThrough(texture2D, width, height, false);
        }

        // Gamma-encodes RGB in place when srgb is set. Overwrites the file so the GUID survives.
        // Refuses to overwrite any of the source textures.
        public static Texture2D SavePng(Color[] pixels, int width, int height, string path, bool srgb, TextureWrapMode wrapMode, params Texture[] sources) {
            foreach (Texture source in sources) {
                if (source != null && AssetDatabase.GetAssetPath(source) == path) {
                    Debug.LogError("[GzPBR] Refusing to overwrite source texture " + path);

                    return null;
                }
            }

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            if (srgb) {
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++) {
                    Color gamma = pixels[pixelIndex].gamma;
                    pixels[pixelIndex] = new Color(gamma.r, gamma.g, gamma.b, pixels[pixelIndex].a);
                }
            }

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            byte[] bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null) {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = srgb;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = wrapMode;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, 8192);
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.crunchedCompression = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void EnsureFolder(string folder) {
            if (AssetDatabase.IsValidFolder(folder)) {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int index = 1; index < parts.Length; index++) {
                string next = current + "/" + parts[index];

                if (!AssetDatabase.IsValidFolder(next)) {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static string SafeName(string name) {
            string safe = name;

            foreach (char invalid in Path.GetInvalidFileNameChars()) {
                safe = safe.Replace(invalid, '_');
            }

            return safe;
        }
    }
}
