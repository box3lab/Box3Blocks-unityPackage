using UnityEditor;
using UnityEngine;

namespace Box3Blocks.Editor
{
    /// <summary>
    /// 图集质量设置窗口。
    /// <para/>
    /// Atlas quality settings window.
    /// </summary>
    public sealed class Box3BlocksAtlasQualityWindow : EditorWindow
    {
        private Box3AtlasQuality _quality;
        private string _status;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _headerTitleStyle;
        private GUIStyle _headerSubtitleStyle;
        private GUIStyle _hintStyle;
        private Vector2 _scroll;
        private readonly Box3AtlasQuality[] _qualityValues =
        {
            Box3AtlasQuality.Q1024,
            Box3AtlasQuality.Q512,
            Box3AtlasQuality.Q256
        };

        [MenuItem("Box3/图集质量", false, 250)]
        private static void Open()
        {
            GetWindow<Box3BlocksAtlasQualityWindow>(L("atlas.window.title"));
        }

        private void OnEnable()
        {
            _quality = Box3BlocksAtlasQualitySettings.GetQuality();
            _status = L("atlas.status.ready");
        }

        private static string L(string key)
        {
            return Box3BlocksI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return Box3BlocksI18n.Format(key, args);
        }

        private void EnsureStyles()
        {
            if (_sectionBoxStyle == null)
            {
                _sectionBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10),
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

            if (_headerTitleStyle == null)
            {
                _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14
                };
            }

            if (_headerSubtitleStyle == null)
            {
                _headerSubtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
                _headerSubtitleStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            if (_primaryButtonStyle == null)
            {
                _primaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 28f,
                    fontStyle = FontStyle.Bold
                };
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
                _hintStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            titleContent = new GUIContent(L("atlas.window.title"));
            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawHeader();

                EditorGUILayout.Space(8f);
                using (new EditorGUILayout.VerticalScope(_sectionBoxStyle))
                {
                    EditorGUILayout.LabelField(L("atlas.quality"), _sectionTitleStyle);
                    EditorGUILayout.Space(4f);
                    DrawQualityButtons();

                }

                EditorGUILayout.Space(8f);
                if (GUILayout.Button(L("atlas.apply_rebuild"), _primaryButtonStyle))
                {
                    ApplyAndRebuild();
                }

                EditorGUILayout.Space(8f);
                DrawStatus();
            }
        }

        private void ApplyAndRebuild()
        {
            Box3BlocksAtlasQualitySettings.SetQuality(_quality);
            Box3BlocksBuilderWindow.ReloadLibraryApi();
            Box3BlocksAssetFactory.InvalidateCaches();
            Material material = Box3BlocksAssetFactory.GetAtlasMaterial();

            if (material == null)
            {
                _status = L("atlas.status.rebuild_failed");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _status = Lf("atlas.status.applied", (int)_quality);
        }

        private void DrawQualityButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < _qualityValues.Length; i++)
                {
                    Box3AtlasQuality option = _qualityValues[i];
                    bool selected = _quality == option;
                    Color prev = GUI.backgroundColor;
                    if (selected)
                    {
                        GUI.backgroundColor = new Color(0.33f, 0.63f, 1f, 1f);
                    }

                    if (GUILayout.Button(Lf("atlas.quality_option", (int)option), GUILayout.Height(24f)))
                    {
                        _quality = option;
                    }

                    GUI.backgroundColor = prev;
                }
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope(_sectionBoxStyle))
            {
                EditorGUILayout.LabelField(L("atlas.window.title"), _headerTitleStyle);
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(L("atlas.window.help"), _headerSubtitleStyle);
            }
        }

        private void DrawStatus()
        {
            MessageType messageType = MessageType.None;
            if (!string.IsNullOrEmpty(_status)
                && (_status.IndexOf("failed", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || _status.IndexOf("失败", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                messageType = MessageType.Error;
            }

            EditorGUILayout.HelpBox(_status, messageType);
        }
    }
}
