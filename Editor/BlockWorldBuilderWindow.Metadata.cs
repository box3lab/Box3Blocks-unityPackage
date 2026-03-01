using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    public partial class BlockWorldBuilderWindow
    {
        private static Dictionary<string, BlockMetadata> LoadBlockMetadata()
        {
            Dictionary<string, BlockMetadata> map = LoadFromBlockSpec();
            if (map.Count > 0)
            {
                return map;
            }

            return LoadFromBlockId();
        }

        private static Dictionary<string, BlockMetadata> LoadFromBlockSpec()
        {
            Dictionary<string, BlockMetadata> map = new Dictionary<string, BlockMetadata>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            Dictionary<string, string> objects = ExtractTopLevelObjectValues(json);
            foreach (KeyValuePair<string, string> pair in objects)
            {
                string name = pair.Key;
                string body = pair.Value;

                string category = ReadStringField(body, "category");
                int numericId = ParseIntSafe(ReadNumberField(body, "id"), -1);
                bool transparent = ReadBoolField(body, "transparent");
                Color emissiveColor = ReadColorArrayField(body, "emissive", InferLightColor(name));
                bool emitsLight = HasMeaningfulEmission(body) || BlockIdRules.IsEmissiveKeyword(name);

                map[name] = new BlockMetadata
                {
                    numericId = numericId,
                    category = string.IsNullOrWhiteSpace(category) ? InferCategory(name) : category,
                    emitsLight = emitsLight,
                    transparent = transparent,
                    lightColor = emissiveColor
                };
            }

            return map;
        }

        private static Dictionary<string, BlockMetadata> LoadFromBlockId()
        {
            Dictionary<string, BlockMetadata> map = new Dictionary<string, BlockMetadata>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockIdPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            MatchCollection flatMatches = FlatMapRegex.Matches(json);
            for (int i = 0; i < flatMatches.Count; i++)
            {
                Match m = flatMatches[i];
                string name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                map[name] = new BlockMetadata
                {
                    numericId = ParseIntSafe(m.Groups["id"].Value, -1),
                    category = InferCategory(name),
                    emitsLight = BlockIdRules.IsEmissiveKeyword(name),
                    transparent = BlockIdRules.IsTransparencyKeyword(name),
                    lightColor = BlockIdRules.InferLightColor(name)
                };
            }

            return map;
        }

        private static string ReadStringField(string text, string fieldName)
        {
            return BlockJsonLite.ReadStringField(text, fieldName);
        }

        private static string ReadNumberField(string text, string fieldName)
        {
            return BlockJsonLite.ReadNumberField(text, fieldName);
        }

        private static bool ReadBoolField(string text, string fieldName)
        {
            return BlockJsonLite.ReadBoolField(text, fieldName);
        }

        private static int ParseIntSafe(string text, int fallback)
        {
            return BlockJsonLite.ParseIntSafe(text, fallback);
        }

        private static float ParseFloatSafe(string text, float fallback)
        {
            return BlockJsonLite.ParseFloatSafe(text, fallback);
        }

        private static bool HasLightKeyword(string id)
        {
            return BlockIdRules.IsEmissiveKeyword(id);
        }

        private static bool IsTransparencyKeyword(string id)
        {
            return BlockIdRules.IsTransparencyKeyword(id);
        }

        private static string InferCategory(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return CategoryUncategorized;
            }

            string lower = id.ToLowerInvariant();
            if (lower.Contains("light") || lower.Contains("lamp") || lower.Contains("lantern") || lower.Contains("led"))
            {
                return "Light";
            }

            if (lower.Contains("glass") || lower.Contains("window"))
            {
                return "Glass";
            }

            if (lower.Contains("grass") || lower.Contains("sand") || lower.Contains("dirt") || lower.Contains("rock") || lower.Contains("stone") || lower.Contains("leaf") || lower.Contains("water") || lower.Contains("snow") || lower.Contains("lava"))
            {
                return "Nature";
            }

            if (lower.Contains("board") || lower.Contains("plank") || lower.Contains("brick") || lower.Contains("wall") || lower.Contains("roof"))
            {
                return "Building";
            }

            if (lower.Length == 1 || lower.Contains("mark") || lower.Contains("slash") || lower.Contains("paren") || lower.Contains("brace") || lower.Contains("bracket"))
            {
                return "Symbol";
            }

            return "Misc";
        }

        private static Color InferLightColor(string id)
        {
            return BlockIdRules.InferLightColor(id);
        }


        private static bool HasMeaningfulEmission(string body)
        {
            Match m = Regex.Match(body, "\"emissive\"\\s*:\\s*\\[(?<value>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return false;
            }

            string[] parts = m.Groups["value"].Value.Split(',');
            float sum = 0f;
            for (int i = 0; i < Mathf.Min(3, parts.Length); i++)
            {
                sum += Mathf.Abs(ParseFloatSafe(parts[i], 0f));
            }

            return sum > 0.001f;
        }

        private static Color ReadColorArrayField(string text, string fieldName, Color fallback)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\\[(?<value>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return fallback;
            }

            string[] parts = match.Groups["value"].Value.Split(',');
            if (parts.Length < 3)
            {
                return fallback;
            }

            float r = ParseFloatSafe(parts[0], fallback.r);
            float g = ParseFloatSafe(parts[1], fallback.g);
            float b = ParseFloatSafe(parts[2], fallback.b);

            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max > 1f)
            {
                r /= max;
                g /= max;
                b /= max;
            }

            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
        }

        private static bool TryParseFaceAnimation(string textureAssetPath, out FaceAnimationSpec spec)
        {
            spec = null;
            if (!FaceAnimationParser.TryParse(textureAssetPath, GetProjectAbsolutePath, out ParsedFaceAnimation parsed))
            {
                return false;
            }

            spec = new FaceAnimationSpec
            {
                frameCount = parsed.frameCount,
                frameDuration = parsed.frameDuration,
                frames = parsed.frames
            };
            return true;
        }

        private static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
        {
            return BlockJsonLite.ExtractTopLevelObjectValues(json);
        }
    }
}
