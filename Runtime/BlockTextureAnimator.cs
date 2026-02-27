using System;
using UnityEngine;

namespace BlockWorldMVP
{
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class BlockTextureAnimator : MonoBehaviour
    {
        [Serializable]
        public class FaceAnimation
        {
            public int materialIndex;
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames;
        }

        [SerializeField]
        private FaceAnimation[] animations = Array.Empty<FaceAnimation>();

        private Renderer _renderer;
        private Material[] _materials;
        private float _startTime;

        public void SetAnimations(FaceAnimation[] value)
        {
            animations = value ?? Array.Empty<FaceAnimation>();
            _startTime = Time.realtimeSinceStartup;
            CacheMaterials();
            ApplyCurrentFrame();
        }

        private void OnEnable()
        {
            _startTime = Time.realtimeSinceStartup;
            CacheMaterials();
            ApplyCurrentFrame();
        }

        private void Update()
        {
            if (animations == null || animations.Length == 0)
            {
                return;
            }

            if (_materials == null || _materials.Length == 0)
            {
                CacheMaterials();
                if (_materials == null || _materials.Length == 0)
                {
                    return;
                }
            }

            ApplyCurrentFrame();
        }

        private void CacheMaterials()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<Renderer>();
            }

            if (_renderer == null)
            {
                _materials = null;
                return;
            }

            _materials = _renderer.sharedMaterials;
        }

        private void ApplyCurrentFrame()
        {
            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _startTime);
            for (int i = 0; i < animations.Length; i++)
            {
                FaceAnimation animation = animations[i];
                if (animation == null || animation.frameCount <= 1 || animation.materialIndex < 0 || animation.materialIndex >= _materials.Length)
                {
                    continue;
                }

                int[] sequence = animation.frames;
                int sequenceLength = sequence != null && sequence.Length > 0 ? sequence.Length : animation.frameCount;
                float duration = Mathf.Max(0.01f, animation.frameDuration);
                int step = Mathf.FloorToInt(elapsed / duration) % sequenceLength;
                int frameIndex = sequence != null && sequence.Length > 0 ? sequence[step] : step;
                frameIndex = Mathf.Clamp(frameIndex, 0, animation.frameCount - 1);

                Material material = _materials[animation.materialIndex];
                if (material == null)
                {
                    continue;
                }

                float scaleY = 1f / animation.frameCount;
                float offsetY = 1f - (frameIndex + 1f) * scaleY;
                material.mainTextureScale = new Vector2(1f, scaleY);
                material.mainTextureOffset = new Vector2(0f, offsetY);
            }
        }
    }
}
