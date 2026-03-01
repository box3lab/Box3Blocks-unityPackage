using System;
using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks.Editor
{
    public partial class Box3BlocksBuilderWindow
    {
        private class BlockDefinition
        {
            public string id;
            public Dictionary<string, string> sideTexturePaths = new Dictionary<string, string>();
            public Dictionary<string, FaceAnimationSpec> sideAnimations = new Dictionary<string, FaceAnimationSpec>();
            public bool hasAnimation;
            public Texture2D previewTexture;
            public int numericId = -1;
            public string category = CategoryUncategorized;
            public bool emitsLight;
            public bool transparent;
            public Color lightColor = new Color(1f, 0.8f, 0.2f, 1f);
            public string displayName;
            public int placementRotationQuarter;
        }

        private class FaceAnimationSpec
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }

        private sealed class AnimatedPreviewCacheEntry
        {
            public int signature;
            public Texture2D texture;
        }

        private sealed class CardRotateAnimState
        {
            public double startTime;
            public float duration;
        }

        private class BlockMetadata
        {
            public int numericId = -1;
            public string category;
            public bool emitsLight;
            public bool transparent;
            public Color lightColor = new Color(1f, 0.8f, 0.2f, 1f);
        }

        private enum EditTool
        {
            Place,
            Erase,
            Replace,
            Rotate
        }
    }
}
