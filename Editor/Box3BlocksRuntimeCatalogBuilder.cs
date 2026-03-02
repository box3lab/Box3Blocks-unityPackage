using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Box3Blocks.Editor
{
    internal static class Box3BlocksRuntimeCatalogBuilder
    {
        private const string BlockTextureFolder = "Packages/com.box3lab.box3/Assets/block";
        private const string BlockSpecPath = "Packages/com.box3lab.box3/Assets/block-spec.json";
        private const string RuntimeFolder = "Assets/Box3/Runtime";
        private const string RuntimeCatalogPath = "Assets/Box3/Runtime/Box3BlocksCatalog.asset";
        private const string OpaqueMaterialPath = "Assets/Box3/Materials/M_Block.mat";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex("^(.*)_(back|bottom|front|left|right|top)\\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [MenuItem("Box3/运行时/构建 UV 目录", false, 210)]
        public static void BuildRuntimeCatalog()
        {
            Box3BlocksAssetFactory.InvalidateCaches();
            Mesh mesh = Box3BlocksAssetFactory.GetOrCreateCubeMesh();
            Material transparentMaterial = Box3BlocksAssetFactory.GetAtlasMaterial();
            Material opaqueMaterial = AssetDatabase.LoadAssetAtPath<Material>(OpaqueMaterialPath);

            if (mesh == null || transparentMaterial == null || opaqueMaterial == null)
            {
                EditorUtility.DisplayDialog(
                    L("runtime.catalog.dialog.title"),
                    L("runtime.catalog.err.missing_assets"),
                    L("dialog.ok"));
                return;
            }

            Dictionary<string, bool> transparentMap = LoadTransparentMapFromSpec();
            Dictionary<string, Color> lightColorMap = LoadLightColorMapFromSpec();
            Dictionary<string, Dictionary<string, string>> sideMap = CollectBlockSideTextures();
            List<Box3BlocksRuntimeCatalog.Entry> entries = new List<Box3BlocksRuntimeCatalog.Entry>();

            foreach (KeyValuePair<string, Dictionary<string, string>> kv in sideMap)
            {
                string blockId = kv.Key;
                Dictionary<string, string> sideTextures = kv.Value;
                if (!Box3BlocksAssetFactory.TryGetFaceRenderData(sideTextures, out Box3BlocksAssetFactory.FaceRenderData renderData)
                    || renderData == null
                    || renderData.faceMainTexSt == null
                    || renderData.faceMainTexSt.Length < SideOrder.Length)
                {
                    continue;
                }

                bool transparent = transparentMap.TryGetValue(blockId, out bool explicitTransparent)
                    ? explicitTransparent
                    : Box3BlocksIdRules.IsTransparencyKeyword(blockId);

                bool emitsLight = Box3BlocksIdRules.IsEmissiveKeyword(blockId);
                if (lightColorMap.TryGetValue(blockId, out Color fromSpec) && fromSpec.maxColorComponent > 0.001f)
                {
                    emitsLight = true;
                }

                Color lightColor = lightColorMap.TryGetValue(blockId, out Color c)
                    ? c
                    : Box3BlocksIdRules.InferLightColor(blockId);

                Box3BlocksRuntimeCatalog.Entry entry = new Box3BlocksRuntimeCatalog.Entry
                {
                    blockId = blockId,
                    transparent = transparent,
                    emitsLight = emitsLight,
                    lightColor = lightColor,
                    faceMainTexSt = (Vector4[])renderData.faceMainTexSt.Clone(),
                    faceAnimations = BuildFaceAnimations(sideTextures)
                };
                entries.Add(entry);
            }

            entries.Sort((a, b) => string.Compare(a.blockId, b.blockId, StringComparison.OrdinalIgnoreCase));
            EnsureFolder(RuntimeFolder);

            Box3BlocksRuntimeCatalog catalog = AssetDatabase.LoadAssetAtPath<Box3BlocksRuntimeCatalog>(RuntimeCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<Box3BlocksRuntimeCatalog>();
                AssetDatabase.CreateAsset(catalog, RuntimeCatalogPath);
            }

            catalog.SetSharedAssets(mesh, opaqueMaterial, transparentMaterial);
            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(catalog);
            Debug.Log(Lf("runtime.catalog.done", entries.Count, RuntimeCatalogPath));
        }

        private static string L(string key)
        {
            return Box3BlocksI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, L(key), args);
        }

        private static Dictionary<string, Dictionary<string, string>> CollectBlockSideTextures()
        {
            Dictionary<string, Dictionary<string, string>> map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlockTextureFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileName(path);
                Match m = SideRegex.Match(fileName);
                if (!m.Success)
                {
                    continue;
                }

                string blockId = m.Groups[1].Value;
                string side = m.Groups[2].Value;
                if (!map.TryGetValue(blockId, out Dictionary<string, string> sideTextures))
                {
                    sideTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    map[blockId] = sideTextures;
                }

                sideTextures[side] = path;
            }

            return map;
        }

        private static Box3BlocksRuntimeCatalog.FaceAnimation[] BuildFaceAnimations(Dictionary<string, string> sideTextures)
        {
            Box3BlocksRuntimeCatalog.FaceAnimation[] animations = new Box3BlocksRuntimeCatalog.FaceAnimation[SideOrder.Length];
            if (sideTextures == null)
            {
                return animations;
            }

            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                if (!sideTextures.TryGetValue(side, out string textureAssetPath) || string.IsNullOrWhiteSpace(textureAssetPath))
                {
                    continue;
                }

                if (!Box3BlocksFaceAnimationParser.TryParse(textureAssetPath, GetProjectAbsolutePath, out ParsedFaceAnimation parsed))
                {
                    continue;
                }

                animations[i] = new Box3BlocksRuntimeCatalog.FaceAnimation
                {
                    frameCount = Mathf.Max(1, parsed.frameCount),
                    frameDuration = Mathf.Max(0.01f, parsed.frameDuration),
                    frames = parsed.frames != null ? (int[])parsed.frames.Clone() : Array.Empty<int>()
                };
            }

            return animations;
        }

        private static Dictionary<string, bool> LoadTransparentMapFromSpec()
        {
            Dictionary<string, bool> map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            Dictionary<string, string> objects = Box3BlocksJsonLite.ExtractTopLevelObjectValues(json);
            foreach (KeyValuePair<string, string> kv in objects)
            {
                bool transparent = Box3BlocksJsonLite.ReadBoolField(kv.Value, "transparent");
                map[kv.Key] = transparent;
            }

            return map;
        }

        private static Dictionary<string, Color> LoadLightColorMapFromSpec()
        {
            Dictionary<string, Color> map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            Dictionary<string, string> objects = Box3BlocksJsonLite.ExtractTopLevelObjectValues(json);
            foreach (KeyValuePair<string, string> kv in objects)
            {
                if (!TryReadEmissiveColor(kv.Value, out Color c))
                {
                    continue;
                }

                map[kv.Key] = c;
            }

            return map;
        }

        private static bool TryReadEmissiveColor(string objectText, out Color color)
        {
            color = Color.black;
            if (string.IsNullOrWhiteSpace(objectText))
            {
                return false;
            }

            Match match = Regex.Match(objectText, "\"emissive\"\\s*:\\s*\\[(?<v>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string[] parts = match.Groups["v"].Value.Split(',');
            if (parts.Length < 3)
            {
                return false;
            }

            float r = Box3BlocksJsonLite.ParseFloatSafe(parts[0], 0f);
            float g = Box3BlocksJsonLite.ParseFloatSafe(parts[1], 0f);
            float b = Box3BlocksJsonLite.ParseFloatSafe(parts[2], 0f);
            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max <= 0.001f)
            {
                return false;
            }

            if (max > 1f)
            {
                r /= max;
                g /= max;
                b /= max;
            }

            color = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
            return true;
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
