/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GeenzShade
{
    // Lightweight utility for packing a set of equally-sized Texture2Ds into a
    // Texture2DArray asset (one input texture per slice). The slice order is the
    // list order, which is the layer index the GzPBR shader samples via UV3.x.
    public class GzTextureArrayBuilder : EditorWindow
    {
        private readonly List<Texture2D> _slices = new List<Texture2D>();
        private bool _sRGB = true;          // color data (albedo/emissive) vs linear data (normal/ORM/etc.)
        private Vector2 _scroll;

        [MenuItem("GeenzTools/GeenzShade/Texture Array Builder")]
        public static void Open()
        {
            var w = GetWindow<GzTextureArrayBuilder>("Texture Array Builder");
            w.minSize = new Vector2(340, 280);
        }

        // ---- Project context menu: build straight from the current selection ----
        [MenuItem("Assets/GeenzTools/GeenzShade/Create Texture2DArray From Selection", false, 1100)]
        public static void CreateFromSelection()
        {
            var texs = Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets)
                                 .OrderBy(t => t.name, System.StringComparer.OrdinalIgnoreCase)
                                 .ToList();
            string dir = "Assets";
            if (texs.Count > 0)
                dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(texs[0]));
            BuildAndSave(texs, true, dir, "TextureArray");
        }

        [MenuItem("Assets/GeenzTools/GeenzShade/Create Texture2DArray From Selection", true)]
        public static bool CreateFromSelectionValidate()
        {
            return Selection.GetFiltered<Texture2D>(SelectionMode.Assets).Length >= 1;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Packs the textures below into a Texture2DArray (one per slice, in list order). " +
                "All textures must share the same size, format, and mip count. The slice index is the " +
                "layer the GzPBR shader samples per-vertex from UV3.x.", MessageType.Info);

            // Drag-and-drop area for quickly adding many textures at once.
            HandleDragAndDrop(GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true)));

            EditorGUILayout.LabelField($"Slices ({_slices.Count})", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _slices.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(24));
                _slices[i] = (Texture2D)EditorGUILayout.ObjectField(_slices[i], typeof(Texture2D), false);
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { Swap(i, i - 1); }
                using (new EditorGUI.DisabledScope(i == _slices.Count - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { Swap(i, i + 1); }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { _slices.RemoveAt(i); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Slot")) _slices.Add(null);
            if (GUILayout.Button("Clear")) _slices.Clear();
            EditorGUILayout.EndHorizontal();

            _sRGB = EditorGUILayout.ToggleLeft(
                new GUIContent("sRGB (color data)", "On for color textures (Base Color, Emissive). Off for linear/data textures (Normal, ORM, etc.)."),
                _sRGB);

            EditorGUILayout.Space();

            string err = Validate(_slices);
            using (new EditorGUI.DisabledScope(err != null))
            {
                if (GUILayout.Button("Build Texture2DArray", GUILayout.Height(28)))
                {
                    string dir = "Assets";
                    var first = _slices.FirstOrDefault(t => t != null);
                    if (first != null) dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(first));
                    BuildAndSave(_slices.Where(t => t != null).ToList(), _sRGB, dir, "TextureArray");
                }
            }
            if (err != null)
                EditorGUILayout.HelpBox(err, MessageType.Warning);
        }

        private void Swap(int a, int b)
        {
            var tmp = _slices[a]; _slices[a] = _slices[b]; _slices[b] = tmp;
        }

        private void HandleDragAndDrop(Rect rect)
        {
            GUI.Box(rect, "Drag textures here to add slices", EditorStyles.helpBox);
            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && rect.Contains(e.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                        if (obj is Texture2D tex) _slices.Add(tex);
                    e.Use();
                }
            }
        }

        // ---- Core build logic (also reused by the context menu) ----

        private static string Validate(List<Texture2D> texs)
        {
            var valid = texs.Where(t => t != null).ToList();
            if (valid.Count == 0) return "Add at least one texture.";
            var first = valid[0];
            foreach (var t in valid)
            {
                if (t.width != first.width || t.height != first.height)
                    return $"'{t.name}' is {t.width}x{t.height}, but the first texture is {first.width}x{first.height}. All slices must match.";
                if (t.format != first.format)
                    return $"'{t.name}' is {t.format}, but the first texture is {first.format}. All slices must share a format.";
                if (t.mipmapCount != first.mipmapCount)
                    return $"'{t.name}' has {t.mipmapCount} mips, but the first texture has {first.mipmapCount}. Mip counts must match.";
            }
            return null;
        }

        private static void BuildAndSave(List<Texture2D> texs, bool sRGB, string defaultDir, string defaultName)
        {
            string err = Validate(texs);
            if (err != null) { EditorUtility.DisplayDialog("Texture Array Builder", err, "OK"); return; }

            if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
            {
                EditorUtility.DisplayDialog("Texture Array Builder",
                    "Graphics.CopyTexture is unavailable on this platform, so the array can't be assembled.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Texture2DArray", defaultName, "asset", "Choose where to save the texture array", defaultDir);
            if (string.IsNullOrEmpty(path)) return;

            var valid = texs.Where(t => t != null).ToList();
            var first = valid[0];
            int mips = first.mipmapCount;

            var array = new Texture2DArray(first.width, first.height, valid.Count, first.format, mips > 1, !sRGB)
            {
                wrapMode = first.wrapMode,
                filterMode = first.filterMode,
                anisoLevel = first.anisoLevel,
            };

            for (int slice = 0; slice < valid.Count; slice++)
                for (int mip = 0; mip < mips; mip++)
                    Graphics.CopyTexture(valid[slice], 0, mip, array, slice, mip);

            // Replace if an asset already exists at the path so re-builds update in place.
            var existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(array, existing);
                Object.DestroyImmediate(array);
                array = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(array, path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(array);
            Selection.activeObject = array;
            Debug.Log($"[GeenzShade] Built Texture2DArray '{path}' with {valid.Count} slices ({first.width}x{first.height}, {first.format}).");
        }
    }
}
