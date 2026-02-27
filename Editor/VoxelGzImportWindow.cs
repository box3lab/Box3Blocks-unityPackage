using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockWorldMVP.Editor
{
    public sealed class VoxelGzImportWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3.blockworld-mvp/Assets/block";
        private const string BlockIdPath = "Packages/com.box3.blockworld-mvp/Assets/block-id.json";
        private const string GeneratedMeshFolder = "Assets/BlockWorldGenerated/Meshes/VoxelImport";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex(@"^(.*)_(back|bottom|front|left|right|top)\.png$", RegexOptions.Compiled);
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

        [Serializable]
        private sealed class VoxelPayload
        {
            public int[] shape;
            public int[] dir;
            public int[] indices;
            public int[] data;
            public int[] rot;
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
                    h = (h * 31) + x;
                    h = (h * 31) + y;
                    h = (h * 31) + z;
                    return h;
                }
            }
        }

        private sealed class BlockDefinition
        {
            public readonly Dictionary<string, string> sideTexturePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PreparedBlock
        {
            public bool valid;
            public Mesh mesh;
            public Material material;
        }

        private sealed class ChunkBucket
        {
            public readonly List<CombineInstance> combines = new List<CombineInstance>(2048);
        }

        private sealed class ImportStats
        {
            public int total;
            public int valid;
            public int createdChunks;
            public int createdBlocks;
            public int createdSurfaceColliders;
            public int skippedAir;
            public int skippedWater;
            public int skippedBarrier;
            public int skippedUnknown;
            public int skippedInvalid;
            public double startTime;
        }

        private enum SourceType
        {
            LocalFile,
            Url
        }

        private enum Phase
        {
            Idle,
            ProcessVoxels,
            BuildBlocks,
            BuildChunks,
            Done
        }

        private enum ImportMode
        {
            ChunkMerged,
            EditableBlocks
        }

        private readonly struct BlockInstanceData
        {
            public readonly string blockName;
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public BlockInstanceData(string blockName, Vector3 position, Quaternion rotation)
            {
                this.blockName = blockName;
                this.position = position;
                this.rotation = rotation;
            }
        }

        private SourceType _sourceType = SourceType.LocalFile;
        private string _localGzPath = string.Empty;
        private string _url = string.Empty;
        private Transform _parent;
        private Vector3Int _origin = Vector3Int.zero;
        private ImportMode _importMode = ImportMode.ChunkMerged;
        private bool _ignoreAir = true;
        private bool _ignoreWater = true;
        private bool _ignoreBarrier = false;
        private int _chunkSize = 16;
        private int _voxelsPerTick = 25000;
        private int _chunksPerTick = 6;
        private int _blocksPerTick = 3000;
        private bool _chunkUseAlphaClip = true;
        private float _chunkAlphaCutoff = 0.33f;
        private bool _clearPrevious = true;
        private bool _addMeshCollider;
        private bool _addSurfaceCollider;
        private bool _addBlockCollider = true;

        private Phase _phase = Phase.Idle;
        private string _status = "Idle";
        private float _progress;

        private VoxelPayload _payload;
        private Dictionary<int, string> _idToName;
        private Dictionary<string, BlockDefinition> _blockDefs;
        private Dictionary<string, PreparedBlock> _preparedByName;
        private Dictionary<ChunkKey, ChunkBucket> _chunkBuckets;
        private List<ChunkKey> _chunkKeys;
        private Transform _importRoot;
        private ImportStats _stats;
        private int _cursorVoxel;
        private int _cursorChunk;
        private Quaternion[] _rotLookup;
        private List<BlockInstanceData> _blockInstances;
        private Material _chunkMaterialInstance;
        private Dictionary<ChunkKey, HashSet<Vector3Int>> _chunkVoxelPositions;
        private HashSet<Vector3Int> _occupiedVoxels;

        [MenuItem("Tools/Block World MVP/Voxel GZ Importer")]
        public static void Open()
        {
            GetWindow<VoxelGzImportWindow>("Voxel GZ Importer");
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();
            if (_chunkMaterialInstance != null)
            {
                DestroyImmediate(_chunkMaterialInstance);
                _chunkMaterialInstance = null;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Voxel GZ Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                _sourceType = (SourceType)EditorGUILayout.EnumPopup("Source", _sourceType);
                if (_sourceType == SourceType.LocalFile)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _localGzPath = EditorGUILayout.TextField("GZ File", _localGzPath);
                        if (GUILayout.Button("Browse", GUILayout.Width(74f)))
                        {
                            string selected = EditorUtility.OpenFilePanel("Select voxel gzip file", Application.dataPath, "gz");
                            if (!string.IsNullOrWhiteSpace(selected))
                            {
                                _localGzPath = selected;
                            }
                        }
                    }
                }
                else
                {
                    _url = EditorGUILayout.TextField("URL", _url);
                }

                _parent = (Transform)EditorGUILayout.ObjectField("Parent", _parent, typeof(Transform), true);
                _origin = EditorGUILayout.Vector3IntField("Origin", _origin);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUILayout.LabelField("Import Options", EditorStyles.boldLabel);
                _importMode = (ImportMode)EditorGUILayout.EnumPopup("Mode", _importMode);
                _ignoreAir = EditorGUILayout.ToggleLeft("Ignore Air(id <= 0)", _ignoreAir);
                _ignoreWater = EditorGUILayout.ToggleLeft("Ignore Water blocks", _ignoreWater);
                _ignoreBarrier = EditorGUILayout.ToggleLeft("Ignore Barrier blocks", _ignoreBarrier);
                _clearPrevious = EditorGUILayout.ToggleLeft("Replace previous __VoxelImportGz", _clearPrevious);
                if (_importMode == ImportMode.ChunkMerged)
                {
                    _addMeshCollider = EditorGUILayout.ToggleLeft("Add Full MeshCollider to chunks", _addMeshCollider);
                    _addSurfaceCollider = EditorGUILayout.ToggleLeft("Add Surface Collider (Top Faces Only)", _addSurfaceCollider);
                }
                else
                {
                    _addBlockCollider = EditorGUILayout.ToggleLeft("Add MeshCollider to each block", _addBlockCollider);
                }

                using (new EditorGUI.DisabledScope(_importMode != ImportMode.ChunkMerged))
                {
                    _chunkSize = Mathf.Clamp(EditorGUILayout.IntField("Chunk Size", _chunkSize), 4, 64);
                    _chunksPerTick = Mathf.Clamp(EditorGUILayout.IntField("Chunks / Tick", _chunksPerTick), 1, 64);
                    _chunkUseAlphaClip = EditorGUILayout.ToggleLeft("Chunk: Use Alpha Clip (fix transparent artifacts)", _chunkUseAlphaClip);
                    _chunkAlphaCutoff = Mathf.Clamp01(EditorGUILayout.Slider("Chunk Alpha Cutoff", _chunkAlphaCutoff, 0.01f, 0.9f));
                }

                _voxelsPerTick = Mathf.Clamp(EditorGUILayout.IntField("Voxels / Tick", _voxelsPerTick), 2000, 200000);
                using (new EditorGUI.DisabledScope(_importMode != ImportMode.EditableBlocks))
                {
                    _blocksPerTick = Mathf.Clamp(EditorGUILayout.IntField("Blocks / Tick", _blocksPerTick), 200, 20000);
                }
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_phase != Phase.Idle);
                if (GUILayout.Button("Import", GUILayout.Height(28f)))
                {
                    StartImport();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(_phase == Phase.Idle);
                if (GUILayout.Button("Cancel", GUILayout.Height(28f)))
                {
                    CancelImport();
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_status, MessageType.None);
            Rect r = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(r, Mathf.Clamp01(_progress), $"{Mathf.RoundToInt(_progress * 100f)}%");
        }

        private void StartImport()
        {
            try
            {
                CancelImport(clearStatus: false);
                string json = _sourceType == SourceType.LocalFile ? ReadGzipJsonFromFile(_localGzPath) : ReadGzipJsonFromUrl(_url);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _status = "Failed: empty gzip json.";
                    return;
                }

                _payload = JsonUtility.FromJson<VoxelPayload>(json);
                if (!ValidatePayload(_payload, out string payloadError))
                {
                    _status = "Failed: " + payloadError;
                    return;
                }

                _idToName = LoadBlockIdMap();
                _blockDefs = LoadBlockDefinitions();
                _preparedByName = new Dictionary<string, PreparedBlock>(StringComparer.OrdinalIgnoreCase);
                _chunkBuckets = new Dictionary<ChunkKey, ChunkBucket>(512);
                _chunkKeys = null;
                _chunkVoxelPositions = _importMode == ImportMode.ChunkMerged && _addSurfaceCollider
                    ? new Dictionary<ChunkKey, HashSet<Vector3Int>>(512)
                    : null;
                _occupiedVoxels = _importMode == ImportMode.ChunkMerged && _addSurfaceCollider
                    ? new HashSet<Vector3Int>()
                    : null;
                if (_chunkMaterialInstance != null)
                {
                    DestroyImmediate(_chunkMaterialInstance);
                    _chunkMaterialInstance = null;
                }
                _stats = new ImportStats
                {
                    total = Mathf.Min(_payload.indices.Length, _payload.data.Length),
                    startTime = EditorApplication.timeSinceStartup
                };
                _blockInstances = _importMode == ImportMode.EditableBlocks
                    ? new List<BlockInstanceData>(Mathf.Max(2048, _stats.total / 2))
                    : null;
                _cursorVoxel = 0;
                _cursorChunk = 0;
                _rotLookup = new[]
                {
                    Quaternion.identity,
                    Quaternion.Euler(0f, 90f, 0f),
                    Quaternion.Euler(0f, 180f, 0f),
                    Quaternion.Euler(0f, 270f, 0f)
                };

                PrepareRoot();
                _phase = Phase.ProcessVoxels;
                _status = "Processing voxels...";
                _progress = 0f;
                EditorApplication.update += OnEditorUpdate;
            }
            catch (Exception ex)
            {
                _phase = Phase.Idle;
                _status = "Failed: " + ex.Message;
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnEditorUpdate()
        {
            try
            {
                if (_phase == Phase.ProcessVoxels)
                {
                    TickProcessVoxels();
                    return;
                }

                if (_phase == Phase.BuildBlocks)
                {
                    TickBuildBlocks();
                    return;
                }

                if (_phase == Phase.BuildChunks)
                {
                    TickBuildChunks();
                    return;
                }

                if (_phase == Phase.Done)
                {
                    CompleteImport();
                }
            }
            catch (Exception ex)
            {
                CancelImport(clearStatus: false);
                _status = "Failed: " + ex.Message;
            }
        }

        private void TickProcessVoxels()
        {
            int shapeX = _payload.shape[0];
            int shapeY = _payload.shape[1];
            int shapeXY = shapeX * shapeY;
            int dirX = _payload.dir[0];
            int dirY = _payload.dir[1];
            int dirZ = _payload.dir[2];
            int total = _stats.total;
            int maxIndex = Mathf.Min(total, _cursorVoxel + _voxelsPerTick);

            for (int i = _cursorVoxel; i < maxIndex; i++)
            {
                int id = _payload.data[i];
                if (_ignoreAir && id <= 0)
                {
                    _stats.skippedAir++;
                    continue;
                }

                if (!_idToName.TryGetValue(id, out string blockName) || string.IsNullOrWhiteSpace(blockName))
                {
                    _stats.skippedUnknown++;
                    continue;
                }

                if (_ignoreWater && IsWaterBlock(blockName))
                {
                    _stats.skippedWater++;
                    continue;
                }

                if (_ignoreBarrier && IsBarrierBlock(blockName))
                {
                    _stats.skippedBarrier++;
                    continue;
                }

                PreparedBlock prepared = GetOrBuildPreparedBlock(blockName);
                if (prepared == null || !prepared.valid || prepared.mesh == null)
                {
                    _stats.skippedInvalid++;
                    continue;
                }

                int idx = _payload.indices[i];
                int x = idx % shapeX;
                int y = (idx / shapeX) % shapeY;
                int z = idx / shapeXY;

                int wx = _origin.x + (dirX * x);
                int wy = _origin.y + (dirY * y);
                int wz = _origin.z + (dirZ * z);
                int rot = (_payload.rot != null && _payload.rot.Length > i) ? (_payload.rot[i] & 3) : 0;
                Vector3 worldPos = new Vector3(wx, wy, wz);
                Quaternion worldRot = _rotLookup[rot];
                if (_importMode == ImportMode.ChunkMerged)
                {
                    ChunkKey key = new ChunkKey(
                        FloorDiv(wx, _chunkSize),
                        FloorDiv(wy, _chunkSize),
                        FloorDiv(wz, _chunkSize));
                    if (!_chunkBuckets.TryGetValue(key, out ChunkBucket bucket))
                    {
                        bucket = new ChunkBucket();
                        _chunkBuckets.Add(key, bucket);
                    }

                    bucket.combines.Add(new CombineInstance
                    {
                        mesh = prepared.mesh,
                        subMeshIndex = 0,
                        transform = Matrix4x4.TRS(worldPos, worldRot, Vector3.one)
                    });

                    if (_addSurfaceCollider && _occupiedVoxels != null && _chunkVoxelPositions != null)
                    {
                        Vector3Int gridPos = new Vector3Int(wx, wy, wz);
                        _occupiedVoxels.Add(gridPos);
                        if (!_chunkVoxelPositions.TryGetValue(key, out HashSet<Vector3Int> set))
                        {
                            set = new HashSet<Vector3Int>();
                            _chunkVoxelPositions.Add(key, set);
                        }

                        set.Add(gridPos);
                    }
                }
                else
                {
                    _blockInstances.Add(new BlockInstanceData(blockName, worldPos, worldRot));
                }
                _stats.valid++;
            }

            _cursorVoxel = maxIndex;
            _progress = total > 0 ? (float)_cursorVoxel / total : 0f;
            _status = $"Processing voxels: {_cursorVoxel}/{total}";
            EditorUtility.DisplayProgressBar("Voxel GZ Import", _status, _progress * 0.85f);
            Repaint();

            if (_cursorVoxel < total)
            {
                return;
            }

            if (_importMode == ImportMode.ChunkMerged)
            {
                _chunkKeys = new List<ChunkKey>(_chunkBuckets.Keys);
                _chunkKeys.Sort((a, b) =>
                {
                    int cmp = a.x.CompareTo(b.x);
                    if (cmp != 0) return cmp;
                    cmp = a.y.CompareTo(b.y);
                    return cmp != 0 ? cmp : a.z.CompareTo(b.z);
                });
                _phase = Phase.BuildChunks;
                _status = "Building chunk meshes...";
            }
            else
            {
                _phase = Phase.BuildBlocks;
                _status = "Creating editable blocks...";
            }
        }

        private void TickBuildChunks()
        {
            if (_chunkKeys == null || _chunkKeys.Count == 0)
            {
                _phase = Phase.Done;
                return;
            }

            int maxIndex = Mathf.Min(_chunkKeys.Count, _cursorChunk + _chunksPerTick);
            for (int i = _cursorChunk; i < maxIndex; i++)
            {
                ChunkKey key = _chunkKeys[i];
                if (!_chunkBuckets.TryGetValue(key, out ChunkBucket bucket) || bucket.combines.Count == 0)
                {
                    continue;
                }

                GameObject go = new GameObject($"chunk_{key.x}_{key.y}_{key.z}");
                go.transform.SetParent(_importRoot, false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ResolveChunkMaterial();

                Mesh mesh = new Mesh
                {
                    name = $"VoxelChunk_{key.x}_{key.y}_{key.z}",
                    indexFormat = IndexFormat.UInt32
                };
                mesh.CombineMeshes(bucket.combines.ToArray(), true, true, false);
                mesh.RecalculateBounds();

                string assetPath = BuildChunkMeshAssetPath(key, i);
                AssetDatabase.CreateAsset(mesh, assetPath);
                mf.sharedMesh = mesh;

                if (_addMeshCollider)
                {
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                }

                if (_addSurfaceCollider
                    && _chunkVoxelPositions != null
                    && _chunkVoxelPositions.TryGetValue(key, out HashSet<Vector3Int> chunkVoxels))
                {
                    Mesh surface = BuildTopSurfaceColliderMesh(key, chunkVoxels, _occupiedVoxels);
                    if (surface != null)
                    {
                        GameObject surfaceGo = new GameObject("surface_collider");
                        surfaceGo.transform.SetParent(go.transform, false);
                        MeshCollider surfaceCollider = surfaceGo.AddComponent<MeshCollider>();
                        surfaceCollider.sharedMesh = surface;
                        _stats.createdSurfaceColliders++;
                    }
                }

                _stats.createdChunks++;
            }

            _cursorChunk = maxIndex;
            _progress = _chunkKeys.Count > 0 ? (float)_cursorChunk / _chunkKeys.Count : 1f;
            _status = $"Building chunks: {_cursorChunk}/{_chunkKeys.Count}";
            EditorUtility.DisplayProgressBar("Voxel GZ Import", _status, 0.85f + (_progress * 0.15f));
            Repaint();

            if (_cursorChunk >= _chunkKeys.Count)
            {
                _phase = Phase.Done;
            }
        }

        private void TickBuildBlocks()
        {
            if (_blockInstances == null || _blockInstances.Count == 0)
            {
                _phase = Phase.Done;
                return;
            }

            int maxIndex = Mathf.Min(_blockInstances.Count, _cursorChunk + _blocksPerTick);
            for (int i = _cursorChunk; i < maxIndex; i++)
            {
                BlockInstanceData entry = _blockInstances[i];
                PreparedBlock prepared = GetOrBuildPreparedBlock(entry.blockName);
                if (prepared == null || !prepared.valid || prepared.mesh == null || prepared.material == null)
                {
                    continue;
                }

                GameObject go = new GameObject($"{entry.blockName}_{Mathf.RoundToInt(entry.position.x)}_{Mathf.RoundToInt(entry.position.y)}_{Mathf.RoundToInt(entry.position.z)}");
                go.transform.SetParent(_importRoot, false);
                go.transform.position = entry.position;
                go.transform.rotation = entry.rotation;

                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = prepared.mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = prepared.material;
                if (_addBlockCollider)
                {
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = prepared.mesh;
                }

                PlacedBlock marker = go.AddComponent<PlacedBlock>();
                marker.BlockId = entry.blockName;
                marker.HasAnimation = false;

                _stats.createdBlocks++;
            }

            _cursorChunk = maxIndex;
            _progress = _blockInstances.Count > 0 ? (float)_cursorChunk / _blockInstances.Count : 1f;
            _status = $"Creating editable blocks: {_cursorChunk}/{_blockInstances.Count}";
            EditorUtility.DisplayProgressBar("Voxel GZ Import", _status, 0.85f + (_progress * 0.15f));
            Repaint();

            if (_cursorChunk >= _blockInstances.Count)
            {
                _phase = Phase.Done;
            }
        }

        private void CompleteImport()
        {
            _phase = Phase.Idle;
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();

            if (_importRoot != null && _importMode == ImportMode.ChunkMerged)
            {
                BlockWorldOcclusionCuller culler = _importRoot.GetComponent<BlockWorldOcclusionCuller>();
                if (culler == null)
                {
                    culler = _importRoot.gameObject.AddComponent<BlockWorldOcclusionCuller>();
                }

                culler.Rebuild();
                EditorUtility.SetDirty(culler);
            }

            if (_importRoot != null)
            {
                Selection.activeObject = _importRoot.gameObject;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            double sec = EditorApplication.timeSinceStartup - _stats.startTime;
            string summary =
                $"Import complete.\n" +
                $"Mode: {_importMode}\n" +
                $"Total Voxels: {_stats.total}\n" +
                $"Imported Voxels: {_stats.valid}\n" +
                $"Created Chunks: {_stats.createdChunks}\n" +
                $"Created Blocks: {_stats.createdBlocks}\n" +
                $"Surface Colliders: {_stats.createdSurfaceColliders}\n" +
                $"Skipped Air/Water/Barrier/Unknown/Invalid: {_stats.skippedAir}/{_stats.skippedWater}/{_stats.skippedBarrier}/{_stats.skippedUnknown}/{_stats.skippedInvalid}\n" +
                $"Time: {sec:F2}s";
            _status = summary.Replace("\n", " | ");
            EditorUtility.DisplayDialog("Voxel GZ Importer", summary, "OK");
            Repaint();
        }

        private void CancelImport(bool clearStatus = true)
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();
            _phase = Phase.Idle;
            _progress = 0f;
            if (clearStatus)
            {
                _status = "Idle";
            }
        }

        private void PrepareRoot()
        {
            Transform parent = _parent;
            if (parent == null)
            {
                GameObject parentGo = new GameObject("VoxelImportRoot");
                parent = parentGo.transform;
                _parent = parent;
            }

            Transform existing = parent.Find("__VoxelImportGz");
            if (existing != null && _clearPrevious)
            {
                DestroyImmediate(existing.gameObject);
            }

            GameObject importRootGo = new GameObject("__VoxelImportGz");
            importRootGo.transform.SetParent(parent, false);
            _importRoot = importRootGo.transform;

            EnsureAssetFolderPath(GeneratedMeshFolder);
            if (_clearPrevious)
            {
                CleanupGeneratedChunkMeshes();
            }
        }

        private string BuildChunkMeshAssetPath(ChunkKey key, int index)
        {
            string parentName = _parent != null ? SanitizeName(_parent.name) : "Root";
            string name = $"{parentName}_chunk_{key.x}_{key.y}_{key.z}_{index}.asset";
            return $"{GeneratedMeshFolder}/{name}";
        }

        private void CleanupGeneratedChunkMeshes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { GeneratedMeshFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private PreparedBlock GetOrBuildPreparedBlock(string blockName)
        {
            if (_preparedByName.TryGetValue(blockName, out PreparedBlock cached))
            {
                return cached;
            }

            PreparedBlock prepared = new PreparedBlock { valid = false };
            if (!_blockDefs.TryGetValue(blockName, out BlockDefinition def) || def == null || def.sideTexturePaths.Count == 0)
            {
                _preparedByName[blockName] = prepared;
                return prepared;
            }

            if (!BlockAssetFactory.TryGetFaceRenderData(def.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData)
                || renderData == null
                || renderData.faceMainTexSt == null
                || renderData.faceMainTexSt.Length < SideOrder.Length
                || renderData.materials == null
                || renderData.materials.Length == 0
                || renderData.materials[0] == null)
            {
                _preparedByName[blockName] = prepared;
                return prepared;
            }

            prepared.mesh = BuildStaticBlockMesh(blockName, renderData.faceMainTexSt);
            prepared.material = renderData.materials[0];
            prepared.valid = prepared.mesh != null && prepared.material != null;
            _preparedByName[blockName] = prepared;
            return prepared;
        }

        private Material ResolveSharedMaterial()
        {
            foreach (KeyValuePair<string, PreparedBlock> kv in _preparedByName)
            {
                if (kv.Value != null && kv.Value.valid && kv.Value.material != null)
                {
                    return kv.Value.material;
                }
            }

            return null;
        }

        private Material ResolveChunkMaterial()
        {
            Material source = ResolveSharedMaterial();
            if (source == null)
            {
                return null;
            }

            if (!_chunkUseAlphaClip)
            {
                return source;
            }

            if (_chunkMaterialInstance != null)
            {
                _chunkMaterialInstance.SetFloat("_Cutoff", _chunkAlphaCutoff);
                return _chunkMaterialInstance;
            }

            Shader cutoutShader = Shader.Find("Standard");
            if (cutoutShader == null)
            {
                cutoutShader = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse");
            }

            if (cutoutShader == null)
            {
                // Fallback if cutout shader is unavailable.
                _chunkMaterialInstance = new Material(source);
                _chunkMaterialInstance.name = source.name + "_ChunkZWrite";
                _chunkMaterialInstance.SetInt("_ZWrite", 1);
                return _chunkMaterialInstance;
            }

            Material m = new Material(cutoutShader)
            {
                name = source.name + "_ChunkCutout"
            };
            m.mainTexture = source.mainTexture;
            m.SetFloat("_Cutoff", _chunkAlphaCutoff);

            if (string.Equals(cutoutShader.name, "Standard", StringComparison.Ordinal))
            {
                m.SetFloat("_Mode", 1f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.Zero);
                m.SetInt("_ZWrite", 1);
                m.DisableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }

            _chunkMaterialInstance = m;
            return _chunkMaterialInstance;
        }

        private static Mesh BuildTopSurfaceColliderMesh(ChunkKey key, HashSet<Vector3Int> chunkVoxels, HashSet<Vector3Int> allVoxels)
        {
            if (chunkVoxels == null || chunkVoxels.Count == 0 || allVoxels == null)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>(chunkVoxels.Count * 4);
            List<int> triangles = new List<int>(chunkVoxels.Count * 6);
            Vector3Int up = Vector3Int.up;

            foreach (Vector3Int pos in chunkVoxels)
            {
                if (allVoxels.Contains(pos + up))
                {
                    continue;
                }

                int baseIndex = vertices.Count;
                float x = pos.x;
                float y = pos.y + 0.5f;
                float z = pos.z;
                vertices.Add(new Vector3(x - 0.5f, y, z - 0.5f));
                vertices.Add(new Vector3(x + 0.5f, y, z - 0.5f));
                vertices.Add(new Vector3(x + 0.5f, y, z + 0.5f));
                vertices.Add(new Vector3(x - 0.5f, y, z + 0.5f));

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
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

        private static Mesh BuildStaticBlockMesh(string blockName, Vector4[] faceMainTexSt)
        {
            Vector3[] vertices = new Vector3[24]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };

            Vector2[] uvs = new Vector2[24];
            for (int face = 0; face < SideOrder.Length; face++)
            {
                Vector4 st = faceMainTexSt[face];
                int offset = face * 4;
                uvs[offset + 0] = new Vector2(st.z, st.w);
                uvs[offset + 1] = new Vector2(st.z + st.x, st.w);
                uvs[offset + 2] = new Vector2(st.z + st.x, st.w + st.y);
                uvs[offset + 3] = new Vector2(st.z, st.w + st.y);
            }

            int[] triangles = new int[36]
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5, 4, 7, 6,
                8, 10, 9, 8, 11, 10,
                12, 14, 13, 12, 15, 14,
                16, 18, 17, 16, 19, 18,
                20, 22, 21, 20, 23, 22
            };

            Mesh mesh = new Mesh { name = "VoxelStatic_" + blockName };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static bool ValidatePayload(VoxelPayload payload, out string error)
        {
            if (payload == null)
            {
                error = "json parse failed.";
                return false;
            }

            if (payload.shape == null || payload.shape.Length < 3)
            {
                error = "shape is missing or invalid.";
                return false;
            }

            if (payload.indices == null || payload.data == null)
            {
                error = "indices/data is missing.";
                return false;
            }

            if (payload.shape[0] <= 0 || payload.shape[1] <= 0 || payload.shape[2] <= 0)
            {
                error = "shape values must be > 0.";
                return false;
            }

            if (payload.dir == null || payload.dir.Length < 3)
            {
                payload.dir = new[] { 1, 1, 1 };
            }

            error = null;
            return true;
        }

        private static string ReadGzipJsonFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception("GZ file path is empty.");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("GZ file not found.", path);
            }

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (MemoryStream ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string ReadGzipJsonFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Exception("URL is empty.");
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 10000;
            request.ReadWriteTimeout = 30000;
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (GZipStream gz = new GZipStream(stream, CompressionMode.Decompress))
            using (MemoryStream ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static Dictionary<int, string> LoadBlockIdMap()
        {
            Dictionary<int, string> map = new Dictionary<int, string>(1024);
            string absPath = GetProjectAbsolutePath(BlockIdPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            MatchCollection matches = FlatMapRegex.Matches(json);
            for (int i = 0; i < matches.Count; i++)
            {
                Match m = matches[i];
                if (!int.TryParse(m.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    continue;
                }

                string name = m.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    map[id] = name;
                }
            }

            return map;
        }

        private static Dictionary<string, BlockDefinition> LoadBlockDefinitions()
        {
            Dictionary<string, BlockDefinition> defs = new Dictionary<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlockTextureFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileName(path);
                Match match = SideRegex.Match(fileName);
                if (!match.Success)
                {
                    continue;
                }

                string id = match.Groups[1].Value;
                string side = match.Groups[2].Value;
                if (!defs.TryGetValue(id, out BlockDefinition def))
                {
                    def = new BlockDefinition();
                    defs[id] = def;
                }

                def.sideTexturePaths[side] = path;
            }

            return defs;
        }

        private static bool IsWaterBlock(string blockName)
        {
            return blockName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBarrierBlock(string blockName)
        {
            return blockName.IndexOf("barrier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0)
            {
                return 0;
            }

            if (value >= 0)
            {
                return value / divisor;
            }

            return -((-value + divisor - 1) / divisor);
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(root, assetPath).Replace("\\", "/");
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "VoxelImport";
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
    }
}
