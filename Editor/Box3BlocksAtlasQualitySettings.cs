using UnityEditor;

namespace Box3Blocks.Editor
{
    /// <summary>
    /// 图集质量选项（像素上限）。
    /// <para/>
    /// Atlas quality options (max atlas size in pixels).
    /// </summary>
    internal enum Box3AtlasQuality
    {
        Q1024 = 1024,
        Q512 = 512,
        Q256 = 256
    }

    internal static class Box3BlocksAtlasQualitySettings
    {
        private const string QualityKey = "Box3Blocks.Atlas.Quality";

        public static Box3AtlasQuality GetQuality()
        {
            int value = EditorPrefs.GetInt(QualityKey, (int)Box3AtlasQuality.Q1024);
            if (value == (int)Box3AtlasQuality.Q1024
                || value == (int)Box3AtlasQuality.Q512
                || value == (int)Box3AtlasQuality.Q256)
            {
                return (Box3AtlasQuality)value;
            }

            return Box3AtlasQuality.Q1024;
        }

        public static void SetQuality(Box3AtlasQuality quality)
        {
            EditorPrefs.SetInt(QualityKey, (int)quality);
        }

        public static int GetMaxSize()
        {
            return (int)GetQuality();
        }
    }
}
