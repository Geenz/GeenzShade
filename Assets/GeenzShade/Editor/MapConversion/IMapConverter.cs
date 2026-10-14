/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace GeenzShade {
    internal interface IMapConverter {
        string DisplayName { get; }

        bool Handles(string shaderName);

        GzMapConversionResult Convert(GzSourceMaterial source, Material target, string outputFolder);
    }

    internal class GzMapConversionResult {
        public bool HasRenderMode;
        public GzPBRShaderGUI.RenderMode RenderMode;
        public List<string> Skipped = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    // Saved properties outlive a shader switch; entries are matched by displayName (the key).
    internal class GzSourceMaterial : System.IDisposable {
        internal const string SourceColorTag = "GzSourceColor";
        internal const string SourceMetallicTag = "GzSourceMetallic";

        private readonly Material sourceMaterial;
        private readonly SerializedObject serializedMaterial;

        public GzSourceMaterial(Material material) {
            sourceMaterial = material;
            serializedMaterial = new SerializedObject(material);
        }

        public void Dispose() {
            serializedMaterial.Dispose();
        }

        public Texture GetTexture(string name) {
            SerializedProperty entry = Find("m_SavedProperties.m_TexEnvs", name);

            if (entry == null) {
                return null;
            }

            return entry.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
        }

        public Vector4 GetScaleOffset(string name) {
            SerializedProperty entry = Find("m_SavedProperties.m_TexEnvs", name);

            if (entry == null) {
                return new Vector4(1f, 1f, 0f, 0f);
            }

            Vector2 scale = entry.FindPropertyRelative("m_Scale").vector2Value;
            Vector2 offset = entry.FindPropertyRelative("m_Offset").vector2Value;

            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        public float GetFloat(string name, float fallback) {
            SerializedProperty entry = Find("m_SavedProperties.m_Floats", name);

            if (entry == null) {
                return fallback;
            }

            return entry.floatValue;
        }

        public Color GetColor(string name, Color fallback) {
            SerializedProperty entry = Find("m_SavedProperties.m_Colors", name);

            if (entry == null) {
                return fallback;
            }

            return entry.colorValue;
        }

        // Cached in a material tag so a repeat conversion reads the original, not its own output.
        public Color GetSourceColor(Color fallback) {
            string cached = sourceMaterial.GetTag(SourceColorTag, false);

            if (!string.IsNullOrEmpty(cached)) {
                string[] parts = cached.Split(',');
                float[] channels = new float[4];
                bool valid = parts.Length == 4;

                for (int index = 0; valid && index < 4; index++) {
                    valid = float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out channels[index]);
                }

                if (valid) {
                    return new Color(channels[0], channels[1], channels[2], channels[3]);
                }
            }

            Color color = GetColor("_Color", fallback);
            sourceMaterial.SetOverrideTag(SourceColorTag, string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R}", color.r, color.g, color.b, color.a));

            return color;
        }

        public float GetSourceMetallic(float fallback) {
            string cached = sourceMaterial.GetTag(SourceMetallicTag, false);

            if (!string.IsNullOrEmpty(cached) && float.TryParse(cached, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) {
                return parsed;
            }

            float metallic = GetFloat("_Metallic", fallback);
            sourceMaterial.SetOverrideTag(SourceMetallicTag, metallic.ToString("R", CultureInfo.InvariantCulture));

            return metallic;
        }

        private SerializedProperty Find(string arrayPath, string name) {
            SerializedProperty array = serializedMaterial.FindProperty(arrayPath);

            if (array == null) {
                return null;
            }

            for (int index = 0; index < array.arraySize; index++) {
                SerializedProperty element = array.GetArrayElementAtIndex(index);

                if (element.displayName == name) {
                    return element.FindPropertyRelative("second");
                }
            }

            return null;
        }
    }
}
