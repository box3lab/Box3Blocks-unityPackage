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
            public Vector4 baseMainTexSt = new Vector4(1f, 1f, 0f, 0f);
        }

        [SerializeField]
        private FaceAnimation[] animations = Array.Empty<FaceAnimation>();

        [SerializeField]
        private Vector4[] baseMainTexStByMaterial = Array.Empty<Vector4>();

        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private float _startTime;

        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

        public void SetAnimations(FaceAnimation[] value)
        {
            SetAnimations(value, null);
        }

        public void SetAnimations(FaceAnimation[] value, Vector4[] baseMainTexSt)
        {
            animations = value ?? Array.Empty<FaceAnimation>();
            baseMainTexStByMaterial = baseMainTexSt ?? Array.Empty<Vector4>();
            _startTime = Time.realtimeSinceStartup;
            ApplyCurrentFrame();
        }

        private void OnEnable()
        {
            _startTime = Time.realtimeSinceStartup;
            ApplyCurrentFrame();
        }

        private void Update()
        {
            ApplyCurrentFrame();
        }

        private void ApplyCurrentFrame()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<Renderer>();
            }

            if (_renderer == null)
            {
                return;
            }

            Material[] materials = _renderer.sharedMaterials;
            int materialCount = materials != null ? materials.Length : 0;
            if (materialCount <= 0)
            {
                return;
            }

            EnsureBaseMainTexSt(materialCount);

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _startTime);
            for (int i = 0; i < materialCount; i++)
            {
                Vector4 st = baseMainTexStByMaterial[i];
                FaceAnimation animation = FindAnimationByMaterialIndex(i);
                if (animation != null && animation.frameCount > 1)
                {
                    st = EvaluateAnimatedSt(animation, elapsed);
                }

                _renderer.GetPropertyBlock(_propertyBlock, i);
                _propertyBlock.SetVector(MainTexStId, st);
                _renderer.SetPropertyBlock(_propertyBlock, i);
            }
        }

        private void EnsureBaseMainTexSt(int materialCount)
        {
            if (baseMainTexStByMaterial != null && baseMainTexStByMaterial.Length >= materialCount)
            {
                return;
            }

            Vector4[] next = new Vector4[materialCount];
            int copyCount = baseMainTexStByMaterial != null ? Mathf.Min(baseMainTexStByMaterial.Length, next.Length) : 0;
            for (int i = 0; i < copyCount; i++)
            {
                next[i] = baseMainTexStByMaterial[i];
            }

            for (int i = copyCount; i < next.Length; i++)
            {
                next[i] = new Vector4(1f, 1f, 0f, 0f);
            }

            baseMainTexStByMaterial = next;
        }

        private FaceAnimation FindAnimationByMaterialIndex(int materialIndex)
        {
            if (animations == null || animations.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < animations.Length; i++)
            {
                FaceAnimation animation = animations[i];
                if (animation != null && animation.materialIndex == materialIndex)
                {
                    return animation;
                }
            }

            return null;
        }

        private static Vector4 EvaluateAnimatedSt(FaceAnimation animation, float elapsed)
        {
            Vector4 baseSt = animation.baseMainTexSt;
            int frameCount = Mathf.Max(1, animation.frameCount);
            float frameDuration = Mathf.Max(0.01f, animation.frameDuration);
            int[] sequence = animation.frames;
            int sequenceLength = sequence != null && sequence.Length > 0 ? sequence.Length : frameCount;

            int step = Mathf.FloorToInt(elapsed / frameDuration) % sequenceLength;
            int frameIndex = sequence != null && sequence.Length > 0 ? sequence[step] : step;
            frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

            float scaleY = baseSt.y / frameCount;
            float offsetY = baseSt.w + baseSt.y - (frameIndex + 1f) * scaleY;
            return new Vector4(baseSt.x, scaleY, baseSt.z, offsetY);
        }
    }
}
