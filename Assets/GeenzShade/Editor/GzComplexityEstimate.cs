/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Jonathan "Geenz" Goodman
 */

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeenzShade {
    internal static class GzComplexityEstimate {
        internal const int ResourceLimit = 16;
        internal const int WarningThreshold = 12;

        private const int ContainerChunkCountOffset = 28;
        private const int ContainerChunkTableOffset = 32;
        private const int ChunkHeaderSize = 8;
        private const int CodeHeaderSize = 8;
        private const int OpcodeMask = 0x7FF;
        private const int InstructionLengthShift = 24;
        private const int InstructionLengthMask = 0x7F;
        private const int ResourceDimensionShift = 11;
        private const int ResourceDimensionMask = 0x1F;
        private const int OpcodeCustomData = 53;
        private const int OpcodeDeclareResource = 88;
        private const int OpcodeDeclareSampler = 90;

        private static readonly string[] ForwardBaseDynamicKeywords = { "DIRECTIONAL", "LIGHTPROBE_SH", "SHADOWS_SCREEN" };
        private static readonly string[] ForwardBaseLightmappedKeywords = { "DIRECTIONAL", "LIGHTMAP_ON", "DIRLIGHTMAP_COMBINED", "SHADOWS_SHADOWMASK" };
        private static readonly string[] ForwardAddKeywords = { "POINT_COOKIE", "SHADOWS_CUBE", "SHADOWS_SOFT" };
        private static readonly string[] ShadowCasterKeywords = { "SHADOWS_DEPTH" };
        private static readonly string[] MaterialTextureKeywords = { "USE_BASE_COLOR_TEXTURE", "USE_ORM_TEXTURE", "USE_NORMAL_TEXTURE", "USE_EMISSIVE_TEXTURE", "USE_SPECULAR_TEXTURE", "USE_CLEARCOAT_NORMAL_TEXTURE", "USE_CLEARCOAT_IRIDESCENCE_TEXTURE", "USE_SHEEN_TEXTURE", "USE_DIFFUSE_TRANSMISSION_TEXTURE" };

        private static readonly Dictionary<string, List<Row>> cache = new Dictionary<string, List<Row>>();
        private static readonly List<Row> pending = new List<Row>();
        private static MaterialEditor activeEditor;
        private static string activeKey;

        internal class Row {
            public int SubshaderIndex;
            public string TierLabel;
            public string PassName;
            public string Label;
            public bool Measured;
            public string PcFailure;
            public string QuestFailure;
            public int VertexInstructions = -1;
            public int FragmentInstructions = -1;
            public int VertexAlu = -1;
            public int FragmentAlu = -1;
            public int FragmentTextureInstructions = -1;
            public int FragmentSamplers = -1;
            public int FragmentTextures = -1;
            public int QuestTextureUnits = -1;
            public Shader TargetShader;
            public int PassIndex;
            public string[] PcKeywords;
            public string[] QuestKeywords;
        }

        internal static List<Row> GetRows(Material material, MaterialEditor editor) {
            activeEditor = editor;

            Shader shader = material.shader;
            string[] sortedKeywords = material.shaderKeywords;
            Array.Sort(sortedKeywords, StringComparer.Ordinal);
            string fogKeyword = GetFogKeyword();
            string key = BuildKey(material, shader, sortedKeywords, fogKeyword);

            List<Row> rows;

            if (!cache.TryGetValue(key, out rows)) {
                rows = BuildRows(material, shader, sortedKeywords, fogKeyword);
                cache[key] = rows;
            }

            if (key != activeKey) {
                activeKey = key;
                pending.Clear();

                foreach (Row row in rows) {
                    if (!row.Measured) {
                        pending.Add(row);
                    }
                }

                if (pending.Count > 0) {
                    EditorApplication.update -= MeasureNext;
                    EditorApplication.update += MeasureNext;
                }
            }

            return rows;
        }

        internal static string DescribeSamplerUsers(Material material) {
            List<string> users = new List<string>();

            if (!material.IsKeywordEnabled("USE_SHARED_SAMPLER")) {
                users.Add("per-texture samplers");
            }

            AddIfEnabled(users, material, "USE_SSR", "SSR (Tier 0)");
            AddIfEnabled(users, material, "USE_AUX_DATA", "Aux Data");
            AddIfEnabled(users, material, "USE_AUX_THICKNESS", "Aux Thickness");
            AddIfEnabled(users, material, "_LTCGI_WORLD", "LTCGI World");
            AddIfEnabled(users, material, "_LTCGI_AVATAR", "LTCGI Avatar");
            AddIfEnabled(users, material, "USE_VRC_LIGHT_VOLUMES", "VRC Light Volumes");
            AddIfEnabled(users, material, "USE_SOFT_DEPTH", "Soft Depth");
            AddIfEnabled(users, material, "USE_SS_SHADOWS", "Screen-Space Shadows");
            AddIfEnabled(users, material, "USE_TEXTURE_ARRAYS", "Texture Arrays");

            int textureCount = 0;

            foreach (string textureKeyword in MaterialTextureKeywords) {
                if (material.IsKeywordEnabled(textureKeyword)) {
                    textureCount++;
                }
            }

            if (textureCount > 0) {
                users.Add(textureCount + " material textures");
            }

            if (users.Count == 0) {
                return "none";
            }

            return string.Join(", ", users);
        }

        private class Issue {
            public int Peak = -1;
            public List<string> Tiers = new List<string>();
            public List<string> Passes = new List<string>();
        }

        internal static List<string> DescribeIssues(List<Row> rows, Material material) {
            List<string> allTiers = new List<string>();
            Issue pcSamplers = new Issue();
            Issue questUnits = new Issue();
            Dictionary<string, Issue> pcErrors = new Dictionary<string, Issue>();
            Dictionary<string, Issue> questErrors = new Dictionary<string, Issue>();
            List<string> pcErrorOrder = new List<string>();
            List<string> questErrorOrder = new List<string>();

            foreach (Row row in rows) {
                if (!allTiers.Contains(row.TierLabel)) {
                    allTiers.Add(row.TierLabel);
                }

                if (!row.Measured) {
                    continue;
                }

                if (row.PcFailure != null) {
                    RecordError(pcErrors, pcErrorOrder, row.PcFailure, row);
                } else if (row.FragmentSamplers >= WarningThreshold) {
                    RecordIssue(pcSamplers, row, row.FragmentSamplers);
                }

                if (row.QuestFailure != null) {
                    RecordError(questErrors, questErrorOrder, row.QuestFailure, row);
                } else if (row.QuestTextureUnits >= WarningThreshold) {
                    RecordIssue(questUnits, row, row.QuestTextureUnits);
                }
            }

            List<string> messages = new List<string>();

            if (pcSamplers.Peak >= 0) {
                string message = "PC samplers at " + pcSamplers.Peak + "/" + ResourceLimit + " in " + DescribeWhere(pcSamplers, allTiers) + ". Using: " + DescribeSamplerUsers(material) + ".";

                if (!material.IsKeywordEnabled("USE_SHARED_SAMPLER")) {
                    message += " Turn on Shared Texture Sampler (Advanced Render Mode) to free PC samplers.";
                }

                messages.Add(message);
            }

            if (questUnits.Peak >= 0) {
                messages.Add("Quest texture units at " + questUnits.Peak + "/" + ResourceLimit + " in " + DescribeWhere(questUnits, allTiers) + ". Using: " + DescribeSamplerUsers(material) + ".");
            }

            foreach (string error in pcErrorOrder) {
                messages.Add("PC compile error in " + DescribeWhere(pcErrors[error], allTiers) + ": " + error.TrimEnd('.') + ".");
            }

            foreach (string error in questErrorOrder) {
                messages.Add("Quest compile error in " + DescribeWhere(questErrors[error], allTiers) + ": " + error.TrimEnd('.') + ".");
            }

            return messages;
        }

        private static void RecordError(Dictionary<string, Issue> issues, List<string> order, string message, Row row) {
            Issue issue;

            if (!issues.TryGetValue(message, out issue)) {
                issue = new Issue();
                issues.Add(message, issue);
                order.Add(message);
            }

            RecordIssue(issue, row, -1);
        }

        private static void RecordIssue(Issue issue, Row row, int value) {
            issue.Peak = Math.Max(issue.Peak, value);

            if (!issue.Tiers.Contains(row.TierLabel)) {
                issue.Tiers.Add(row.TierLabel);
            }

            if (!issue.Passes.Contains(row.PassName)) {
                issue.Passes.Add(row.PassName);
            }
        }

        private static string DescribeWhere(Issue issue, List<string> allTiers) {
            string tierText = "all tiers";
            if (issue.Tiers.Count != allTiers.Count) {
                tierText = string.Join(", ", issue.Tiers);
            }

            return string.Join(", ", issue.Passes) + " (" + tierText + ")";
        }

        internal static string FormatCount(int value) {
            if (value < 0) {
                return "n/a";
            }

            return value.ToString();
        }

        internal static string FormatLimit(int value) {
            if (value < 0) {
                return "n/a";
            }

            return value + "/" + ResourceLimit;
        }

        internal static string DescribeRow(Row row) {
            string prefix = row.TierLabel + " " + row.Label + ": ";

            if (!row.Measured) {
                return prefix + "measuring...";
            }

            string pcText = "compile error";
            if (row.PcFailure == null) {
                pcText = "instr VS " + FormatCount(row.VertexInstructions) + " / PS " + FormatCount(row.FragmentInstructions)
                    + ", ALU VS " + FormatCount(row.VertexAlu) + " / PS " + FormatCount(row.FragmentAlu)
                    + ", " + FormatCount(row.FragmentTextureInstructions) + " tex samples"
                    + ", " + FormatLimit(row.FragmentSamplers) + " samplers"
                    + ", " + FormatCount(row.FragmentTextures) + " textures";
            }

            string questText = "compile error";
            if (row.QuestFailure == null) {
                questText = FormatLimit(row.QuestTextureUnits) + " units";
            }

            return prefix + "PC " + pcText + " | Quest " + questText;
        }

        private static void AddIfEnabled(List<string> users, Material material, string keyword, string label) {
            if (material.IsKeywordEnabled(keyword)) {
                users.Add(label);
            }
        }

        private static string GetFogKeyword() {
            if (!RenderSettings.fog) {
                return "";
            }

            FogMode fogMode = RenderSettings.fogMode;

            if (fogMode == FogMode.Linear) {
                return "FOG_LINEAR";
            }

            if (fogMode == FogMode.Exponential) {
                return "FOG_EXP";
            }

            if (fogMode == FogMode.ExponentialSquared) {
                return "FOG_EXP2";
            }

            return "";
        }

        private static string BuildKey(Material material, Shader shader, string[] sortedKeywords, string fogKeyword) {
            string shaderPath = AssetDatabase.GetAssetPath(shader);
            string dependencyHash = AssetDatabase.GetAssetDependencyHash(shaderPath).ToString();

            return dependencyHash + "|" + string.Join(" ", sortedKeywords) + "|" + material.enableInstancing + "|" + fogKeyword;
        }

        private static List<Row> BuildRows(Material material, Shader shader, string[] sortedKeywords, string fogKeyword) {
            List<Row> rows = new List<Row>();
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            // Runtime subshaders past the serialized ones are appended from the FallBack shader.
            int ownSubshaderCount = Math.Min(shaderData.SubshaderCount, shaderData.SerializedSubshaderCount);

            for (int subshaderIndex = 0; subshaderIndex < ownSubshaderCount; subshaderIndex++) {
                ShaderData.Subshader subshader = shaderData.GetSubshader(subshaderIndex);

                if (subshader == null) {
                    continue;
                }

                for (int passIndex = 0; passIndex < subshader.PassCount; passIndex++) {
                    ShaderData.Pass pass = subshader.GetPass(passIndex);

                    if (pass == null) {
                        continue;
                    }

                    AddPassRows(rows, material, shader, sortedKeywords, fogKeyword, subshaderIndex, subshader.LevelOfDetail, passIndex, pass.Name);
                }
            }

            return rows;
        }

        private static void AddPassRows(List<Row> rows, Material material, Shader shader, string[] sortedKeywords, string fogKeyword, int subshaderIndex, int levelOfDetail, int passIndex, string passName) {
            bool forwardBase = string.Equals(passName, "ForwardBase", StringComparison.OrdinalIgnoreCase);
            bool forwardAdd = string.Equals(passName, "ForwardAdd", StringComparison.OrdinalIgnoreCase);
            bool shadowCaster = string.Equals(passName, "ShadowCaster", StringComparison.OrdinalIgnoreCase);

            if (!forwardBase && !forwardAdd && !shadowCaster) {
                return;
            }

            List<string> passKeywords = FilterPassKeywords(shader, subshaderIndex, passName, sortedKeywords);
            string missingPass = null;
            if (passKeywords == null) {
                passKeywords = new List<string>();
                missingPass = "no runtime pass with LightMode " + passName;
            }

            bool instancing = material.enableInstancing;
            int firstNewRow = rows.Count;

            if (forwardBase) {
                rows.Add(NewRow(shader, subshaderIndex, levelOfDetail, passIndex, "ForwardBase", "ForwardBase dynamic",
                    passKeywords, ForwardBaseDynamicKeywords, instancing, fogKeyword, true));
                rows.Add(NewRow(shader, subshaderIndex, levelOfDetail, passIndex, "ForwardBase", "ForwardBase lightmapped",
                    passKeywords, ForwardBaseLightmappedKeywords, instancing, fogKeyword, true));
            }

            if (forwardAdd) {
                rows.Add(NewRow(shader, subshaderIndex, levelOfDetail, passIndex, "ForwardAdd", "ForwardAdd",
                    passKeywords, ForwardAddKeywords, instancing, fogKeyword, true));
            }

            if (shadowCaster) {
                rows.Add(NewRow(shader, subshaderIndex, levelOfDetail, passIndex, "ShadowCaster", "ShadowCaster",
                    passKeywords, ShadowCasterKeywords, instancing, "", false));
            }

            if (missingPass != null) {
                for (int rowIndex = firstNewRow; rowIndex < rows.Count; rowIndex++) {
                    rows[rowIndex].Measured = true;
                    rows[rowIndex].PcFailure = missingPass;
                    rows[rowIndex].QuestFailure = missingPass;
                }
            }
        }

        private static List<string> FilterPassKeywords(Shader shader, int subshaderIndex, string lightMode, string[] materialKeywords) {
            int runtimePassIndex = FindRuntimePass(shader, subshaderIndex, lightMode);

            if (runtimePassIndex < 0) {
                return null;
            }

            PassIdentifier passIdentifier = new PassIdentifier((uint)subshaderIndex, (uint)runtimePassIndex);
            LocalKeyword[] passKeywords = ShaderUtil.GetPassKeywords(shader, passIdentifier);
            HashSet<string> passKeywordNames = new HashSet<string>();

            foreach (LocalKeyword passKeyword in passKeywords) {
                passKeywordNames.Add(passKeyword.name);
            }

            List<string> filtered = new List<string>();

            foreach (string materialKeyword in materialKeywords) {
                if (passKeywordNames.Contains(materialKeyword)) {
                    filtered.Add(materialKeyword);
                }
            }

            return filtered;
        }

        private static int FindRuntimePass(Shader shader, int subshaderIndex, string lightMode) {
            if (subshaderIndex >= shader.subshaderCount) {
                return -1;
            }

            ShaderTagId lightModeTag = new ShaderTagId("LightMode");
            int passCount = shader.GetPassCountInSubshader(subshaderIndex);

            for (int passIndex = 0; passIndex < passCount; passIndex++) {
                string passLightMode = shader.FindPassTagValue(subshaderIndex, passIndex, lightModeTag).name;

                if (string.Equals(passLightMode, lightMode, StringComparison.OrdinalIgnoreCase)) {
                    return passIndex;
                }
            }

            return -1;
        }

        private static Row NewRow(Shader shader, int subshaderIndex, int levelOfDetail, int passIndex, string passName, string label, List<string> passKeywords, string[] builtInKeywords, bool instancing, string fogKeyword, bool stereo) {
            string pcStereoKeyword = "";
            string questStereoKeyword = "";
            if (stereo) {
                pcStereoKeyword = "STEREO_INSTANCING_ON";
                questStereoKeyword = "STEREO_MULTIVIEW_ON";
            }

            Row row = new Row();
            row.TargetShader = shader;
            row.SubshaderIndex = subshaderIndex;
            row.TierLabel = "LOD " + levelOfDetail;
            row.PassIndex = passIndex;
            row.PassName = passName;
            row.Label = label;
            row.PcKeywords = BuildKeywords(passKeywords, builtInKeywords, instancing, fogKeyword, pcStereoKeyword);
            row.QuestKeywords = BuildKeywords(passKeywords, builtInKeywords, instancing, fogKeyword, questStereoKeyword);

            return row;
        }

        private static string[] BuildKeywords(List<string> passKeywords, string[] builtInKeywords, bool instancing, string fogKeyword, string stereoKeyword) {
            List<string> keywords = new List<string>(passKeywords);
            keywords.AddRange(builtInKeywords);

            if (instancing) {
                keywords.Add("INSTANCING_ON");
            }

            if (fogKeyword.Length > 0) {
                keywords.Add(fogKeyword);
            }

            if (stereoKeyword.Length > 0) {
                keywords.Add(stereoKeyword);
            }

            return keywords.ToArray();
        }

        private static void MeasureNext() {
            if (activeEditor == null) {
                pending.Clear();
                activeKey = null;
                EditorApplication.update -= MeasureNext;
                return;
            }

            if (pending.Count == 0) {
                EditorApplication.update -= MeasureNext;
                return;
            }

            Row row = pending[0];
            pending.RemoveAt(0);

            try {
                Measure(row);
            } catch (Exception exception) {
                row.PcFailure = exception.Message;
                row.QuestFailure = exception.Message;
                Debug.LogException(exception);
            }

            activeEditor.Repaint();
        }

        private static void Measure(Row row) {
            row.Measured = true;

            if (row.TargetShader == null) {
                row.PcFailure = "shader unavailable";
                row.QuestFailure = "shader unavailable";
                return;
            }

            ShaderData shaderData = ShaderUtil.GetShaderData(row.TargetShader);
            ShaderData.Subshader subshader = shaderData.GetSubshader(row.SubshaderIndex);

            if (subshader == null) {
                row.PcFailure = "subshader unavailable";
                row.QuestFailure = "subshader unavailable";
                return;
            }

            ShaderData.Pass pass = subshader.GetPass(row.PassIndex);

            if (pass == null) {
                row.PcFailure = "pass unavailable";
                row.QuestFailure = "pass unavailable";
                return;
            }

            ShaderData.VariantCompileInfo vertexInfo = pass.CompileVariant(ShaderType.Vertex, row.PcKeywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, true);
            ShaderData.VariantCompileInfo fragmentInfo = pass.CompileVariant(ShaderType.Fragment, row.PcKeywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, true);
            ShaderData.VariantCompileInfo questInfo = pass.CompileVariant(ShaderType.Vertex, row.QuestKeywords, ShaderCompilerPlatform.GLES3x, BuildTarget.Android);

            RecordFailure(ref row.PcFailure, vertexInfo);
            RecordFailure(ref row.PcFailure, fragmentInfo);
            RecordFailure(ref row.QuestFailure, questInfo);

            if (vertexInfo.Success) {
                int vertexTextureInstructions;
                int vertexSamplers;
                int vertexTextures;
                ParseDxbc(vertexInfo.ShaderData, out row.VertexInstructions, out row.VertexAlu, out vertexTextureInstructions, out vertexSamplers, out vertexTextures);
            }

            if (fragmentInfo.Success) {
                ParseDxbc(fragmentInfo.ShaderData, out row.FragmentInstructions, out row.FragmentAlu, out row.FragmentTextureInstructions, out row.FragmentSamplers, out row.FragmentTextures);
            }

            if (questInfo.Success && questInfo.TextureBindings != null) {
                int questUnits = 0;

                foreach (ShaderData.TextureBindingInfo binding in questInfo.TextureBindings) {
                    questUnits += Math.Max(1, binding.ArraySize);
                }

                row.QuestTextureUnits = questUnits;
            }
        }

        private static void RecordFailure(ref string failure, ShaderData.VariantCompileInfo info) {
            if (info.Success || failure != null) {
                return;
            }

            failure = FirstError(info.Messages);
        }

        private static string FirstError(ShaderMessage[] messages) {
            if (messages == null || messages.Length == 0) {
                return "unknown error";
            }

            foreach (ShaderMessage message in messages) {
                if (message.severity == ShaderCompilerMessageSeverity.Error) {
                    return message.message;
                }
            }

            return messages[0].message;
        }

        // DXBC container: 32-byte header, chunk offset table, then chunks of FourCC, size, data.
        // Unity strips STAT and RDEF, so the SHEX/SHDR token stream is walked and counted instead.
        private static void ParseDxbc(byte[] data, out int instructions, out int alu, out int textureInstructions, out int samplers, out int textures) {
            instructions = -1;
            alu = -1;
            textureInstructions = -1;
            samplers = -1;
            textures = -1;

            int containerStart = FindMagic(data);

            if (containerStart < 0) {
                return;
            }

            int codeChunk = FindChunk(data, containerStart, "SHEX");

            if (codeChunk < 0) {
                codeChunk = FindChunk(data, containerStart, "SHDR");
            }

            if (codeChunk < 0) {
                return;
            }

            WalkTokens(data, codeChunk + ChunkHeaderSize, ReadInt(data, codeChunk + 4), out instructions, out alu, out textureInstructions, out samplers, out textures);
        }

        private static void WalkTokens(byte[] data, int codeStart, int codeSize, out int instructions, out int alu, out int textureInstructions, out int samplers, out int textures) {
            instructions = -1;
            alu = -1;
            textureInstructions = -1;
            samplers = -1;
            textures = -1;

            int versionToken = ReadInt(data, codeStart);
            int lengthToken = ReadInt(data, codeStart + 4);

            if (codeSize < CodeHeaderSize || lengthToken < 2) {
                return;
            }

            int majorVersion = (versionToken >> 4) & 0xF;
            int minorVersion = versionToken & 0xF;

            if (majorVersion == 5 && minorVersion >= 1) {
                return;
            }

            int streamEnd = (int)Math.Min(codeStart + 4L * lengthToken, Math.Min(codeStart + (long)codeSize, data.Length));
            int position = codeStart + CodeHeaderSize;
            int instructionTotal = 0;
            int aluTotal = 0;
            int textureInstructionTotal = 0;
            int samplerTotal = 0;
            int textureTotal = 0;

            while (position < streamEnd) {
                int token = ReadInt(data, position);
                int opcode = token & OpcodeMask;
                int length = (token >> InstructionLengthShift) & InstructionLengthMask;

                if (opcode == OpcodeCustomData) {
                    length = ReadInt(data, position + 4);

                    if (length < 2) {
                        return;
                    }
                }

                if (length <= 0 || position + 4L * length > streamEnd) {
                    return;
                }

                if (opcode == OpcodeDeclareSampler) {
                    samplerTotal++;
                }

                if (opcode == OpcodeDeclareResource) {
                    int dimension = (token >> ResourceDimensionShift) & ResourceDimensionMask;

                    if (dimension >= 2 && dimension <= 10) {
                        textureTotal++;
                    }
                }

                if (!IsDeclaration(opcode) && !IsSkipped(opcode)) {
                    instructionTotal++;

                    if (IsTextureInstruction(opcode)) {
                        textureInstructionTotal++;
                    } else if (!IsResourceQuery(opcode) && !IsFlowControl(opcode)) {
                        aluTotal++;
                    }
                }

                position += 4 * length;
            }

            instructions = instructionTotal;
            alu = aluTotal;
            textureInstructions = textureInstructionTotal;
            samplers = samplerTotal;
            textures = textureTotal;
        }

        private static bool IsDeclaration(int opcode) {
            return (opcode >= 88 && opcode <= 106)
                || (opcode >= 113 && opcode <= 116)
                || (opcode >= 143 && opcode <= 162)
                || opcode == 206;
        }

        private static bool IsSkipped(int opcode) {
            return opcode == OpcodeCustomData || opcode == 107 || opcode == 112 || opcode == 209 || opcode == 218 || opcode == 235;
        }

        private static bool IsTextureInstruction(int opcode) {
            return opcode == 45 || opcode == 46
                || (opcode >= 69 && opcode <= 74)
                || opcode == 108 || opcode == 109
                || (opcode >= 126 && opcode <= 128)
                || (opcode >= 219 && opcode <= 233);
        }

        private static bool IsResourceQuery(int opcode) {
            return opcode == 61 || opcode == 110 || opcode == 111 || opcode == 121;
        }

        private static bool IsFlowControl(int opcode) {
            return (opcode >= 2 && opcode <= 8)
                || opcode == 10 || opcode == 13 || opcode == 18
                || (opcode >= 21 && opcode <= 23)
                || opcode == 31 || opcode == 44 || opcode == 48 || opcode == 58 || opcode == 62 || opcode == 63 || opcode == 76 || opcode == 120;
        }

        private static int FindMagic(byte[] data) {
            if (data == null) {
                return -1;
            }

            for (int index = 0; index + 4 <= data.Length; index++) {
                if (data[index] == (byte)'D' && data[index + 1] == (byte)'X' && data[index + 2] == (byte)'B' && data[index + 3] == (byte)'C') {
                    return index;
                }
            }

            return -1;
        }

        private static int FindChunk(byte[] data, int containerStart, string fourCc) {
            int chunkCount = ReadInt(data, containerStart + ContainerChunkCountOffset);

            if (chunkCount < 0) {
                return -1;
            }

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
                int chunkOffset = ReadInt(data, containerStart + ContainerChunkTableOffset + chunkIndex * 4);

                if (chunkOffset < 0) {
                    return -1;
                }

                int chunkStart = containerStart + chunkOffset;

                if (MatchesFourCc(data, chunkStart, fourCc)) {
                    return chunkStart;
                }
            }

            return -1;
        }

        private static bool MatchesFourCc(byte[] data, int offset, string fourCc) {
            if (offset < 0 || offset > data.Length - ChunkHeaderSize) {
                return false;
            }

            for (int index = 0; index < 4; index++) {
                if (data[offset + index] != (byte)fourCc[index]) {
                    return false;
                }
            }

            return true;
        }

        private static int ReadInt(byte[] data, int offset) {
            if (offset < 0 || offset > data.Length - 4) {
                return -1;
            }

            return BitConverter.ToInt32(data, offset);
        }
    }
}
