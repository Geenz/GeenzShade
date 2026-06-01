/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GeenzShade
{
    // Utility (+ public API) for baking GzPBR auxiliary data textures.
    //
    // Two flows:
    //  - Single swatch: read one material's params (IOR, Iridescence IOR, Sheen
    //    Rim, Cull Mode) into a solid texture and assign it back — no re-entry.
    //  - Combined from a Renderer: per selected material slot (= submesh), encode
    //    that material's params and rasterize ONLY that submesh's UV footprint into
    //    one shared texture. Other submeshes are never touched, so overlapping
    //    submesh UVs are the artist's call (uncheck slots to scope narrowly). The
    //    result is assigned back to the chosen materials.
    public class GzAuxDataBaker : EditorWindow
    {
        private enum BakeMode { BakeOnly, BakeAndAssign }
        private BakeMode _bakeMode = BakeMode.BakeAndAssign;

        // Encode remap ranges (x = min, y = max). Defaults match the shader's decode.
        private Vector2 _iorRange = new Vector2(GzAuxData.IorMin, GzAuxData.IorMax);
        private Vector2 _iridIorRange = new Vector2(GzAuxData.IridescenceIorMin, GzAuxData.IridescenceIorMax);
        private Vector2 _sheenRimRange = new Vector2(GzAuxData.SheenRimMin, GzAuxData.SheenRimMax);

        // Second aux page: per-variant iridescence thickness range. Optional; writes a
        // sibling "<name>_Thickness" texture. _thicknessRange is the nm storage range.
        private bool _bakeThickness = false;
        private Vector2 _thicknessRange = new Vector2(0f, 2000f);

        // Output path. If folder + name are set, bakes write straight there
        // (re-baking the same path updates in place); otherwise a save dialog opens.
        private string _outputFolder = "Assets";
        private string _outputName = "AuxData";

        // Single-swatch state (params read from the material — never re-entered)
        private int _solidSize = 4;
        private Material _target;

        // Combined (renderer) state
        private Renderer _renderer;
        private bool[] _include;
        private int _resolution = 256;
        private int _uvChannel = 0;
        private Vector2 _scroll;

        [MenuItem("GeenzTools/GeenzShade/Aux Data Baker")]
        public static void Open()
        {
            var w = GetWindow<GzAuxDataBaker>("Aux Data Baker");
            w.minSize = new Vector2(340, 420);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("R: IOR (0-4)   G: Iridescence IOR (0-4)   B: Face Cull (0=off, 0.5=back, 1=front)   A: Sheen Rim Boost (0-10)", MessageType.None);

            EditorGUILayout.Space();
            _bakeMode = (BakeMode)EditorGUILayout.EnumPopup(
                new GUIContent("Bake Mode", "Bake Only: just write the texture asset. Bake And Assign: also assign it to the material(s) and enable the Aux Data feature (and Texture face culling where a cull mode applies)."),
                _bakeMode);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField(
                new GUIContent("Folder", "Project-relative output folder (e.g. Assets/Baked). Leave blank to be prompted each bake."), _outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                string abs = EditorUtility.OpenFolderPanel("Output Folder",
                    string.IsNullOrEmpty(_outputFolder) ? "Assets" : _outputFolder, "");
                if (!string.IsNullOrEmpty(abs))
                {
                    string dataPath = Application.dataPath; // <project>/Assets
                    if (abs == dataPath || abs.StartsWith(dataPath + "/"))
                        _outputFolder = "Assets" + abs.Substring(dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("Output Folder", "Folder must be inside the project's Assets folder.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();
            _outputName = EditorGUILayout.TextField(new GUIContent("Name", "Base file name (no extension)."), _outputName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encode Ranges (Min, Max)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("How param values map to 0-1 in the texture. The shader must decode with the same ranges.", MessageType.None);
            _iorRange = EditorGUILayout.Vector2Field("IOR", _iorRange);
            _iridIorRange = EditorGUILayout.Vector2Field("Iridescence IOR", _iridIorRange);
            _sheenRimRange = EditorGUILayout.Vector2Field("Sheen Rim Boost", _sheenRimRange);

            _bakeThickness = EditorGUILayout.ToggleLeft(
                new GUIContent("Bake Thickness Range (2nd texture)", "Also bake a second texture packing each material's iridescence thickness Min/Max (R/G). On assign, enables Aux Thickness Range and points the material at it."),
                _bakeThickness);
            if (_bakeThickness)
            {
                EditorGUI.indentLevel++;
                _thicknessRange = EditorGUILayout.Vector2Field(
                    new GUIContent("Thickness Storage (nm)", "The nm range thickness Min/Max normalize into for storage. Must match the material's Aux Thickness Range."),
                    _thicknessRange);
                EditorGUI.indentLevel--;
            }

            // ---------- Single swatch (from a material) ----------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Single Swatch (from a Material)", EditorStyles.boldLabel);
            _target = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Material", "Reads this material's IOR, Iridescence IOR, Sheen Rim Boost and Cull Mode, bakes a solid aux swatch, and assigns it back (enabling the Aux Data feature)."),
                _target, typeof(Material), false);
            _solidSize = Mathf.Clamp(EditorGUILayout.IntField("Texture Size", _solidSize), 1, 64);

            if (_target != null && !_target.HasProperty("_AuxDataTexture"))
                EditorGUILayout.HelpBox("Material isn't a GzPBR material (no Aux Data slot).", MessageType.Warning);

            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (_target != null)
                {
                    float ior = _target.HasProperty("_IOR") ? _target.GetFloat("_IOR") : 1.5f;
                    float iir = _target.HasProperty("_IridescenceIOR") ? _target.GetFloat("_IridescenceIOR") : 1.3f;
                    float rim = _target.HasProperty("_SheenRimBoost") ? _target.GetFloat("_SheenRimBoost") : 1f;
                    EditorGUILayout.LabelField("From material",
                        $"IOR {ior:0.##}, IridIOR {iir:0.##}, Cull {ReadCull(_target)}, Rim {rim:0.##}");
                }
                if (GUILayout.Button("Bake Swatch..."))
                {
                    string path = ResolveOutputPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var tex = BakeSolidFromMaterial(_target, _solidSize, _iorRange, _iridIorRange, _sheenRimRange);
                        tex = SaveAndSelect(tex, path);
                        if (_bakeMode == BakeMode.BakeAndAssign && _target.HasProperty("_AuxDataTexture"))
                            AssignToMaterial(_target, tex, ReadCull(_target), _iorRange, _iridIorRange, _sheenRimRange);

                        if (_bakeThickness && _target.HasProperty("_AuxDataTexture2"))
                        {
                            var t2 = BakeSolidColor(EncodeThicknessFromMaterial(_target, _thicknessRange), _solidSize);
                            t2 = SaveAndSelect(t2, ThicknessPath(path));
                            if (_bakeMode == BakeMode.BakeAndAssign)
                                AssignThicknessToMaterial(_target, t2, _thicknessRange);
                        }
                    }
                }
            }

            // ---------- Combined from renderer ----------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Combined From Renderer (UV-space)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bakes the checked material slots into one texture using each submesh's UVs (slot = submesh). " +
                "Each material's own params (incl. Cull Mode -> B) are read and written only where that submesh's UVs land. " +
                "UVs are not validated — uncheck slots to scope narrowly if submeshes overlap in UV space.", MessageType.Info);

            _renderer = (Renderer)EditorGUILayout.ObjectField("Source Renderer", _renderer, typeof(Renderer), true);

            Mesh mesh = GetMesh(_renderer);
            if (_renderer != null && mesh == null)
                EditorGUILayout.HelpBox("Renderer has no readable mesh (need a MeshRenderer+MeshFilter or SkinnedMeshRenderer).", MessageType.Warning);

            if (_renderer != null && mesh != null)
            {
                var mats = _renderer.sharedMaterials;
                if (_include == null || _include.Length != mats.Length)
                {
                    _include = new bool[mats.Length];
                    for (int i = 0; i < _include.Length; i++) _include[i] = true;
                }

                EditorGUILayout.LabelField($"Material Slots ({mats.Length}, submeshes: {mesh.subMeshCount})");
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(140));
                for (int i = 0; i < mats.Length; i++)
                {
                    string label = $"Slot {i}: {(mats[i] != null ? mats[i].name : "(none)")}";
                    bool valid = mats[i] != null && i < mesh.subMeshCount;
                    using (new EditorGUI.DisabledScope(!valid))
                        _include[i] = EditorGUILayout.ToggleLeft(label, _include[i] && valid);
                }
                EditorGUILayout.EndScrollView();

                _resolution = Mathf.Clamp(EditorGUILayout.IntField("Resolution", _resolution), 4, 4096);
                _uvChannel = EditorGUILayout.Popup(
                    new GUIContent("Rasterize From UV", "Which mesh UV set to rasterize submesh footprints from. Ensure the aux texture is sampled by this same channel at runtime (artist's responsibility)."),
                    _uvChannel, new[] { "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7" });

                if (GUILayout.Button("Bake Combined...", GUILayout.Height(26)))
                {
                    string path = ResolveOutputPath();
                    if (!string.IsNullOrEmpty(path))
                    {
                        var tex = BakeFromRenderer(_renderer, _include, _resolution, _uvChannel, _iorRange, _iridIorRange, _sheenRimRange);
                        if (tex != null)
                        {
                            tex = SaveAndSelect(tex, path);
                            for (int i = 0; _bakeMode == BakeMode.BakeAndAssign && i < mats.Length; i++)
                            {
                                if (_include != null && i < _include.Length && _include[i]
                                    && mats[i] != null && mats[i].HasProperty("_AuxDataTexture"))
                                {
                                    AssignToMaterial(mats[i], tex, ReadCull(mats[i]), _iorRange, _iridIorRange, _sheenRimRange);
                                }
                            }

                            if (_bakeThickness)
                            {
                                var t2 = BakeRendererWithEncoder(_renderer, _include, _resolution, _uvChannel,
                                    m => EncodeThicknessFromMaterial(m, _thicknessRange));
                                if (t2 != null)
                                {
                                    t2 = SaveAndSelect(t2, ThicknessPath(path));
                                    for (int i = 0; _bakeMode == BakeMode.BakeAndAssign && i < mats.Length; i++)
                                    {
                                        if (_include != null && i < _include.Length && _include[i]
                                            && mats[i] != null && mats[i].HasProperty("_AuxDataTexture2"))
                                        {
                                            AssignThicknessToMaterial(mats[i], t2, _thicknessRange);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Resolve where to write. Uses the configured Folder + Name when both are set
        // (creating the folder if needed); otherwise opens a save dialog.
        private string ResolveOutputPath()
        {
            if (!string.IsNullOrEmpty(_outputFolder) && _outputFolder.StartsWith("Assets") && !string.IsNullOrEmpty(_outputName))
            {
                EnsureFolder(_outputFolder);
                return $"{_outputFolder}/{_outputName}.asset";
            }
            return EditorUtility.SaveFilePanelInProject(
                "Save Aux Data Texture", string.IsNullOrEmpty(_outputName) ? "AuxData" : _outputName, "asset",
                "Save the baked aux data texture", string.IsNullOrEmpty(_outputFolder) ? "Assets" : _outputFolder);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // Create the asset, or update it in place if one already exists at the path
        // (preserves the GUID so materials keep their reference on re-bake). Returns
        // the persisted texture to use for assignment.
        private static Texture2D SaveAndSelect(Texture2D tex, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex, existing);
                UnityEngine.Object.DestroyImmediate(tex);
                tex = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(tex, path);
            }
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(tex);
            Selection.activeObject = tex;
            return tex;
        }

        // ============================================================
        // Public API for project tooling
        // ============================================================

        /// <summary>Build a solid aux data Texture2D encoding one parameter set.</summary>
        public static Texture2D BakeSolid(float ior, float iridescenceIOR, GzFaceCull faceCull, float sheenRimBoost, int size = 4)
            => BakeSolidColor(GzAuxData.Encode(ior, iridescenceIOR, faceCull, sheenRimBoost), size);

        /// <summary>Build a solid aux data Texture2D from a material's existing params
        /// (IOR, Iridescence IOR, Sheen Rim Boost, Cull Mode). No values are re-entered.
        /// Optional remap ranges (x=min, y=max); null = canonical ranges.</summary>
        public static Texture2D BakeSolidFromMaterial(Material material, int size = 4,
            Vector2? iorRange = null, Vector2? iridIorRange = null, Vector2? sheenRimRange = null)
            => BakeSolidColor(EncodeFromMaterial(material, iorRange, iridIorRange, sheenRimRange), size);

        private static Texture2D BakeSolidColor(Color c, int size)
        {
            size = Mathf.Max(1, size);
            var tex = NewAuxTexture(size, "AuxData");
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// Bake the chosen material slots of a Renderer into one UV-space aux texture.
        /// For each included slot, the matching submesh's UVs (from uvChannel, 0-7) are
        /// rasterized with that material's encoded params (Cull Mode -> B channel).
        /// includeSlots == null bakes all slots. UVs are not validated; overlapping submesh
        /// UVs overwrite (later slot wins). The shader must sample the aux texture from the
        /// same UV channel at runtime — that's the artist's responsibility.
        /// </summary>
        public static Texture2D BakeFromRenderer(Renderer renderer, bool[] includeSlots, int resolution, int uvChannel = 0,
            Vector2? iorRange = null, Vector2? iridIorRange = null, Vector2? sheenRimRange = null)
        {
            return BakeRendererWithEncoder(renderer, includeSlots, resolution, uvChannel,
                m => EncodeFromMaterial(m, iorRange, iridIorRange, sheenRimRange));
        }

        // Rasterization core: for each included slot, rasterize that submesh's UV
        // footprint filled with encode(material). Shared by the aux and thickness bakes.
        private static Texture2D BakeRendererWithEncoder(Renderer renderer, bool[] includeSlots, int resolution,
            int uvChannel, Func<Material, Color> encode)
        {
            if (renderer == null) return null;
            Mesh mesh = GetMesh(renderer);
            if (mesh == null) return null;
            var uv = new List<Vector2>();
            mesh.GetUVs(Mathf.Clamp(uvChannel, 0, 7), uv);
            if (uv.Count == 0) return null;

            resolution = Mathf.Clamp(resolution, 4, 4096);
            var px = new Color[resolution * resolution]; // uncovered texels stay (0,0,0,0)
            var mats = renderer.sharedMaterials;

            for (int slot = 0; slot < mats.Length && slot < mesh.subMeshCount; slot++)
            {
                if (includeSlots != null && slot < includeSlots.Length && !includeSlots[slot]) continue;
                if (mats[slot] == null) continue;

                Color col = encode(mats[slot]);
                int[] tris = mesh.GetTriangles(slot);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    if (i0 >= uv.Count || i1 >= uv.Count || i2 >= uv.Count) continue;
                    FillTriangle(px, resolution, resolution,
                        uv[i0] * resolution, uv[i1] * resolution, uv[i2] * resolution, col);
                }
            }

            var tex = NewAuxTexture(resolution, "AuxData");
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static Texture2D NewAuxTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false, true) // linear data
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point, // no blending across regions
            };
        }

        private static Mesh GetMesh(Renderer r)
        {
            if (r == null) return null;
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static GzFaceCull ReadCull(Material m)
        {
            if (m == null) return GzFaceCull.Off;
            // In Texture cull mode the render _Cull is forced Off (double-sided), so the
            // artist's intent lives in _FaceCullBake. Prefer it there; otherwise read the
            // hardware _Cull. UnityEngine.Rendering.CullMode: Off=0, Front=1, Back=2.
            bool textureMode = m.HasProperty("_FaceCullSource") && m.GetFloat("_FaceCullSource") >= 0.5f;
            float cull;
            if (textureMode && m.HasProperty("_FaceCullBake")) cull = m.GetFloat("_FaceCullBake");
            else if (m.HasProperty("_Cull")) cull = m.GetFloat("_Cull");
            else return GzFaceCull.Off;
            switch (Mathf.RoundToInt(cull))
            {
                case 1: return GzFaceCull.Front;
                case 2: return GzFaceCull.Back;
                default: return GzFaceCull.Off;
            }
        }

        private static Color EncodeFromMaterial(Material m,
            Vector2? iorRange = null, Vector2? iridIorRange = null, Vector2? sheenRimRange = null)
        {
            float ior = (m != null && m.HasProperty("_IOR")) ? m.GetFloat("_IOR") : 1.5f;
            float iir = (m != null && m.HasProperty("_IridescenceIOR")) ? m.GetFloat("_IridescenceIOR") : 1.3f;
            float rim = (m != null && m.HasProperty("_SheenRimBoost")) ? m.GetFloat("_SheenRimBoost") : 1f;

            Vector2 ir = iorRange ?? new Vector2(GzAuxData.IorMin, GzAuxData.IorMax);
            Vector2 ii = iridIorRange ?? new Vector2(GzAuxData.IridescenceIorMin, GzAuxData.IridescenceIorMax);
            Vector2 sr = sheenRimRange ?? new Vector2(GzAuxData.SheenRimMin, GzAuxData.SheenRimMax);

            return GzAuxData.Encode(ior, iir, ReadCull(m), rim, ir.x, ir.y, ii.x, ii.y, sr.x, sr.y);
        }

        // Encode a material's iridescence thickness Min/Max into R/G, normalized into the
        // nm storage range. B/A reserved (second aux page).
        private static Color EncodeThicknessFromMaterial(Material m, Vector2 range)
        {
            float tmin = (m != null && m.HasProperty("_IridescenceThicknessMin")) ? m.GetFloat("_IridescenceThicknessMin") : 100f;
            float tmax = (m != null && m.HasProperty("_IridescenceThicknessMax")) ? m.GetFloat("_IridescenceThicknessMax") : 400f;
            return new Color(Mathf.InverseLerp(range.x, range.y, tmin), Mathf.InverseLerp(range.x, range.y, tmax), 0f, 0f);
        }

        // Sibling asset path with a "_Thickness" suffix next to the aux texture.
        private static string ThicknessPath(string auxPath)
        {
            string dir = Path.GetDirectoryName(auxPath);
            string name = Path.GetFileNameWithoutExtension(auxPath);
            return $"{dir}/{name}_Thickness.asset".Replace('\\', '/');
        }

        // Assign the thickness page to a material, enable USE_AUX_THICKNESS, and write the
        // storage range so the shader's decode matches the bake.
        private static void AssignThicknessToMaterial(Material mat, Texture2D tex, Vector2 range)
        {
            if (!mat.HasProperty("_AuxDataTexture2")) return;
            mat.SetTexture("_AuxDataTexture2", tex);
            mat.SetFloat("_UseAuxThickness", 1f);
            mat.EnableKeyword("USE_AUX_THICKNESS");
            SetIfHas(mat, "_AuxThicknessRangeMin", range.x);
            SetIfHas(mat, "_AuxThicknessRangeMax", range.y);
            EditorUtility.SetDirty(mat);
        }

        // Assign a baked aux texture to a material's 2D Aux Data slot, turn on the
        // Auxiliary Data feature, and write the decode ranges so they match the bake
        // (this is what prevents iridescence/IOR artifacts from range drift). If a
        // cull mode is involved, switch Face Cull Source to Texture (needs Cull Off).
        private static void AssignToMaterial(Material mat, Texture2D tex, GzFaceCull cull,
            Vector2 iorRange, Vector2 iridIorRange, Vector2 sheenRimRange)
        {
            mat.SetTexture("_AuxDataTexture", tex);
            mat.SetFloat("_UseAuxData", 1f);
            mat.EnableKeyword("USE_AUX_DATA");

            SetIfHas(mat, "_AuxIORMin", iorRange.x);
            SetIfHas(mat, "_AuxIORMax", iorRange.y);
            SetIfHas(mat, "_AuxIridescenceIORMin", iridIorRange.x);
            SetIfHas(mat, "_AuxIridescenceIORMax", iridIorRange.y);
            SetIfHas(mat, "_AuxSheenRimMin", sheenRimRange.x);
            SetIfHas(mat, "_AuxSheenRimMax", sheenRimRange.y);

            if (cull != GzFaceCull.Off && mat.HasProperty("_FaceCullSource"))
            {
                mat.SetFloat("_FaceCullSource", 1f); // Texture
                mat.EnableKeyword("_FACECULLSOURCE_TEXTURE");
                mat.DisableKeyword("_FACECULLSOURCE_HARDWARE");
                // Record the intent so re-bakes don't read the forced-Off _Cull, then
                // force Cull Off for double-sided rendering. GzFaceCull -> CullMode.
                int cullModeInt = cull == GzFaceCull.Back ? 2 : (cull == GzFaceCull.Front ? 1 : 0);
                SetIfHas(mat, "_FaceCullBake", cullModeInt);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0); // Off — in-shader culling needs double-sided
            }

            EditorUtility.SetDirty(mat);
        }

        private static void SetIfHas(Material mat, string prop, float value)
        {
            if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
        }

        // Solid-colour triangle fill in pixel space (no interpolation, no AA).
        private static void FillTriangle(Color[] px, int w, int h, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (Mathf.Abs(Edge(a, b, c)) < 1e-7f) return; // degenerate

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float e0 = Edge(b, c, p);
                    float e1 = Edge(c, a, p);
                    float e2 = Edge(a, b, p);
                    bool inside = (e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0);
                    if (inside) px[y * w + x] = col;
                }
            }
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 c)
            => (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
    }
}
