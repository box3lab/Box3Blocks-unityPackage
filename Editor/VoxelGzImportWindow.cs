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
using BlockWorldMVP;

namespace BlockWorldMVP.Editor
{
    public sealed class VoxelGzImportWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3.blockworld-mvp/Assets/block";
        private const string BlockIdPath = "Packages/com.box3.blockworld-mvp/Assets/block-id.json";
        private const string BlockSpecPath = "Packages/com.box3.blockworld-mvp/Assets/block-spec.json";
        private const string GeneratedMeshFolder = "Assets/BlockWorldGenerated/Meshes/VoxelImport";
        private const string GeneratedMaterialFolder = "Assets/BlockWorldGenerated/Materials";
        private const string ChunkOpaqueMaterialPath = "Assets/BlockWorldGenerated/Materials/VoxelImport_ChunkOpaque.mat";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Vector3[] FaceNormals =
        {
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, -1f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f)
        };
        private static readonly Vector3[][] FaceVertices =
        {
            new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            },
            new[]
            {
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f)
            },
            new[]
            {
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            }
        };
        private static readonly Regex SideRegex = new Regex(@"^(.*)_(back|bottom|front|left|right|top)\.png$", RegexOptions.Compiled);
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly Dictionary<string, string> FallbackEn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voxel.window.title"] = "Voxel GZ Importer",
            ["voxel.section.source"] = "Source",
            ["voxel.section.options"] = "Import Options",
            ["voxel.section.run"] = "Run",
            ["voxel.section.export"] = "Export",
            ["voxel.section.status"] = "Status",
            ["voxel.source.mode"] = "Source Mode",
            ["voxel.source.local"] = "Local File",
            ["voxel.source.url"] = "URL",
            ["voxel.source.gz_file"] = "GZ File",
            ["voxel.source.browse"] = "Browse",
            ["voxel.source.select_file"] = "Select voxel gzip file",
            ["voxel.source.url_value"] = "URL",
            ["voxel.source.parent"] = "Parent",
            ["voxel.source.create_root"] = "Create Root",
            ["voxel.source.origin"] = "Origin",
            ["voxel.option.ignore_barrier"] = "Ignore Barrier blocks",
            ["voxel.option.import_mode"] = "Import Mode",
            ["voxel.mode.chunk"] = "Chunk (Recommended)",
            ["voxel.mode.single_block"] = "Single Block (Editable)",
            ["voxel.option.replace_previous"] = "Replace previous __VoxelImportGz",
            ["voxel.option.surface_collider"] = "Add Surface Collider (Top Faces Only)",
            ["voxel.option.mesh_collider"] = "Add Full MeshCollider",
            ["voxel.option.chunk_size"] = "Chunk Size (1 = Whole Scene)",
            ["voxel.option.chunks_per_tick"] = "Chunks / Tick",
            ["voxel.option.alpha_clip"] = "Chunk: Use Alpha Clip (fix transparent artifacts)",
            ["voxel.option.alpha_cutoff"] = "Chunk Alpha Cutoff",
            ["voxel.option.voxels_per_tick"] = "Voxels / Tick",
            ["voxel.option.fixed_filters"] = "Fixed filters: Air and Water are always ignored for stability.",
            ["voxel.run.import"] = "Import",
            ["voxel.run.cancel"] = "Cancel",
            ["voxel.export.root"] = "Export Root",
            ["voxel.export.gz_file"] = "Export GZ",
            ["voxel.export.browse"] = "Browse",
            ["voxel.export.select_file"] = "Save voxel gzip file",
            ["voxel.export.run"] = "Export GZ",
            ["voxel.export.err.no_root"] = "Export Root is empty.",
            ["voxel.export.err.empty"] = "No PlacedBlock found under Export Root.",
            ["voxel.export.done"] = "Export complete. Blocks: {0}, Skipped unknown: {1}",
            ["voxel.status.idle"] = "Idle",
            ["voxel.status.percent"] = "{0}%",
            ["voxel.status.processing_start"] = "Processing voxels...",
            ["voxel.status.processing_progress"] = "Processing voxels: {0}/{1}",
            ["voxel.status.placing_blocks"] = "Placing blocks...",
            ["voxel.status.placing_progress"] = "Placing blocks: {0}/{1}",
            ["voxel.status.building_start"] = "Building chunk meshes...",
            ["voxel.status.building_progress"] = "Building chunks: {0}/{1}",
            ["voxel.done.title"] = "Import complete.",
            ["voxel.done.total"] = "Total Voxels: {0}",
            ["voxel.done.imported"] = "Imported Voxels: {0}",
            ["voxel.done.chunks"] = "Created Chunks: {0}",
            ["voxel.done.blocks"] = "Created Blocks: {0}",
            ["voxel.done.surface_colliders"] = "Surface Colliders: {0}",
            ["voxel.done.mesh_colliders"] = "Mesh Colliders: {0}",
            ["voxel.done.skipped"] = "Skipped Air/Water/Barrier/Unknown/Invalid: {0}/{1}/{2}/{3}/{4}",
            ["voxel.done.time"] = "Time: {0}s",
            ["voxel.err.failed_with_reason"] = "Failed: {0}",
            ["voxel.err.empty_json"] = "Failed: empty gzip json.",
            ["voxel.err.json_parse"] = "json parse failed.",
            ["voxel.err.shape_invalid"] = "shape is missing or invalid.",
            ["voxel.err.indices_data_missing"] = "indices/data is missing.",
            ["voxel.err.shape_values_invalid"] = "shape values must be > 0.",
            ["voxel.err.gz_path_empty"] = "GZ file path is empty.",
            ["voxel.err.gz_not_found"] = "GZ file not found.",
            ["voxel.err.url_empty"] = "URL is empty."
        };
        private static readonly Dictionary<string, string> FallbackZh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["voxel.window.title"] = "体素 GZ 导入器",
            ["voxel.section.source"] = "导入来源",
            ["voxel.section.options"] = "导入选项",
            ["voxel.section.run"] = "执行",
            ["voxel.section.export"] = "导出",
            ["voxel.section.status"] = "状态",
            ["voxel.source.mode"] = "来源模式",
            ["voxel.source.local"] = "本地文件",
            ["voxel.source.url"] = "网络地址",
            ["voxel.source.gz_file"] = "GZ 文件",
            ["voxel.source.browse"] = "浏览",
            ["voxel.source.select_file"] = "选择体素 gzip 文件",
            ["voxel.source.url_value"] = "URL",
            ["voxel.source.parent"] = "父节点",
            ["voxel.source.create_root"] = "创建根节点",
            ["voxel.source.origin"] = "原点",
            ["voxel.option.ignore_barrier"] = "忽略 Barrier 方块",
            ["voxel.option.import_mode"] = "导入模式",
            ["voxel.mode.chunk"] = "Chunk（推荐）",
            ["voxel.mode.single_block"] = "单个方块（可编辑）",
            ["voxel.option.replace_previous"] = "替换上一次 __VoxelImportGz",
            ["voxel.option.surface_collider"] = "添加表面碰撞（仅顶面）",
            ["voxel.option.mesh_collider"] = "添加完整 MeshCollider",
            ["voxel.option.chunk_size"] = "Chunk 尺寸（1=整体）",
            ["voxel.option.chunks_per_tick"] = "每 Tick Chunk 数",
            ["voxel.option.alpha_clip"] = "Chunk 使用 Alpha Clip（修复透明伪影）",
            ["voxel.option.alpha_cutoff"] = "Chunk Alpha 阈值",
            ["voxel.option.voxels_per_tick"] = "每 Tick 体素数",
            ["voxel.option.fixed_filters"] = "固定过滤：默认始终忽略 Air 和 Water。",
            ["voxel.run.import"] = "导入",
            ["voxel.run.cancel"] = "取消",
            ["voxel.export.root"] = "导出根节点",
            ["voxel.export.gz_file"] = "导出 GZ",
            ["voxel.export.browse"] = "浏览",
            ["voxel.export.select_file"] = "保存体素 gzip 文件",
            ["voxel.export.run"] = "导出 GZ",
            ["voxel.export.err.no_root"] = "导出根节点为空。",
            ["voxel.export.err.empty"] = "导出根节点下没有 PlacedBlock。",
            ["voxel.export.done"] = "导出完成。方块数: {0}，跳过未知: {1}",
            ["voxel.status.idle"] = "空闲",
            ["voxel.status.percent"] = "{0}%",
            ["voxel.status.processing_start"] = "正在处理体素...",
            ["voxel.status.processing_progress"] = "处理体素: {0}/{1}",
            ["voxel.status.placing_blocks"] = "正在放置方块...",
            ["voxel.status.placing_progress"] = "放置方块: {0}/{1}",
            ["voxel.status.building_start"] = "正在构建 Chunk 网格...",
            ["voxel.status.building_progress"] = "构建 Chunk: {0}/{1}",
            ["voxel.done.title"] = "导入完成。",
            ["voxel.done.total"] = "体素总数: {0}",
            ["voxel.done.imported"] = "导入体素: {0}",
            ["voxel.done.chunks"] = "生成 Chunk: {0}",
            ["voxel.done.blocks"] = "生成方块: {0}",
            ["voxel.done.surface_colliders"] = "表面碰撞体: {0}",
            ["voxel.done.mesh_colliders"] = "完整 MeshCollider: {0}",
            ["voxel.done.skipped"] = "跳过 Air/Water/Barrier/Unknown/Invalid: {0}/{1}/{2}/{3}/{4}",
            ["voxel.done.time"] = "耗时: {0}s",
            ["voxel.err.failed_with_reason"] = "失败: {0}",
            ["voxel.err.empty_json"] = "失败: gzip json 为空。",
            ["voxel.err.json_parse"] = "json 解析失败。",
            ["voxel.err.shape_invalid"] = "shape 缺失或格式错误。",
            ["voxel.err.indices_data_missing"] = "indices/data 缺失。",
            ["voxel.err.shape_values_invalid"] = "shape 值必须大于 0。",
            ["voxel.err.gz_path_empty"] = "GZ 文件路径为空。",
            ["voxel.err.gz_not_found"] = "未找到 GZ 文件。",
            ["voxel.err.url_empty"] = "URL 为空。"
        };

        [Serializable]
        private sealed class VoxelPayload
        {
            public string formatVersion;
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
            public Material[] materials;
            public Vector4[] faceMainTexSt;
            public bool hasAnimation;
            public BlockTextureAnimator.FaceAnimation[] animations;
            public bool usesSubmeshes;
        }

        private sealed class ChunkBucket
        {
            public readonly List<CombineInstance> opaqueCombines = new List<CombineInstance>(2048);
            public readonly List<TransparentVoxel> transparentVoxels = new List<TransparentVoxel>(512);
        }

        private sealed class ImportStats
        {
            public int total;
            public int valid;
            public int createdChunks;
            public int createdBlocks;
            public int createdSurfaceColliders;
            public int createdMeshColliders;
            public int skippedAir;
            public int skippedWater;
            public int skippedBarrier;
            public int skippedUnknown;
            public int skippedInvalid;
            public double startTime;
        }

        private readonly struct TransparentVoxel
        {
            public readonly Vector3Int pos;
            public readonly int rot;
            public readonly PreparedBlock prepared;

            public TransparentVoxel(Vector3Int pos, int rot, PreparedBlock prepared)
            {
                this.pos = pos;
                this.rot = rot;
                this.prepared = prepared;
            }
        }

        private readonly struct PendingBlock
        {
            public readonly Vector3Int pos;
            public readonly int rot;
            public readonly PreparedBlock prepared;
            public readonly string blockName;

            public PendingBlock(Vector3Int pos, int rot, PreparedBlock prepared, string blockName)
            {
                this.pos = pos;
                this.rot = rot;
                this.prepared = prepared;
                this.blockName = blockName;
            }
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
            PlaceSingleBlocks,
            BuildChunks,
            Done
        }

        private enum ImportMode
        {
            Chunk = 0,
            SingleBlock = 1
        }

        private sealed class FaceAnimationSpec
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }

        private SourceType _sourceType = SourceType.LocalFile;
        private string _localGzPath = string.Empty;
        private string _url = string.Empty;
        private Transform _parent;
        private Transform _exportRoot;
        private string _exportGzPath = string.Empty;
        private Vector3Int _origin = Vector3Int.zero;
        private bool _ignoreAir = true;
        private bool _ignoreWater = true;
        private bool _ignoreBarrier = false;
        private ImportMode _importMode = ImportMode.Chunk;
        private int _chunkSize = 16;
        private int _voxelsPerTick = 25000;
        private int _chunksPerTick = 6;
        private bool _chunkUseAlphaClip = true;
        private float _chunkAlphaCutoff = 0.33f;
        private bool _clearPrevious = true;
        private bool _addSurfaceCollider;
        private bool _addMeshCollider;

        private Phase _phase = Phase.Idle;
        private string _status = string.Empty;
        private float _progress;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _subtleLabelStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _textFieldStyle;

        private VoxelPayload _payload;
        private Dictionary<int, string> _idToName;
        private Dictionary<string, BlockDefinition> _blockDefs;
        private Dictionary<string, PreparedBlock> _preparedByName;
        private Dictionary<string, bool> _transparentByName;
        private Dictionary<ChunkKey, ChunkBucket> _chunkBuckets;
        private List<ChunkKey> _chunkKeys;
        private Transform _importRoot;
        private ImportStats _stats;
        private int _cursorVoxel;
        private int _cursorChunk;
        private int _cursorPlace;
        private Quaternion[] _rotLookup;
        private Material _chunkOpaqueMaterialInstance;
        private Dictionary<ChunkKey, HashSet<Vector3Int>> _chunkVoxelPositions;
        private HashSet<Vector3Int> _occupiedVoxels;
        private HashSet<Vector3Int> _allVoxels;
        private List<PendingBlock> _pendingBlocks;

        [MenuItem("Tools/Block World MVP/Voxel GZ Importer")]
        public static void Open()
        {
            GetWindow<VoxelGzImportWindow>(L("voxel.window.title"));
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();
        }

        private void OnEnable()
        {
            _status = L("voxel.status.idle");
        }

        private void OnGUI()
        {
            EnsureStyles();
            titleContent = new GUIContent(L("voxel.window.title"));

            DrawSection(L("voxel.section.source"), DrawSourceSection);
            EditorGUILayout.Space(6f);
            DrawSection(L("voxel.section.options"), DrawOptionsSection);
            EditorGUILayout.Space(6f);
            DrawSection(L("voxel.section.run"), DrawRunSection);
            EditorGUILayout.Space(6f);
            DrawSection(L("voxel.section.status"), DrawStatusSection);
        }

        private static string L(string key)
        {
            string localized = BlockWorldBuilderI18n.Get(key);
            if (!string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }

            Dictionary<string, string> fallback = IsChineseUI() ? FallbackZh : FallbackEn;
            if (fallback.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return key;
        }

        private static string Lf(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, L(key), args);
        }

        private static bool IsChineseUI()
        {
            string prefLanguage = EditorPrefs.GetString("Editor.kLanguage", string.Empty);
            if (!string.IsNullOrWhiteSpace(prefLanguage))
            {
                string lower = prefLanguage.Trim().ToLowerInvariant();
                if (lower.StartsWith("zh", StringComparison.Ordinal) || lower.Contains("chinese", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            SystemLanguage lang = Application.systemLanguage;
            return lang == SystemLanguage.ChineseSimplified || lang == SystemLanguage.ChineseTraditional;
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
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
                };
            }

            if (_subtleLabelStyle == null)
            {
                _subtleLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) }
                };
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
                _dangerButtonStyle.hover.textColor = new Color(1f, 0.66f, 0.66f, 1f);
            }

            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(EditorStyles.textField)
                {
                    fixedHeight = 22f
                };
            }
        }

        private void DrawSection(string title, Action body)
        {
            using (new EditorGUILayout.VerticalScope(_sectionBoxStyle))
            {
                EditorGUILayout.LabelField(title, _sectionTitleStyle);
                EditorGUILayout.Space(4f);
                body?.Invoke();
            }
        }

        private void DrawSourceSection()
        {
            int sourceIndex = (int)_sourceType;
            sourceIndex = EditorGUILayout.Popup(L("voxel.source.mode"), sourceIndex, new[] { L("voxel.source.local"), L("voxel.source.url") });
            _sourceType = (SourceType)Mathf.Clamp(sourceIndex, 0, 1);

            if (_sourceType == SourceType.LocalFile)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _localGzPath = EditorGUILayout.TextField(L("voxel.source.gz_file"), _localGzPath, _textFieldStyle);
                    if (GUILayout.Button(L("voxel.source.browse"), GUILayout.Width(78f)))
                    {
                        string selected = EditorUtility.OpenFilePanel(L("voxel.source.select_file"), Application.dataPath, "gz");
                        if (!string.IsNullOrWhiteSpace(selected))
                        {
                            _localGzPath = selected;
                        }
                    }
                }
            }
            else
            {
                _url = EditorGUILayout.TextField(L("voxel.source.url_value"), _url, _textFieldStyle);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _parent = (Transform)EditorGUILayout.ObjectField(L("voxel.source.parent"), _parent, typeof(Transform), true);
                EditorGUI.BeginDisabledGroup(_phase != Phase.Idle);
                if (GUILayout.Button(L("voxel.source.create_root"), GUILayout.Width(96f)))
                {
                    GameObject parentGo = new GameObject("VoxelImportRoot");
                    Undo.RegisterCreatedObjectUndo(parentGo, "Create Voxel Import Root");
                    _parent = parentGo.transform;
                    Selection.activeObject = parentGo;
                }
                EditorGUI.EndDisabledGroup();
            }
            _origin = EditorGUILayout.Vector3IntField(L("voxel.source.origin"), _origin);
        }

        private void DrawOptionsSection()
        {
            _ignoreBarrier = EditorGUILayout.ToggleLeft(L("voxel.option.ignore_barrier"), _ignoreBarrier);
            int modeIndex = EditorGUILayout.Popup(
                L("voxel.option.import_mode"),
                (int)_importMode,
                new[] { L("voxel.mode.chunk"), L("voxel.mode.single_block") });
            _importMode = (ImportMode)Mathf.Clamp(modeIndex, 0, 1);
            _clearPrevious = EditorGUILayout.ToggleLeft(L("voxel.option.replace_previous"), _clearPrevious);
            EditorGUI.BeginDisabledGroup(_importMode != ImportMode.Chunk);
            _addSurfaceCollider = EditorGUILayout.ToggleLeft(L("voxel.option.surface_collider"), _addSurfaceCollider);
            _addMeshCollider = EditorGUILayout.ToggleLeft(L("voxel.option.mesh_collider"), _addMeshCollider);
            _chunkSize = Mathf.Max(1, EditorGUILayout.IntField(L("voxel.option.chunk_size"), _chunkSize));
            _chunksPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("voxel.option.chunks_per_tick"), _chunksPerTick), 1, 64);
            _chunkUseAlphaClip = EditorGUILayout.ToggleLeft(L("voxel.option.alpha_clip"), _chunkUseAlphaClip);
            _chunkAlphaCutoff = Mathf.Clamp01(EditorGUILayout.Slider(L("voxel.option.alpha_cutoff"), _chunkAlphaCutoff, 0.01f, 0.9f));
            EditorGUI.EndDisabledGroup();
            _voxelsPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("voxel.option.voxels_per_tick"), _voxelsPerTick), 2000, 200000);
            EditorGUILayout.LabelField(L("voxel.option.fixed_filters"), _subtleLabelStyle);
        }

        private void DrawRunSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_phase != Phase.Idle);
                if (GUILayout.Button(L("voxel.run.import"), _primaryButtonStyle))
                {
                    StartImport();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(_phase == Phase.Idle);
                if (GUILayout.Button(L("voxel.run.cancel"), _dangerButtonStyle))
                {
                    CancelImport();
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawExportSection()
        {
            _exportRoot = (Transform)EditorGUILayout.ObjectField(L("voxel.export.root"), _exportRoot, typeof(Transform), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                _exportGzPath = EditorGUILayout.TextField(L("voxel.export.gz_file"), _exportGzPath, _textFieldStyle);
                if (GUILayout.Button(L("voxel.export.browse"), GUILayout.Width(78f)))
                {
                    string initialDir = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(_exportGzPath))
                    {
                        string dir = Path.GetDirectoryName(_exportGzPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            initialDir = dir;
                        }
                    }

                    string selected = EditorUtility.SaveFilePanel(
                        L("voxel.export.select_file"),
                        initialDir,
                        "voxel-export",
                        "gz");
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        _exportGzPath = selected;
                    }
                }
            }

            EditorGUI.BeginDisabledGroup(_phase != Phase.Idle);
            if (GUILayout.Button(L("voxel.export.run"), _primaryButtonStyle))
            {
                ExportGz();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.HelpBox(_status, MessageType.None);
            Rect r = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(r, Mathf.Clamp01(_progress), Lf("voxel.status.percent", Mathf.RoundToInt(_progress * 100f)));
        }

        private void StartImport()
        {
            try
            {
                CancelImport(clearStatus: false);
                string json = _sourceType == SourceType.LocalFile ? ReadGzipJsonFromFile(_localGzPath) : ReadGzipJsonFromUrl(_url);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _status = L("voxel.err.empty_json");
                    return;
                }

                _payload = JsonUtility.FromJson<VoxelPayload>(json);
                if (!ValidatePayload(_payload, out string payloadError))
                {
                    _status = Lf("voxel.err.failed_with_reason", payloadError);
                    return;
                }

                _idToName = LoadBlockIdMap();
                _transparentByName = LoadBlockTransparencyMap();
                _blockDefs = LoadBlockDefinitions();
                _preparedByName = new Dictionary<string, PreparedBlock>(StringComparer.OrdinalIgnoreCase);
                bool useChunkMode = _importMode == ImportMode.Chunk;
                _chunkBuckets = useChunkMode ? new Dictionary<ChunkKey, ChunkBucket>(512) : null;
                _chunkKeys = null;
                _chunkVoxelPositions = useChunkMode && _addSurfaceCollider
                    ? new Dictionary<ChunkKey, HashSet<Vector3Int>>(512)
                    : null;
                _occupiedVoxels = useChunkMode && _addSurfaceCollider
                    ? new HashSet<Vector3Int>()
                    : null;
                _allVoxels = _importMode == ImportMode.SingleBlock || useChunkMode ? new HashSet<Vector3Int>() : null;
                _chunkOpaqueMaterialInstance = null;
                _stats = new ImportStats
                {
                    total = Mathf.Min(_payload.indices.Length, _payload.data.Length),
                    startTime = EditorApplication.timeSinceStartup
                };
                _cursorVoxel = 0;
                _cursorChunk = 0;
                _cursorPlace = 0;
                _rotLookup = new[]
                {
                    Quaternion.identity,
                    Quaternion.Euler(0f, 90f, 0f),
                    Quaternion.Euler(0f, 180f, 0f),
                    Quaternion.Euler(0f, 270f, 0f)
                };
                _pendingBlocks = _importMode == ImportMode.SingleBlock ? new List<PendingBlock>(_stats.total) : null;

                PrepareRoot();
                _phase = Phase.ProcessVoxels;
                _status = L("voxel.status.processing_start");
                _progress = 0f;
                EditorApplication.update += OnEditorUpdate;
            }
            catch (Exception ex)
            {
                _phase = Phase.Idle;
                _status = Lf("voxel.err.failed_with_reason", ex.Message);
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

                if (_phase == Phase.PlaceSingleBlocks)
                {
                    TickPlaceSingleBlocks();
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
                _status = Lf("voxel.err.failed_with_reason", ex.Message);
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
                // MC voxel data and Unity scene space differ in handedness on horizontal axis.
                // Mirror X within shape bounds so import layout matches source orientation.
                x = (shapeX - 1) - x;

                int wx = _origin.x + (dirX * x);
                int wy = _origin.y + (dirY * y);
                int wz = _origin.z + (dirZ * z);
                int rot = (_payload.rot != null && _payload.rot.Length > i) ? (_payload.rot[i] & 3) : 0;
                // Import convention: always rotate voxel yaw by +180 degrees.
                rot = (rot + 2) & 3;
                Vector3 worldPos = new Vector3(wx, wy, wz);
                Quaternion worldRot = _rotLookup[rot];
                if (_importMode == ImportMode.SingleBlock || prepared.hasAnimation)
                {
                    bool isTransparent = IsTransparentBlock(blockName);
                    Vector3Int gridPos = new Vector3Int(wx, wy, wz);
                    if (_allVoxels != null)
                    {
                        _allVoxels.Add(gridPos);
                    }

                    if (_importMode == ImportMode.SingleBlock && isTransparent && !prepared.hasAnimation)
                    {
                        _pendingBlocks?.Add(new PendingBlock(gridPos, rot, prepared, blockName));
                    }
                    else
                    {
                        PlaceSingleBlock(prepared, blockName, worldPos, worldRot);
                        _stats.createdBlocks++;
                    }
                }
                else
                {
                    bool isTransparent = IsTransparentBlock(blockName);
                    Vector3Int gridPos = new Vector3Int(wx, wy, wz);
                    if (_allVoxels != null)
                    {
                        _allVoxels.Add(gridPos);
                    }
                    ChunkKey key = BuildChunkKey(wx, wy, wz, _chunkSize);
                    if (!_chunkBuckets.TryGetValue(key, out ChunkBucket bucket))
                    {
                        bucket = new ChunkBucket();
                        _chunkBuckets.Add(key, bucket);
                    }

                    if (isTransparent)
                    {
                        bucket.transparentVoxels.Add(new TransparentVoxel(gridPos, rot, prepared));
                    }
                    else
                    {
                        bucket.opaqueCombines.Add(new CombineInstance
                        {
                            mesh = prepared.mesh,
                            subMeshIndex = 0,
                            transform = Matrix4x4.TRS(worldPos, worldRot, Vector3.one)
                        });
                    }

                    if (_addSurfaceCollider && _occupiedVoxels != null && _chunkVoxelPositions != null)
                    {
                        _occupiedVoxels.Add(gridPos);
                        if (!_chunkVoxelPositions.TryGetValue(key, out HashSet<Vector3Int> set))
                        {
                            set = new HashSet<Vector3Int>();
                            _chunkVoxelPositions.Add(key, set);
                        }

                        set.Add(gridPos);
                    }
                }
                _stats.valid++;
            }

            _cursorVoxel = maxIndex;
            _progress = total > 0 ? (float)_cursorVoxel / total : 0f;
            _status = Lf("voxel.status.processing_progress", _cursorVoxel, total);
            EditorUtility.DisplayProgressBar(L("voxel.window.title"), _status, _progress * 0.85f);
            Repaint();

            if (_cursorVoxel < total)
            {
                return;
            }

            if (_importMode == ImportMode.SingleBlock)
            {
                if (_pendingBlocks != null && _pendingBlocks.Count > 0)
                {
                    _phase = Phase.PlaceSingleBlocks;
                    _status = L("voxel.status.placing_blocks");
                }
                else
                {
                    _phase = Phase.Done;
                    _status = L("voxel.done.title");
                }
                return;
            }

            _chunkKeys = new List<ChunkKey>(_chunkBuckets.Keys);
            _chunkKeys.Sort((a, b) =>
            {
                int cmp = a.x.CompareTo(b.x);
                if (cmp != 0) return cmp;
                cmp = a.y.CompareTo(b.y);
                return cmp != 0 ? cmp : a.z.CompareTo(b.z);
            });
            _phase = Phase.BuildChunks;
            _status = L("voxel.status.building_start");
        }

        private void TickPlaceSingleBlocks()
        {
            if (_pendingBlocks == null || _pendingBlocks.Count == 0)
            {
                _phase = Phase.Done;
                _status = L("voxel.done.title");
                return;
            }

            int total = _pendingBlocks.Count;
            int maxIndex = Mathf.Min(total, _cursorPlace + _voxelsPerTick);
            for (int i = _cursorPlace; i < maxIndex; i++)
            {
                PendingBlock pending = _pendingBlocks[i];
                Mesh mesh = BuildCulledSingleBlockMesh(pending.prepared, pending.pos, pending.rot, _allVoxels);
                if (mesh == null)
                {
                    continue;
                }

                Vector3 worldPos = new Vector3(pending.pos.x, pending.pos.y, pending.pos.z);
                Quaternion worldRot = _rotLookup[pending.rot];
                PlaceSingleBlock(pending.prepared, pending.blockName, worldPos, worldRot, mesh);
                _stats.createdBlocks++;
            }

            _cursorPlace = maxIndex;
            _progress = total > 0 ? (float)_cursorPlace / total : 1f;
            _status = Lf("voxel.status.placing_progress", _cursorPlace, total);
            EditorUtility.DisplayProgressBar(L("voxel.window.title"), _status, _progress);
            Repaint();

            if (_cursorPlace >= total)
            {
                _phase = Phase.Done;
                _status = L("voxel.done.title");
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
                if (!_chunkBuckets.TryGetValue(key, out ChunkBucket bucket)
                    || (bucket.opaqueCombines.Count == 0 && bucket.transparentVoxels.Count == 0))
                {
                    continue;
                }

                GameObject go = new GameObject($"chunk_{key.x}_{key.y}_{key.z}");
                go.transform.SetParent(_importRoot, false);

                if (bucket.opaqueCombines.Count > 0)
                {
                    GameObject opaqueGo = new GameObject("opaque");
                    opaqueGo.transform.SetParent(go.transform, false);
                    MeshFilter mf = opaqueGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = opaqueGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ResolveChunkOpaqueMaterial();

                    Mesh mesh = new Mesh
                    {
                        name = $"VoxelChunk_{key.x}_{key.y}_{key.z}_opaque",
                        indexFormat = IndexFormat.UInt32
                    };
                    mesh.CombineMeshes(bucket.opaqueCombines.ToArray(), true, true, false);
                    mesh.RecalculateBounds();

                    string assetPath = BuildChunkMeshAssetPath(key, i, "opaque");
                    AssetDatabase.CreateAsset(mesh, assetPath);
                    mf.sharedMesh = mesh;
                    if (_addMeshCollider)
                    {
                        MeshCollider chunkCollider = opaqueGo.AddComponent<MeshCollider>();
                        chunkCollider.sharedMesh = mesh;
                        _stats.createdMeshColliders++;
                    }
                }

                if (bucket.transparentVoxels.Count > 0)
                {
                    GameObject transparentGo = new GameObject("transparent");
                    transparentGo.transform.SetParent(go.transform, false);
                    MeshFilter mf = transparentGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = transparentGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ResolveChunkTransparentMaterial();

                    Mesh mesh = BuildTransparentChunkMesh(key, bucket.transparentVoxels, _allVoxels);
                    if (mesh == null)
                    {
                        continue;
                    }

                    string assetPath = BuildChunkMeshAssetPath(key, i, "transparent");
                    AssetDatabase.CreateAsset(mesh, assetPath);
                    mf.sharedMesh = mesh;
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
            _status = Lf("voxel.status.building_progress", _cursorChunk, _chunkKeys.Count);
            EditorUtility.DisplayProgressBar(L("voxel.window.title"), _status, 0.85f + (_progress * 0.15f));
            Repaint();

            if (_cursorChunk >= _chunkKeys.Count)
            {
                _phase = Phase.Done;
            }
        }

        private void CompleteImport()
        {
            _phase = Phase.Idle;
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();

            if (_importRoot != null)
            {
                global::BlockWorldMVP.BlockWorldOcclusionCuller culler = _importRoot.GetComponent<global::BlockWorldMVP.BlockWorldOcclusionCuller>();
                if (culler == null)
                {
                    culler = _importRoot.gameObject.AddComponent<global::BlockWorldMVP.BlockWorldOcclusionCuller>();
                }

                if (_importMode == ImportMode.Chunk)
                {
                    culler.Configure(300f, 340f, 2);
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
                L("voxel.done.title") + "\n" +
                Lf("voxel.done.total", _stats.total) + "\n" +
                Lf("voxel.done.imported", _stats.valid) + "\n" +
                Lf("voxel.done.chunks", _stats.createdChunks) + "\n" +
                Lf("voxel.done.blocks", _stats.createdBlocks) + "\n" +
                Lf("voxel.done.surface_colliders", _stats.createdSurfaceColliders) + "\n" +
                Lf("voxel.done.mesh_colliders", _stats.createdMeshColliders) + "\n" +
                Lf("voxel.done.skipped", _stats.skippedAir, _stats.skippedWater, _stats.skippedBarrier, _stats.skippedUnknown, _stats.skippedInvalid) + "\n" +
                Lf("voxel.done.time", sec.ToString("F2", CultureInfo.InvariantCulture));
            _status = summary.Replace("\n", " | ");
            EditorUtility.DisplayDialog(L("voxel.window.title"), summary, L("dialog.ok"));
            Repaint();
        }

        private void ExportGz()
        {
            try
            {
                if (_exportRoot == null)
                {
                    _status = L("voxel.export.err.no_root");
                    EditorUtility.DisplayDialog(L("voxel.window.title"), _status, L("dialog.ok"));
                    return;
                }

                string exportPath = _exportGzPath;
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    exportPath = EditorUtility.SaveFilePanel(
                        L("voxel.export.select_file"),
                        Application.dataPath,
                        "voxel-export",
                        "gz");
                }

                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    return;
                }

                if (!exportPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    exportPath += ".gz";
                }
                _exportGzPath = exportPath;

                List<PlacedBlock> allBlocks = CollectPlacedBlocksForExport(_exportRoot);
                if (allBlocks.Count == 0)
                {
                    _status = L("voxel.export.err.empty");
                    EditorUtility.DisplayDialog(L("voxel.window.title"), _status, L("dialog.ok"));
                    return;
                }

                Dictionary<string, int> nameToId = LoadNameToBlockIdMap();
                List<Vector3Int> positions = new List<Vector3Int>(allBlocks.Count);
                List<int> ids = new List<int>(allBlocks.Count);
                List<int> rots = new List<int>(allBlocks.Count);
                int skippedUnknown = 0;

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    PlacedBlock block = allBlocks[i];
                    if (block == null || string.IsNullOrWhiteSpace(block.BlockId))
                    {
                        skippedUnknown++;
                        continue;
                    }

                    if (!nameToId.TryGetValue(block.BlockId, out int numericId))
                    {
                        skippedUnknown++;
                        continue;
                    }

                    Vector3Int pos = Vector3Int.RoundToInt(block.transform.position);
                    float yRot = NormalizeYRotation(block.transform.eulerAngles.y);
                    int rotQuarter = (Mathf.RoundToInt(yRot / 90f) + 2) & 3;

                    positions.Add(pos);
                    ids.Add(numericId);
                    rots.Add(rotQuarter);
                }

                if (positions.Count == 0)
                {
                    _status = L("voxel.export.err.empty");
                    EditorUtility.DisplayDialog(L("voxel.window.title"), _status, L("dialog.ok"));
                    return;
                }

                int minX = int.MaxValue;
                int minY = int.MaxValue;
                int minZ = int.MaxValue;
                int maxX = int.MinValue;
                int maxY = int.MinValue;
                int maxZ = int.MinValue;
                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3Int p = positions[i];
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.z < minZ) minZ = p.z;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                    if (p.z > maxZ) maxZ = p.z;
                }

                int shapeX = maxX - minX + 1;
                int shapeY = maxY - minY + 1;
                int shapeZ = maxZ - minZ + 1;
                int shapeXY = shapeX * shapeY;

                int count = positions.Count;
                int[] indices = new int[count];
                int[] data = new int[count];
                int[] rot = new int[count];
                for (int i = 0; i < count; i++)
                {
                    Vector3Int p = positions[i];
                    int x = p.x - minX;
                    int y = p.y - minY;
                    int z = p.z - minZ;
                    // Keep Unity->MC axis convention consistent with importer correction.
                    x = (shapeX - 1) - x;
                    indices[i] = x + (y * shapeX) + (z * shapeXY);
                    data[i] = ids[i];
                    rot[i] = rots[i];
                }

                VoxelPayload payload = new VoxelPayload
                {
                    formatVersion = "unity",
                    shape = new[] { shapeX, shapeY, shapeZ },
                    dir = new[] { 1, 1, 1 },
                    indices = indices,
                    data = data,
                    rot = rot
                };

                string json = JsonUtility.ToJson(payload);
                WriteGzipJsonToFile(exportPath, json);
                AssetDatabase.Refresh();

                string done = Lf("voxel.export.done", count, skippedUnknown);
                _status = done;
                EditorUtility.DisplayDialog(L("voxel.window.title"), done, L("dialog.ok"));
            }
            catch (Exception ex)
            {
                _status = Lf("voxel.err.failed_with_reason", ex.Message);
                EditorUtility.DisplayDialog(L("voxel.window.title"), _status, L("dialog.ok"));
            }
        }

        private void PlaceSingleBlock(PreparedBlock prepared, string blockName, Vector3 position, Quaternion rotation, Mesh overrideMesh = null)
        {
            Mesh meshToUse = overrideMesh ?? prepared?.mesh;
            if (_importRoot == null || prepared == null || meshToUse == null)
            {
                return;
            }

            GameObject go = new GameObject(blockName);
            go.transform.SetParent(_importRoot, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = meshToUse;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (prepared.usesSubmeshes && prepared.materials != null && prepared.materials.Length > 0)
            {
                mr.sharedMaterials = prepared.materials;
            }
            else if (prepared.material != null)
            {
                mr.sharedMaterial = prepared.material;
            }

            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = meshToUse;

            PlacedBlock marker = go.AddComponent<PlacedBlock>();
            marker.BlockId = blockName;
            marker.HasAnimation = prepared.hasAnimation;

            if (prepared.hasAnimation && prepared.animations != null && prepared.animations.Length > 0)
            {
                BlockTextureAnimator animator = go.GetComponent<BlockTextureAnimator>();
                if (animator == null)
                {
                    animator = go.AddComponent<BlockTextureAnimator>();
                }

                animator.SetAnimations(prepared.animations, prepared.faceMainTexSt);
            }
        }

        private void CancelImport(bool clearStatus = true)
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();
            _phase = Phase.Idle;
            _progress = 0f;
            if (clearStatus)
            {
                _status = L("voxel.status.idle");
            }
        }

        private static List<PlacedBlock> CollectPlacedBlocksForExport(Transform root)
        {
            List<PlacedBlock> list = new List<PlacedBlock>();
            if (root == null)
            {
                return list;
            }

            PlacedBlock[] found = root.GetComponentsInChildren<PlacedBlock>(true);
            if (found == null)
            {
                return list;
            }

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    list.Add(found[i]);
                }
            }

            return list;
        }

        private static float NormalizeYRotation(float y)
        {
            float n = y % 360f;
            if (n < 0f)
            {
                n += 360f;
            }

            return n;
        }

        private static void WriteGzipJsonToFile(string path, string json)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (GZipStream gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
            {
                gz.Write(bytes, 0, bytes.Length);
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

        private string BuildChunkMeshAssetPath(ChunkKey key, int index, string suffix)
        {
            string parentName = _parent != null ? SanitizeName(_parent.name) : "Root";
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "chunk" : suffix;
            string name = $"{parentName}_chunk_{key.x}_{key.y}_{key.z}_{safeSuffix}_{index}.asset";
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

            prepared.faceMainTexSt = renderData.faceMainTexSt;
            prepared.materials = renderData.materials;
            prepared.material = renderData.materials[0];
            prepared.hasAnimation = TryBuildAnimations(def.sideTexturePaths, renderData.faceMainTexSt, out prepared.animations);
            prepared.usesSubmeshes = prepared.hasAnimation;
            prepared.mesh = prepared.usesSubmeshes
                ? BuildStaticBlockMeshMultiBaseUv(blockName)
                : BuildStaticBlockMesh(blockName, renderData.faceMainTexSt);
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

        private Material ResolveChunkTransparentMaterial()
        {
            Material source = ResolveSharedMaterial();
            if (source == null)
            {
                return null;
            }

            return source;
        }

        private Material ResolveChunkOpaqueMaterial()
        {
            Material source = ResolveSharedMaterial();
            if (source == null)
            {
                return null;
            }

            EnsureAssetFolderPath(GeneratedMaterialFolder);

            if (_chunkOpaqueMaterialInstance != null)
            {
                if (_chunkOpaqueMaterialInstance.mainTexture != source.mainTexture)
                {
                    _chunkOpaqueMaterialInstance.mainTexture = source.mainTexture;
                }

                return _chunkOpaqueMaterialInstance;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(ChunkOpaqueMaterialPath);
            if (existing != null)
            {
                _chunkOpaqueMaterialInstance = existing;
                _chunkOpaqueMaterialInstance.mainTexture = source.mainTexture;
                EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
                return _chunkOpaqueMaterialInstance;
            }

            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                _chunkOpaqueMaterialInstance = new Material(source) { name = "VoxelImport_ChunkOpaque" };
                _chunkOpaqueMaterialInstance.renderQueue = (int)RenderQueue.Geometry;
                _chunkOpaqueMaterialInstance.SetInt("_ZWrite", 1);
                AssetDatabase.CreateAsset(_chunkOpaqueMaterialInstance, ChunkOpaqueMaterialPath);
                EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
                return _chunkOpaqueMaterialInstance;
            }

            Material m = new Material(shader)
            {
                name = "VoxelImport_ChunkOpaque"
            };
            m.mainTexture = source.mainTexture;
            m.SetFloat("_Mode", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Geometry;

            _chunkOpaqueMaterialInstance = m;
            AssetDatabase.CreateAsset(_chunkOpaqueMaterialInstance, ChunkOpaqueMaterialPath);
            EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
            return _chunkOpaqueMaterialInstance;
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

        private Mesh BuildTransparentChunkMesh(ChunkKey key, List<TransparentVoxel> voxels, HashSet<Vector3Int> allVoxels)
        {
            if (voxels == null || voxels.Count == 0 || allVoxels == null)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>(voxels.Count * 24);
            List<Vector2> uvs = new List<Vector2>(voxels.Count * 24);
            List<int> triangles = new List<int>(voxels.Count * 36);

            for (int i = 0; i < voxels.Count; i++)
            {
                TransparentVoxel voxel = voxels[i];
                PreparedBlock prepared = voxel.prepared;
                if (prepared == null || prepared.faceMainTexSt == null || prepared.faceMainTexSt.Length < SideOrder.Length)
                {
                    continue;
                }

                Quaternion rot = _rotLookup[voxel.rot];
                for (int face = 0; face < SideOrder.Length; face++)
                {
                    Vector3 dir = rot * FaceNormals[face];
                    Vector3Int neighbor = voxel.pos + ToVector3Int(dir);
                    if (allVoxels.Contains(neighbor))
                    {
                        continue;
                    }

                    Vector4 st = prepared.faceMainTexSt[face];
                    int baseIndex = vertices.Count;
                    for (int v = 0; v < 4; v++)
                    {
                        Vector3 local = FaceVertices[face][v];
                        Vector3 rotated = rot * local;
                        vertices.Add(rotated + (Vector3)voxel.pos);
                    }

                    uvs.Add(new Vector2(st.z, st.w));
                    uvs.Add(new Vector2(st.z + st.x, st.w));
                    uvs.Add(new Vector2(st.z + st.x, st.w + st.y));
                    uvs.Add(new Vector2(st.z, st.w + st.y));

                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 2);
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"VoxelChunk_{key.x}_{key.y}_{key.z}_transparent",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildCulledSingleBlockMesh(PreparedBlock prepared, Vector3Int pos, int rot, HashSet<Vector3Int> allVoxels)
        {
            if (prepared == null || prepared.faceMainTexSt == null || prepared.faceMainTexSt.Length < SideOrder.Length || allVoxels == null)
            {
                return prepared?.mesh;
            }

            List<Vector3> vertices = new List<Vector3>(24);
            List<Vector2> uvs = new List<Vector2>(24);
            List<int> triangles = new List<int>(36);
            Quaternion rotation = _rotLookup[rot];

            for (int face = 0; face < SideOrder.Length; face++)
            {
                Vector3 dir = rotation * FaceNormals[face];
                Vector3Int neighbor = pos + ToVector3Int(dir);
                if (allVoxels.Contains(neighbor))
                {
                    continue;
                }

                Vector4 st = prepared.faceMainTexSt[face];
                int baseIndex = vertices.Count;
                vertices.Add(FaceVertices[face][0]);
                vertices.Add(FaceVertices[face][1]);
                vertices.Add(FaceVertices[face][2]);
                vertices.Add(FaceVertices[face][3]);

                uvs.Add(new Vector2(st.z, st.w));
                uvs.Add(new Vector2(st.z + st.x, st.w));
                uvs.Add(new Vector2(st.z + st.x, st.w + st.y));
                uvs.Add(new Vector2(st.z, st.w + st.y));

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
                name = $"VoxelSingle_{pos.x}_{pos.y}_{pos.z}",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3Int ToVector3Int(Vector3 dir)
        {
            return new Vector3Int(
                Mathf.RoundToInt(dir.x),
                Mathf.RoundToInt(dir.y),
                Mathf.RoundToInt(dir.z));
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

        private static Mesh BuildStaticBlockMeshMulti(string blockName, Vector4[] faceMainTexSt)
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

            int[][] subTris =
            {
                new[] { 0, 2, 1, 0, 3, 2 },
                new[] { 4, 6, 5, 4, 7, 6 },
                new[] { 8, 10, 9, 8, 11, 10 },
                new[] { 12, 14, 13, 12, 15, 14 },
                new[] { 16, 18, 17, 16, 19, 18 },
                new[] { 20, 22, 21, 20, 23, 22 }
            };

            Mesh mesh = new Mesh { name = "VoxelStaticMulti_" + blockName };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 6;
            for (int i = 0; i < 6; i++)
            {
                mesh.SetTriangles(subTris[i], i, true);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static Mesh BuildStaticBlockMeshMultiBaseUv(string blockName)
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
                int offset = face * 4;
                uvs[offset + 0] = new Vector2(0f, 0f);
                uvs[offset + 1] = new Vector2(1f, 0f);
                uvs[offset + 2] = new Vector2(1f, 1f);
                uvs[offset + 3] = new Vector2(0f, 1f);
            }

            int[][] subTris =
            {
                new[] { 0, 2, 1, 0, 3, 2 },
                new[] { 4, 6, 5, 4, 7, 6 },
                new[] { 8, 10, 9, 8, 11, 10 },
                new[] { 12, 14, 13, 12, 15, 14 },
                new[] { 16, 18, 17, 16, 19, 18 },
                new[] { 20, 22, 21, 20, 23, 22 }
            };

            Mesh mesh = new Mesh { name = "VoxelStaticAnim_" + blockName };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 6;
            for (int i = 0; i < 6; i++)
            {
                mesh.SetTriangles(subTris[i], i, true);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static bool ValidatePayload(VoxelPayload payload, out string error)
        {
            if (payload == null)
            {
                error = L("voxel.err.json_parse");
                return false;
            }

            if (payload.shape == null || payload.shape.Length < 3)
            {
                error = L("voxel.err.shape_invalid");
                return false;
            }

            if (payload.indices == null || payload.data == null)
            {
                error = L("voxel.err.indices_data_missing");
                return false;
            }

            if (payload.shape[0] <= 0 || payload.shape[1] <= 0 || payload.shape[2] <= 0)
            {
                error = L("voxel.err.shape_values_invalid");
                return false;
            }

            if (payload.dir == null || payload.dir.Length < 3)
            {
                payload.dir = new[] { 1, 1, 1 };
            }

            error = null;
            return true;
        }

        private static bool TryBuildAnimations(Dictionary<string, string> sideTexturePaths, Vector4[] faceMainTexSt, out BlockTextureAnimator.FaceAnimation[] animations)
        {
            animations = Array.Empty<BlockTextureAnimator.FaceAnimation>();
            if (sideTexturePaths == null || faceMainTexSt == null || faceMainTexSt.Length < SideOrder.Length)
            {
                return false;
            }

            List<BlockTextureAnimator.FaceAnimation> list = new List<BlockTextureAnimator.FaceAnimation>();
            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                if (!sideTexturePaths.TryGetValue(side, out string texPath) || string.IsNullOrWhiteSpace(texPath))
                {
                    continue;
                }

                if (!TryParseFaceAnimation(texPath, out FaceAnimationSpec spec))
                {
                    continue;
                }

                if (spec == null || spec.frameCount <= 1)
                {
                    continue;
                }

                list.Add(new BlockTextureAnimator.FaceAnimation
                {
                    materialIndex = i,
                    frameCount = spec.frameCount,
                    frameDuration = spec.frameDuration,
                    frames = spec.frames,
                    baseMainTexSt = faceMainTexSt[i]
                });
            }

            if (list.Count == 0)
            {
                return false;
            }

            animations = list.ToArray();
            return true;
        }

        private static bool TryParseFaceAnimation(string textureAssetPath, out FaceAnimationSpec spec)
        {
            spec = null;
            string mcmetaPath = GetProjectAbsolutePath(textureAssetPath + ".mcmeta");
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

            int frameTimeTicks = ParseIntSafe(ReadNumberField(animationBody, "frametime"), 1);
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

            spec = new FaceAnimationSpec
            {
                frameCount = frameCount,
                frameDuration = frameDuration,
                frames = frames
            };
            return true;
        }

        private static string ExtractAnimationObjectBody(string json)
        {
            Match m = Regex.Match(json, "\"animation\"\\s*:\\s*\\{(?<body>[\\s\\S]*?)\\}", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["body"].Value : json;
        }

        private static int[] ParseFrameSequence(string body)
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

        private static string ReadNumberField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static int ParseIntSafe(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static string ReadGzipJsonFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception(L("voxel.err.gz_path_empty"));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(L("voxel.err.gz_not_found"), path);
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
                throw new Exception(L("voxel.err.url_empty"));
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

        private static Dictionary<string, bool> LoadBlockTransparencyMap()
        {
            Dictionary<string, bool> map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (File.Exists(absPath))
            {
                string json = File.ReadAllText(absPath);
                Dictionary<string, string> objects = ExtractTopLevelObjectValues(json);
                foreach (KeyValuePair<string, string> pair in objects)
                {
                    string name = pair.Key;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    bool transparent = ReadBoolField(pair.Value, "transparent");
                    map[name] = transparent;
                }

                if (map.Count > 0)
                {
                    return map;
                }
            }

            string fallback = GetProjectAbsolutePath(BlockIdPath);
            if (!File.Exists(fallback))
            {
                return map;
            }

            string fallbackJson = File.ReadAllText(fallback);
            MatchCollection flatMatches = FlatMapRegex.Matches(fallbackJson);
            for (int i = 0; i < flatMatches.Count; i++)
            {
                Match m = flatMatches[i];
                string name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!map.ContainsKey(name))
                {
                    map[name] = IsTransparencyKeyword(name);
                }
            }

            return map;
        }

        private static Dictionary<string, int> LoadNameToBlockIdMap()
        {
            Dictionary<int, string> idToName = LoadBlockIdMap();
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<int, string> kv in idToName)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                {
                    map[kv.Value] = kv.Key;
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

        private bool IsTransparentBlock(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return false;
            }

            if (_transparentByName != null && _transparentByName.TryGetValue(blockName, out bool transparent))
            {
                return transparent;
            }

            return IsTransparencyKeyword(blockName);
        }

        private static bool IsWaterBlock(string blockName)
        {
            return blockName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBarrierBlock(string blockName)
        {
            return blockName.IndexOf("barrier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ReadBoolField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>true|false|1|0)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string raw = match.Groups["value"].Value;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        private static bool IsTransparencyKeyword(string id)
        {
            return id.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{')
            {
                return result;
            }

            i++;
            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    break;
                }

                string key = ReadJsonString(json, ref i);
                if (key == null)
                {
                    break;
                }

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':')
                {
                    break;
                }

                i++;
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != '{')
                {
                    SkipJsonValue(json, ref i);
                }
                else
                {
                    int start = i;
                    SkipJsonObject(json, ref i);
                    string objectText = json.Substring(start, i - start);
                    result[key] = objectText;
                }

                SkipWhitespace(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                }
            }

            return result;
        }

        private static void SkipWhitespace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
        }

        private static string ReadJsonString(string text, ref int i)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length || text[i] != '"')
            {
                return null;
            }

            i++;
            int start = i;
            bool escape = false;
            StringBuilder sb = null;
            while (i < text.Length)
            {
                char c = text[i++];
                if (!escape && c == '"')
                {
                    if (sb == null)
                    {
                        return text.Substring(start, i - start - 1);
                    }

                    return sb.ToString();
                }

                if (!escape && c == '\\')
                {
                    escape = true;
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                        sb.Append(text, start, (i - 1) - start);
                    }
                    continue;
                }

                if (sb != null)
                {
                    sb.Append(c);
                }

                escape = false;
            }

            return null;
        }

        private static void SkipJsonValue(string text, ref int i)
        {
            SkipWhitespace(text, ref i);
            if (i >= text.Length)
            {
                return;
            }

            if (text[i] == '{')
            {
                SkipJsonObject(text, ref i);
                return;
            }

            if (text[i] == '[')
            {
                int depth = 0;
                bool inString = false;
                bool escape = false;
                while (i < text.Length)
                {
                    char c = text[i++];
                    if (inString)
                    {
                        if (!escape && c == '"')
                        {
                            inString = false;
                        }
                        escape = !escape && c == '\\';
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (c == '[')
                    {
                        depth++;
                    }
                    else if (c == ']')
                    {
                        depth--;
                        if (depth <= 0)
                        {
                            break;
                        }
                    }
                }
                return;
            }

            if (text[i] == '"')
            {
                ReadJsonString(text, ref i);
                return;
            }

            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']')
            {
                i++;
            }
        }

        private static void SkipJsonObject(string text, ref int i)
        {
            if (i >= text.Length || text[i] != '{')
            {
                return;
            }

            int depth = 0;
            bool inString = false;
            bool escape = false;
            while (i < text.Length)
            {
                char c = text[i++];
                if (inString)
                {
                    if (!escape && c == '"')
                    {
                        inString = false;
                    }
                    escape = !escape && c == '\\';
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }
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

        private static ChunkKey BuildChunkKey(int x, int y, int z, int chunkSize)
        {
            if (chunkSize <= 1)
            {
                return new ChunkKey(0, 0, 0);
            }

            return new ChunkKey(
                FloorDiv(x, chunkSize),
                FloorDiv(y, chunkSize),
                FloorDiv(z, chunkSize));
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
