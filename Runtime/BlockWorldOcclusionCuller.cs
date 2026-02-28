using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BlockWorldMVP
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BlockWorldOcclusionCuller : MonoBehaviour
    {
        [Serializable]
        private sealed class ChunkData
        {
            public Bounds bounds;
            public Renderer[] renderers = Array.Empty<Renderer>();
            public bool visible = true;
        }

        private readonly struct ChunkKey : IEquatable<ChunkKey>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;

            public ChunkKey(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(ChunkKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + x;
                    hash = hash * 31 + y;
                    hash = hash * 31 + z;
                    return hash;
                }
            }
        }

        [Header("Target")]
        [SerializeField]
        private Transform targetRoot;

        [Header("Chunk")]
        [SerializeField]
        private int chunkSize = 16;

        [Header("Distance")]
        [SerializeField]
        private float visibleDistance = 120f;

        [SerializeField]
        private float hideDistance = 160f;

        [Header("Update")]
        [SerializeField]
        private int updateFrames = 2;

        [FormerlySerializedAs("maxObjectsPerTick")]
        [SerializeField]
        private int maxChunksPerTick = 256;

        private readonly List<ChunkData> _chunks = new List<ChunkData>(256);
        private readonly Plane[] _planes = new Plane[6];

        private Camera _camera;
        private int _chunkCursor;
        private int _frameCounter;
        private int _knownChildCount = -1;
        private float _nextHierarchyCheckTime;

        public void Configure(float visibleDist, float hideDist, int frameStep)
        {
            visibleDistance = Mathf.Max(1f, visibleDist);
            hideDistance = Mathf.Max(visibleDistance, hideDist);
            updateFrames = Mathf.Clamp(frameStep, 1, 60);
        }

        public void Rebuild()
        {
            Transform root = targetRoot != null ? targetRoot : transform;
            _chunks.Clear();
            _chunkCursor = 0;

            if (root == null)
            {
                _knownChildCount = 0;
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                _knownChildCount = root.childCount;
                return;
            }

            int size = Mathf.Max(1, chunkSize);
            Dictionary<ChunkKey, ChunkData> map = new Dictionary<ChunkKey, ChunkData>(256);
            Dictionary<ChunkKey, List<Renderer>> lists = new Dictionary<ChunkKey, List<Renderer>>(256);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                Vector3 c = b.center;
                ChunkKey key = new ChunkKey(
                    FloorDiv(Mathf.FloorToInt(c.x), size),
                    FloorDiv(Mathf.FloorToInt(c.y), size),
                    FloorDiv(Mathf.FloorToInt(c.z), size));

                if (!map.TryGetValue(key, out ChunkData chunk))
                {
                    chunk = new ChunkData { bounds = b, visible = renderer.enabled };
                    map.Add(key, chunk);
                }
                else
                {
                    chunk.bounds.Encapsulate(b);
                }

                if (!lists.TryGetValue(key, out List<Renderer> list))
                {
                    list = new List<Renderer>(64);
                    lists.Add(key, list);
                }

                list.Add(renderer);
            }

            foreach (KeyValuePair<ChunkKey, ChunkData> kv in map)
            {
                ChunkData chunk = kv.Value;
                if (lists.TryGetValue(kv.Key, out List<Renderer> list))
                {
                    chunk.renderers = list.ToArray();
                }

                _chunks.Add(chunk);
            }

            _knownChildCount = root.childCount;
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                ChunkData chunk = _chunks[i];
                if (chunk == null)
                {
                    continue;
                }

                chunk.visible = true;
                SetChunkVisible(chunk, true);
            }
        }

        private void LateUpdate()
        {
            Transform root = targetRoot != null ? targetRoot : transform;
            if (root == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextHierarchyCheckTime)
            {
                _nextHierarchyCheckTime = Time.unscaledTime + 0.5f;
                if (_knownChildCount != root.childCount)
                {
                    Rebuild();
                }
            }

            if (_chunks.Count == 0)
            {
                return;
            }

            _camera = ResolveCamera();
            if (_camera == null)
            {
                return;
            }

            _frameCounter++;
            int frameStep = Mathf.Clamp(updateFrames, 1, 60);
            if ((_frameCounter % frameStep) != 0)
            {
                return;
            }

            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);

            int budget = Mathf.Clamp((_chunks.Count + frameStep - 1) / frameStep, 1, Mathf.Max(1, maxChunksPerTick));
            for (int i = 0; i < budget; i++)
            {
                ChunkData chunk = _chunks[_chunkCursor];
                _chunkCursor = (_chunkCursor + 1) % _chunks.Count;
                EvaluateChunk(chunk);
            }
        }

        private Camera ResolveCamera()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                SceneView drawing = SceneView.currentDrawingSceneView;
                if (drawing != null && drawing.camera != null)
                {
                    return drawing.camera;
                }

                SceneView last = SceneView.lastActiveSceneView;
                if (last != null && last.camera != null)
                {
                    return last.camera;
                }
#endif
            }

            if (Camera.main != null)
            {
                return Camera.main;
            }

            Camera[] all = Camera.allCameras;
            return all != null && all.Length > 0 ? all[0] : null;
        }

        private void EvaluateChunk(ChunkData chunk)
        {
            if (chunk == null)
            {
                return;
            }

            Vector3 camPos = _camera.transform.position;
            float distance = Vector3.Distance(camPos, chunk.bounds.center);

            bool shouldBeVisible = chunk.visible
                ? (distance <= hideDistance)
                : (distance <= visibleDistance);

            if (shouldBeVisible)
            {
                shouldBeVisible = GeometryUtility.TestPlanesAABB(_planes, chunk.bounds);
            }

            if (chunk.visible != shouldBeVisible)
            {
                chunk.visible = shouldBeVisible;
                SetChunkVisible(chunk, shouldBeVisible);
            }
        }

        private static void SetChunkVisible(ChunkData chunk, bool visible)
        {
            if (chunk == null || chunk.renderers == null)
            {
                return;
            }

            for (int i = 0; i < chunk.renderers.Length; i++)
            {
                Renderer renderer = chunk.renderers[i];
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r < 0) != (divisor < 0)))
            {
                q--;
            }

            return q;
        }

        private void OnValidate()
        {
            chunkSize = Mathf.Clamp(chunkSize, 1, 256);
            visibleDistance = Mathf.Max(1f, visibleDistance);
            hideDistance = Mathf.Max(visibleDistance, hideDistance);
            updateFrames = Mathf.Clamp(updateFrames, 1, 60);
            maxChunksPerTick = Mathf.Clamp(maxChunksPerTick, 1, 4096);
        }
    }
}
