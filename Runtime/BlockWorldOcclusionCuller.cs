using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockWorldMVP
{
    [DisallowMultipleComponent]
    public class BlockWorldOcclusionCuller : MonoBehaviour
    {
        [Serializable]
        private sealed class ChunkData
        {
            public Bounds bounds;
            public Renderer[] renderers = Array.Empty<Renderer>();
            public HashSet<Collider> colliderSet = new HashSet<Collider>();
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

        [SerializeField]
        private bool runInEditor;

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

        [SerializeField]
        private int maxChunksPerTick = 256;

        [Header("Culling")]
        [SerializeField]
        private bool useFrustumCulling = true;

        [SerializeField]
        private bool useOcclusionCulling = true;

        [SerializeField]
        private float occlusionMinDistance = 6f;

        [SerializeField]
        private LayerMask occlusionMask = ~0;

        private readonly List<ChunkData> _chunks = new List<ChunkData>(256);
        private readonly Plane[] _planes = new Plane[6];
        private readonly RaycastHit[] _rayHits = new RaycastHit[8];

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

            Dictionary<ChunkKey, ChunkData> map = new Dictionary<ChunkKey, ChunkData>(256);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                Vector3 c = b.center;
                int cx = FloorDiv(Mathf.FloorToInt(c.x), Mathf.Max(1, chunkSize));
                int cy = FloorDiv(Mathf.FloorToInt(c.y), Mathf.Max(1, chunkSize));
                int cz = FloorDiv(Mathf.FloorToInt(c.z), Mathf.Max(1, chunkSize));
                ChunkKey key = new ChunkKey(cx, cy, cz);

                if (!map.TryGetValue(key, out ChunkData chunk))
                {
                    chunk = new ChunkData
                    {
                        bounds = b,
                        visible = true
                    };
                    map.Add(key, chunk);
                }
                else
                {
                    chunk.bounds.Encapsulate(b);
                }
            }

            Dictionary<ChunkKey, List<Renderer>> rendererLists = new Dictionary<ChunkKey, List<Renderer>>(map.Count);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds b = renderer.bounds;
                Vector3 c = b.center;
                int cx = FloorDiv(Mathf.FloorToInt(c.x), Mathf.Max(1, chunkSize));
                int cy = FloorDiv(Mathf.FloorToInt(c.y), Mathf.Max(1, chunkSize));
                int cz = FloorDiv(Mathf.FloorToInt(c.z), Mathf.Max(1, chunkSize));
                ChunkKey key = new ChunkKey(cx, cy, cz);

                if (!rendererLists.TryGetValue(key, out List<Renderer> list))
                {
                    list = new List<Renderer>(64);
                    rendererLists.Add(key, list);
                }

                list.Add(renderer);
            }

            foreach (KeyValuePair<ChunkKey, ChunkData> kv in map)
            {
                ChunkKey key = kv.Key;
                ChunkData chunk = kv.Value;
                if (rendererLists.TryGetValue(key, out List<Renderer> list))
                {
                    chunk.renderers = list.ToArray();
                    for (int i = 0; i < chunk.renderers.Length; i++)
                    {
                        Renderer r = chunk.renderers[i];
                        if (r == null)
                        {
                            continue;
                        }

                        Collider co = r.GetComponent<Collider>();
                        if (co != null)
                        {
                            chunk.colliderSet.Add(co);
                        }
                    }
                }

                _chunks.Add(chunk);
            }

            _knownChildCount = root.childCount;
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying && !runInEditor)
            {
                return;
            }

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

            _frameCounter++;
            int frameStep = Mathf.Clamp(updateFrames, 1, 60);
            if ((_frameCounter % frameStep) != 0)
            {
                return;
            }

            _camera = ResolveCamera();
            if (_camera == null)
            {
                return;
            }

            if (useFrustumCulling)
            {
                GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            }

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

            bool shouldBeVisible;
            if (chunk.visible)
            {
                shouldBeVisible = distance <= hideDistance;
            }
            else
            {
                shouldBeVisible = distance <= visibleDistance;
            }

            if (shouldBeVisible && useFrustumCulling)
            {
                shouldBeVisible = GeometryUtility.TestPlanesAABB(_planes, chunk.bounds);
            }

            if (shouldBeVisible && useOcclusionCulling && distance >= Mathf.Max(0f, occlusionMinDistance))
            {
                shouldBeVisible = !IsOccluded(chunk, camPos);
            }

            if (chunk.visible != shouldBeVisible)
            {
                chunk.visible = shouldBeVisible;
                SetChunkVisible(chunk, shouldBeVisible);
            }
        }

        private bool IsOccluded(ChunkData chunk, Vector3 camPos)
        {
            if (chunk == null || chunk.colliderSet == null || chunk.colliderSet.Count == 0)
            {
                return false;
            }

            Vector3 c = chunk.bounds.center;
            Vector3 e = chunk.bounds.extents;
            Vector3[] samplePoints =
            {
                c,
                c + new Vector3(e.x, 0f, e.z),
                c + new Vector3(-e.x, 0f, e.z),
                c + new Vector3(e.x, 0f, -e.z),
                c + new Vector3(-e.x, 0f, -e.z)
            };

            for (int i = 0; i < samplePoints.Length; i++)
            {
                if (IsSamplePointVisible(samplePoints[i], camPos, chunk))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSamplePointVisible(Vector3 sample, Vector3 camPos, ChunkData chunk)
        {
            Vector3 dir = sample - camPos;
            float dist = dir.magnitude;
            if (dist <= 0.001f)
            {
                return true;
            }

            Ray ray = new Ray(camPos, dir / dist);
            int hitCount = Physics.RaycastNonAlloc(ray, _rayHits, dist - 0.03f, occlusionMask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return true;
            }

            float nearest = float.MaxValue;
            Collider nearestCollider = null;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _rayHits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    nearestCollider = hit.collider;
                }
            }

            if (nearestCollider == null)
            {
                return true;
            }

            return chunk.colliderSet.Contains(nearestCollider);
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
            chunkSize = Mathf.Clamp(chunkSize, 4, 128);
            visibleDistance = Mathf.Max(1f, visibleDistance);
            hideDistance = Mathf.Max(visibleDistance, hideDistance);
            updateFrames = Mathf.Clamp(updateFrames, 1, 60);
            maxChunksPerTick = Mathf.Clamp(maxChunksPerTick, 1, 2048);
            occlusionMinDistance = Mathf.Max(0f, occlusionMinDistance);
        }
    }
}
