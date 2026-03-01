using System;
using UnityEngine;

namespace Box3Blocks.Editor
{
    internal static class Box3BlocksIdRules
    {
        public static bool IsEmissiveKeyword(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return id.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lava", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("led", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsTransparencyKeyword(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return id.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Color InferLightColor(string id)
        {
            string lower = (id ?? string.Empty).ToLowerInvariant();
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
    }
}
