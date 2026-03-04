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
using Box3Blocks;

namespace Box3Blocks.Editor
{
    public sealed partial class Box3BlocksGzImportWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3lab.box3/Editor/SourceAssets/block";
        private const string BlockIdPath = "Packages/com.box3lab.box3/Editor/SourceAssets/block-id.json";
        private const string BlockSpecPath = "Packages/com.box3lab.box3/Editor/SourceAssets/block-spec.json";
        private const string GeneratedMeshFolder = "Assets/Box3/Meshes/Import";
        private const string GeneratedMaterialFolder = "Assets/Box3/Materials";
        private const string ChunkOpaqueMaterialPath = "Assets/Box3/Materials/M_Block_Chunk_Opaque.mat";
        private const string ChunkTransparentMaterialPath = "Assets/Box3/Materials/M_Block_Chunk_Transparent.mat";
        private const string ChunkOpaqueShaderName = "Box3Blocks/ChunkOpaqueTiled";
        private const string ChunkTransparentShaderName = "Box3Blocks/ChunkTransparentTiled";
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
        private static readonly Vector3Int[] WorldFaceDirs =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.up,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1)
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
        private int _chunkSize = 32;
        private int _voxelsPerTick = 25000;
        private int _chunksPerTick = 6;
        private bool _chunkMergeAnimatedAsStatic = false;
        private bool _clearPrevious = true;
        private Box3ColliderMode _chunkColliderMode = Box3ColliderMode.None;
        private RealtimeLightMode _realtimeLightMode = RealtimeLightMode.None;

        private Phase _phase = Phase.Idle;
        private string _status = string.Empty;
        private float _progress;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _subtleLabelStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _insetPanelStyle;
        private GUIStyle _optionsGroupTitleStyle;

        private VoxelPayload _payload;
        private Dictionary<int, string> _idToName;
        private Dictionary<string, BlockDefinition> _blockDefs;
        private Dictionary<string, PreparedBlock> _preparedByName;
        private Dictionary<string, bool> _transparentByName;
        private Dictionary<string, bool> _emissiveByName;
        private Dictionary<string, Color> _emissiveLightColorByName;
        private Dictionary<ChunkKey, ChunkBucket> _chunkBuckets;
        private List<ChunkKey> _chunkKeys;
        private Transform _importRoot;
        private ImportStats _stats;
        private int _cursorVoxel;
        private int _cursorChunk;
        private Quaternion[] _rotLookup;
        private Material _chunkOpaqueMaterialInstance;
        private Material _chunkTransparentMaterialInstance;
        private Dictionary<ChunkKey, HashSet<Vector3Int>> _chunkVoxelPositions;
        private HashSet<Vector3Int> _occupiedVoxels;
        private HashSet<Vector3Int> _allVoxels;
        private HashSet<Vector3Int> _opaqueVoxels;
        private int _createdRealtimeLights;
        private Dictionary<int, PayloadLightData> _payloadLightByFlatIndex;
        private bool _suppressDialogs;
        private bool _lastImportSucceeded;
        private bool _useInjectedPayload;
        private VoxelPayload _injectedPayload;

        [MenuItem("Box3/地形导入", false, 20)]
        public static void Open()
        {
            GetWindow<Box3BlocksGzImportWindow>(L("voxel.window.title"));
        }

        public static bool ImportChunkFromFileApi(
            string gzFilePath,
            Transform parent,
            Vector3Int origin,
            bool ignoreBarrier,
            bool clearPrevious,
            int realtimeLightMode,
            Box3ColliderMode colliderMode,
            int chunkSize,
            int chunksPerTick,
            int voxelsPerTick)
        {
            if (string.IsNullOrWhiteSpace(gzFilePath))
            {
                return false;
            }

            return ExecuteImportApi(window =>
            {
                window._sourceType = SourceType.LocalFile;
                window._localGzPath = gzFilePath;
                window._parent = parent;
                window._origin = origin;
                window._ignoreBarrier = ignoreBarrier;
                window._clearPrevious = clearPrevious;
                window._realtimeLightMode = (RealtimeLightMode)Mathf.Clamp(realtimeLightMode, 0, 2);
                window._chunkColliderMode = colliderMode;
                window._chunkSize = Mathf.Max(1, chunkSize);
                window._chunksPerTick = Mathf.Clamp(chunksPerTick, 1, 64);
                window._voxelsPerTick = Mathf.Clamp(voxelsPerTick, 2000, 200000);
                window._chunkMergeAnimatedAsStatic = false;
            });
        }

        public static bool ImportChunkFromUrlApi(
            string gzUrl,
            Transform parent,
            Vector3Int origin,
            bool ignoreBarrier,
            bool clearPrevious,
            int realtimeLightMode,
            Box3ColliderMode colliderMode,
            int chunkSize,
            int chunksPerTick,
            int voxelsPerTick)
        {
            if (string.IsNullOrWhiteSpace(gzUrl))
            {
                return false;
            }

            return ExecuteImportApi(window =>
            {
                window._sourceType = SourceType.Url;
                window._url = gzUrl;
                window._parent = parent;
                window._origin = origin;
                window._ignoreBarrier = ignoreBarrier;
                window._clearPrevious = clearPrevious;
                window._realtimeLightMode = (RealtimeLightMode)Mathf.Clamp(realtimeLightMode, 0, 2);
                window._chunkColliderMode = colliderMode;
                window._chunkSize = Mathf.Max(1, chunkSize);
                window._chunksPerTick = Mathf.Clamp(chunksPerTick, 1, 64);
                window._voxelsPerTick = Mathf.Clamp(voxelsPerTick, 2000, 200000);
                window._chunkMergeAnimatedAsStatic = false;
            });
        }

        public static bool ImportChunkFromRootApi(
            Transform sourceRoot,
            Transform parent,
            Vector3Int origin,
            bool ignoreBarrier,
            bool clearPrevious,
            int realtimeLightMode,
            Box3ColliderMode colliderMode,
            int chunkSize,
            int chunksPerTick,
            int voxelsPerTick,
            bool deleteSourceBlocksAfterBuild)
        {
            if (sourceRoot == null)
            {
                return false;
            }

            if (!TryBuildPayloadFromRoot(sourceRoot, ignoreBarrier, out VoxelPayload payload, out Vector3Int minCorner))
            {
                return false;
            }

            bool success = ExecuteImportApi(window =>
            {
                window._parent = parent;
                // Keep source world-space by default; origin works as extra offset.
                window._origin = minCorner + origin;
                window._ignoreBarrier = ignoreBarrier;
                window._clearPrevious = clearPrevious;
                window._realtimeLightMode = (RealtimeLightMode)Mathf.Clamp(realtimeLightMode, 0, 2);
                window._chunkColliderMode = colliderMode;
                window._chunkSize = Mathf.Max(1, chunkSize);
                window._chunksPerTick = Mathf.Clamp(chunksPerTick, 1, 64);
                window._voxelsPerTick = Mathf.Clamp(voxelsPerTick, 2000, 200000);
                window._chunkMergeAnimatedAsStatic = false;
                window._useInjectedPayload = true;
                window._injectedPayload = payload;
            });

            if (success && deleteSourceBlocksAfterBuild)
            {
                DeletePlacedBlocksUnderRoot(sourceRoot);
            }

            return success;
        }

        private static bool ExecuteImportApi(Action<Box3BlocksGzImportWindow> configure)
        {
            Box3BlocksGzImportWindow window = GetApiWindowInstance();
            if (window == null)
            {
                return false;
            }

            configure?.Invoke(window);
            window._suppressDialogs = true;
            window._lastImportSucceeded = false;
            window.StartImport();

            int safety = 0;
            while (window._phase != Phase.Idle && safety < 100000)
            {
                window.OnEditorUpdate();
                safety++;
            }

            window._suppressDialogs = false;
            if (safety >= 100000)
            {
                window.CancelImport(clearStatus: false);
                window._useInjectedPayload = false;
                window._injectedPayload = null;
                return false;
            }

            bool success = window._lastImportSucceeded;
            window._useInjectedPayload = false;
            window._injectedPayload = null;
            return success;
        }

        private static Box3BlocksGzImportWindow GetApiWindowInstance()
        {
            Box3BlocksGzImportWindow[] existing = Resources.FindObjectsOfTypeAll<Box3BlocksGzImportWindow>();
            if (existing != null && existing.Length > 0)
            {
                return existing[0];
            }

            Box3BlocksGzImportWindow created = CreateInstance<Box3BlocksGzImportWindow>();
            created.hideFlags = HideFlags.HideAndDontSave;
            return created;
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
            return Box3BlocksI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, L(key), args);
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

            if (_insetPanelStyle == null)
            {
                _insetPanelStyle = new GUIStyle(GUIStyle.none)
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            if (_optionsGroupTitleStyle == null)
            {
                _optionsGroupTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    normal = { textColor = new Color(0.78f, 0.86f, 0.98f, 1f) }
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
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
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

                EditorGUILayout.Space(2f);
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
        }

        private void DrawOptionsSection()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                EditorGUILayout.LabelField(L("voxel.group.general"), _optionsGroupTitleStyle);
                _ignoreBarrier = EditorGUILayout.ToggleLeft(L("voxel.option.ignore_barrier"), _ignoreBarrier);
                _clearPrevious = EditorGUILayout.ToggleLeft(L("voxel.option.replace_previous"), _clearPrevious);
                int lightModeIndex = EditorGUILayout.Popup(
                    L("voxel.option.realtime_light_mode"),
                    (int)_realtimeLightMode,
                    new[]
                    {
                        L("voxel.light_mode.none"),
                        L("voxel.light_mode.all"),
                        L("voxel.light_mode.data_only")
                    });
                _realtimeLightMode = (RealtimeLightMode)Mathf.Clamp(lightModeIndex, 0, 2);

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(L("voxel.group.chunk"), _optionsGroupTitleStyle);
                int colliderModeIndex = EditorGUILayout.Popup(
                    L("voxel.option.collider_mode"),
                    (int)_chunkColliderMode,
                    new[]
                    {
                        L("voxel.collider_mode.none"),
                        L("voxel.collider_mode.top"),
                        L("voxel.collider_mode.full")
                    });
                _chunkColliderMode = (Box3ColliderMode)Mathf.Clamp(colliderModeIndex, 0, 2);
                _chunkSize = Mathf.Max(1, EditorGUILayout.IntField(L("voxel.option.chunk_size"), _chunkSize));
                _chunksPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("voxel.option.chunks_per_tick"), _chunksPerTick), 1, 64);

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(L("voxel.group.performance"), _optionsGroupTitleStyle);
                _voxelsPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("voxel.option.voxels_per_tick"), _voxelsPerTick), 2000, 200000);
            }
        }

        private void DrawRunSection()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
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
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
                Rect r = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, Mathf.Clamp01(_progress), Lf("voxel.status.percent", Mathf.RoundToInt(_progress * 100f)));
            }
        }

        private void StartImport()
        {
            try
            {
                _lastImportSucceeded = false;
                CancelImport(clearStatus: false);
                if (_useInjectedPayload && _injectedPayload != null)
                {
                    _payload = _injectedPayload;
                }
                else
                {
                    string json = _sourceType == SourceType.LocalFile ? ReadGzipJsonFromFile(_localGzPath) : ReadGzipJsonFromUrl(_url);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _status = L("voxel.err.empty_json");
                        return;
                    }

                    _payload = JsonUtility.FromJson<VoxelPayload>(json);
                }

                if (!ValidatePayload(_payload, out string payloadError))
                {
                    _status = Lf("voxel.err.failed_with_reason", payloadError);
                    return;
                }

                _idToName = LoadBlockIdMap();
                _transparentByName = LoadBlockTransparencyMap();
                _emissiveByName = LoadBlockEmissiveMap();
                _emissiveLightColorByName = LoadBlockLightColorMap();
                _blockDefs = LoadBlockDefinitions();
                _preparedByName = new Dictionary<string, PreparedBlock>(StringComparer.OrdinalIgnoreCase);
                _chunkBuckets = new Dictionary<ChunkKey, ChunkBucket>(512);
                _chunkKeys = null;
                _chunkVoxelPositions = _chunkColliderMode == Box3ColliderMode.TopOnly
                    ? new Dictionary<ChunkKey, HashSet<Vector3Int>>(512)
                    : null;
                _occupiedVoxels = _chunkColliderMode == Box3ColliderMode.TopOnly
                    ? new HashSet<Vector3Int>()
                    : null;
                _allVoxels = new HashSet<Vector3Int>();
                _opaqueVoxels = new HashSet<Vector3Int>();
                _chunkOpaqueMaterialInstance = null;
                _chunkTransparentMaterialInstance = null;
                _stats = new ImportStats
                {
                    total = Mathf.Min(_payload.indices.Length, _payload.data.Length),
                    startTime = EditorApplication.timeSinceStartup
                };
                _cursorVoxel = 0;
                _cursorChunk = 0;
                _rotLookup = new[]
                {
                    Quaternion.identity,
                    Quaternion.Euler(0f, 90f, 0f),
                    Quaternion.Euler(0f, 180f, 0f),
                    Quaternion.Euler(0f, 270f, 0f)
                };
                _createdRealtimeLights = 0;
                _payloadLightByFlatIndex = null;

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
                _lastImportSucceeded = false;
            }
        }

        private static bool TryBuildPayloadFromRoot(Transform sourceRoot, bool ignoreBarrier, out VoxelPayload payload, out Vector3Int minCorner)
        {
            payload = null;
            minCorner = Vector3Int.zero;
            List<Box3BlocksPlacedBlock> allBlocks = CollectPlacedBlocksForExport(sourceRoot);
            if (allBlocks.Count == 0)
            {
                return false;
            }

            Dictionary<string, int> nameToId = LoadNameToBlockIdMap();
            List<Vector3Int> positions = new List<Vector3Int>(allBlocks.Count);
            List<int> ids = new List<int>(allBlocks.Count);
            List<int> rots = new List<int>(allBlocks.Count);

            for (int i = 0; i < allBlocks.Count; i++)
            {
                Box3BlocksPlacedBlock block = allBlocks[i];
                if (block == null || string.IsNullOrWhiteSpace(block.BlockId))
                {
                    continue;
                }

                if (ignoreBarrier && IsBarrierBlock(block.BlockId))
                {
                    continue;
                }

                if (!nameToId.TryGetValue(block.BlockId, out int numericId))
                {
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
                return false;
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

            minCorner = new Vector3Int(minX, minY, minZ);
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
                x = (shapeX - 1) - x;
                indices[i] = x + (y * shapeX) + (z * shapeXY);
                data[i] = ids[i];
                rot[i] = rots[i];
            }

            payload = new VoxelPayload
            {
                formatVersion = "unity",
                shape = new[] { shapeX, shapeY, shapeZ },
                dir = new[] { 1, 1, 1 },
                indices = indices,
                data = data,
                rot = rot
            };
            return true;
        }

        private static void DeletePlacedBlocksUnderRoot(Transform sourceRoot)
        {
            if (sourceRoot == null)
            {
                return;
            }

            List<Box3BlocksPlacedBlock> blocks = CollectPlacedBlocksForExport(sourceRoot);
            for (int i = 0; i < blocks.Count; i++)
            {
                Box3BlocksPlacedBlock block = blocks[i];
                if (block != null)
                {
                    DestroyImmediate(block.gameObject);
                }
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
                _lastImportSucceeded = false;
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
                bool isEmissive = IsEmissiveBlock(blockName);
                bool hasLightData = TryGetPayloadLightData(i, idx, out Color payloadLightColor, out float payloadLightIntensity, out float payloadLightRange, out Vector3 payloadLightOffset);
                bool isTransparent = IsTransparentBlock(blockName);
                Vector3Int gridPos = new Vector3Int(wx, wy, wz);
                _allVoxels?.Add(gridPos);
                ChunkKey key = BuildChunkKey(wx, wy, wz, _chunkSize);
                if (!_chunkBuckets.TryGetValue(key, out ChunkBucket bucket))
                {
                    bucket = new ChunkBucket();
                    _chunkBuckets.Add(key, bucket);
                }

                if (prepared.hasAnimation && !_chunkMergeAnimatedAsStatic)
                {
                    bucket.animatedVoxels.Add(new AnimatedChunkVoxel(gridPos, rot, prepared, blockName, isTransparent));
                    if (!isTransparent)
                    {
                        _opaqueVoxels?.Add(gridPos);
                    }
                }
                else if (isTransparent)
                {
                    bucket.transparentVoxels.Add(new TransparentVoxel(gridPos, rot, prepared));
                }
                else
                {
                    bucket.opaqueVoxels.Add(new OpaqueVoxel(gridPos, rot, prepared));
                    _opaqueVoxels?.Add(gridPos);
                }

                if (ShouldSpawnRealtimeLight(isEmissive, hasLightData))
                {
                    Color lightColor = hasLightData ? payloadLightColor : ResolveEmissiveLightColor(blockName);
                    float lightIntensity = hasLightData ? payloadLightIntensity : 1.05f;
                    float lightRange = hasLightData ? payloadLightRange : 6f;
                    Vector3 lightOffset = hasLightData ? payloadLightOffset : Vector3.zero;
                    bucket.emissiveVoxels.Add(new EmissiveLightVoxel(gridPos, lightColor, lightIntensity, lightRange, lightOffset));
                }

                if (_chunkColliderMode == Box3ColliderMode.TopOnly && _occupiedVoxels != null && _chunkVoxelPositions != null)
                {
                    _occupiedVoxels.Add(gridPos);
                    if (!_chunkVoxelPositions.TryGetValue(key, out HashSet<Vector3Int> set))
                    {
                        set = new HashSet<Vector3Int>();
                        _chunkVoxelPositions.Add(key, set);
                    }

                    set.Add(gridPos);
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
                    || (bucket.opaqueVoxels.Count == 0 && bucket.transparentVoxels.Count == 0 && bucket.animatedVoxels.Count == 0 && bucket.emissiveVoxels.Count == 0))
                {
                    continue;
                }

                GameObject go = new GameObject($"chunk_{key.x}_{key.y}_{key.z}");
                go.transform.SetParent(_importRoot, false);

                if (bucket.opaqueVoxels.Count > 0)
                {
                    GameObject opaqueGo = new GameObject("opaque");
                    opaqueGo.transform.SetParent(go.transform, false);
                    MeshFilter mf = opaqueGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = opaqueGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ResolveChunkOpaqueMaterial();

                    Mesh mesh = BuildOpaqueChunkMesh(key, bucket.opaqueVoxels, _opaqueVoxels);
                    if (mesh != null)
                    {
                        string assetPath = BuildChunkMeshAssetPath(key, i, "opaque");
                        AssetDatabase.CreateAsset(mesh, assetPath);
                        mf.sharedMesh = mesh;
                        if (_chunkColliderMode == Box3ColliderMode.Full)
                        {
                            MeshCollider chunkCollider = opaqueGo.AddComponent<MeshCollider>();
                            chunkCollider.sharedMesh = mesh;
                            _stats.createdMeshColliders++;
                        }
                    }
                    else
                    {
                        DestroyImmediate(opaqueGo);
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
                    if (mesh != null)
                    {
                        string assetPath = BuildChunkMeshAssetPath(key, i, "transparent");
                        AssetDatabase.CreateAsset(mesh, assetPath);
                        mf.sharedMesh = mesh;
                        if (_chunkColliderMode == Box3ColliderMode.Full)
                        {
                            MeshCollider chunkCollider = transparentGo.AddComponent<MeshCollider>();
                            chunkCollider.sharedMesh = mesh;
                            _stats.createdMeshColliders++;
                        }
                    }
                    else
                    {
                        DestroyImmediate(transparentGo);
                    }
                }

                if (bucket.animatedVoxels.Count > 0)
                {
                    Dictionary<string, List<AnimatedChunkVoxel>> animatedGroups = new Dictionary<string, List<AnimatedChunkVoxel>>();
                    for (int a = 0; a < bucket.animatedVoxels.Count; a++)
                    {
                        AnimatedChunkVoxel av = bucket.animatedVoxels[a];
                        string groupKey = BuildAnimatedGroupKey(av);
                        if (!animatedGroups.TryGetValue(groupKey, out List<AnimatedChunkVoxel> list))
                        {
                            list = new List<AnimatedChunkVoxel>(32);
                            animatedGroups.Add(groupKey, list);
                        }

                        list.Add(av);
                    }

                    int groupIndex = 0;
                    foreach (KeyValuePair<string, List<AnimatedChunkVoxel>> kv in animatedGroups)
                    {
                        List<AnimatedChunkVoxel> group = kv.Value;
                        if (group == null || group.Count == 0)
                        {
                            continue;
                        }

                        AnimatedChunkVoxel sample = group[0];
                        GameObject animatedGo = new GameObject($"animated_{groupIndex}");
                        animatedGo.transform.SetParent(go.transform, false);
                        MeshFilter mf = animatedGo.AddComponent<MeshFilter>();
                        MeshRenderer mr = animatedGo.AddComponent<MeshRenderer>();

                        Mesh mesh = BuildAnimatedChunkGroupMesh(
                            key,
                            group,
                            sample.isTransparent ? _allVoxels : _opaqueVoxels);
                        if (mesh != null)
                        {
                            string assetPath = BuildChunkMeshAssetPath(key, i, $"animated_{groupIndex}");
                            AssetDatabase.CreateAsset(mesh, assetPath);
                            mf.sharedMesh = mesh;
                            if (_chunkColliderMode == Box3ColliderMode.Full)
                            {
                                MeshCollider chunkCollider = animatedGo.AddComponent<MeshCollider>();
                                chunkCollider.sharedMesh = mesh;
                                _stats.createdMeshColliders++;
                            }

                            PreparedBlock prepared = sample.prepared;
                            Material chunkMat = sample.isTransparent ? ResolveChunkTransparentMaterial() : ResolveChunkOpaqueMaterial();
                            if (chunkMat != null)
                            {
                                Material[] shared = new Material[SideOrder.Length];
                                for (int sm = 0; sm < shared.Length; sm++)
                                {
                                    shared[sm] = chunkMat;
                                }

                                mr.sharedMaterials = shared;
                            }
                            else if (prepared != null && prepared.materials != null && prepared.materials.Length > 0)
                            {
                                mr.sharedMaterials = prepared.materials;
                            }

                            if (prepared != null && prepared.animations != null && prepared.animations.Length > 0)
                            {
                                Box3BlocksTextureAnimator animator = animatedGo.GetComponent<Box3BlocksTextureAnimator>();
                                if (animator == null)
                                {
                                    animator = animatedGo.AddComponent<Box3BlocksTextureAnimator>();
                                }

                                Vector4[] unitSt = BuildUnitFaceMainTexSt();
                                Box3BlocksTextureAnimator.FaceAnimation[] tiledAnimations = CloneAnimationsForTiled(prepared.animations);
                                animator.SetAnimations(tiledAnimations, unitSt);
                            }

                            AnimatedChunkGroupMeta meta = animatedGo.GetComponent<AnimatedChunkGroupMeta>();
                            if (meta == null)
                            {
                                meta = animatedGo.AddComponent<AnimatedChunkGroupMeta>();
                            }

                            meta.groupKey = kv.Key;
                            meta.faceMainTexSt = BuildUnitFaceMainTexSt();
                            meta.animations = prepared != null && prepared.animations != null ? CloneAnimationsForTiled(prepared.animations) : null;
                        }
                        else
                        {
                            DestroyImmediate(animatedGo);
                        }

                        groupIndex++;
                    }
                }

                if (_chunkColliderMode == Box3ColliderMode.TopOnly
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

                if (_realtimeLightMode != RealtimeLightMode.None && bucket.emissiveVoxels.Count > 0)
                {
                    for (int l = 0; l < bucket.emissiveVoxels.Count; l++)
                    {
                        EmissiveLightVoxel lightVoxel = bucket.emissiveVoxels[l];
                        CreateRealtimeLight(go.transform, lightVoxel.pos + lightVoxel.offset, lightVoxel.color, lightVoxel.intensity, lightVoxel.range);
                        _createdRealtimeLights++;
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

            if (!_chunkMergeAnimatedAsStatic)
            {
                MergeAnimatedChunkGroupsAcrossChunks();
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
                Lf("voxel.done.skipped", _stats.skippedBarrier) + "\n" +
                Lf("voxel.done.time", sec.ToString("F2", CultureInfo.InvariantCulture));
            _status = summary.Replace("\n", " | ");
            _lastImportSucceeded = true;
            if (!_suppressDialogs)
            {
                EditorUtility.DisplayDialog(L("voxel.window.title"), summary, L("dialog.ok"));
            }
            Repaint();
        }

        private void MergeAnimatedChunkGroupsAcrossChunks()
        {
            if (_importRoot == null)
            {
                return;
            }

            AnimatedChunkGroupMeta[] metas = _importRoot.GetComponentsInChildren<AnimatedChunkGroupMeta>(true);
            if (metas == null || metas.Length <= 1)
            {
                return;
            }

            Dictionary<string, List<AnimatedChunkGroupMeta>> groups = new Dictionary<string, List<AnimatedChunkGroupMeta>>();
            for (int i = 0; i < metas.Length; i++)
            {
                AnimatedChunkGroupMeta meta = metas[i];
                if (meta == null || string.IsNullOrWhiteSpace(meta.groupKey) || meta.gameObject == null)
                {
                    continue;
                }

                if (!groups.TryGetValue(meta.groupKey, out List<AnimatedChunkGroupMeta> list))
                {
                    list = new List<AnimatedChunkGroupMeta>(8);
                    groups.Add(meta.groupKey, list);
                }

                list.Add(meta);
            }

            int mergedIndex = 0;
            foreach (KeyValuePair<string, List<AnimatedChunkGroupMeta>> kv in groups)
            {
                List<AnimatedChunkGroupMeta> list = kv.Value;
                if (list == null || list.Count <= 1)
                {
                    continue;
                }

                MeshRenderer sampleRenderer = list[0] != null ? list[0].GetComponent<MeshRenderer>() : null;
                if (sampleRenderer == null || sampleRenderer.sharedMaterials == null || sampleRenderer.sharedMaterials.Length == 0)
                {
                    continue;
                }

                List<CombineInstance>[] bySubmesh = new List<CombineInstance>[SideOrder.Length];
                for (int s = 0; s < SideOrder.Length; s++)
                {
                    bySubmesh[s] = new List<CombineInstance>(list.Count);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    AnimatedChunkGroupMeta meta = list[i];
                    if (meta == null || meta.gameObject == null)
                    {
                        continue;
                    }

                    MeshFilter mf = meta.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                    {
                        continue;
                    }

                    Mesh mesh = mf.sharedMesh;
                    int subCount = Mathf.Min(mesh.subMeshCount, SideOrder.Length);
                    Matrix4x4 trs = _importRoot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int s = 0; s < subCount; s++)
                    {
                        bySubmesh[s].Add(new CombineInstance
                        {
                            mesh = mesh,
                            subMeshIndex = s,
                            transform = trs
                        });
                    }
                }

                Mesh merged = CombineAnimatedSubmeshes(bySubmesh);
                if (merged == null)
                {
                    continue;
                }

                string mergedName = $"animated_merged_{SanitizeName(kv.Key)}";
                GameObject mergedGo = new GameObject(mergedName);
                mergedGo.transform.SetParent(_importRoot, false);
                MeshFilter mergedFilter = mergedGo.AddComponent<MeshFilter>();
                MeshRenderer mergedRenderer = mergedGo.AddComponent<MeshRenderer>();

                string assetPath = $"{GeneratedMeshFolder}/{mergedName}_{mergedIndex}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                AssetDatabase.CreateAsset(merged, assetPath);
                mergedFilter.sharedMesh = merged;
                mergedRenderer.sharedMaterials = sampleRenderer.sharedMaterials;
                if (_chunkColliderMode == Box3ColliderMode.Full)
                {
                    MeshCollider mergedCollider = mergedGo.AddComponent<MeshCollider>();
                    mergedCollider.sharedMesh = merged;
                    _stats.createdMeshColliders++;
                }

                AnimatedChunkGroupMeta mergedMeta = mergedGo.AddComponent<AnimatedChunkGroupMeta>();
                mergedMeta.groupKey = kv.Key;
                mergedMeta.faceMainTexSt = list[0].faceMainTexSt;
                mergedMeta.animations = list[0].animations;

                if (mergedMeta.animations != null && mergedMeta.animations.Length > 0)
                {
                    Box3BlocksTextureAnimator animator = mergedGo.AddComponent<Box3BlocksTextureAnimator>();
                    animator.SetAnimations(mergedMeta.animations, mergedMeta.faceMainTexSt);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    AnimatedChunkGroupMeta meta = list[i];
                    if (meta != null && meta.gameObject != null)
                    {
                        DestroyImmediate(meta.gameObject);
                    }
                }

                mergedIndex++;
            }
        }

        private static Mesh CombineAnimatedSubmeshes(List<CombineInstance>[] bySubmesh)
        {
            if (bySubmesh == null || bySubmesh.Length == 0)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs0 = new List<Vector2>();
            List<Vector2> uvs1 = new List<Vector2>();
            List<Vector2> uvs2 = new List<Vector2>();
            List<int>[] trisBySub = new List<int>[SideOrder.Length];
            for (int s = 0; s < SideOrder.Length; s++)
            {
                trisBySub[s] = new List<int>();
            }

            for (int s = 0; s < SideOrder.Length; s++)
            {
                List<CombineInstance> combines = bySubmesh[s];
                if (combines == null || combines.Count == 0)
                {
                    continue;
                }

                Mesh temp = new Mesh { indexFormat = IndexFormat.UInt32 };
                temp.CombineMeshes(combines.ToArray(), true, true, false);

                int baseIndex = vertices.Count;
                Vector3[] tempVerts = temp.vertices;
                Vector2[] tempUv0 = temp.uv;
                List<Vector2> tempUv1 = new List<Vector2>();
                List<Vector2> tempUv2 = new List<Vector2>();
                temp.GetUVs(1, tempUv1);
                temp.GetUVs(2, tempUv2);
                int[] tempTris = temp.triangles;
                vertices.AddRange(tempVerts);
                if (tempUv0 != null && tempUv0.Length == tempVerts.Length)
                {
                    uvs0.AddRange(tempUv0);
                }
                else
                {
                    for (int i = 0; i < tempVerts.Length; i++)
                    {
                        uvs0.Add(Vector2.zero);
                    }
                }

                if (tempUv1 != null && tempUv1.Count == tempVerts.Length)
                {
                    uvs1.AddRange(tempUv1);
                }
                else
                {
                    for (int i = 0; i < tempVerts.Length; i++)
                    {
                        uvs1.Add(Vector2.zero);
                    }
                }

                if (tempUv2 != null && tempUv2.Count == tempVerts.Length)
                {
                    uvs2.AddRange(tempUv2);
                }
                else
                {
                    for (int i = 0; i < tempVerts.Length; i++)
                    {
                        uvs2.Add(Vector2.zero);
                    }
                }

                for (int i = 0; i < tempTris.Length; i++)
                {
                    trisBySub[s].Add(baseIndex + tempTris[i]);
                }

                DestroyImmediate(temp);
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs0);
            mesh.SetUVs(1, uvs1);
            mesh.SetUVs(2, uvs2);
            mesh.subMeshCount = SideOrder.Length;
            for (int s = 0; s < SideOrder.Length; s++)
            {
                mesh.SetTriangles(trisBySub[s], s, true);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

                List<Box3BlocksPlacedBlock> allBlocks = CollectPlacedBlocksForExport(_exportRoot);
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
                    Box3BlocksPlacedBlock block = allBlocks[i];
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

        private bool ShouldSpawnRealtimeLight(bool isEmissive, bool hasLightData)
        {
            switch (_realtimeLightMode)
            {
                case RealtimeLightMode.None:
                    return false;
                case RealtimeLightMode.AllEmissive:
                    return isEmissive;
                case RealtimeLightMode.DataOnly:
                    return isEmissive && hasLightData;
                default:
                    return false;
            }
        }

        private bool TryGetPayloadLightData(int voxelArrayIndex, int voxelFlatIndex, out Color color, out float intensity, out float range, out Vector3 offset)
        {
            color = Color.white;
            intensity = 1.05f;
            range = 6f;
            offset = Vector3.zero;

            if (_payload == null)
            {
                return false;
            }

            if (_payload.lightIndices != null && _payload.lightIndices.Length > 0)
            {
                EnsurePayloadLightLookup();
                if (_payloadLightByFlatIndex != null && _payloadLightByFlatIndex.TryGetValue(voxelFlatIndex, out PayloadLightData lightData))
                {
                    color = lightData.color;
                    intensity = lightData.intensity;
                    range = lightData.range;
                    offset = lightData.offset;
                    return true;
                }

                return false;
            }

            if (_payload.lightFlags == null || voxelArrayIndex < 0 || voxelArrayIndex >= _payload.lightFlags.Length)
            {
                return false;
            }

            if (_payload.lightFlags[voxelArrayIndex] == 0)
            {
                return false;
            }

            if (_payload.lightIntensity != null && voxelArrayIndex < _payload.lightIntensity.Length)
            {
                intensity = Mathf.Max(0f, _payload.lightIntensity[voxelArrayIndex]);
            }

            if (_payload.lightRange != null && voxelArrayIndex < _payload.lightRange.Length)
            {
                range = Mathf.Max(0f, _payload.lightRange[voxelArrayIndex]);
            }

            int c = voxelArrayIndex * 3;
            if (_payload.lightColorRgb != null && c + 2 < _payload.lightColorRgb.Length)
            {
                color = new Color(
                    Mathf.Clamp01(_payload.lightColorRgb[c]),
                    Mathf.Clamp01(_payload.lightColorRgb[c + 1]),
                    Mathf.Clamp01(_payload.lightColorRgb[c + 2]),
                    1f);
            }

            int o = voxelArrayIndex * 3;
            if (_payload.lightOffsetXyz != null && o + 2 < _payload.lightOffsetXyz.Length)
            {
                offset = new Vector3(_payload.lightOffsetXyz[o], _payload.lightOffsetXyz[o + 1], _payload.lightOffsetXyz[o + 2]);
            }

            return true;
        }

        private void EnsurePayloadLightLookup()
        {
            if (_payloadLightByFlatIndex != null)
            {
                return;
            }

            _payloadLightByFlatIndex = new Dictionary<int, PayloadLightData>();
            if (_payload == null || _payload.lightIndices == null)
            {
                return;
            }

            int count = _payload.lightIndices.Length;
            for (int i = 0; i < count; i++)
            {
                if (_payload.lightFlags != null && i < _payload.lightFlags.Length && _payload.lightFlags[i] == 0)
                {
                    continue;
                }

                int flatIndex = _payload.lightIndices[i];
                if (_payloadLightByFlatIndex.ContainsKey(flatIndex))
                {
                    continue;
                }

                float intensity = 1.05f;
                if (_payload.lightIntensity != null && i < _payload.lightIntensity.Length)
                {
                    intensity = Mathf.Max(0f, _payload.lightIntensity[i]);
                }

                float range = 6f;
                if (_payload.lightRange != null && i < _payload.lightRange.Length)
                {
                    range = Mathf.Max(0f, _payload.lightRange[i]);
                }

                Color color = Color.white;
                int c = i * 3;
                if (_payload.lightColorRgb != null && c + 2 < _payload.lightColorRgb.Length)
                {
                    color = new Color(
                        Mathf.Clamp01(_payload.lightColorRgb[c]),
                        Mathf.Clamp01(_payload.lightColorRgb[c + 1]),
                        Mathf.Clamp01(_payload.lightColorRgb[c + 2]),
                        1f);
                }

                Vector3 offset = Vector3.zero;
                int o = i * 3;
                if (_payload.lightOffsetXyz != null && o + 2 < _payload.lightOffsetXyz.Length)
                {
                    offset = new Vector3(_payload.lightOffsetXyz[o], _payload.lightOffsetXyz[o + 1], _payload.lightOffsetXyz[o + 2]);
                }

                _payloadLightByFlatIndex.Add(flatIndex, new PayloadLightData(color, intensity, range, offset));
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

        private static List<Box3BlocksPlacedBlock> CollectPlacedBlocksForExport(Transform root)
        {
            List<Box3BlocksPlacedBlock> list = new List<Box3BlocksPlacedBlock>();
            if (root == null)
            {
                return list;
            }

            Box3BlocksPlacedBlock[] found = root.GetComponentsInChildren<Box3BlocksPlacedBlock>(true);
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

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(def.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData)
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

            EnsureAssetFolderPath(GeneratedMaterialFolder);

            if (_chunkTransparentMaterialInstance != null)
            {
                EnsureChunkTransparentShader(_chunkTransparentMaterialInstance);
                Texture sourceMain = source.mainTexture;
                if (sourceMain != null && _chunkTransparentMaterialInstance.mainTexture != sourceMain)
                {
                    _chunkTransparentMaterialInstance.mainTexture = sourceMain;
                }

                ApplyMapsToChunkTransparent(_chunkTransparentMaterialInstance);
                return _chunkTransparentMaterialInstance;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(ChunkTransparentMaterialPath);
            if (existing != null)
            {
                _chunkTransparentMaterialInstance = existing;
                EnsureChunkTransparentShader(_chunkTransparentMaterialInstance);
                if (source.mainTexture != null)
                {
                    _chunkTransparentMaterialInstance.mainTexture = source.mainTexture;
                }

                ApplyMapsToChunkTransparent(_chunkTransparentMaterialInstance);
                EditorUtility.SetDirty(_chunkTransparentMaterialInstance);
                return _chunkTransparentMaterialInstance;
            }

            Shader shader = Shader.Find(ChunkTransparentShaderName);
            if (shader == null)
            {
                return source;
            }

            Material m = new Material(shader)
            {
                name = "M_Block_Chunk_Transparent"
            };
            if (source.mainTexture != null)
            {
                m.mainTexture = source.mainTexture;
            }

            ApplyMapsToChunkTransparent(m);
            _chunkTransparentMaterialInstance = m;
            AssetDatabase.CreateAsset(_chunkTransparentMaterialInstance, ChunkTransparentMaterialPath);
            EditorUtility.SetDirty(_chunkTransparentMaterialInstance);
            return _chunkTransparentMaterialInstance;
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
                EnsureChunkOpaqueShader(_chunkOpaqueMaterialInstance);
                Texture sourceMain = source.mainTexture;
                if (sourceMain != null && _chunkOpaqueMaterialInstance.mainTexture != sourceMain)
                {
                    _chunkOpaqueMaterialInstance.mainTexture = sourceMain;
                }

                ApplyBumpToChunkOpaque(_chunkOpaqueMaterialInstance);
                return _chunkOpaqueMaterialInstance;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(ChunkOpaqueMaterialPath);
            if (existing != null)
            {
                _chunkOpaqueMaterialInstance = existing;
                EnsureChunkOpaqueShader(_chunkOpaqueMaterialInstance);
                if (source.mainTexture != null)
                {
                    _chunkOpaqueMaterialInstance.mainTexture = source.mainTexture;
                }
                ApplyBumpToChunkOpaque(_chunkOpaqueMaterialInstance);
                EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
                return _chunkOpaqueMaterialInstance;
            }

            Shader shader = Shader.Find(ChunkOpaqueShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                _chunkOpaqueMaterialInstance = new Material(source) { name = "M_Block_Chunk_Opaque" };
                _chunkOpaqueMaterialInstance.renderQueue = (int)RenderQueue.Geometry;
                _chunkOpaqueMaterialInstance.SetInt("_ZWrite", 1);
                ApplyBumpToChunkOpaque(_chunkOpaqueMaterialInstance);
                AssetDatabase.CreateAsset(_chunkOpaqueMaterialInstance, ChunkOpaqueMaterialPath);
                EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
                return _chunkOpaqueMaterialInstance;
            }

            Material m = new Material(shader)
            {
                name = "M_Block_Chunk_Opaque"
            };
            if (source.mainTexture != null)
            {
                m.mainTexture = source.mainTexture;
            }
            m.SetFloat("_Mode", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Geometry;

            ApplyBumpToChunkOpaque(m);
            _chunkOpaqueMaterialInstance = m;
            AssetDatabase.CreateAsset(_chunkOpaqueMaterialInstance, ChunkOpaqueMaterialPath);
            EditorUtility.SetDirty(_chunkOpaqueMaterialInstance);
            return _chunkOpaqueMaterialInstance;
        }

        private static void EnsureChunkOpaqueShader(Material material)
        {
            if (material == null)
            {
                return;
            }

            Shader tiledShader = Shader.Find(ChunkOpaqueShaderName);
            if (tiledShader != null && material.shader != tiledShader)
            {
                material.shader = tiledShader;
            }
        }

        private static void EnsureChunkTransparentShader(Material material)
        {
            if (material == null)
            {
                return;
            }

            Shader tiledShader = Shader.Find(ChunkTransparentShaderName);
            if (tiledShader != null && material.shader != tiledShader)
            {
                material.shader = tiledShader;
            }
        }

        private static void ApplyMapsToChunkTransparent(Material material)
        {
            if (material == null)
            {
                return;
            }

            Texture2D bump = Box3BlocksAssetFactory.GetAtlasBumpTexture();
            if (bump != null)
            {
                material.SetTexture("_BumpMap", bump);
                material.SetFloat("_BumpScale", 0.1f);
                material.EnableKeyword("_NORMALMAP");
            }

            Texture2D metallic = Box3BlocksAssetFactory.GetAtlasMaterialTexture();
            if (metallic != null)
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.SetFloat("_Metallic", 0.2f);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }

            Texture2D emission = Box3BlocksAssetFactory.GetAtlasEmissionTexture();
            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", Color.white);
                material.EnableKeyword("_EMISSION");
            }
        }

        private static void ApplyBumpToChunkOpaque(Material material)
        {
            Box3BlocksOpaqueMaterialConfigurator.Apply(material);
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

        private static string BuildAnimatedGroupKey(AnimatedChunkVoxel voxel)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|r{1}|t{2}",
                voxel.blockName ?? string.Empty,
                voxel.rot & 3,
                voxel.isTransparent ? 1 : 0);
        }

        private Mesh BuildAnimatedChunkGroupMesh(ChunkKey key, List<AnimatedChunkVoxel> group, HashSet<Vector3Int> cullSet)
        {
            if (group == null || group.Count == 0)
            {
                return null;
            }
            int size = Mathf.Max(1, _chunkSize);
            int baseX = key.x * size;
            int baseY = key.y * size;
            int baseZ = key.z * size;
            int layers = size + 1;
            int cellsPerLayer = size * size;
            int rot = group[0].rot & 3;
            PreparedBlock samplePrepared = group[0].prepared;
            if (samplePrepared == null || samplePrepared.faceMainTexSt == null || samplePrepared.faceMainTexSt.Length < SideOrder.Length)
            {
                return null;
            }

            int[] localFaceByDir = new int[WorldFaceDirs.Length];
            Vector4[] stByDir = new Vector4[WorldFaceDirs.Length];
            int[] uvPatternByDir = new int[WorldFaceDirs.Length];
            for (int d = 0; d < WorldFaceDirs.Length; d++)
            {
                int localFace = ResolveLocalFaceIndexForWorldDir(rot, WorldFaceDirs[d]);
                localFaceByDir[d] = localFace;
                stByDir[d] = localFace >= 0 && localFace < SideOrder.Length ? samplePrepared.faceMainTexSt[localFace] : new Vector4(1f, 1f, 0f, 0f);
                uvPatternByDir[d] = localFace >= 0 && localFace < SideOrder.Length
                    ? BuildUvPatternForFace(localFace, rot, d)
                    : 0xE4;
            }

            int[][] masks = new int[WorldFaceDirs.Length][];
            for (int d = 0; d < WorldFaceDirs.Length; d++)
            {
                masks[d] = new int[layers * cellsPerLayer];
                for (int j = 0; j < masks[d].Length; j++)
                {
                    masks[d][j] = 0;
                }
            }

            for (int i = 0; i < group.Count; i++)
            {
                AnimatedChunkVoxel voxel = group[i];
                int lx = voxel.pos.x - baseX;
                int ly = voxel.pos.y - baseY;
                int lz = voxel.pos.z - baseZ;
                if (lx < 0 || ly < 0 || lz < 0 || lx >= size || ly >= size || lz >= size)
                {
                    continue;
                }

                for (int d = 0; d < WorldFaceDirs.Length; d++)
                {
                    Vector3Int worldDir = WorldFaceDirs[d];
                    if (cullSet != null && cullSet.Contains(voxel.pos + worldDir))
                    {
                        continue;
                    }

                    MapFaceToMaskCoordinates(d, lx, ly, lz, out int layer, out int u, out int v);
                    if (layer < 0 || layer >= layers || u < 0 || u >= size || v < 0 || v >= size)
                    {
                        continue;
                    }

                    int idx = (layer * cellsPerLayer) + (v * size) + u;
                    masks[d][idx] = 1;
                }
            }

            List<Vector3> vertices = new List<Vector3>(group.Count * 8);
            List<Vector2> uvs = new List<Vector2>(group.Count * 8);
            List<Vector2> uvMin = new List<Vector2>(group.Count * 8);
            List<Vector2> uvSize = new List<Vector2>(group.Count * 8);
            List<int>[] subTris = new List<int>[SideOrder.Length];
            for (int s = 0; s < SideOrder.Length; s++)
            {
                subTris[s] = new List<int>(group.Count * 6);
            }

            for (int d = 0; d < WorldFaceDirs.Length; d++)
            {
                int localFace = localFaceByDir[d];
                if (localFace < 0 || localFace >= SideOrder.Length)
                {
                    continue;
                }

                int[] mask = masks[d];
                for (int layer = 0; layer < layers; layer++)
                {
                    int layerOffset = layer * cellsPerLayer;
                    for (int v = 0; v < size; v++)
                    {
                        for (int u = 0; u < size; u++)
                        {
                            int cellIndex = layerOffset + (v * size) + u;
                            if (mask[cellIndex] == 0)
                            {
                                continue;
                            }

                            int width = 1;
                            while (u + width < size && mask[layerOffset + (v * size) + (u + width)] != 0)
                            {
                                width++;
                            }

                            int height = 1;
                            bool canGrow = true;
                            while (v + height < size && canGrow)
                            {
                                int rowOffset = layerOffset + ((v + height) * size);
                                for (int x = 0; x < width; x++)
                                {
                                    if (mask[rowOffset + u + x] == 0)
                                    {
                                        canGrow = false;
                                        break;
                                    }
                                }

                                if (canGrow)
                                {
                                    height++;
                                }
                            }

                            AddGreedyAnimatedTiledQuad(
                                d,
                                key,
                                size,
                                layer,
                                u,
                                v,
                                width,
                                height,
                                stByDir[d],
                                uvPatternByDir[d],
                                vertices,
                                uvs,
                                uvMin,
                                uvSize,
                                subTris[localFace]);

                            for (int yy = 0; yy < height; yy++)
                            {
                                int rowOffset = layerOffset + ((v + yy) * size);
                                for (int xx = 0; xx < width; xx++)
                                {
                                    mask[rowOffset + u + xx] = 0;
                                }
                            }
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"VoxelChunk_{key.x}_{key.y}_{key.z}_animated",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvMin);
            mesh.SetUVs(2, uvSize);
            mesh.subMeshCount = SideOrder.Length;
            for (int s = 0; s < SideOrder.Length; s++)
            {
                mesh.SetTriangles(subTris[s], s, true);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildOpaqueChunkMesh(ChunkKey key, List<OpaqueVoxel> voxels, HashSet<Vector3Int> opaqueVoxels)
        {
            if (voxels == null || voxels.Count == 0 || opaqueVoxels == null)
            {
                return null;
            }

            int size = Mathf.Max(1, _chunkSize);
            int baseX = key.x * size;
            int baseY = key.y * size;
            int baseZ = key.z * size;
            int layers = size + 1;
            int cellsPerLayer = size * size;

            int[][] masks = new int[WorldFaceDirs.Length][];
            for (int i = 0; i < masks.Length; i++)
            {
                masks[i] = new int[layers * cellsPerLayer];
                for (int j = 0; j < masks[i].Length; j++)
                {
                    masks[i][j] = -1;
                }
            }

            Dictionary<string, int> signatureToId = new Dictionary<string, int>();
            List<Vector4> idToSt = new List<Vector4>(128);
            List<int> idToUvPattern = new List<int>(128);
            List<Vector3> vertices = new List<Vector3>(voxels.Count * 8);
            List<Vector2> tiledUvs = new List<Vector2>(voxels.Count * 8);
            List<Vector2> atlasUvMin = new List<Vector2>(voxels.Count * 8);
            List<Vector2> atlasUvSize = new List<Vector2>(voxels.Count * 8);
            List<int> triangles = new List<int>(voxels.Count * 12);

            for (int i = 0; i < voxels.Count; i++)
            {
                OpaqueVoxel voxel = voxels[i];
                PreparedBlock prepared = voxel.prepared;
                if (prepared == null || prepared.faceMainTexSt == null || prepared.faceMainTexSt.Length < SideOrder.Length)
                {
                    continue;
                }

                if (prepared.hasAnimation)
                {
                    AppendDirectFacesForOpaqueAnimated(voxel, opaqueVoxels, vertices, tiledUvs, atlasUvMin, atlasUvSize, triangles);
                    continue;
                }

                int lx = voxel.pos.x - baseX;
                int ly = voxel.pos.y - baseY;
                int lz = voxel.pos.z - baseZ;
                if (lx < 0 || ly < 0 || lz < 0 || lx >= size || ly >= size || lz >= size)
                {
                    continue;
                }

                for (int dirIndex = 0; dirIndex < WorldFaceDirs.Length; dirIndex++)
                {
                    Vector3Int worldDir = WorldFaceDirs[dirIndex];
                    if (opaqueVoxels.Contains(voxel.pos + worldDir))
                    {
                        continue;
                    }

                    if (!TryGetFaceStForWorldDir(prepared, voxel.rot, dirIndex, worldDir, out Vector4 st, out int uvPattern))
                    {
                        continue;
                    }

                    string sigKey = BuildFaceSignatureKey(st, uvPattern);
                    if (!signatureToId.TryGetValue(sigKey, out int sigId))
                    {
                        sigId = idToSt.Count;
                        idToSt.Add(st);
                        idToUvPattern.Add(uvPattern);
                        signatureToId.Add(sigKey, sigId);
                    }

                    MapFaceToMaskCoordinates(dirIndex, lx, ly, lz, out int layer, out int u, out int v);
                    if (layer < 0 || layer >= layers || u < 0 || u >= size || v < 0 || v >= size)
                    {
                        continue;
                    }

                    int idx = (layer * cellsPerLayer) + (v * size) + u;
                    masks[dirIndex][idx] = sigId;
                }
            }

            for (int dirIndex = 0; dirIndex < WorldFaceDirs.Length; dirIndex++)
            {
                int[] mask = masks[dirIndex];
                for (int layer = 0; layer < layers; layer++)
                {
                    int layerOffset = layer * cellsPerLayer;
                    for (int v = 0; v < size; v++)
                    {
                        for (int u = 0; u < size; u++)
                        {
                            int cellIndex = layerOffset + (v * size) + u;
                            int sigId = mask[cellIndex];
                            if (sigId < 0)
                            {
                                continue;
                            }

                            int width = 1;
                            while (u + width < size && mask[layerOffset + (v * size) + (u + width)] == sigId)
                            {
                                width++;
                            }

                            int height = 1;
                            bool canGrow = true;
                            while (v + height < size && canGrow)
                            {
                                int rowOffset = layerOffset + ((v + height) * size);
                                for (int x = 0; x < width; x++)
                                {
                                    if (mask[rowOffset + u + x] != sigId)
                                    {
                                        canGrow = false;
                                        break;
                                    }
                                }

                                if (canGrow)
                                {
                                    height++;
                                }
                            }

                            AddGreedyFaceQuad(
                                dirIndex,
                                key,
                                size,
                                layer,
                                u,
                                v,
                                width,
                                height,
                                idToSt[sigId],
                                idToUvPattern[sigId],
                                vertices,
                                tiledUvs,
                                atlasUvMin,
                                atlasUvSize,
                                triangles);

                            for (int yy = 0; yy < height; yy++)
                            {
                                int rowOffset = layerOffset + ((v + yy) * size);
                                for (int xx = 0; xx < width; xx++)
                                {
                                    mask[rowOffset + u + xx] = -1;
                                }
                            }
                        }
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh
            {
                name = $"VoxelChunk_{key.x}_{key.y}_{key.z}_opaque",
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, tiledUvs);
            mesh.SetUVs(1, atlasUvMin);
            mesh.SetUVs(2, atlasUvSize);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AppendDirectFacesForOpaqueAnimated(
            OpaqueVoxel voxel,
            HashSet<Vector3Int> opaqueVoxels,
            List<Vector3> vertices,
            List<Vector2> tiledUvs,
            List<Vector2> atlasUvMin,
            List<Vector2> atlasUvSize,
            List<int> triangles)
        {
            if (voxel.prepared == null || voxel.prepared.faceMainTexSt == null || voxel.prepared.faceMainTexSt.Length < SideOrder.Length)
            {
                return;
            }

            Quaternion rotQ = _rotLookup[voxel.rot & 3];
            for (int localFace = 0; localFace < SideOrder.Length; localFace++)
            {
                Vector3 worldDirF = rotQ * FaceNormals[localFace];
                Vector3Int worldDir = ToVector3Int(worldDirF);
                if (opaqueVoxels != null && opaqueVoxels.Contains(voxel.pos + worldDir))
                {
                    continue;
                }

                int dirIndex = GetWorldDirIndex(worldDir);
                if (dirIndex < 0)
                {
                    continue;
                }

                Vector4 st = ResolveStaticFaceSt(voxel.prepared, localFace);
                int baseIndex = vertices.Count;
                for (int v = 0; v < 4; v++)
                {
                    Vector3 rotated = rotQ * FaceVertices[localFace][v];
                    vertices.Add(rotated + (Vector3)voxel.pos);
                }

                int uvPattern = BuildUvPatternForFace(localFace, voxel.rot & 3, dirIndex);
                AddMappedTiledUv(tiledUvs, 1, 1, uvPattern);
                Vector2 min = new Vector2(st.z, st.w);
                Vector2 size = new Vector2(st.x, st.y);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
        }

        private bool TryGetFaceStForWorldDir(PreparedBlock prepared, int rot, int dirIndex, Vector3Int worldDir, out Vector4 st, out int uvPattern)
        {
            st = default;
            uvPattern = 0xE4;
            if (prepared == null || prepared.faceMainTexSt == null || prepared.faceMainTexSt.Length < SideOrder.Length)
            {
                return false;
            }

            Quaternion q = _rotLookup[rot & 3];
            for (int i = 0; i < SideOrder.Length; i++)
            {
                Vector3Int dir = ToVector3Int(q * FaceNormals[i]);
                if (dir == worldDir)
                {
                    st = ResolveStaticFaceSt(prepared, i);
                    uvPattern = BuildUvPatternForFace(i, rot, dirIndex);
                    return true;
                }
            }

            return false;
        }

        private static int BuildUvPatternForFace(int localFaceIndex, int rot, int dirIndex)
        {
            Quaternion q = Quaternion.Euler(0f, (rot & 3) * 90f, 0f);
            int pattern = 0;
            for (int localVertexIndex = 0; localVertexIndex < 4; localVertexIndex++)
            {
                Vector3 rotated = q * FaceVertices[localFaceIndex][localVertexIndex];
                int worldCorner = MapRotatedVertexToWorldCorner(dirIndex, rotated);
                pattern |= (localVertexIndex & 0x3) << (worldCorner * 2);
            }

            return pattern;
        }

        private static int MapRotatedVertexToWorldCorner(int dirIndex, Vector3 rotated)
        {
            bool xPos = rotated.x >= 0f;
            bool yPos = rotated.y >= 0f;
            bool zPos = rotated.z >= 0f;

            switch (dirIndex)
            {
                case 0: // -X: 0(-y,+z),1(-y,-z),2(+y,-z),3(+y,+z)
                    if (!yPos && zPos) return 0;
                    if (!yPos && !zPos) return 1;
                    if (yPos && !zPos) return 2;
                    return 3;
                case 1: // +X: 0(-y,-z),1(-y,+z),2(+y,+z),3(+y,-z)
                    if (!yPos && !zPos) return 0;
                    if (!yPos && zPos) return 1;
                    if (yPos && zPos) return 2;
                    return 3;
                case 2: // -Y: 0(-x,+z),1(+x,+z),2(+x,-z),3(-x,-z)
                    if (!xPos && zPos) return 0;
                    if (xPos && zPos) return 1;
                    if (xPos && !zPos) return 2;
                    return 3;
                case 3: // +Y: 0(-x,-z),1(+x,-z),2(+x,+z),3(-x,+z)
                    if (!xPos && !zPos) return 0;
                    if (xPos && !zPos) return 1;
                    if (xPos && zPos) return 2;
                    return 3;
                case 4: // -Z: 0(-x,-y),1(+x,-y),2(+x,+y),3(-x,+y)
                    if (!xPos && !yPos) return 0;
                    if (xPos && !yPos) return 1;
                    if (xPos && yPos) return 2;
                    return 3;
                default: // +Z: 0(+x,-y),1(-x,-y),2(-x,+y),3(+x,+y)
                    if (xPos && !yPos) return 0;
                    if (!xPos && !yPos) return 1;
                    if (!xPos && yPos) return 2;
                    return 3;
            }
        }

        private static string BuildFaceSignatureKey(Vector4 st, int uvPattern)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F6}|{1:F6}|{2:F6}|{3:F6}|{4}",
                st.x,
                st.y,
                st.z,
                st.w,
                uvPattern & 0xFF);
        }

        private static Vector4 ResolveStaticFaceSt(PreparedBlock prepared, int faceIndex)
        {
            if (prepared == null || prepared.faceMainTexSt == null || faceIndex < 0 || faceIndex >= prepared.faceMainTexSt.Length)
            {
                return new Vector4(1f, 1f, 0f, 0f);
            }

            Vector4 baseSt = prepared.faceMainTexSt[faceIndex];
            if (prepared.animations == null || prepared.animations.Length == 0)
            {
                return baseSt;
            }

            for (int i = 0; i < prepared.animations.Length; i++)
            {
                Box3BlocksTextureAnimator.FaceAnimation anim = prepared.animations[i];
                if (anim == null || anim.materialIndex != faceIndex || anim.frameCount <= 1)
                {
                    continue;
                }

                int frameCount = Mathf.Max(1, anim.frameCount);
                int firstFrame = 0;
                if (anim.frames != null && anim.frames.Length > 0)
                {
                    firstFrame = Mathf.Clamp(anim.frames[0], 0, frameCount - 1);
                }

                float frameScaleY = baseSt.y / frameCount;
                float frameOffsetY = baseSt.w + baseSt.y - ((firstFrame + 1f) * frameScaleY);
                return new Vector4(baseSt.x, frameScaleY, baseSt.z, frameOffsetY);
            }

            return baseSt;
        }

        private static int GetWorldDirIndex(Vector3Int worldDir)
        {
            for (int i = 0; i < WorldFaceDirs.Length; i++)
            {
                if (WorldFaceDirs[i] == worldDir)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void MapFaceToMaskCoordinates(int dirIndex, int lx, int ly, int lz, out int layer, out int u, out int v)
        {
            switch (dirIndex)
            {
                case 0: // -X
                    layer = lx;
                    u = lz;
                    v = ly;
                    break;
                case 1: // +X
                    layer = lx + 1;
                    u = lz;
                    v = ly;
                    break;
                case 2: // -Y
                    layer = ly;
                    u = lx;
                    v = lz;
                    break;
                case 3: // +Y
                    layer = ly + 1;
                    u = lx;
                    v = lz;
                    break;
                case 4: // -Z
                    layer = lz;
                    u = lx;
                    v = ly;
                    break;
                default: // +Z
                    layer = lz + 1;
                    u = lx;
                    v = ly;
                    break;
            }
        }

        private static void AddGreedyFaceQuad(
            int dirIndex,
            ChunkKey key,
            int chunkSize,
            int layer,
            int u,
            int v,
            int width,
            int height,
            Vector4 st,
            int uvPattern,
            List<Vector3> vertices,
            List<Vector2> tiledUvs,
            List<Vector2> atlasUvMin,
            List<Vector2> atlasUvSize,
            List<int> triangles)
        {
            float bx = key.x * chunkSize;
            float by = key.y * chunkSize;
            float bz = key.z * chunkSize;

            Vector3 v0;
            Vector3 v1;
            Vector3 v2;
            Vector3 v3;
            switch (dirIndex)
            {
                case 0: // -X (u=z, v=y)
                {
                    float x = (key.x * chunkSize) + layer - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    float zz0 = bz + u - 0.5f;
                    float zz1 = bz + u + width - 0.5f;
                    v0 = new Vector3(x, yy0, zz1);
                    v1 = new Vector3(x, yy0, zz0);
                    v2 = new Vector3(x, yy1, zz0);
                    v3 = new Vector3(x, yy1, zz1);
                    break;
                }
                case 1: // +X (u=z, v=y)
                {
                    float x = (key.x * chunkSize) + layer - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    float zz0 = bz + u - 0.5f;
                    float zz1 = bz + u + width - 0.5f;
                    v0 = new Vector3(x, yy0, zz0);
                    v1 = new Vector3(x, yy0, zz1);
                    v2 = new Vector3(x, yy1, zz1);
                    v3 = new Vector3(x, yy1, zz0);
                    break;
                }
                case 2: // -Y (u=x, v=z)
                {
                    float y = (key.y * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float zz0 = bz + v - 0.5f;
                    float zz1 = bz + v + height - 0.5f;
                    v0 = new Vector3(xx0, y, zz1);
                    v1 = new Vector3(xx1, y, zz1);
                    v2 = new Vector3(xx1, y, zz0);
                    v3 = new Vector3(xx0, y, zz0);
                    break;
                }
                case 3: // +Y (u=x, v=z)
                {
                    float y = (key.y * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float zz0 = bz + v - 0.5f;
                    float zz1 = bz + v + height - 0.5f;
                    v0 = new Vector3(xx0, y, zz0);
                    v1 = new Vector3(xx1, y, zz0);
                    v2 = new Vector3(xx1, y, zz1);
                    v3 = new Vector3(xx0, y, zz1);
                    break;
                }
                case 4: // -Z (u=x, v=y)
                {
                    float z = (key.z * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    v0 = new Vector3(xx0, yy0, z);
                    v1 = new Vector3(xx1, yy0, z);
                    v2 = new Vector3(xx1, yy1, z);
                    v3 = new Vector3(xx0, yy1, z);
                    break;
                }
                default: // +Z (u=x, v=y)
                {
                    float z = (key.z * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    v0 = new Vector3(xx1, yy0, z);
                    v1 = new Vector3(xx0, yy0, z);
                    v2 = new Vector3(xx0, yy1, z);
                    v3 = new Vector3(xx1, yy1, z);
                    break;
                }
            }

            int baseIndex = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            AddMappedTiledUv(tiledUvs, width, height, uvPattern);

            Vector2 min = new Vector2(st.z, st.w);
            Vector2 size = new Vector2(st.x, st.y);
            atlasUvMin.Add(min);
            atlasUvMin.Add(min);
            atlasUvMin.Add(min);
            atlasUvMin.Add(min);
            atlasUvSize.Add(size);
            atlasUvSize.Add(size);
            atlasUvSize.Add(size);
            atlasUvSize.Add(size);

            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        private static void AddMappedTiledUv(List<Vector2> tiledUvs, int width, int height, int uvPattern)
        {
            Vector2[] unit = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            int localAtWorld0 = (uvPattern >> 0) & 0x3;
            int localAtWorld1 = (uvPattern >> 2) & 0x3;
            bool worldUMapsToLocalV = Mathf.Abs(unit[localAtWorld1].y - unit[localAtWorld0].y) > 0.5f;
            int uScale = worldUMapsToLocalV ? height : width;
            int vScale = worldUMapsToLocalV ? width : height;
            for (int worldCorner = 0; worldCorner < 4; worldCorner++)
            {
                int localUvCorner = (uvPattern >> (worldCorner * 2)) & 0x3;
                Vector2 uv = unit[localUvCorner];
                tiledUvs.Add(new Vector2(uv.x * uScale, uv.y * vScale));
            }
        }

        private int ResolveLocalFaceIndexForWorldDir(int rot, Vector3Int worldDir)
        {
            Quaternion q = _rotLookup[rot & 3];
            for (int i = 0; i < SideOrder.Length; i++)
            {
                Vector3Int dir = ToVector3Int(q * FaceNormals[i]);
                if (dir == worldDir)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AddGreedyAnimatedTiledQuad(
            int dirIndex,
            ChunkKey key,
            int chunkSize,
            int layer,
            int u,
            int v,
            int width,
            int height,
            Vector4 st,
            int uvPattern,
            List<Vector3> vertices,
            List<Vector2> tiledUv,
            List<Vector2> uvMin,
            List<Vector2> uvSize,
            List<int> triangles)
        {
            float bx = key.x * chunkSize;
            float by = key.y * chunkSize;
            float bz = key.z * chunkSize;

            Vector3 v0;
            Vector3 v1;
            Vector3 v2;
            Vector3 v3;
            switch (dirIndex)
            {
                case 0:
                {
                    float x = (key.x * chunkSize) + layer - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    float zz0 = bz + u - 0.5f;
                    float zz1 = bz + u + width - 0.5f;
                    v0 = new Vector3(x, yy0, zz1);
                    v1 = new Vector3(x, yy0, zz0);
                    v2 = new Vector3(x, yy1, zz0);
                    v3 = new Vector3(x, yy1, zz1);
                    break;
                }
                case 1:
                {
                    float x = (key.x * chunkSize) + layer - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    float zz0 = bz + u - 0.5f;
                    float zz1 = bz + u + width - 0.5f;
                    v0 = new Vector3(x, yy0, zz0);
                    v1 = new Vector3(x, yy0, zz1);
                    v2 = new Vector3(x, yy1, zz1);
                    v3 = new Vector3(x, yy1, zz0);
                    break;
                }
                case 2:
                {
                    float y = (key.y * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float zz0 = bz + v - 0.5f;
                    float zz1 = bz + v + height - 0.5f;
                    v0 = new Vector3(xx0, y, zz1);
                    v1 = new Vector3(xx1, y, zz1);
                    v2 = new Vector3(xx1, y, zz0);
                    v3 = new Vector3(xx0, y, zz0);
                    break;
                }
                case 3:
                {
                    float y = (key.y * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float zz0 = bz + v - 0.5f;
                    float zz1 = bz + v + height - 0.5f;
                    v0 = new Vector3(xx0, y, zz0);
                    v1 = new Vector3(xx1, y, zz0);
                    v2 = new Vector3(xx1, y, zz1);
                    v3 = new Vector3(xx0, y, zz1);
                    break;
                }
                case 4:
                {
                    float z = (key.z * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    v0 = new Vector3(xx0, yy0, z);
                    v1 = new Vector3(xx1, yy0, z);
                    v2 = new Vector3(xx1, yy1, z);
                    v3 = new Vector3(xx0, yy1, z);
                    break;
                }
                default:
                {
                    float z = (key.z * chunkSize) + layer - 0.5f;
                    float xx0 = bx + u - 0.5f;
                    float xx1 = bx + u + width - 0.5f;
                    float yy0 = by + v - 0.5f;
                    float yy1 = by + v + height - 0.5f;
                    v0 = new Vector3(xx1, yy0, z);
                    v1 = new Vector3(xx0, yy0, z);
                    v2 = new Vector3(xx0, yy1, z);
                    v3 = new Vector3(xx1, yy1, z);
                    break;
                }
            }

            int baseIndex = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            AddMappedTiledUv(tiledUv, width, height, uvPattern);

            Vector2 min = new Vector2(st.z, st.w);
            Vector2 size = new Vector2(st.x, st.y);
            uvMin.Add(min);
            uvMin.Add(min);
            uvMin.Add(min);
            uvMin.Add(min);
            uvSize.Add(size);
            uvSize.Add(size);
            uvSize.Add(size);
            uvSize.Add(size);

            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 0);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        private static Vector4[] BuildUnitFaceMainTexSt()
        {
            Vector4[] unit = new Vector4[SideOrder.Length];
            for (int i = 0; i < unit.Length; i++)
            {
                unit[i] = new Vector4(1f, 1f, 0f, 0f);
            }

            return unit;
        }

        private static Box3BlocksTextureAnimator.FaceAnimation[] CloneAnimationsForTiled(Box3BlocksTextureAnimator.FaceAnimation[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<Box3BlocksTextureAnimator.FaceAnimation>();
            }

            Box3BlocksTextureAnimator.FaceAnimation[] cloned = new Box3BlocksTextureAnimator.FaceAnimation[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Box3BlocksTextureAnimator.FaceAnimation s = source[i];
                if (s == null)
                {
                    continue;
                }

                cloned[i] = new Box3BlocksTextureAnimator.FaceAnimation
                {
                    materialIndex = s.materialIndex,
                    frameCount = Mathf.Max(1, s.frameCount),
                    frameDuration = Mathf.Max(0.01f, s.frameDuration),
                    frames = s.frames != null ? (int[])s.frames.Clone() : Array.Empty<int>(),
                    baseMainTexSt = new Vector4(1f, 1f, 0f, 0f)
                };
            }

            return cloned;
        }

        private Mesh BuildTransparentChunkMesh(ChunkKey key, List<TransparentVoxel> voxels, HashSet<Vector3Int> allVoxels)
        {
            if (voxels == null || voxels.Count == 0 || allVoxels == null)
            {
                return null;
            }

            int size = Mathf.Max(1, _chunkSize);
            int baseX = key.x * size;
            int baseY = key.y * size;
            int baseZ = key.z * size;
            int layers = size + 1;
            int cellsPerLayer = size * size;

            int[][] masks = new int[WorldFaceDirs.Length][];
            for (int i = 0; i < masks.Length; i++)
            {
                masks[i] = new int[layers * cellsPerLayer];
                for (int j = 0; j < masks[i].Length; j++)
                {
                    masks[i][j] = -1;
                }
            }

            Dictionary<string, int> signatureToId = new Dictionary<string, int>();
            List<Vector4> idToSt = new List<Vector4>(128);
            List<int> idToUvPattern = new List<int>(128);
            List<Vector3> vertices = new List<Vector3>(voxels.Count * 8);
            List<Vector2> tiledUvs = new List<Vector2>(voxels.Count * 8);
            List<Vector2> atlasUvMin = new List<Vector2>(voxels.Count * 8);
            List<Vector2> atlasUvSize = new List<Vector2>(voxels.Count * 8);
            List<int> triangles = new List<int>(voxels.Count * 12);

            for (int i = 0; i < voxels.Count; i++)
            {
                TransparentVoxel voxel = voxels[i];
                PreparedBlock prepared = voxel.prepared;
                if (prepared == null || prepared.faceMainTexSt == null || prepared.faceMainTexSt.Length < SideOrder.Length)
                {
                    continue;
                }

                if (prepared.hasAnimation)
                {
                    AppendDirectFacesForTransparentAnimated(voxel, allVoxels, vertices, tiledUvs, atlasUvMin, atlasUvSize, triangles);
                    continue;
                }

                int lx = voxel.pos.x - baseX;
                int ly = voxel.pos.y - baseY;
                int lz = voxel.pos.z - baseZ;
                if (lx < 0 || ly < 0 || lz < 0 || lx >= size || ly >= size || lz >= size)
                {
                    continue;
                }

                for (int dirIndex = 0; dirIndex < WorldFaceDirs.Length; dirIndex++)
                {
                    Vector3Int worldDir = WorldFaceDirs[dirIndex];
                    if (allVoxels.Contains(voxel.pos + worldDir))
                    {
                        continue;
                    }

                    if (!TryGetFaceStForWorldDir(prepared, voxel.rot, dirIndex, worldDir, out Vector4 st, out int uvPattern))
                    {
                        continue;
                    }

                    string sigKey = BuildFaceSignatureKey(st, uvPattern);
                    if (!signatureToId.TryGetValue(sigKey, out int sigId))
                    {
                        sigId = idToSt.Count;
                        idToSt.Add(st);
                        idToUvPattern.Add(uvPattern);
                        signatureToId.Add(sigKey, sigId);
                    }

                    MapFaceToMaskCoordinates(dirIndex, lx, ly, lz, out int layer, out int u, out int v);
                    if (layer < 0 || layer >= layers || u < 0 || u >= size || v < 0 || v >= size)
                    {
                        continue;
                    }

                    int idx = (layer * cellsPerLayer) + (v * size) + u;
                    masks[dirIndex][idx] = sigId;
                }
            }

            for (int dirIndex = 0; dirIndex < WorldFaceDirs.Length; dirIndex++)
            {
                int[] mask = masks[dirIndex];
                for (int layer = 0; layer < layers; layer++)
                {
                    int layerOffset = layer * cellsPerLayer;
                    for (int v = 0; v < size; v++)
                    {
                        for (int u = 0; u < size; u++)
                        {
                            int cellIndex = layerOffset + (v * size) + u;
                            int sigId = mask[cellIndex];
                            if (sigId < 0)
                            {
                                continue;
                            }

                            int width = 1;
                            while (u + width < size && mask[layerOffset + (v * size) + (u + width)] == sigId)
                            {
                                width++;
                            }

                            int height = 1;
                            bool canGrow = true;
                            while (v + height < size && canGrow)
                            {
                                int rowOffset = layerOffset + ((v + height) * size);
                                for (int x = 0; x < width; x++)
                                {
                                    if (mask[rowOffset + u + x] != sigId)
                                    {
                                        canGrow = false;
                                        break;
                                    }
                                }

                                if (canGrow)
                                {
                                    height++;
                                }
                            }

                            AddGreedyFaceQuad(
                                dirIndex,
                                key,
                                size,
                                layer,
                                u,
                                v,
                                width,
                                height,
                                idToSt[sigId],
                                idToUvPattern[sigId],
                                vertices,
                                tiledUvs,
                                atlasUvMin,
                                atlasUvSize,
                                triangles);

                            for (int yy = 0; yy < height; yy++)
                            {
                                int rowOffset = layerOffset + ((v + yy) * size);
                                for (int xx = 0; xx < width; xx++)
                                {
                                    mask[rowOffset + u + xx] = -1;
                                }
                            }
                        }
                    }
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
            mesh.SetUVs(0, tiledUvs);
            mesh.SetUVs(1, atlasUvMin);
            mesh.SetUVs(2, atlasUvSize);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AppendDirectFacesForTransparentAnimated(
            TransparentVoxel voxel,
            HashSet<Vector3Int> allVoxels,
            List<Vector3> vertices,
            List<Vector2> tiledUvs,
            List<Vector2> atlasUvMin,
            List<Vector2> atlasUvSize,
            List<int> triangles)
        {
            if (voxel.prepared == null || voxel.prepared.faceMainTexSt == null || voxel.prepared.faceMainTexSt.Length < SideOrder.Length)
            {
                return;
            }

            Quaternion rotQ = _rotLookup[voxel.rot & 3];
            for (int localFace = 0; localFace < SideOrder.Length; localFace++)
            {
                Vector3 worldDirF = rotQ * FaceNormals[localFace];
                Vector3Int worldDir = ToVector3Int(worldDirF);
                if (allVoxels != null && allVoxels.Contains(voxel.pos + worldDir))
                {
                    continue;
                }

                int dirIndex = GetWorldDirIndex(worldDir);
                if (dirIndex < 0)
                {
                    continue;
                }

                Vector4 st = ResolveStaticFaceSt(voxel.prepared, localFace);
                int baseIndex = vertices.Count;
                for (int v = 0; v < 4; v++)
                {
                    Vector3 rotated = rotQ * FaceVertices[localFace][v];
                    vertices.Add(rotated + (Vector3)voxel.pos);
                }

                int uvPattern = BuildUvPatternForFace(localFace, voxel.rot & 3, dirIndex);
                AddMappedTiledUv(tiledUvs, 1, 1, uvPattern);
                Vector2 min = new Vector2(st.z, st.w);
                Vector2 size = new Vector2(st.x, st.y);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvMin.Add(min);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);
                atlasUvSize.Add(size);

                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
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

        private static bool TryBuildAnimations(Dictionary<string, string> sideTexturePaths, Vector4[] faceMainTexSt, out Box3BlocksTextureAnimator.FaceAnimation[] animations)
        {
            animations = Array.Empty<Box3BlocksTextureAnimator.FaceAnimation>();
            if (sideTexturePaths == null || faceMainTexSt == null || faceMainTexSt.Length < SideOrder.Length)
            {
                return false;
            }

            List<Box3BlocksTextureAnimator.FaceAnimation> list = new List<Box3BlocksTextureAnimator.FaceAnimation>();
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

                list.Add(new Box3BlocksTextureAnimator.FaceAnimation
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
            if (!Box3BlocksFaceAnimationParser.TryParse(textureAssetPath, GetProjectAbsolutePath, out ParsedFaceAnimation parsed))
            {
                return false;
            }

            spec = new FaceAnimationSpec
            {
                frameCount = parsed.frameCount,
                frameDuration = parsed.frameDuration,
                frames = parsed.frames
            };
            return true;
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
                    map[name] = Box3BlocksIdRules.IsTransparencyKeyword(name);
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

            return Box3BlocksIdRules.IsTransparencyKeyword(blockName);
        }

        private bool IsEmissiveBlock(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return false;
            }

            if (_emissiveByName != null && _emissiveByName.TryGetValue(blockName, out bool emits))
            {
                return emits;
            }

            return Box3BlocksIdRules.IsEmissiveKeyword(blockName);
        }

        private Color ResolveEmissiveLightColor(string blockName)
        {
            if (!string.IsNullOrWhiteSpace(blockName)
                && _emissiveLightColorByName != null
                && _emissiveLightColorByName.TryGetValue(blockName, out Color mapped))
            {
                return mapped;
            }

            return Box3BlocksIdRules.InferLightColor(blockName);
        }

        private static bool IsWaterBlock(string blockName)
        {
            return blockName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBarrierBlock(string blockName)
        {
            return blockName.IndexOf("barrier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, bool> LoadBlockEmissiveMap()
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
                    string body = pair.Value;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    bool emits = HasMeaningfulEmission(body)
                        || ReadBoolField(body, "emissive")
                        || Regex.IsMatch(body, "\"glow\"\\s*:\\s*(true|1)", RegexOptions.IgnoreCase)
                        || Box3BlocksIdRules.IsEmissiveKeyword(name);
                    map[name] = emits;
                }
            }

            if (map.Count > 0)
            {
                return map;
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
                    map[name] = Box3BlocksIdRules.IsEmissiveKeyword(name);
                }
            }

            return map;
        }

        private static Dictionary<string, Color> LoadBlockLightColorMap()
        {
            Dictionary<string, Color> map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (File.Exists(absPath))
            {
                string json = File.ReadAllText(absPath);
                Dictionary<string, string> objects = ExtractTopLevelObjectValues(json);
                foreach (KeyValuePair<string, string> pair in objects)
                {
                    string name = pair.Key;
                    string body = pair.Value;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    Color color = ReadColorArrayField(body, "emissive", Box3BlocksIdRules.InferLightColor(name));
                    map[name] = color;
                }
            }

            return map;
        }

        private static bool HasMeaningfulEmission(string body)
        {
            Match m = Regex.Match(body, "\"emissive\"\\s*:\\s*\\[(?<value>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return false;
            }

            string[] parts = m.Groups["value"].Value.Split(',');
            float sum = 0f;
            for (int i = 0; i < Mathf.Min(3, parts.Length); i++)
            {
                sum += Mathf.Abs(Box3BlocksJsonLite.ParseFloatSafe(parts[i], 0f));
            }

            return sum > 0.001f;
        }

        private static Color ReadColorArrayField(string text, string fieldName, Color fallback)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\\[(?<value>[^\\]]+)\\]", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return fallback;
            }

            string[] parts = match.Groups["value"].Value.Split(',');
            if (parts.Length < 3)
            {
                return fallback;
            }

            float r = Box3BlocksJsonLite.ParseFloatSafe(parts[0], fallback.r);
            float g = Box3BlocksJsonLite.ParseFloatSafe(parts[1], fallback.g);
            float b = Box3BlocksJsonLite.ParseFloatSafe(parts[2], fallback.b);

            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max > 1f)
            {
                r /= max;
                g /= max;
                b /= max;
            }

            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
        }

        private static void CreateRealtimeLight(Transform parent, Vector3 localPosition, Color color, float intensity = 1.05f, float range = 6f)
        {
            if (parent == null)
            {
                return;
            }

            GameObject lightGo = new GameObject("__VoxelLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPosition;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = Mathf.Max(0f, intensity);
            light.range = Mathf.Max(0f, range);
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        private static bool ReadBoolField(string text, string fieldName)
        {
            return Box3BlocksJsonLite.ReadBoolField(text, fieldName);
        }

        private static Dictionary<string, string> ExtractTopLevelObjectValues(string json)
        {
            return Box3BlocksJsonLite.ExtractTopLevelObjectValues(json);
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
            int size = Mathf.Max(1, chunkSize);

            return new ChunkKey(
                FloorDiv(x, size),
                FloorDiv(y, size),
                FloorDiv(z, size));
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
