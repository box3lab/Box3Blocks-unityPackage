using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Box3Blocks.Editor
{
    internal static class Box3BlocksJsonLite
    {
        public static string ReadStringField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        public static string ReadNumberField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        public static bool ReadBoolField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>true|false|1|0)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string raw = match.Groups["value"].Value;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        public static int ParseIntSafe(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        public static float ParseFloatSafe(string text, float fallback)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        public static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
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
            StringBuilder sb = null;
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
                        sb = new StringBuilder();
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
                        escape = false;
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

            while (i < text.Length)
            {
                char c = text[i];
                if (c == ',' || c == '}' || c == ']')
                {
                    break;
                }

                i++;
            }
        }

        private static void SkipJsonObject(string text, ref int i)
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
                    escape = false;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth <= 0)
                    {
                        break;
                    }
                }
            }
        }
    }
}
