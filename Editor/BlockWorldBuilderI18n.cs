using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    internal static class BlockWorldBuilderI18n
    {
        private const string EnJsonPath = "Packages/com.box3lab.box3/Editor/I18n/blockworld-ui.en.json";
        private const string ZhCnJsonPath = "Packages/com.box3lab.box3/Editor/I18n/blockworld-ui.zh-CN.json";
        private const string EnBlockNamesJsonPath = "Packages/com.box3lab.box3/Editor/I18n/block-names.en.json";
        private const string ZhCnBlockNamesJsonPath = "Packages/com.box3lab.box3/Editor/I18n/block-names.zh-CN.json";
        private static readonly Regex DynamicPairRegex = new Regex("\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

        private static Dictionary<string, string> _enEntries;
        private static Dictionary<string, string> _zhEntries;
        private static Dictionary<string, string> _enCategories;
        private static Dictionary<string, string> _zhCategories;
        private static Dictionary<string, string> _enBlocks;
        private static Dictionary<string, string> _zhBlocks;
        private static readonly Dictionary<string, string> AutoZhTokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dark"] = "深色",
            ["light"] = "浅色",
            ["blue"] = "蓝色",
            ["red"] = "红色",
            ["green"] = "绿色",
            ["yellow"] = "黄色",
            ["purple"] = "紫色",
            ["pink"] = "粉色",
            ["white"] = "白色",
            ["black"] = "黑色",
            ["brown"] = "棕色",
            ["orange"] = "橙色",
            ["cyan"] = "青色",
            ["gray"] = "灰色",
            ["grey"] = "灰色",
            ["grass"] = "草地",
            ["sand"] = "沙地",
            ["rock"] = "岩石",
            ["stone"] = "石头",
            ["wood"] = "木头",
            ["brick"] = "砖块",
            ["glass"] = "玻璃",
            ["water"] = "水",
            ["lava"] = "岩浆",
            ["ice"] = "冰",
            ["snow"] = "雪",
            ["dirt"] = "泥土",
            ["soil"] = "土壤",
            ["leaf"] = "树叶",
            ["leaves"] = "树叶",
            ["window"] = "窗户",
            ["wall"] = "墙",
            ["roof"] = "屋顶",
            ["door"] = "门",
            ["lamp"] = "灯",
            ["lantern"] = "灯笼",
            ["lightbulb"] = "灯泡",
            ["block"] = "方块",
            ["tile"] = "瓷砖",
            ["panel"] = "面板",
            ["frame"] = "边框",
            ["all"] = "全纹理"
        };

        [Serializable]
        private class LocalizationItem
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class LocalizationDocument
        {
            public LocalizationItem[] entries;
            public LocalizationItem[] categories;
            public LocalizationItem[] blocks;
        }

        public static string Get(string key)
        {
            EnsureLoaded();

            Dictionary<string, string> primary = IsChineseUI() ? _zhEntries : _enEntries;
            Dictionary<string, string> fallback = IsChineseUI() ? _enEntries : _zhEntries;

            if (primary != null && primary.TryGetValue(key, out string value))
            {
                return value;
            }

            if (fallback != null && fallback.TryGetValue(key, out value))
            {
                return value;
            }

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Get(key), args);
        }

        public static string GetCategoryLabel(string categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey))
            {
                return categoryKey;
            }

            EnsureLoaded();

            Dictionary<string, string> primary = IsChineseUI() ? _zhCategories : _enCategories;
            Dictionary<string, string> fallback = IsChineseUI() ? _enCategories : _zhCategories;

            if (primary != null && primary.TryGetValue(categoryKey, out string value))
            {
                return value;
            }

            if (fallback != null && fallback.TryGetValue(categoryKey, out value))
            {
                return value;
            }

            return categoryKey;
        }

        public static string GetBlockDisplayName(string blockKey, string fallback)
        {
            if (string.IsNullOrWhiteSpace(blockKey))
            {
                return fallback;
            }

            EnsureLoaded();

            Dictionary<string, string> primary = IsChineseUI() ? _zhBlocks : _enBlocks;
            Dictionary<string, string> fallbackMap = IsChineseUI() ? _enBlocks : _zhBlocks;

            if (primary != null && primary.TryGetValue(blockKey, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (fallbackMap != null && fallbackMap.TryGetValue(blockKey, out value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (IsChineseUI())
            {
                string autoZh = AutoTranslateBlockNameZh(blockKey);
                if (!string.IsNullOrWhiteSpace(autoZh))
                {
                    return autoZh;
                }
            }

            return fallback;
        }

        private static bool IsChineseUI()
        {
            if (TryGetEditorLanguage(out SystemLanguage editorLanguage))
            {
                return editorLanguage == SystemLanguage.ChineseSimplified || editorLanguage == SystemLanguage.ChineseTraditional;
            }

            string prefLanguage = EditorPrefs.GetString("Editor.kLanguage", string.Empty);
            if (!string.IsNullOrWhiteSpace(prefLanguage))
            {
                string lower = prefLanguage.Trim().ToLowerInvariant();
                return lower.StartsWith("zh", StringComparison.Ordinal)
                    || lower.Contains("chinese", StringComparison.Ordinal);
            }

            SystemLanguage systemLanguage = Application.systemLanguage;
            return systemLanguage == SystemLanguage.ChineseSimplified || systemLanguage == SystemLanguage.ChineseTraditional;
        }

        private static bool TryGetEditorLanguage(out SystemLanguage language)
        {
            language = SystemLanguage.Unknown;
            try
            {
                Type localizationDatabaseType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LocalizationDatabase");
                if (localizationDatabaseType == null)
                {
                    return false;
                }

                PropertyInfo currentLanguageProperty = localizationDatabaseType.GetProperty(
                    "currentEditorLanguage",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (currentLanguageProperty == null)
                {
                    return false;
                }

                object value = currentLanguageProperty.GetValue(null, null);
                if (value is SystemLanguage sl)
                {
                    language = sl;
                    return true;
                }

                if (value is Enum enumValue)
                {
                    language = (SystemLanguage)Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch
            {
                // Ignore reflection failure and continue fallback checks.
            }

            return false;
        }

        private static void EnsureLoaded()
        {
            if (_enEntries != null && _zhEntries != null)
            {
                return;
            }

            (_enEntries, _enCategories, _enBlocks) = LoadDocument(EnJsonPath);
            (_zhEntries, _zhCategories, _zhBlocks) = LoadDocument(ZhCnJsonPath);
            MergeMap(_enBlocks, LoadDynamicBlockMap(EnBlockNamesJsonPath));
            MergeMap(_zhBlocks, LoadDynamicBlockMap(ZhCnBlockNamesJsonPath));
        }

        private static (Dictionary<string, string> entries, Dictionary<string, string> categories, Dictionary<string, string> blocks) LoadDocument(string path)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return (
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            LocalizationDocument doc;
            try
            {
                doc = JsonUtility.FromJson<LocalizationDocument>(asset.text);
            }
            catch
            {
                doc = null;
            }

            if (doc == null)
            {
                return (
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            Dictionary<string, string> entries = BuildMap(doc.entries, StringComparer.Ordinal);
            Dictionary<string, string> categories = BuildMap(doc.categories, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> blocks = BuildMap(doc.blocks, StringComparer.OrdinalIgnoreCase);
            return (entries, categories, blocks);
        }

        private static Dictionary<string, string> BuildMap(LocalizationItem[] items, IEqualityComparer<string> comparer)
        {
            if (items == null || items.Length == 0)
            {
                return new Dictionary<string, string>(comparer);
            }

            Dictionary<string, string> map = new Dictionary<string, string>(comparer);
            for (int i = 0; i < items.Length; i++)
            {
                LocalizationItem item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.key))
                {
                    continue;
                }

                map[item.key] = item.value ?? string.Empty;
            }

            return map;
        }

        private static Dictionary<string, string> LoadDynamicBlockMap(string path)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return map;
            }

            MatchCollection matches = DynamicPairRegex.Matches(asset.text);
            for (int i = 0; i < matches.Count; i++)
            {
                Match m = matches[i];
                if (!m.Success)
                {
                    continue;
                }

                string rawKey = m.Groups["key"].Value;
                string value = m.Groups["value"].Value;
                if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string key = rawKey.StartsWith("block.box3.", StringComparison.OrdinalIgnoreCase)
                    ? rawKey.Substring("block.box3.".Length)
                    : rawKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                map[key] = value;
            }

            return map;
        }

        private static void MergeMap(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            if (target == null || source == null || source.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, string> kv in source)
            {
                target[kv.Key] = kv.Value;
            }
        }

        private static string AutoTranslateBlockNameZh(string blockKey)
        {
            if (string.IsNullOrWhiteSpace(blockKey))
            {
                return blockKey;
            }

            string[] tokens = blockKey.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return blockKey;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (AutoZhTokenMap.TryGetValue(token, out string zh))
                {
                    sb.Append(zh);
                }
                else
                {
                    sb.Append(token);
                }
            }

            return sb.ToString();
        }
    }
}
