using System;
using System.Collections.Generic;
using UnityEngine;

namespace Box3Blocks
{
    public sealed class Box3BlocksRuntimeCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class FaceAnimation
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }

        [Serializable]
        public sealed class Entry
        {
            public string blockId;
            public bool transparent;
            public bool emitsLight;
            public Color lightColor = Color.white;
            public Vector4[] faceMainTexSt = new Vector4[6];
            public FaceAnimation[] faceAnimations = new FaceAnimation[6];
        }

        [SerializeField]
        private List<Entry> entries = new List<Entry>();

        [SerializeField]
        private Mesh cubeMesh;

        [SerializeField]
        private Material opaqueMaterial;

        [SerializeField]
        private Material transparentMaterial;

        private Dictionary<string, Entry> _lookup;

        public Mesh CubeMesh => cubeMesh;
        public Material OpaqueMaterial => opaqueMaterial;
        public Material TransparentMaterial => transparentMaterial;
        public IReadOnlyList<Entry> Entries => entries;

        public bool TryGetEntry(string blockId, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            EnsureLookup();
            return _lookup.TryGetValue(blockId, out entry) && entry != null;
        }

        public void SetSharedAssets(Mesh mesh, Material opaque, Material transparent)
        {
            cubeMesh = mesh;
            opaqueMaterial = opaque;
            transparentMaterial = transparent;
        }

        public void SetEntries(IReadOnlyList<Entry> source)
        {
            entries.Clear();
            if (source == null)
            {
                _lookup = null;
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                Entry s = source[i];
                if (s == null || string.IsNullOrWhiteSpace(s.blockId))
                {
                    continue;
                }

                Entry copy = new Entry
                {
                    blockId = s.blockId,
                    transparent = s.transparent,
                    emitsLight = s.emitsLight,
                    lightColor = s.lightColor,
                    faceMainTexSt = (s.faceMainTexSt != null && s.faceMainTexSt.Length > 0)
                        ? (Vector4[])s.faceMainTexSt.Clone()
                        : new Vector4[6],
                    faceAnimations = CloneFaceAnimations(s.faceAnimations)
                };
                entries.Add(copy);
            }

            _lookup = null;
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null || string.IsNullOrWhiteSpace(e.blockId))
                {
                    continue;
                }

                _lookup[e.blockId] = e;
            }
        }

        private void OnValidate()
        {
            _lookup = null;
        }

        private static FaceAnimation[] CloneFaceAnimations(FaceAnimation[] source)
        {
            FaceAnimation[] copied = new FaceAnimation[6];
            if (source == null)
            {
                return copied;
            }

            int count = Mathf.Min(copied.Length, source.Length);
            for (int i = 0; i < count; i++)
            {
                FaceAnimation s = source[i];
                if (s == null)
                {
                    continue;
                }

                copied[i] = new FaceAnimation
                {
                    frameCount = Mathf.Max(1, s.frameCount),
                    frameDuration = Mathf.Max(0.01f, s.frameDuration),
                    frames = s.frames != null ? (int[])s.frames.Clone() : Array.Empty<int>()
                };
            }

            return copied;
        }
    }
}
