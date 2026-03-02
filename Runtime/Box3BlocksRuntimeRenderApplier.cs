using System;
using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks
{
    /// <summary>
    /// 渲染应用辅助类：负责 UV、发光和面动画的渲染参数设置。
    /// <para/>
    /// Render helper for applying UV, emission, and face animation settings.
    /// </summary>
    internal static class Box3BlocksRuntimeRenderApplier
    {
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// 将每个面的 UV 变换写入材质属性块。
        /// <para/>
        /// Apply per-face UV transform using material property blocks.
        /// </summary>
        /// <param name="renderer">目标渲染器 / Target renderer.</param>
        /// <param name="faceMainTexSt">每个面的 _MainTex_ST / Per-face _MainTex_ST array.</param>
        public static void ApplyFaceMainTexSt(Renderer renderer, Vector4[] faceMainTexSt)
        {
            if (renderer == null)
            {
                return;
            }

            Material[] shared = renderer.sharedMaterials;
            int count = shared != null ? shared.Length : 0;
            if (count <= 0)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                Vector4 st = (faceMainTexSt != null && i < faceMainTexSt.Length)
                    ? faceMainTexSt[i]
                    : new Vector4(1f, 1f, 0f, 0f);
                renderer.GetPropertyBlock(block, i);
                block.SetVector(MainTexStId, st);
                renderer.SetPropertyBlock(block, i);
            }
        }

        /// <summary>
        /// 向材质属性块写入发光颜色。
        /// <para/>
        /// Apply emission color through material property blocks.
        /// </summary>
        /// <param name="renderer">目标渲染器 / Target renderer.</param>
        /// <param name="emissionColor">发光颜色 / Emission color.</param>
        public static void ApplyEmission(Renderer renderer, Color emissionColor)
        {
            if (renderer == null)
            {
                return;
            }

            Material[] shared = renderer.sharedMaterials;
            int count = shared != null ? shared.Length : 0;
            if (count <= 0)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                Material mat = shared[i];
                if (mat == null || !mat.HasProperty(EmissionColorId))
                {
                    continue;
                }

                renderer.GetPropertyBlock(block, i);
                block.SetColor(EmissionColorId, emissionColor);
                renderer.SetPropertyBlock(block, i);
            }
        }

        /// <summary>
        /// 根据目录中的面动画配置挂载并初始化 <see cref="Box3BlocksTextureAnimator"/>。
        /// <para/>
        /// Configure and initialize <see cref="Box3BlocksTextureAnimator"/> from catalog face animation data.
        /// </summary>
        /// <param name="renderer">目标渲染器 / Target renderer.</param>
        /// <param name="entry">方块目录条目 / Catalog entry.</param>
        /// <returns>至少存在一个有效动画面时返回 true / Returns true when at least one animated face is configured.</returns>
        public static bool ConfigureFaceAnimations(Renderer renderer, Box3BlocksRuntimeCatalog.Entry entry)
        {
            if (renderer == null || entry == null || entry.faceAnimations == null || entry.faceMainTexSt == null)
            {
                return false;
            }

            List<Box3BlocksTextureAnimator.FaceAnimation> list = new List<Box3BlocksTextureAnimator.FaceAnimation>();
            int count = Mathf.Min(entry.faceAnimations.Length, entry.faceMainTexSt.Length);
            for (int i = 0; i < count; i++)
            {
                Box3BlocksRuntimeCatalog.FaceAnimation spec = entry.faceAnimations[i];
                if (spec == null || spec.frameCount <= 1)
                {
                    continue;
                }

                list.Add(new Box3BlocksTextureAnimator.FaceAnimation
                {
                    materialIndex = i,
                    frameCount = Mathf.Max(1, spec.frameCount),
                    frameDuration = Mathf.Max(0.01f, spec.frameDuration),
                    frames = spec.frames != null ? (int[])spec.frames.Clone() : Array.Empty<int>(),
                    baseMainTexSt = entry.faceMainTexSt[i]
                });
            }

            if (list.Count == 0)
            {
                return false;
            }

            Box3BlocksTextureAnimator animator = renderer.GetComponent<Box3BlocksTextureAnimator>();
            if (animator == null)
            {
                animator = renderer.gameObject.AddComponent<Box3BlocksTextureAnimator>();
            }

            animator.SetAnimations(list.ToArray(), entry.faceMainTexSt);
            return true;
        }
    }
}
