using UnityEditor;
using UnityEngine;
using System;

namespace Box3Blocks.Editor
{
    public sealed class Box3BlocksChunkBuildWindow : EditorWindow
    {
        private Transform _sourceRoot;
        private Transform _targetParent;
        private Vector3Int _originOffset = Vector3Int.zero;

        private bool _ignoreBarrier = false;
        private bool _clearPrevious = false;
        private bool _deleteSourceBlocksAfterBuild = true;

        private int _realtimeLightMode = 2;
        private Box3ColliderMode _colliderMode = Box3ColliderMode.Full;

        private int _chunkSize = 32;
        private int _chunksPerTick = 6;
        private int _voxelsPerTick = 25000;
        private string _status = string.Empty;
        private Vector2 _scroll;

        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _insetPanelStyle;

        private static string L(string key)
        {
            return Box3BlocksI18n.Get(key);
        }

        private static string[] GetRealtimeLightModeLabels()
        {
            return new[]
            {
                L("voxel.light_mode.none"),
                L("voxel.light_mode.all"),
                L("voxel.light_mode.data_only")
            };
        }

        public static void OpenFromBuilder(Transform root, bool generateCollider, Box3ColliderMode toolColliderMode)
        {
            Box3BlocksChunkBuildWindow window = GetWindow<Box3BlocksChunkBuildWindow>(L("builder.chunk.window.title"));
            window.minSize = new Vector2(420f, 420f);
            Transform defaultRoot = root != null ? root : Box3BlocksBuilderWindow.GetCurrentRootApi();
            window._sourceRoot = defaultRoot;
            window._targetParent = defaultRoot;
            window._colliderMode = generateCollider ? toolColliderMode : Box3ColliderMode.None;
            window._realtimeLightMode = 2;
            window._status = string.Empty;
            window.Show();
        }

        private void OnEnable()
        {
            if (_sourceRoot == null)
            {
                _sourceRoot = Box3BlocksBuilderWindow.GetCurrentRootApi();
            }

            if (_targetParent == null)
            {
                _targetParent = _sourceRoot;
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
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
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

            if (_insetPanelStyle == null)
            {
                _insetPanelStyle = new GUIStyle(GUIStyle.none)
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(0, 0, 0, 0)
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

        private void OnGUI()
        {
            EnsureStyles();
            titleContent = new GUIContent(L("builder.chunk.window.title"));

            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                DrawSection(L("builder.chunk.section.title"), DrawConfigSection);
                EditorGUILayout.Space(6f);
                DrawSection(L("voxel.section.run"), DrawRunSection);
                EditorGUILayout.Space(6f);
                DrawSection(L("voxel.section.status"), DrawStatusSection);
            }
        }

        private void DrawConfigSection()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                _sourceRoot = (Transform)EditorGUILayout.ObjectField(L("builder.chunk.source_root"), _sourceRoot, typeof(Transform), true);
                _targetParent = (Transform)EditorGUILayout.ObjectField(L("builder.chunk.target_parent"), _targetParent, typeof(Transform), true);
                _originOffset = EditorGUILayout.Vector3IntField(L("builder.chunk.origin_offset"), _originOffset);

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(L("builder.chunk.group.basic"), EditorStyles.miniBoldLabel);
                _ignoreBarrier = EditorGUILayout.ToggleLeft(L("builder.chunk.ignore_barrier"), _ignoreBarrier);
                _clearPrevious = EditorGUILayout.ToggleLeft(L("builder.chunk.clear_previous"), _clearPrevious);
                _deleteSourceBlocksAfterBuild = EditorGUILayout.ToggleLeft(L("builder.chunk.delete_source_after_build"), _deleteSourceBlocksAfterBuild);

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(L("builder.chunk.group.build"), EditorStyles.miniBoldLabel);
                _realtimeLightMode = EditorGUILayout.Popup(L("builder.chunk.realtime_light"), Mathf.Clamp(_realtimeLightMode, 0, 2), GetRealtimeLightModeLabels());
                _colliderMode = (Box3ColliderMode)EditorGUILayout.EnumPopup(L("builder.chunk.collider_mode"), _colliderMode);
                _chunkSize = Mathf.Max(1, EditorGUILayout.IntField(L("builder.chunk.chunk_size"), _chunkSize));
                _chunksPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("builder.chunk.chunks_per_tick"), _chunksPerTick), 1, 64);
                _voxelsPerTick = Mathf.Clamp(EditorGUILayout.IntField(L("builder.chunk.voxels_per_tick"), _voxelsPerTick), 2000, 200000);
            }
        }

        private void DrawRunSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_sourceRoot == null);
                if (GUILayout.Button(L("builder.chunk.run"), _primaryButtonStyle))
                {
                    RunBuild();
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button(L("voxel.run.cancel"), _dangerButtonStyle))
                {
                    _status = string.Empty;
                }
            }
        }

        private void DrawStatusSection()
        {
            MessageType messageType = MessageType.None;
            if (!string.IsNullOrWhiteSpace(_status)
                && (_status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                    || _status.IndexOf("失败", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                messageType = MessageType.Error;
            }

            EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(_status) ? "-" : _status, messageType);
        }

        private void RunBuild()
        {
            if (_sourceRoot == null)
            {
                _status = L("builder.chunk.err.source_root_empty");
                EditorUtility.DisplayDialog(
                    L("builder.chunk.window.title"),
                    _status,
                    L("dialog.ok"));
                return;
            }

            if (_targetParent == null)
            {
                _targetParent = _sourceRoot;
            }

            bool success = Box3BlocksGzImportWindow.ImportChunkFromRootApi(
                _sourceRoot,
                _targetParent,
                _originOffset,
                _ignoreBarrier,
                _clearPrevious,
                Mathf.Clamp(_realtimeLightMode, 0, 2),
                _colliderMode,
                _chunkSize,
                _chunksPerTick,
                _voxelsPerTick,
                _deleteSourceBlocksAfterBuild);

            if (success)
            {
                _status = L("builder.chunk.done");
                EditorUtility.DisplayDialog(
                    L("builder.chunk.window.title"),
                    _status,
                    L("dialog.ok"));
                Close();
            }
            else
            {
                _status = L("builder.chunk.err.failed");
                EditorUtility.DisplayDialog(
                    L("builder.chunk.window.title"),
                    _status,
                    L("dialog.ok"));
            }
        }
    }
}
