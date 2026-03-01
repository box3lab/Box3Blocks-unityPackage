using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    internal readonly struct ParsedFaceAnimation
    {
        public readonly int frameCount;
        public readonly float frameDuration;
        public readonly int[] frames;

        public ParsedFaceAnimation(int frameCount, float frameDuration, int[] frames)
        {
            this.frameCount = frameCount;
            this.frameDuration = frameDuration;
            this.frames = frames ?? Array.Empty<int>();
        }
    }

    internal static class FaceAnimationParser
    {
        public static bool TryParse(string textureAssetPath, Func<string, string> absolutePathResolver, out ParsedFaceAnimation parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(textureAssetPath) || absolutePathResolver == null)
            {
                return false;
            }

            string mcmetaPath = absolutePathResolver(textureAssetPath + ".mcmeta");
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

            int frameTimeTicks = BlockJsonLite.ParseIntSafe(BlockJsonLite.ReadNumberField(animationBody, "frametime"), 1);
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

            parsed = new ParsedFaceAnimation(frameCount, frameDuration, frames);
            return true;
        }

        public static string ExtractAnimationObjectBody(string json)
        {
            Match m = Regex.Match(json, "\"animation\"\\s*:\\s*\\{(?<body>[\\s\\S]*?)\\}", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["body"].Value : json;
        }

        public static int[] ParseFrameSequence(string body)
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
    }
}
