using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockWorldMVP.Editor
{
    public sealed class BlockWorldChunkConverterWindow : EditorWindow
    {
        private const string GeneratedMeshFolder = "Assets/BlockWorldGenerated/Meshes";
        private const string GeneratedChunkMeshFolder = "Assets/BlockWorldGenerated/Meshes/ChunkConverted";
        private const string ChunkMergedRootName = "__ChunkMerged";
        private enum SurfaceColliderMode
        {
            None = 0,
            TopOnly = 1,
            ExposedFaces = 2
        }

        private readonly struct ChunkMatKey : IEquatable<ChunkMatKey>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;
            public readonly int materialId;

            public ChunkMatKey(int x, int y, int z, int materialId)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.materialId = materialId;
            }

            public bool Equals(ChunkMatKey other)
            {
                return x == other.x && y == other.y && z == other.z && materialId == other.materialId;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkMatKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + x;
                    h = h * 31 + y;
                    h = h * 31 + z;
                    h = h * 31 + materialId;
                    return h;
                }
            }
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
                    int h = 17;
                    h = h * 31 + x;
                    h = h * 31 + y;
                    h = h * 31 + z;
                    return h;
                }
            }
        }

        private Transform _root;
        private int _chunkSize = 16;
        private SurfaceColliderMode _surfaceColliderMode = SurfaceColliderMode.TopOnly;
        private bool _addMeshCollider;
        private bool _rebuildCuller = true;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _subtleLabelStyle;

        [MenuItem("Tools/Block World MVP/Chunk Converter")]
        public static void Open()
        {
            GetWindow<BlockWorldChunkConverterWindow>(L("chunkconv.window.title"));
        }

        private static string L(string key)
        {
            return BlockWorldBuilderI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return BlockWorldBuilderI18n.Format(key, args);
        }

        private void OnGUI()
        {
            EnsureStyles();
            titleContent = new GUIContent(L("chunkconv.window.title"));

            using (new EditorGUILayout.VerticalScope(_sectionBoxStyle))
            {
                EditorGUILayout.LabelField(L("chunkconv.section.root"), _sectionTitleStyle);
                EditorGUILayout.Space(4f);
                _root = (Transform)EditorGUILayout.ObjectField(L("root.root"), _root, typeof(Transform), true);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L("root.chunk_size"), _subtleLabelStyle, GUILayout.Width(110f));
                    _chunkSize = Mathf.Max(1, EditorGUILayout.IntField(_chunkSize, GUILayout.Width(68f)));
                }
                _surfaceColliderMode = (SurfaceColliderMode)EditorGUILayout.IntPopup(
                    L("chunkconv.option.surface_mode"),
                    (int)_surfaceColliderMode,
                    new[]
                    {
                        L("chunkconv.surface.none"),
                        L("chunkconv.surface.top_only"),
                        L("chunkconv.surface.exposed")
                    },
                    new[] { 0, 1, 2 });
                _addMeshCollider = EditorGUILayout.ToggleLeft(L("chunkconv.option.mesh_collider"), _addMeshCollider);
                _rebuildCuller = EditorGUILayout.ToggleLeft(L("chunkconv.option.rebuild_culler"), _rebuildCuller);
                EditorGUILayout.LabelField(L("chunkconv.tip"), _subtleLabelStyle);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("root.convert_chunk_keep"), _primaryButtonStyle))
                {
                    Convert(deleteOriginal: false);
                }

                if (GUILayout.Button(L("root.convert_chunk_replace"), _dangerButtonStyle))
                {
                    Convert(deleteOriginal: true);
                }
            }
        }

        private void EnsureStyles()
        {
            if (_sectionBoxStyle == null)
            {
                _sectionBoxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            if (_sectionTitleStyle == null)
            {
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            }

            if (_primaryButtonStyle == null)
            {
                _primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 24f,
                    fontStyle = FontStyle.Bold
                };
            }

            if (_dangerButtonStyle == null)
            {
                _dangerButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 24f
                };
                _dangerButtonStyle.normal.textColor = new Color(1f, 0.56f, 0.56f, 1f);
            }

            if (_subtleLabelStyle == null)
            {
                _subtleLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) }
                };
            }
        }

        private void Convert(bool deleteOriginal)
        {
            if (_root == null)
            {
                EditorUtility.DisplayDialog(L("chunkconv.window.title"), L("tool.assign_root_help"), L("dialog.ok"));
                return;
            }

            List<PlacedBlock> sources = CollectPlacedBlocks(_root);
            if (sources.Count == 0)
            {
                EditorUtility.DisplayDialog(L("chunkconv.window.title"), L("dialog.convert_chunk_empty"), L("dialog.ok"));
                return;
            }

            Transform existingMerged = _root.Find(ChunkMergedRootName);
            if (existingMerged != null)
            {
                Undo.DestroyObjectImmediate(existingMerged.gameObject);
            }

            EnsureAssetFolderPath(GeneratedMeshFolder);
            EnsureAssetFolderPath(GeneratedChunkMeshFolder);
            CleanupChunkMeshAssets();

            GameObject mergedRootGo = new GameObject(ChunkMergedRootName);
            Undo.RegisterCreatedObjectUndo(mergedRootGo, "Convert To Chunk");
            mergedRootGo.transform.SetParent(_root, false);

            Dictionary<ChunkMatKey, List<CombineInstance>> buckets = new Dictionary<ChunkMatKey, List<CombineInstance>>(512);
            Dictionary<int, Material> materials = new Dictionary<int, Material>(64);
            bool buildSurfaceCollider = _surfaceColliderMode != SurfaceColliderMode.None;
            Dictionary<ChunkKey, HashSet<Vector3Int>> chunkVoxelMap = buildSurfaceCollider ? new Dictionary<ChunkKey, HashSet<Vector3Int>>(256) : null;
            HashSet<Vector3Int> occupied = buildSurfaceCollider ? new HashSet<Vector3Int>() : null;
            int accepted = 0;
            int skippedInvalid = 0;

            int chunkSize = Mathf.Max(1, _chunkSize);
            for (int i = 0; i < sources.Count; i++)
            {
                PlacedBlock placed = sources[i];
                if (placed == null)
                {
                    continue;
                }

                MeshFilter mf = placed.GetComponent<MeshFilter>();
                MeshRenderer mr = placed.GetComponent<MeshRenderer>();
                if (mf == null || mr == null || mf.sharedMesh == null || mr.sharedMaterial == null)
                {
                    skippedInvalid++;
                    continue;
                }

                Mesh mesh = mf.sharedMesh;
                Material mat = mr.sharedMaterial;
                int matId = mat.GetInstanceID();
                materials[matId] = mat;

                Vector3Int p = Vector3Int.RoundToInt(placed.transform.position);
                ChunkKey chunkKey = new ChunkKey(
                    FloorDiv(p.x, chunkSize),
                    FloorDiv(p.y, chunkSize),
                    FloorDiv(p.z, chunkSize));
                ChunkMatKey key = new ChunkMatKey(
                    chunkKey.x,
                    chunkKey.y,
                    chunkKey.z,
                    matId);

                if (!buckets.TryGetValue(key, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>(128);
                    buckets.Add(key, list);
                }

                list.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = 0,
                    transform = _root.worldToLocalMatrix * placed.transform.localToWorldMatrix
                });

                if (buildSurfaceCollider && occupied != null && chunkVoxelMap != null)
                {
                    occupied.Add(p);
                    if (!chunkVoxelMap.TryGetValue(chunkKey, out HashSet<Vector3Int> set))
                    {
                        set = new HashSet<Vector3Int>();
                        chunkVoxelMap.Add(chunkKey, set);
                    }

                    set.Add(p);
                }
                accepted++;
            }

            int createdRenderers = 0;
            int createdMeshColliders = 0;
            int createdSurfaceColliders = 0;
            Dictionary<ChunkKey, Transform> chunkRootMap = new Dictionary<ChunkKey, Transform>(128);
            foreach (KeyValuePair<ChunkMatKey, List<CombineInstance>> kv in buckets)
            {
                ChunkMatKey key = kv.Key;
                List<CombineInstance> list = kv.Value;
                if (list == null || list.Count == 0 || !materials.TryGetValue(key.materialId, out Material mat))
                {
                    continue;
                }

                ChunkKey chunkKey = new ChunkKey(key.x, key.y, key.z);
                if (!chunkRootMap.TryGetValue(chunkKey, out Transform chunkRoot))
                {
                    GameObject rootGo = new GameObject($"chunk_{key.x}_{key.y}_{key.z}");
                    rootGo.transform.SetParent(mergedRootGo.transform, false);
                    chunkRoot = rootGo.transform;
                    chunkRootMap.Add(chunkKey, chunkRoot);
                }

                GameObject go = new GameObject($"m{key.materialId}");
                go.transform.SetParent(chunkRoot, false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;

                Mesh merged = new Mesh
                {
                    name = $"Chunk_{key.x}_{key.y}_{key.z}_{createdRenderers}",
                    indexFormat = IndexFormat.UInt32
                };
                merged.CombineMeshes(list.ToArray(), true, true, false);
                merged.RecalculateBounds();

                string meshPath = $"{GeneratedChunkMeshFolder}/{SanitizeAssetName(_root.name)}_{key.x}_{key.y}_{key.z}_{createdRenderers}.asset";
                AssetDatabase.CreateAsset(merged, meshPath);
                mf.sharedMesh = merged;

                if (_addMeshCollider)
                {
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = merged;
                    createdMeshColliders++;
                }

                createdRenderers++;
            }

            if (buildSurfaceCollider && chunkVoxelMap != null && occupied != null)
            {
                foreach (KeyValuePair<ChunkKey, HashSet<Vector3Int>> kv in chunkVoxelMap)
                {
                    ChunkKey key = kv.Key;
                    if (!chunkRootMap.TryGetValue(key, out Transform chunkRoot))
                    {
                        continue;
                    }

                    Mesh surface = BuildSurfaceColliderMesh(key, kv.Value, occupied, _surfaceColliderMode);
                    if (surface == null)
                    {
                        continue;
                    }

                    GameObject surfaceGo = new GameObject("surface_collider");
                    surfaceGo.transform.SetParent(chunkRoot, false);
                    MeshCollider surfaceCollider = surfaceGo.AddComponent<MeshCollider>();
                    surfaceCollider.sharedMesh = surface;
                    createdSurfaceColliders++;
                }
            }

            if (_rebuildCuller)
            {
                EnsureRuntimeCuller(mergedRootGo.transform);
            }

            if (deleteOriginal)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (sources[i] != null)
                    {
                        Undo.DestroyObjectImmediate(sources[i].gameObject);
                    }
                }
            }

            EditorUtility.SetDirty(mergedRootGo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                L("chunkconv.window.title"),
                Lf("dialog.convert_chunk_summary", sources.Count, accepted, buckets.Count, createdRenderers, 0, 0, skippedInvalid)
                + "\n"
                + Lf("chunkconv.summary.colliders", createdMeshColliders, createdSurfaceColliders),
                L("dialog.ok"));
        }

        private static List<PlacedBlock> CollectPlacedBlocks(Transform root)
        {
            List<PlacedBlock> list = new List<PlacedBlock>();
            if (root == null)
            {
                return list;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null || string.Equals(child.name, ChunkMergedRootName, StringComparison.Ordinal))
                {
                    continue;
                }

                PlacedBlock placed = child.GetComponent<PlacedBlock>();
                if (placed != null)
                {
                    list.Add(placed);
                }
            }

            return list;
        }

        private static void EnsureRuntimeCuller(Transform root)
        {
            if (root == null)
            {
                return;
            }

            global::BlockWorldMVP.BlockWorldOcclusionCuller culler = root.GetComponent<global::BlockWorldMVP.BlockWorldOcclusionCuller>();
            if (culler == null)
            {
                culler = Undo.AddComponent<global::BlockWorldMVP.BlockWorldOcclusionCuller>(root.gameObject);
            }

            if (culler != null)
            {
                culler.Rebuild();
                EditorUtility.SetDirty(culler);
            }
        }

        private static void EnsureAssetFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void CleanupChunkMeshAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { GeneratedChunkMeshFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AssetDatabase.DeleteAsset(path);
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

        private static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "BlockWorldRoot";
            }

            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static Mesh BuildSurfaceColliderMesh(
            ChunkKey key,
            HashSet<Vector3Int> chunkVoxels,
            HashSet<Vector3Int> allVoxels,
            SurfaceColliderMode mode)
        {
            if (chunkVoxels == null || chunkVoxels.Count == 0 || allVoxels == null || mode == SurfaceColliderMode.None)
            {
                return null;
            }

            int facePerVoxel = mode == SurfaceColliderMode.ExposedFaces ? 6 : 1;
            List<Vector3> vertices = new List<Vector3>(chunkVoxels.Count * facePerVoxel * 4);
            List<int> triangles = new List<int>(chunkVoxels.Count * facePerVoxel * 6);

            foreach (Vector3Int pos in chunkVoxels)
            {
                if (mode == SurfaceColliderMode.TopOnly)
                {
                    if (allVoxels.Contains(pos + Vector3Int.up))
                    {
                        continue;
                    }

                    AddTopFace(vertices, triangles, pos);
                    continue;
                }

                if (!allVoxels.Contains(pos + Vector3Int.up))
                {
                    AddTopFace(vertices, triangles, pos);
                }
                if (!allVoxels.Contains(pos + Vector3Int.down))
                {
                    AddBottomFace(vertices, triangles, pos);
                }
                if (!allVoxels.Contains(pos + Vector3Int.left))
                {
                    AddLeftFace(vertices, triangles, pos);
                }
                if (!allVoxels.Contains(pos + Vector3Int.right))
                {
                    AddRightFace(vertices, triangles, pos);
                }
                if (!allVoxels.Contains(pos + new Vector3Int(0, 0, 1)))
                {
                    AddFrontFace(vertices, triangles, pos);
                }
                if (!allVoxels.Contains(pos + new Vector3Int(0, 0, -1)))
                {
                    AddBackFace(vertices, triangles, pos);
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"SurfaceCollider_{key.x}_{key.y}_{key.z}",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTopFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x;
            float y = pos.y + 0.5f;
            float z = pos.z;
            vertices.Add(new Vector3(x - 0.5f, y, z - 0.5f));
            vertices.Add(new Vector3(x + 0.5f, y, z - 0.5f));
            vertices.Add(new Vector3(x + 0.5f, y, z + 0.5f));
            vertices.Add(new Vector3(x - 0.5f, y, z + 0.5f));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddBottomFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x;
            float y = pos.y - 0.5f;
            float z = pos.z;
            vertices.Add(new Vector3(x - 0.5f, y, z + 0.5f));
            vertices.Add(new Vector3(x + 0.5f, y, z + 0.5f));
            vertices.Add(new Vector3(x + 0.5f, y, z - 0.5f));
            vertices.Add(new Vector3(x - 0.5f, y, z - 0.5f));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddLeftFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x - 0.5f;
            float y = pos.y;
            float z = pos.z;
            vertices.Add(new Vector3(x, y - 0.5f, z - 0.5f));
            vertices.Add(new Vector3(x, y - 0.5f, z + 0.5f));
            vertices.Add(new Vector3(x, y + 0.5f, z + 0.5f));
            vertices.Add(new Vector3(x, y + 0.5f, z - 0.5f));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddRightFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x + 0.5f;
            float y = pos.y;
            float z = pos.z;
            vertices.Add(new Vector3(x, y - 0.5f, z + 0.5f));
            vertices.Add(new Vector3(x, y - 0.5f, z - 0.5f));
            vertices.Add(new Vector3(x, y + 0.5f, z - 0.5f));
            vertices.Add(new Vector3(x, y + 0.5f, z + 0.5f));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddFrontFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x;
            float y = pos.y;
            float z = pos.z + 0.5f;
            vertices.Add(new Vector3(x + 0.5f, y - 0.5f, z));
            vertices.Add(new Vector3(x - 0.5f, y - 0.5f, z));
            vertices.Add(new Vector3(x - 0.5f, y + 0.5f, z));
            vertices.Add(new Vector3(x + 0.5f, y + 0.5f, z));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddBackFace(List<Vector3> vertices, List<int> triangles, Vector3Int pos)
        {
            int baseIndex = vertices.Count;
            float x = pos.x;
            float y = pos.y;
            float z = pos.z - 0.5f;
            vertices.Add(new Vector3(x - 0.5f, y - 0.5f, z));
            vertices.Add(new Vector3(x + 0.5f, y - 0.5f, z));
            vertices.Add(new Vector3(x + 0.5f, y + 0.5f, z));
            vertices.Add(new Vector3(x - 0.5f, y + 0.5f, z));
            AddQuadTriangles(triangles, baseIndex);
        }

        private static void AddQuadTriangles(List<int> triangles, int baseIndex)
        {
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }
    }
}
