using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
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
                bool emitsLight = HasMeaningfulEmission(body) || HasLightKeyword(name);

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
                    emitsLight = HasLightKeyword(name),
                    transparent = IsTransparencyKeyword(name),
                    lightColor = InferLightColor(name)
                };
            }

            return map;
        }

        private static string ReadStringField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static string ReadNumberField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static bool ReadBoolField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>true|false|1|0)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string raw = match.Groups["value"].Value;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        private static int ParseIntSafe(string text, int fallback)
        {
            return int.TryParse(text, out int value) ? value : fallback;
        }

        private static float ParseFloatSafe(string text, float fallback)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private static bool HasLightKeyword(string id)
        {
            return id.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lava", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTransparencyKeyword(string id)
        {
            return id.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
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
            string lower = id.ToLowerInvariant();
            if (lower.Contains("red"))
            {
                return new Color(1f, 0.32f, 0.28f, 1f);
            }

            if (lower.Contains("blue"))
            {
                return new Color(0.35f, 0.7f, 1f, 1f);
            }

            if (lower.Contains("green") || lower.Contains("mint"))
            {
                return new Color(0.4f, 1f, 0.55f, 1f);
            }

            if (lower.Contains("yellow") || lower.Contains("warm"))
            {
                return new Color(1f, 0.9f, 0.3f, 1f);
            }

            if (lower.Contains("purple") || lower.Contains("indigo"))
            {
                return new Color(0.62f, 0.45f, 1f, 1f);
            }

            if (lower.Contains("pink"))
            {
                return new Color(1f, 0.52f, 0.8f, 1f);
            }

            return new Color(1f, 0.8f, 0.2f, 1f);
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
            string mcmetaPath = GetProjectAbsolutePath(textureAssetPath + ".mcmeta");
            if (!File.Exists(mcmetaPath))
            {
                return false;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureAssetPath);
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return false;
            }

            int frameCountFromTexture = Mathf.Max(1, texture.height / texture.width);
            string json = File.ReadAllText(mcmetaPath);
            string animationBody = ExtractAnimationObjectBody(json);

            int frameTimeTicks = ParseIntSafe(ReadNumberField(animationBody, "frametime"), 1);
            float frameDuration = Mathf.Max(0.01f, frameTimeTicks * 0.05f);
            int[] frames = ParseFrameSequence(animationBody);
            int maxFrame = -1;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] > maxFrame)
                {
                    maxFrame = frames[i];
                }
            }

            int frameCount = Mathf.Max(frameCountFromTexture, maxFrame + 1);
            if (frameCount <= 1 && frames.Length <= 1)
            {
                return false;
            }

            if (frames.Length == 0)
            {
                frames = new int[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    frames[i] = i;
                }
            }

            spec = new FaceAnimationSpec
            {
                frameCount = frameCount,
                frameDuration = frameDuration,
                frames = frames
            };
            return true;
        }

        private static string ExtractAnimationObjectBody(string json)
        {
            Match m = Regex.Match(json, "\"animation\"\\s*:\\s*\\{(?<body>[\\s\\S]*?)\\}", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["body"].Value : json;
        }

        private static int[] ParseFrameSequence(string body)
        {
            Match m = Regex.Match(body, "\"frames\"\\s*:\\s*\\[(?<frames>[\\s\\S]*?)\\]", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return Array.Empty<int>();
            }

            string framesBody = m.Groups["frames"].Value;
            List<int> frames = new List<int>();

            MatchCollection objectIndexMatches = Regex.Matches(framesBody, "\"index\"\\s*:\\s*(?<idx>\\d+)", RegexOptions.IgnoreCase);
            if (objectIndexMatches.Count > 0)
            {
                for (int i = 0; i < objectIndexMatches.Count; i++)
                {
                    if (int.TryParse(objectIndexMatches[i].Groups["idx"].Value, out int idx) && idx >= 0)
                    {
                        frames.Add(idx);
                    }
                }

                return frames.ToArray();
            }

            string[] tokens = framesBody.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (int.TryParse(token, out int frameIndex) && frameIndex >= 0)
                {
                    frames.Add(frameIndex);
                }
            }

            return frames.ToArray();
        }

        private static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{')
            {
                return result;
            }

            i++;
            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    break;
                }

                string key = ReadJsonString(json, ref i);
                if (key == null)
                {
                    break;
                }

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':')
                {
                    break;
                }

                i++;
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != '{')
                {
                    SkipJsonValue(json, ref i);
                }
                else
                {
                    int start = i;
                    SkipJsonObject(json, ref i);
                    string objectText = json.Substring(start, i - start);
                    result[key] = objectText;
                }

                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                }
            }

            return result;
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        private static string ReadJsonString(string text, ref int i)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length || text[i] != '"')
            {
                return null;
            }

            i++;
            int start = i;
            bool escape = false;
            System.Text.StringBuilder sb = null;
            while (i < text.Length)
            {
                char c = text[i++];
                if (!escape && c == '"')
                {
                    if (sb == null)
                    {
                        return text.Substring(start, i - start - 1);
                    }

                    return sb.ToString();
                }

                if (!escape && c == '\\')
                {
                    escape = true;
                    if (sb == null)
                    {
                        sb = new System.Text.StringBuilder();
                        sb.Append(text, start, (i - 1) - start);
                    }
                    continue;
                }

                if (sb != null)
                {
                    sb.Append(c);
                }

                escape = false;
            }

            return null;
        }

        private static void SkipJsonValue(string text, ref int i)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length)
            {
                return;
            }

            if (text[i] == '{')
            {
                SkipJsonObject(text, ref i);
                return;
            }

            if (text[i] == '[')
            {
                int depth = 0;
                bool inString = false;
                bool escape = false;
                while (i < text.Length)
                {
                    char c = text[i++];
                    if (inString)
                    {
                        if (!escape && c == '"')
                        {
                            inString = false;
                        }
                        escape = !escape && c == '\\';
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (c == '[')
                    {
                        depth++;
                    }
                    else if (c == ']')
                    {
                        depth--;
                        if (depth <= 0)
                        {
                            break;
                        }
                    }
                }
                return;
            }

            if (text[i] == '"')
            {
                ReadJsonString(text, ref i);
                return;
            }

            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']')
            {
                i++;
            }
        }

        private static void SkipJsonObject(string text, ref int i)
        {
            if (i >= text.Length || text[i] != '{')
            {
                return;
            }

            int depth = 0;
            bool inString = false;
            bool escape = false;
            while (i < text.Length)
            {
                char c = text[i++];
                if (inString)
                {
                    if (!escape && c == '"')
                    {
                        inString = false;
                    }
                    escape = !escape && c == '\\';
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }
        }
    }
}
