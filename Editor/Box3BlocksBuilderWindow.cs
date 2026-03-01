using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Box3Blocks.Editor
{
    public partial class Box3BlocksBuilderWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3lab.box3/Assets/block";
        private const string BlockSpecPath = "Packages/com.box3lab.box3/Assets/block-spec.json";
        private const string BlockIdPath = "Packages/com.box3lab.box3/Assets/block-id.json";
        private const string GeneratedMaterialFolder = "Assets/Box3/Materials";
        private const string GeneratedMeshFolder = "Assets/Box3/Meshes";
        private const string VoxelImportChunkOpaqueMaterialPath = "Assets/Box3/Materials/M_Block.mat";
        private const string CategoryAll = "All";
        private const string CategoryRecent = "Recent";
        private const string CategoryUncategorized = "Uncategorized";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex(@"^(.*)_(back|bottom|front|left|right|top)\.png$", RegexOptions.Compiled);
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly int MainTexStShaderId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
        private const string RealtimeLightChildName = "__BlockLight";
        private static readonly Vector3Int[] OcclusionNeighborOffsets =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.up,
            Vector3Int.down,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        private Transform _root;
        private List<BlockDefinition> _allBlocks = new List<BlockDefinition>();
        private List<BlockDefinition> _filteredBlocks = new List<BlockDefinition>();
        private List<string> _recentBlockIds = new List<string>();
        private List<string> _categories = new List<string> { CategoryAll };
        private int _selectedIndex;
        private int _selectedCategory;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private Vector2 _categoryScroll;
        private EditTool _tool = EditTool.Place;
        private int _brushHorizontalSize = 1;
        private int _brushHeight = 1;
        private bool _spawnPointLightForEmissive = true;
        private const float PreviewSize = 75f;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _subtleLabelStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _categoryTabStyle;
        private GUIStyle _categoryTabSelectedStyle;
        private GUIStyle _toolTabStyle;
        private GUIStyle _toolTabSelectedStyle;
        private GUIStyle _insetPanelStyle;
        private GUIStyle _cardBoxStyle;
        private GUIStyle _cardTitleStyle;
        private GUIStyle _cardSubtitleStyle;
        private GUIStyle _cardTitleSelectedStyle;
        private GUIStyle _cardSubtitleSelectedStyle;
        private GUIStyle _rotateBadgeStyle;
        private bool _filteredHasAnimatedBlocks;
        private double _lastBlockListScrollTime;
        private const double AnimatedCardPreviewFps = 4.0;
        private float _blockListViewportHeight = 420f;
        private const float CardVisiblePadding = 120f;
        private readonly Dictionary<Vector3Int, GameObject> _blockLookup = new Dictionary<Vector3Int, GameObject>();
        private Transform _blockLookupRoot;
        private bool _blockLookupDirty = true;
        private readonly Dictionary<string, Mesh> _staticBlockMeshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _blockCardPreviewCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AnimatedPreviewCacheEntry> _animatedBlockCardPreviewCache = new Dictionary<string, AnimatedPreviewCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CardRotateAnimState> _cardRotateAnimations = new Dictionary<string, CardRotateAnimState>(StringComparer.OrdinalIgnoreCase);
        private PreviewRenderUtility _blockCardPreviewUtility;
        private double _nextAnimatedPreviewRepaintTime;

        [MenuItem("Box3/方块库", false, 0)]
        public static void Open()
        {
            GetWindow<Box3BlocksBuilderWindow>(L("window.title"));
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            _blockLookupDirty = true;
            ReloadBlockLibrary();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _blockLookup.Clear();
            _blockLookupRoot = null;
            _blockLookupDirty = true;
            _staticBlockMeshCache.Clear();
            ClearBlockCardPreviewCache();
            if (_blockCardPreviewUtility != null)
            {
                _blockCardPreviewUtility.Cleanup();
                _blockCardPreviewUtility = null;
            }
        }

        private static string L(string key)
        {
            return Box3BlocksI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return Box3BlocksI18n.Format(key, args);
        }

        private static string LocalizeCategoryLabel(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return L("category.uncategorized");
            }

            if (string.Equals(category, CategoryAll, StringComparison.OrdinalIgnoreCase))
            {
                return L("category.all");
            }

            if (string.Equals(category, CategoryRecent, StringComparison.OrdinalIgnoreCase))
            {
                return L("category.recent");
            }

            if (string.Equals(category, CategoryUncategorized, StringComparison.OrdinalIgnoreCase))
            {
                return L("category.uncategorized");
            }

            return Box3BlocksI18n.GetCategoryLabel(category.Trim());
        }

        private void OnGUI()
        {
            EnsureStyles();
            HandleToolHotkeys(Event.current);
            titleContent = new GUIContent(L("window.title"));
            DrawSection(L("section.world_root"), DrawRootSection);
            EditorGUILayout.Space(6f);
            DrawSection(L("section.editor_tool"), DrawToolSection);
            EditorGUILayout.Space(6f);
            DrawSection(L("section.block_library"), DrawBlockListSection);
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

            if (_searchFieldStyle == null)
            {
                _searchFieldStyle = new GUIStyle(EditorStyles.textField)
                {
                    fixedHeight = 22f
                };
            }

            if (_categoryTabStyle == null)
            {
                _categoryTabStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 22f
                };
            }

            if (_categoryTabSelectedStyle == null)
            {
                _categoryTabSelectedStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 22f,
                    fontStyle = FontStyle.Bold
                };
                _categoryTabSelectedStyle.normal.textColor = new Color(0.55f, 1f, 0.65f, 1f);
            }

            if (_toolTabStyle == null)
            {
                _toolTabStyle = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    fixedHeight = 26f,
                    fontStyle = FontStyle.Bold
                };
            }

            if (_toolTabSelectedStyle == null)
            {
                _toolTabSelectedStyle = new GUIStyle(_toolTabStyle);
                _toolTabSelectedStyle.normal.textColor = new Color(0.62f, 0.8f, 1f, 1f);
            }

            if (_insetPanelStyle == null)
            {
                _insetPanelStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(8, 8, 8, 8),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            if (_cardBoxStyle == null)
            {
                _cardBoxStyle = new GUIStyle("HelpBox")
                {
                    fixedHeight = Mathf.Max(136f, PreviewSize + 68f),
                    wordWrap = true,
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 11,
                    padding = new RectOffset(6, 6, 8, 8)
                };
            }

            if (_cardTitleStyle == null)
            {
                _cardTitleStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
            }

            if (_cardTitleSelectedStyle == null)
            {
                _cardTitleSelectedStyle = new GUIStyle(_cardTitleStyle);
                _cardTitleSelectedStyle.normal.textColor = new Color(0.78f, 1f, 0.8f, 1f);
            }

            if (_cardSubtitleStyle == null)
            {
                _cardSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true
                };
                _cardSubtitleStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            }

            if (_cardSubtitleSelectedStyle == null)
            {
                _cardSubtitleSelectedStyle = new GUIStyle(_cardSubtitleStyle);
                _cardSubtitleSelectedStyle.normal.textColor = new Color(0.68f, 0.95f, 0.72f, 1f);
            }

            if (_rotateBadgeStyle == null)
            {
                _rotateBadgeStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 10
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

        private void DrawRootSection()
        {
            Transform newRoot = (Transform)EditorGUILayout.ObjectField(L("root.root"), _root, typeof(Transform), true);
            if (newRoot != _root)
            {
                _root = newRoot;
                _blockLookupDirty = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("root.create"), _primaryButtonStyle))
                {
                    CreateRoot();
                }

                if (GUILayout.Button(L("root.clear"), _dangerButtonStyle))
                {
                    ClearRoot();
                }
            }

        }

        private void DrawToolSection()
        {
            DrawToolTabs();
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L("tool.horizontal"), _subtleLabelStyle, GUILayout.Width(104f));
                    int horizontalInput = Mathf.Max(1, EditorGUILayout.IntField(_brushHorizontalSize, GUILayout.Width(52f)));
                    float horizontalSliderMax = Mathf.Max(16f, horizontalInput);
                    _brushHorizontalSize = Mathf.Max(1, Mathf.RoundToInt(GUILayout.HorizontalSlider(horizontalInput, 1f, horizontalSliderMax)));
                }

                EditorGUILayout.Space(2f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(L("tool.height"), _subtleLabelStyle, GUILayout.Width(104f));
                    int heightInput = Mathf.Max(1, EditorGUILayout.IntField(_brushHeight, GUILayout.Width(52f)));
                    float heightSliderMax = Mathf.Max(16f, heightInput);
                    _brushHeight = Mathf.Max(1, Mathf.RoundToInt(GUILayout.HorizontalSlider(heightInput, 1f, heightSliderMax)));
                }
            }

            EditorGUILayout.LabelField(Lf("tool.brush_volume", _brushHorizontalSize, _brushHorizontalSize, _brushHeight), _subtleLabelStyle);

            if (_root == null)
            {
                EditorGUILayout.HelpBox(L("tool.assign_root_help"), MessageType.Info);
            }
        }

        private void DrawBlockListSection()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string newSearch = EditorGUILayout.TextField(L("library.search"), _search, _searchFieldStyle);
                    if (!string.Equals(newSearch, _search, StringComparison.Ordinal))
                    {
                        _search = newSearch;
                        ApplyFilter();
                    }
                }

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_categories.Count > 0)
                    {
                        DrawCategoryTabsSidebar();
                        GUILayout.Space(8f);
                    }

                    using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
                    {
                        float rightPaneWidth = Mathf.Max(220f, position.width - (_categories.Count > 0 ? 158f : 34f));
                        int columns = CalculateColumnCount(rightPaneWidth);
                        Vector2 prevScroll = _scroll;
                        _scroll = GUILayout.BeginScrollView(
                            _scroll,
                            false,
                            true,
                            GUIStyle.none,
                            GUI.skin.verticalScrollbar);
                        DrawBlockGrid(columns, rightPaneWidth - 20f);
                        EditorGUILayout.EndScrollView();
                        Rect viewportRect = GUILayoutUtility.GetLastRect();
                        if (viewportRect.height > 1f)
                        {
                            _blockListViewportHeight = viewportRect.height;
                        }
                        if ((prevScroll - _scroll).sqrMagnitude > 0.0001f)
                        {
                            _lastBlockListScrollTime = EditorApplication.timeSinceStartup;
                        }
                    }
                }
            }

            RequestAnimatedCardPreviewRepaint();
            RequestCardRotateAnimationRepaint();
        }

        private void DrawBlockGrid(int columns, float availableWidth)
        {
            const float gap = 4f;
            float safeWidth = Mathf.Max(120f, availableWidth);
            int clampedColumns = Mathf.Max(1, columns);
            float totalGap = (clampedColumns - 1) * gap;
            float cellWidth = Mathf.Max(120f, (safeWidth - totalGap) / clampedColumns);
            float cellHeight = Mathf.Max(136f, PreviewSize + 68f);
            float rowHeight = cellHeight + gap;
            int rowCount = Mathf.CeilToInt(_filteredBlocks.Count / (float)clampedColumns);
            if (rowCount <= 0)
            {
                return;
            }

            int startRow = Mathf.Clamp(Mathf.FloorToInt((_scroll.y - CardVisiblePadding) / Mathf.Max(1f, rowHeight)), 0, rowCount - 1);
            int endRow = Mathf.Clamp(Mathf.CeilToInt((_scroll.y + _blockListViewportHeight + CardVisiblePadding) / Mathf.Max(1f, rowHeight)), startRow, rowCount - 1);

            float topSpace = startRow * rowHeight;
            if (topSpace > 0f)
            {
                GUILayout.Space(topSpace);
            }

            for (int row = startRow; row <= endRow; row++)
            {
                int rowStartIndex = row * clampedColumns;
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < clampedColumns; c++)
                    {
                        int index = rowStartIndex + c;
                        if (index < _filteredBlocks.Count)
                        {
                            DrawBlockCard(index, _filteredBlocks[index], cellWidth);
                        }
                        else
                        {
                            GUILayout.Space(cellWidth);
                        }

                        if (c < clampedColumns - 1)
                        {
                            GUILayout.Space(gap);
                        }
                    }
                }

                GUILayout.Space(gap);
            }

            float bottomRows = Mathf.Max(0, rowCount - 1 - endRow);
            float bottomSpace = bottomRows * rowHeight;
            if (bottomSpace > 0f)
            {
                GUILayout.Space(bottomSpace);
            }
        }

        private void DrawBlockCard(int index, BlockDefinition block, float width)
        {
            string info = string.Empty;
            if (block.hasAnimation)
            {
                info = L("card.anim");
            }

            if (block.emitsLight)
            {
                info = string.IsNullOrEmpty(info) ? L("card.glow") : $"{info} | {L("card.glow")}";
            }
            if (block.transparent)
            {
                info = string.IsNullOrEmpty(info) ? L("card.transparent") : $"{info} | {L("card.transparent")}";
            }

            string fallbackTitle = string.IsNullOrWhiteSpace(block.displayName) ? block.id : block.displayName;
            string title = Box3BlocksI18n.GetBlockDisplayName(block.id, fallbackTitle);
            string categoryLabel = LocalizeCategoryLabel(block.category);
            string subtitle = string.IsNullOrWhiteSpace(info) ? categoryLabel : $"{categoryLabel} | {info}";
            Rect rect = GUILayoutUtility.GetRect(width, _cardBoxStyle.fixedHeight, GUILayout.Width(width), GUILayout.Height(_cardBoxStyle.fixedHeight));
            GUI.Box(rect, GUIContent.none, _cardBoxStyle);
            Rect rotateRect = DrawCardRotateButton(rect, block);
            bool isVisible = IsCardRectVisible(rect);

            if (!isVisible)
            {
                return;
            }

            Rect previewRect = new Rect(
                rect.x + (rect.width - PreviewSize) * 0.5f,
                rect.y + 10f,
                PreviewSize,
                PreviewSize);
            float extraYaw = GetCardPreviewExtraAngle(block);
            bool disposePreviewAfterDraw = false;
            Texture2D cardPreview = Mathf.Abs(extraYaw) > 0.01f
                ? BuildTransientCardPreview(block, extraYaw, out disposePreviewAfterDraw)
                : GetOrBuildBlockCardPreview(block);
            if (cardPreview != null)
            {
                GUI.DrawTexture(previewRect, cardPreview, ScaleMode.ScaleToFit, true);
            }

            if (disposePreviewAfterDraw && cardPreview != null)
            {
                DestroyImmediate(cardPreview);
            }

            Rect titleRect = new Rect(rect.x + 6f, previewRect.yMax + 6f, rect.width - 12f, 34f);
            Rect subtitleRect = new Rect(rect.x + 6f, previewRect.yMax + 36f, rect.width - 12f, rect.height - (PreviewSize + 46f));
            GUIStyle titleStyle = _selectedIndex == index ? _cardTitleSelectedStyle : _cardTitleStyle;
            GUIStyle subtitleStyle = _selectedIndex == index ? _cardSubtitleSelectedStyle : _cardSubtitleStyle;
            EditorGUI.LabelField(titleRect, title, titleStyle);
            EditorGUI.LabelField(subtitleRect, subtitle, subtitleStyle);

            if (_selectedIndex == index)
            {
                DrawSelectedCardFrame(rect);
            }

            if (block.emitsLight)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 6f, rect.yMax - 4f, rect.width - 12f, 2f), block.lightColor);
            }

            HandleCardClick(index, block, rect, rotateRect);
        }

        private void HandleCardClick(int index, BlockDefinition block, Rect cardRect, Rect rotateRect)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.MouseDown || e.button != 0)
            {
                return;
            }

            if (rotateRect.Contains(e.mousePosition))
            {
                if (block != null)
                {
                    block.placementRotationQuarter = (block.placementRotationQuarter + 3) & 3;
                    StartCardRotateAnimation(block);
                    Repaint();
                }
                e.Use();
                return;
            }

            if (cardRect.Contains(e.mousePosition))
            {
                _selectedIndex = index;
                Repaint();
                e.Use();
            }
        }

        private Rect DrawCardRotateButton(Rect cardRect, BlockDefinition block)
        {
            Rect buttonRect = new Rect(cardRect.xMax - 48f, cardRect.y + 6f, 42f, 18f);
            if (block == null)
            {
                GUI.Box(buttonRect, "R0", _rotateBadgeStyle);
                return buttonRect;
            }

            int degrees = (block.placementRotationQuarter & 3) * 90;
            string label = Lf("card.rotate_badge", degrees);
            GUI.Box(buttonRect, label, _rotateBadgeStyle);
            return buttonRect;
        }

        private static void DrawSelectedCardFrame(Rect rect)
        {
            Color fill = new Color(0.16f, 0.48f, 0.2f, 0.28f);
            Color border = new Color(0.15f, 1f, 0.25f, 1f);
            EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill);

            const float t = 3f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, t), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - t, rect.width, t), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, t, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.y, t, rect.height), border);

            // Rect badgeRect = new Rect(rect.x + 6f, rect.y + 6f, 62f, 16f);
            // EditorGUI.DrawRect(badgeRect, new Color(0.05f, 0.18f, 0.08f, 0.95f));
            // EditorGUI.LabelField(badgeRect, L("card.selected"), new GUIStyle(EditorStyles.miniLabel)
            // {
            //     alignment = TextAnchor.MiddleCenter,
            //     normal = { textColor = new Color(0.5f, 1f, 0.55f, 1f) },
            //     fontStyle = FontStyle.Bold
            // });
        }

        private int CalculateColumnCount(float availableWidth)
        {
            const float minCellWidth = 150f;
            return Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(1f, availableWidth) / minCellWidth));
        }

        private void DrawCategoryTabsSidebar()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle, GUILayout.Width(90f)))
            {
                _categoryScroll = EditorGUILayout.BeginScrollView(_categoryScroll, GUILayout.Width(80f), GUILayout.ExpandHeight(true));
                for (int i = 0; i < _categories.Count; i++)
                {
                    string categoryKey = _categories[i];
                    string label = LocalizeCategoryLabel(categoryKey);

                    bool selected = i == _selectedCategory;
                    GUIStyle style = selected ? _categoryTabSelectedStyle : _categoryTabStyle;
                    bool clicked = GUILayout.Toggle(selected, label, style, GUILayout.Width(74f), GUILayout.Height(24f));
                    if (clicked && !selected)
                    {
                        _selectedCategory = i;
                        ApplyFilter();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawToolTabs()
        {
            string[] labels = { L("tool.place"), L("tool.erase"), L("tool.replace"), L("tool.rotate") };
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    bool selected = (int)_tool == i;
                    GUIStyle style = selected ? _toolTabSelectedStyle : _toolTabStyle;
                    if (GUILayout.Toggle(selected, labels[i], style, GUILayout.Height(26f)))
                    {
                        _tool = (EditTool)i;
                    }
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            HandleToolHotkeys(Event.current);

            if (_root == null)
            {
                return;
            }

            bool needsSelectedBlock = _tool == EditTool.Place || _tool == EditTool.Replace;
            if (needsSelectedBlock && (_selectedIndex < 0 || _selectedIndex >= _filteredBlocks.Count))
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (!TryGetTargetPosition(e.mousePosition, out Vector3Int target, out Box3BlocksPlacedBlock hitBlock))
            {
                return;
            }

            DrawScenePreview(target);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (_tool == EditTool.Place)
                {
                    PlaceBlockBrush(target);
                }
                else if (_tool == EditTool.Replace)
                {
                    ReplaceBlockBrush(hitBlock, target);
                }
                else if (_tool == EditTool.Rotate)
                {
                    RotateBlockBrush(hitBlock, target);
                }
                else
                {
                    EraseBlockBrush(hitBlock, target);
                }

                e.Use();
            }
        }

        private void HandleToolHotkeys(Event e)
        {
            if (e == null || e.type != EventType.KeyDown || !e.shift)
            {
                return;
            }

            EditTool? nextTool = null;
            switch (e.keyCode)
            {
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    nextTool = EditTool.Place;
                    break;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    nextTool = EditTool.Erase;
                    break;
                case KeyCode.Alpha3:
                case KeyCode.Keypad3:
                    nextTool = EditTool.Replace;
                    break;
                case KeyCode.Alpha4:
                case KeyCode.Keypad4:
                    nextTool = EditTool.Rotate;
                    break;
            }

            if (!nextTool.HasValue || _tool == nextTool.Value)
            {
                return;
            }

            _tool = nextTool.Value;
            Repaint();
            SceneView.RepaintAll();
            e.Use();
        }

        private bool TryGetTargetPosition(Vector2 mousePosition, out Vector3Int target, out Box3BlocksPlacedBlock hitBlock)
        {
            target = default;
            hitBlock = null;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                hitBlock = hit.collider.GetComponentInParent<Box3BlocksPlacedBlock>();
                if (hitBlock != null)
                {
                    Vector3Int blockPos = Vector3Int.RoundToInt(hitBlock.transform.position);
                    if (_tool == EditTool.Erase || _tool == EditTool.Replace || _tool == EditTool.Rotate)
                    {
                        target = blockPos;
                        return true;
                    }

                    Vector3Int normal = Vector3Int.RoundToInt(hit.normal);
                    if (normal == Vector3Int.zero)
                    {
                        normal = Vector3Int.up;
                    }

                    target = blockPos + normal;
                    return true;
                }
            }

            if (_tool == EditTool.Erase || _tool == EditTool.Replace || _tool == EditTool.Rotate)
            {
                if (TryFindClosestBlockFromScreen(mousePosition, out Box3BlocksPlacedBlock closest))
                {
                    hitBlock = closest;
                    target = Vector3Int.RoundToInt(closest.transform.position);
                    return true;
                }

                return false;
            }

            const int fallbackPlaneY = 0;
            Plane plane = new Plane(Vector3.up, new Vector3(0f, fallbackPlaneY, 0f));
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            Vector3 point = ray.GetPoint(distance);
            target = new Vector3Int(Mathf.RoundToInt(point.x), fallbackPlaneY, Mathf.RoundToInt(point.z));
            return true;
        }

        private bool TryFindClosestBlockFromScreen(Vector2 mousePosition, out Box3BlocksPlacedBlock block)
        {
            block = null;
            if (_root == null)
            {
                return false;
            }

            const float maxPixelDistance = 36f;
            float bestSq = maxPixelDistance * maxPixelDistance;
            for (int i = 0; i < _root.childCount; i++)
            {
                Transform child = _root.GetChild(i);
                Box3BlocksPlacedBlock candidate = child.GetComponent<Box3BlocksPlacedBlock>();
                if (candidate == null)
                {
                    continue;
                }

                Vector2 gui = HandleUtility.WorldToGUIPoint(candidate.transform.position);
                float sq = (gui - mousePosition).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    block = candidate;
                }
            }

            return block != null;
        }

        private void DrawScenePreview(Vector3Int target)
        {
            Handles.color = _tool == EditTool.Place
                ? Color.green
                : (_tool == EditTool.Replace ? Color.yellow : (_tool == EditTool.Rotate ? Color.cyan : Color.red));
            List<Vector3Int> positions = BuildBrushPositions(target);
            for (int i = 0; i < positions.Count; i++)
            {
                Handles.DrawWireCube(positions[i], Vector3.one);
            }
        }

        private void PlaceBlockBrush(Vector3Int origin)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredBlocks.Count)
            {
                return;
            }

            BlockDefinition definition = _filteredBlocks[_selectedIndex];
            bool placedAny = false;
            List<Vector3Int> positions = BuildBrushPositions(origin);
            for (int i = 0; i < positions.Count; i++)
            {
                if (TryPlaceSingleBlock(definition, positions[i]))
                {
                    placedAny = true;
                }
            }

            if (placedAny)
            {
                RegisterRecentPlaced(definition.id);
            }
        }

        private bool TryPlaceSingleBlock(BlockDefinition definition, Vector3Int position)
        {
            if (FindBlockAt(position) != null)
            {
                return false;
            }

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(definition.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData))
            {
                return false;
            }

            bool hasAnimatedFaces = HasAnimatedFaces(definition);
            Mesh meshToUse = hasAnimatedFaces
                ? Box3BlocksAssetFactory.GetOrCreateCubeMesh()
                : GetOrCreateStaticBlockMesh(definition, renderData);
            if (meshToUse == null || renderData == null || renderData.materials == null)
            {
                return false;
            }

            GameObject go = new GameObject($"{definition.id}_{position.x}_{position.y}_{position.z}");
            Undo.RegisterCreatedObjectUndo(go, "Place Block");

            go.transform.SetParent(_root);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, (definition.placementRotationQuarter & 3) * 90f, 0f);

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = meshToUse;

            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            if (hasAnimatedFaces)
            {
                ConfigureRendererMaterials(meshRenderer, renderData, definition);
            }
            else
            {
                if (definition.transparent)
                {
                    meshRenderer.sharedMaterial = renderData.materials[0];
                }
                else
                {
                    meshRenderer.sharedMaterial = GetOrCreateOpaqueAtlasMaterial(renderData.materials[0]);
                }
            }

            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshToUse;

            Box3BlocksPlacedBlock marker = go.AddComponent<Box3BlocksPlacedBlock>();
            marker.BlockId = definition.id;
            marker.HasAnimation = hasAnimatedFaces;
            RegisterBlockInLookup(position, go);
            ApplyEmissionForDefinition(meshRenderer, definition);
            ConfigureRealtimeLight(go.transform, definition, _spawnPointLightForEmissive);

            RefreshTransparentAround(position);
            RefreshOcclusionAround(position);
            EditorUtility.SetDirty(go);
            return true;
        }

        private void EraseBlockBrush(Box3BlocksPlacedBlock hitBlock, Vector3Int fallbackPosition)
        {
            Vector3Int origin = hitBlock != null ? Vector3Int.RoundToInt(hitBlock.transform.position) : fallbackPosition;
            List<Vector3Int> positions = BuildBrushPositions(origin);
            HashSet<Vector3Int> refresh = new HashSet<Vector3Int>();
            for (int i = 0; i < positions.Count; i++)
            {
                GameObject target = FindBlockAt(positions[i]);
                if (target == null)
                {
                    continue;
                }

                UnregisterBlockInLookup(positions[i], target);
                Undo.DestroyObjectImmediate(target);
                refresh.Add(positions[i]);
            }

            foreach (Vector3Int pos in refresh)
            {
                RefreshTransparentAround(pos);
                RefreshOcclusionAround(pos);
            }
        }

        private void ReplaceBlockBrush(Box3BlocksPlacedBlock hitBlock, Vector3Int fallbackPosition)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredBlocks.Count)
            {
                return;
            }

            BlockDefinition replacement = _filteredBlocks[_selectedIndex];
            Vector3Int origin = hitBlock != null ? Vector3Int.RoundToInt(hitBlock.transform.position) : fallbackPosition;
            List<Vector3Int> positions = BuildBrushPositions(origin);
            bool replacedAny = false;
            HashSet<Vector3Int> refresh = new HashSet<Vector3Int>();
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3Int pos = positions[i];
                GameObject target = FindBlockAt(pos);
                if (target == null)
                {
                    continue;
                }

                Box3BlocksPlacedBlock existing = target.GetComponent<Box3BlocksPlacedBlock>();
                if (existing != null && string.Equals(existing.BlockId, replacement.id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                UnregisterBlockInLookup(pos, target);
                Undo.DestroyObjectImmediate(target);
                if (TryPlaceSingleBlock(replacement, pos))
                {
                    replacedAny = true;
                    refresh.Add(pos);
                }
            }

            if (replacedAny)
            {
                RegisterRecentPlaced(replacement.id);
            }

            foreach (Vector3Int pos in refresh)
            {
                RefreshTransparentAround(pos);
                RefreshOcclusionAround(pos);
            }
        }

        private void RotateBlockBrush(Box3BlocksPlacedBlock hitBlock, Vector3Int fallbackPosition)
        {
            Vector3Int origin = hitBlock != null ? Vector3Int.RoundToInt(hitBlock.transform.position) : fallbackPosition;
            List<Vector3Int> positions = BuildBrushPositions(origin);
            for (int i = 0; i < positions.Count; i++)
            {
                GameObject target = FindBlockAt(positions[i]);
                if (target == null)
                {
                    continue;
                }

                Undo.RecordObject(target.transform, "Rotate Block 90");
                target.transform.Rotate(0f, 90f, 0f, Space.World);
                EditorUtility.SetDirty(target.transform);
                UpdateTransparentBlockMesh(target);
            }
        }

        private GameObject FindBlockAt(Vector3Int position)
        {
            EnsureBlockLookup();
            if (_blockLookup.TryGetValue(position, out GameObject cached) && cached != null)
            {
                return cached;
            }

            return null;
        }

        private void RefreshOcclusionAround(Vector3Int center)
        {
            UpdateOcclusionAt(center);
            for (int i = 0; i < OcclusionNeighborOffsets.Length; i++)
            {
                UpdateOcclusionAt(center + OcclusionNeighborOffsets[i]);
            }
        }

        private void UpdateOcclusionAt(Vector3Int position)
        {
            GameObject go = FindBlockAt(position);
            if (go == null)
            {
                return;
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            bool shouldRender = !IsBlockFullyOccluded(position);
            if (renderer.enabled != shouldRender)
            {
                renderer.enabled = shouldRender;
                EditorUtility.SetDirty(renderer);
            }
        }

        private bool IsBlockFullyOccluded(Vector3Int position)
        {
            for (int i = 0; i < OcclusionNeighborOffsets.Length; i++)
            {
                Vector3Int neighborPos = position + OcclusionNeighborOffsets[i];
                if (FindBlockAt(neighborPos) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private List<Vector3Int> BuildBrushPositions(Vector3Int origin)
        {
            int size = Mathf.Max(1, _brushHorizontalSize);
            int height = Mathf.Max(1, _brushHeight);
            List<Vector3Int> positions = new List<Vector3Int>(size * size * height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        positions.Add(new Vector3Int(origin.x + x, origin.y + y, origin.z + z));
                    }
                }
            }

            return positions;
        }

        private void EnsureBlockLookup()
        {
            if (_root == null)
            {
                if (_blockLookup.Count > 0)
                {
                    _blockLookup.Clear();
                }

                _blockLookupRoot = null;
                _blockLookupDirty = true;
                return;
            }

            if (!_blockLookupDirty && _blockLookupRoot == _root)
            {
                return;
            }

            _blockLookup.Clear();
            _blockLookupRoot = _root;
            for (int i = 0; i < _root.childCount; i++)
            {
                Transform child = _root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Box3BlocksPlacedBlock marker = child.GetComponent<Box3BlocksPlacedBlock>();
                if (marker == null)
                {
                    continue;
                }

                _blockLookup[Vector3Int.RoundToInt(child.position)] = child.gameObject;
            }

            _blockLookupDirty = false;
        }

        private void RegisterBlockInLookup(Vector3Int position, GameObject go)
        {
            if (_root == null || go == null)
            {
                return;
            }

            EnsureBlockLookup();
            _blockLookup[position] = go;
        }

        private void UnregisterBlockInLookup(Vector3Int position, GameObject go)
        {
            EnsureBlockLookup();
            if (_blockLookup.TryGetValue(position, out GameObject existing) && (go == null || existing == go))
            {
                _blockLookup.Remove(position);
            }
        }

        private BlockDefinition FindDefinitionById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < _allBlocks.Count; i++)
            {
                BlockDefinition def = _allBlocks[i];
                if (def != null && string.Equals(def.id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }

            return null;
        }

        private void CreateRoot()
        {
            GameObject go = new GameObject("BlockWorldRoot");
            Undo.RegisterCreatedObjectUndo(go, "Create Block Root");
            _root = go.transform;
            _blockLookupDirty = true;
            Selection.activeObject = go;
        }

        private void ClearRoot()
        {
            if (_root == null)
            {
                return;
            }

            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(_root.GetChild(i).gameObject);
            }

            _blockLookupDirty = true;
        }

        private List<Box3BlocksPlacedBlock> CollectPlacedBlocksFromRoot()
        {
            List<Box3BlocksPlacedBlock> list = new List<Box3BlocksPlacedBlock>();
            if (_root == null)
            {
                return list;
            }

            for (int i = 0; i < _root.childCount; i++)
            {
                Transform child = _root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Box3BlocksPlacedBlock placed = child.GetComponent<Box3BlocksPlacedBlock>();
                if (placed != null)
                {
                    list.Add(placed);
                }
            }

            return list;
        }

        private Dictionary<string, BlockDefinition> BuildBlockMap()
        {
            Dictionary<string, BlockDefinition> map = new Dictionary<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _allBlocks.Count; i++)
            {
                BlockDefinition def = _allBlocks[i];
                if (def != null && !string.IsNullOrWhiteSpace(def.id))
                {
                    map[def.id] = def;
                }
            }

            return map;
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

        private static Material GetOrCreateOpaqueAtlasMaterial(Material atlasSource)
        {
            if (atlasSource == null)
            {
                return null;
            }

            EnsureAssetFolderPath(GeneratedMaterialFolder);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(VoxelImportChunkOpaqueMaterialPath);
            if (existing != null)
            {
                Texture atlasMain = atlasSource.mainTexture;
                if (atlasMain != null && existing.mainTexture != atlasMain)
                {
                    existing.mainTexture = atlasMain;
                    EditorUtility.SetDirty(existing);
                }

                ApplyMapsToOpaque(existing);
                return existing;
            }

            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                Material fallback = new Material(atlasSource) { name = "M_Block" };
                fallback.renderQueue = (int)RenderQueue.Geometry;
                fallback.SetInt("_ZWrite", 1);
                ApplyMapsToOpaque(fallback);
                AssetDatabase.CreateAsset(fallback, VoxelImportChunkOpaqueMaterialPath);
                EditorUtility.SetDirty(fallback);
                return fallback;
            }

            Material material = new Material(shader) { name = "M_Block" };
            if (atlasSource.mainTexture != null)
            {
                material.mainTexture = atlasSource.mainTexture;
            }
            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            ApplyMapsToOpaque(material);
            AssetDatabase.CreateAsset(material, VoxelImportChunkOpaqueMaterialPath);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyMapsToOpaque(Material material)
        {
            Box3BlocksOpaqueMaterialConfigurator.Apply(material);
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

        private int CleanupUnusedGeneratedMaterials()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedMaterialFolder))
            {
                return 0;
            }

            HashSet<string> used = CollectUsedGeneratedMaterialPaths();
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { GeneratedMaterialFolder });
            int deleted = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrWhiteSpace(path) || used.Contains(path))
                {
                    continue;
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                AssetDatabase.Refresh();
            }

            return deleted;
        }

        private static HashSet<string> CollectUsedGeneratedMaterialPaths()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Renderer[] sceneRenderers = FindObjectsOfType<Renderer>(true);
            for (int i = 0; i < sceneRenderers.Length; i++)
            {
                Material[] mats = sceneRenderers[i].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(mat);
                    if (!string.IsNullOrWhiteSpace(path) && path.StartsWith(GeneratedMaterialFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        used.Add(path);
                    }
                }
            }

            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allAssetPaths.Length; i++)
            {
                string assetPath = allAssetPaths[i];
                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (assetPath.StartsWith(GeneratedMaterialFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!(assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    || assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    || assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string[] deps = AssetDatabase.GetDependencies(assetPath, true);
                for (int d = 0; d < deps.Length; d++)
                {
                    string dep = deps[d];
                    if (dep.StartsWith(GeneratedMaterialFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        used.Add(dep);
                    }
                }
            }

            return used;
        }

        private void ReloadBlockLibrary()
        {
            EnforceCrispImportForAllBlockTextures();
            Box3BlocksAssetFactory.InvalidateCaches();
            _staticBlockMeshCache.Clear();
            ClearBlockCardPreviewCache();
            _allBlocks.Clear();
            Dictionary<string, BlockMetadata> metadataMap = LoadBlockMetadata();

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlockTextureFolder });
            Dictionary<string, BlockDefinition> map = new Dictionary<string, BlockDefinition>();

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileName(assetPath);
                Match match = SideRegex.Match(fileName);
                if (!match.Success)
                {
                    continue;
                }

                string id = match.Groups[1].Value;
                string side = match.Groups[2].Value;
                if (!map.TryGetValue(id, out BlockDefinition definition))
                {
                    definition = new BlockDefinition { id = id };
                    map.Add(id, definition);
                }

                definition.sideTexturePaths[side] = assetPath;
                if (File.Exists(GetProjectAbsolutePath(assetPath + ".mcmeta")))
                {
                    definition.hasAnimation = true;
                    if (TryParseFaceAnimation(assetPath, out FaceAnimationSpec animationSpec))
                    {
                        definition.sideAnimations[side] = animationSpec;
                    }
                }
            }

            foreach (KeyValuePair<string, BlockDefinition> pair in map)
            {
                BlockDefinition def = pair.Value;
                def.previewTexture = ResolvePreviewTexture(def);
                def.displayName = def.id;
                if (metadataMap.TryGetValue(def.id, out BlockMetadata metadata))
                {
                    def.numericId = metadata.numericId;
                    def.category = string.IsNullOrWhiteSpace(metadata.category) ? InferCategory(def.id) : metadata.category;
                    def.emitsLight = metadata.emitsLight;
                    def.transparent = metadata.transparent;
                    def.lightColor = metadata.lightColor;
                }
                else
                {
                    def.category = InferCategory(def.id);
                    def.emitsLight = HasLightKeyword(def.id);
                    def.transparent = IsTransparencyKeyword(def.id);
                }

                _allBlocks.Add(def);
            }

            _allBlocks.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            BuildCategories();
            ApplyFilter();
        }

        private static void EnforceCrispImportForAllBlockTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlockTextureFolder });
            bool changedAny = false;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (ApplyCrispTextureImportSettings(importer))
                {
                    importer.SaveAndReimport();
                    changedAny = true;
                }
            }

            if (changedAny)
            {
                AssetDatabase.Refresh();
            }
        }

        private static bool ApplyCrispTextureImportSettings(TextureImporter importer)
        {
            bool changed = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.streamingMipmaps)
            {
                importer.streamingMipmaps = false;
                changed = true;
            }

            if (importer.anisoLevel != 0)
            {
                importer.anisoLevel = 0;
                changed = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }

            // Remove platform overrides that can force compression back on.
            if (ClearPlatformOverride(importer, "Standalone"))
            {
                changed = true;
            }
            if (ClearPlatformOverride(importer, "Android"))
            {
                changed = true;
            }
            if (ClearPlatformOverride(importer, "iPhone"))
            {
                changed = true;
            }

            return changed;
        }

        private static bool ClearPlatformOverride(TextureImporter importer, string platform)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            if (!settings.overridden)
            {
                return false;
            }

            settings.overridden = false;
            importer.SetPlatformTextureSettings(settings);
            return true;
        }

        private void ApplyFilter()
        {
            _filteredBlocks.Clear();
            _filteredHasAnimatedBlocks = false;
            string selectedCategory = _categories[Mathf.Clamp(_selectedCategory, 0, _categories.Count - 1)];
            IEnumerable<BlockDefinition> source = string.Equals(selectedCategory, CategoryRecent, StringComparison.OrdinalIgnoreCase)
                ? EnumerateRecentBlocks()
                : _allBlocks;

            foreach (BlockDefinition block in source)
            {
                bool matchSearch = string.IsNullOrWhiteSpace(_search)
                    || block.id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (!string.IsNullOrWhiteSpace(block.displayName) && block.displayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
                bool matchCategory = selectedCategory == CategoryAll
                    || string.Equals(selectedCategory, CategoryRecent, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(block.category, selectedCategory, StringComparison.OrdinalIgnoreCase);
                if (matchSearch && matchCategory)
                {
                    _filteredBlocks.Add(block);
                    if (!_filteredHasAnimatedBlocks && HasAnimatedFaces(block))
                    {
                        _filteredHasAnimatedBlocks = true;
                    }
                }
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _filteredBlocks.Count - 1));
        }

        private static void ConfigureRendererMaterials(MeshRenderer renderer, Box3BlocksAssetFactory.FaceRenderData renderData, BlockDefinition definition)
        {
            if (renderer == null || renderData == null || renderData.materials == null || renderData.faceMainTexSt == null)
            {
                return;
            }

            bool isTransparent = definition != null && definition.transparent;
            if (!isTransparent)
            {
                Material opaque = GetOrCreateOpaqueAtlasMaterial(renderData.materials[0]);
                if (opaque != null)
                {
                    Material[] shared = new Material[renderData.materials.Length];
                    for (int i = 0; i < shared.Length; i++)
                    {
                        shared[i] = opaque;
                    }

                    renderer.sharedMaterials = shared;
                }
                else
                {
                    renderer.sharedMaterials = renderData.materials;
                }
            }
            else
            {
                renderer.sharedMaterials = renderData.materials;
            }

            if (!definition.hasAnimation || definition.sideAnimations.Count == 0)
            {
                ApplyFaceMainTexSt(renderer, renderData.faceMainTexSt);
                Box3BlocksTextureAnimator existingAnimator = renderer.GetComponent<Box3BlocksTextureAnimator>();
                if (existingAnimator != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(existingAnimator);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(existingAnimator);
                    }
                }
                return;
            }

            List<Box3BlocksTextureAnimator.FaceAnimation> runtimeAnimations = new List<Box3BlocksTextureAnimator.FaceAnimation>();
            for (int i = 0; i < SideOrder.Length && i < renderData.faceMainTexSt.Length; i++)
            {
                string side = SideOrder[i];
                if (!definition.sideAnimations.TryGetValue(side, out FaceAnimationSpec spec))
                {
                    continue;
                }

                if (spec == null || spec.frameCount <= 1)
                {
                    continue;
                }

                runtimeAnimations.Add(new Box3BlocksTextureAnimator.FaceAnimation
                {
                    materialIndex = i,
                    frameCount = spec.frameCount,
                    frameDuration = Mathf.Max(0.01f, spec.frameDuration),
                    frames = spec.frames,
                    baseMainTexSt = renderData.faceMainTexSt[i]
                });
            }

            if (runtimeAnimations.Count == 0)
            {
                ApplyFaceMainTexSt(renderer, renderData.faceMainTexSt);
                Box3BlocksTextureAnimator existingAnimator = renderer.GetComponent<Box3BlocksTextureAnimator>();
                if (existingAnimator != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(existingAnimator);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(existingAnimator);
                    }
                }
                return;
            }

            Box3BlocksTextureAnimator animator = renderer.GetComponent<Box3BlocksTextureAnimator>();
            if (animator == null)
            {
                animator = renderer.gameObject.AddComponent<Box3BlocksTextureAnimator>();
            }

            animator.SetAnimations(runtimeAnimations.ToArray(), renderData.faceMainTexSt);
            ApplyEmissionForDefinition(renderer, definition);
        }

        private static bool HasAnimatedFaces(BlockDefinition definition)
        {
            if (definition == null || !definition.hasAnimation || definition.sideAnimations == null || definition.sideAnimations.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < SideOrder.Length; i++)
            {
                string side = SideOrder[i];
                if (!definition.sideAnimations.TryGetValue(side, out FaceAnimationSpec spec))
                {
                    continue;
                }

                if (spec != null && spec.frameCount > 1)
                {
                    return true;
                }
            }

            return false;
        }

        private Mesh GetOrCreateStaticBlockMesh(BlockDefinition definition, Box3BlocksAssetFactory.FaceRenderData renderData)
        {
            if (definition == null || renderData == null || renderData.faceMainTexSt == null)
            {
                return null;
            }

            if (_staticBlockMeshCache.TryGetValue(definition.id, out Mesh cached) && cached != null)
            {
                return cached;
            }

            Mesh mesh = BuildStaticBlockMesh(definition.id, renderData.faceMainTexSt);
            _staticBlockMeshCache[definition.id] = mesh;
            return mesh;
        }

        private static Mesh BuildStaticBlockMesh(string id, Vector4[] faceMainTexSt)
        {
            if (faceMainTexSt == null || faceMainTexSt.Length < SideOrder.Length)
            {
                return null;
            }

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

            Mesh mesh = new Mesh
            {
                name = $"StaticBlock_{id}"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Texture2D GetOrBuildBlockCardPreview(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return null;
            }

            string cacheKey = BuildCardPreviewCacheKey(block);

            if (HasAnimatedFaces(block))
            {
                return GetOrBuildAnimatedBlockCardPreview(block);
            }

            if (_blockCardPreviewCache.TryGetValue(cacheKey, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(block.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData)
                || renderData == null
                || renderData.materials == null
                || renderData.materials.Length == 0
                || renderData.materials[0] == null)
            {
                return block.previewTexture;
            }

            Mesh mesh = GetOrCreateStaticBlockMesh(block, renderData);
            if (mesh == null)
            {
                return block.previewTexture;
            }

            Texture2D preview = RenderBlockCardPreview(mesh, renderData.materials[0], block.placementRotationQuarter);
            if (preview != null)
            {
                _blockCardPreviewCache[cacheKey] = preview;
                return preview;
            }

            return block.previewTexture;
        }

        private Texture2D BuildTransientCardPreview(BlockDefinition block, float extraYawDeg, out bool createdTemporaryTexture)
        {
            createdTemporaryTexture = false;
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return null;
            }

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(block.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData)
                || renderData == null
                || renderData.materials == null
                || renderData.materials.Length == 0
                || renderData.materials[0] == null)
            {
                return block.previewTexture;
            }

            Mesh mesh = null;
            bool destroyMesh = false;
            if (HasAnimatedFaces(block))
            {
                if (renderData.faceMainTexSt == null || renderData.faceMainTexSt.Length < SideOrder.Length)
                {
                    return block.previewTexture;
                }

                float now = GetQuantizedAnimatedPreviewTime();
                mesh = BuildAnimatedPreviewMesh(block.id, renderData.faceMainTexSt, block.sideAnimations, now);
                destroyMesh = mesh != null;
            }
            else
            {
                mesh = GetOrCreateStaticBlockMesh(block, renderData);
            }

            if (mesh == null)
            {
                return block.previewTexture;
            }

            Texture2D preview = RenderBlockCardPreview(mesh, renderData.materials[0], block.placementRotationQuarter, extraYawDeg);
            if (destroyMesh)
            {
                DestroyImmediate(mesh);
            }

            if (preview == null)
            {
                return block.previewTexture;
            }

            createdTemporaryTexture = true;
            return preview;
        }

        private Texture2D GetOrBuildAnimatedBlockCardPreview(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return null;
            }

            if (!Box3BlocksAssetFactory.TryGetFaceRenderData(block.sideTexturePaths, out Box3BlocksAssetFactory.FaceRenderData renderData)
                || renderData == null
                || renderData.materials == null
                || renderData.materials.Length == 0
                || renderData.materials[0] == null
                || renderData.faceMainTexSt == null
                || renderData.faceMainTexSt.Length < SideOrder.Length)
            {
                return block.previewTexture;
            }

            if (IsBlockListScrolling())
            {
                if (_animatedBlockCardPreviewCache.TryGetValue(block.id, out AnimatedPreviewCacheEntry cachedEntry)
                    && cachedEntry != null
                    && cachedEntry.texture != null)
                {
                    return cachedEntry.texture;
                }

                return block.previewTexture;
            }

            float now = GetQuantizedAnimatedPreviewTime();
            int signature = ComputeAnimatedPreviewSignature(block, now);
            if (_animatedBlockCardPreviewCache.TryGetValue(block.id, out AnimatedPreviewCacheEntry entry)
                && entry != null
                && entry.signature == signature
                && entry.texture != null)
            {
                return entry.texture;
            }

            Mesh animatedMesh = BuildAnimatedPreviewMesh(block.id, renderData.faceMainTexSt, block.sideAnimations, now);
            if (animatedMesh == null)
            {
                return block.previewTexture;
            }

            Texture2D nextTexture = RenderBlockCardPreview(animatedMesh, renderData.materials[0], block.placementRotationQuarter);
            DestroyImmediate(animatedMesh);
            if (nextTexture == null)
            {
                return block.previewTexture;
            }

            if (entry == null)
            {
                entry = new AnimatedPreviewCacheEntry();
                _animatedBlockCardPreviewCache[block.id] = entry;
            }
            else if (entry.texture != null)
            {
                DestroyImmediate(entry.texture);
            }

            entry.signature = signature;
            entry.texture = nextTexture;
            return nextTexture;
        }

        private static int ComputeAnimatedPreviewSignature(BlockDefinition block, float timeNow)
        {
            if (block == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < SideOrder.Length; i++)
                {
                    int frame = ResolveAnimationFrameIndexForSide(block, SideOrder[i], timeNow);
                    hash = hash * 31 + frame;
                }

                hash = hash * 31 + (block.placementRotationQuarter & 3);

                return hash;
            }
        }

        private static string BuildCardPreviewCacheKey(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return string.Empty;
            }

            return $"{block.id}#{block.placementRotationQuarter & 3}";
        }

        private static int ResolveAnimationFrameIndexForSide(BlockDefinition block, string side, float timeNow)
        {
            if (block == null
                || block.sideAnimations == null
                || !block.sideAnimations.TryGetValue(side, out FaceAnimationSpec spec)
                || spec == null
                || spec.frameCount <= 1)
            {
                return 0;
            }

            return ResolveAnimationFrameIndex(spec, timeNow);
        }

        private static int ResolveAnimationFrameIndex(FaceAnimationSpec spec, float timeNow)
        {
            if (spec == null || spec.frameCount <= 1)
            {
                return 0;
            }

            int[] sequence = spec.frames;
            int sequenceLength = sequence != null && sequence.Length > 0 ? sequence.Length : spec.frameCount;
            float frameDuration = Mathf.Max(0.01f, spec.frameDuration);
            int step = Mathf.FloorToInt(Mathf.Max(0f, timeNow) / frameDuration) % sequenceLength;
            int frameIndex = sequence != null && sequence.Length > 0 ? sequence[step] : step;
            return Mathf.Clamp(frameIndex, 0, spec.frameCount - 1);
        }

        private static Mesh BuildAnimatedPreviewMesh(string id, Vector4[] baseFaceSt, Dictionary<string, FaceAnimationSpec> sideAnimations, float timeNow)
        {
            if (baseFaceSt == null || baseFaceSt.Length < SideOrder.Length)
            {
                return null;
            }

            Vector4[] animatedFaceSt = new Vector4[SideOrder.Length];
            for (int i = 0; i < SideOrder.Length; i++)
            {
                Vector4 st = baseFaceSt[i];
                string side = SideOrder[i];
                if (sideAnimations != null
                    && sideAnimations.TryGetValue(side, out FaceAnimationSpec spec)
                    && spec != null
                    && spec.frameCount > 1)
                {
                    int frameIndex = ResolveAnimationFrameIndex(spec, timeNow);
                    float scaleY = st.y / spec.frameCount;
                    float offsetY = st.w + st.y - (frameIndex + 1f) * scaleY;
                    st = new Vector4(st.x, scaleY, st.z, offsetY);
                }

                animatedFaceSt[i] = st;
            }

            return BuildStaticBlockMesh(id + "_animPreview", animatedFaceSt);
        }

        private Texture2D RenderBlockCardPreview(Mesh mesh, Material material, int rotationQuarter, float extraYawDeg = 0f)
        {
            if (mesh == null || material == null)
            {
                return null;
            }

            if (_blockCardPreviewUtility == null)
            {
                _blockCardPreviewUtility = new PreviewRenderUtility();
                _blockCardPreviewUtility.cameraFieldOfView = 22f;
            }

            Texture2D onBlack = RenderPreviewWithBackground(mesh, material, new Color(0f, 0f, 0f, 1f), rotationQuarter, extraYawDeg);
            Texture2D onWhite = RenderPreviewWithBackground(mesh, material, new Color(1f, 1f, 1f, 1f), rotationQuarter, extraYawDeg);
            if (onBlack == null || onWhite == null)
            {
                if (onBlack != null)
                {
                    DestroyImmediate(onBlack);
                }

                if (onWhite != null)
                {
                    DestroyImmediate(onWhite);
                }

                return onBlack ?? onWhite;
            }

            Texture2D composed = ComposeTransparentPreview(onBlack, onWhite);
            DestroyImmediate(onBlack);
            DestroyImmediate(onWhite);
            return composed;
        }

        private Texture2D RenderPreviewWithBackground(Mesh mesh, Material material, Color backgroundColor, int rotationQuarter, float extraYawDeg = 0f)
        {
            const int size = 128;
            Rect r = new Rect(0f, 0f, size, size);
            _blockCardPreviewUtility.BeginStaticPreview(r);

            Camera cam = _blockCardPreviewUtility.camera;
            cam.clearFlags = CameraClearFlags.Color;
            cam.backgroundColor = backgroundColor;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;

            _blockCardPreviewUtility.lights[0].intensity = 1.2f;
            _blockCardPreviewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 35f, 0f);
            _blockCardPreviewUtility.lights[1].intensity = 0.9f;
            _blockCardPreviewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 220f, 0f);
            _blockCardPreviewUtility.ambientColor = new Color(0.55f, 0.55f, 0.55f, 1f);

            Bounds b = mesh.bounds;
            float radius = Mathf.Max(0.5f, b.extents.magnitude);
            Quaternion viewRot = Quaternion.Euler(22f, -30f, 0f);
            Vector3 target = b.center;
            Vector3 camDir = viewRot * new Vector3(0f, 0f, 1.1f);
            cam.transform.position = target - camDir * radius * 4.6f;
            cam.transform.rotation = viewRot;
            cam.transform.LookAt(target);

            Quaternion modelRot = Quaternion.Euler(0f, ((rotationQuarter & 3) * 90f) + extraYawDeg, 0f);
            _blockCardPreviewUtility.DrawMesh(mesh, Matrix4x4.TRS(Vector3.zero, modelRot, Vector3.one), material, 0);
            cam.Render();
            return _blockCardPreviewUtility.EndStaticPreview();
        }

        private static Texture2D ComposeTransparentPreview(Texture2D onBlack, Texture2D onWhite)
        {
            if (onBlack == null || onWhite == null || onBlack.width != onWhite.width || onBlack.height != onWhite.height)
            {
                return onBlack ?? onWhite;
            }

            int w = onBlack.width;
            int h = onBlack.height;
            Color32[] bPixels = onBlack.GetPixels32();
            Color32[] wPixels = onWhite.GetPixels32();
            Color32[] outPixels = new Color32[bPixels.Length];

            for (int i = 0; i < bPixels.Length; i++)
            {
                Color32 cb = bPixels[i];
                Color32 cw = wPixels[i];

                float cbR = cb.r / 255f;
                float cbG = cb.g / 255f;
                float cbB = cb.b / 255f;
                float cwR = cw.r / 255f;
                float cwG = cw.g / 255f;
                float cwB = cw.b / 255f;

                // Derived from:
                // Cblack = A * F
                // Cwhite = A * F + (1 - A)
                float aR = 1f - (cwR - cbR);
                float aG = 1f - (cwG - cbG);
                float aB = 1f - (cwB - cbB);
                float alpha = Mathf.Clamp01((aR + aG + aB) / 3f);

                float outR = 0f;
                float outG = 0f;
                float outB = 0f;
                if (alpha > 1e-5f)
                {
                    outR = Mathf.Clamp01(cbR / alpha);
                    outG = Mathf.Clamp01(cbG / alpha);
                    outB = Mathf.Clamp01(cbB / alpha);
                }

                outPixels[i] = new Color(outR, outG, outB, alpha);
            }

            Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            outTex.SetPixels32(outPixels);
            outTex.Apply(false, false);
            return outTex;
        }

        private void ClearBlockCardPreviewCache()
        {
            foreach (KeyValuePair<string, Texture2D> kv in _blockCardPreviewCache)
            {
                if (kv.Value != null)
                {
                    DestroyImmediate(kv.Value);
                }
            }

            _blockCardPreviewCache.Clear();

            foreach (KeyValuePair<string, AnimatedPreviewCacheEntry> kv in _animatedBlockCardPreviewCache)
            {
                if (kv.Value != null && kv.Value.texture != null)
                {
                    DestroyImmediate(kv.Value.texture);
                }
            }

            _animatedBlockCardPreviewCache.Clear();
        }

        private void RequestAnimatedCardPreviewRepaint()
        {
            if (!_filteredHasAnimatedBlocks || IsBlockListScrolling() || !hasFocus)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextAnimatedPreviewRepaintTime)
            {
                return;
            }

            _nextAnimatedPreviewRepaintTime = now + (1.0 / AnimatedCardPreviewFps);
            Repaint();
        }

        private bool IsBlockListScrolling()
        {
            return EditorApplication.timeSinceStartup - _lastBlockListScrollTime < 0.16;
        }

        private bool IsCardRectVisible(Rect rect)
        {
            float minY = _scroll.y - CardVisiblePadding;
            float maxY = _scroll.y + Mathf.Max(120f, _blockListViewportHeight) + CardVisiblePadding;
            return rect.yMax >= minY && rect.yMin <= maxY;
        }

        private float GetCardPreviewExtraAngle(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return 0f;
            }

            if (!_cardRotateAnimations.TryGetValue(block.id, out CardRotateAnimState state) || state == null)
            {
                return 0f;
            }

            float elapsed = (float)(EditorApplication.timeSinceStartup - state.startTime);
            if (elapsed >= state.duration)
            {
                _cardRotateAnimations.Remove(block.id);
                return 0f;
            }

            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, state.duration));
            return Mathf.Lerp(90f, 0f, Mathf.SmoothStep(0f, 1f, t));
        }

        private void StartCardRotateAnimation(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return;
            }

            _cardRotateAnimations[block.id] = new CardRotateAnimState
            {
                startTime = EditorApplication.timeSinceStartup,
                duration = 0.16f
            };
        }

        private void RequestCardRotateAnimationRepaint()
        {
            if (_cardRotateAnimations.Count == 0 || IsBlockListScrolling())
            {
                return;
            }

            List<string> completed = null;
            double now = EditorApplication.timeSinceStartup;
            foreach (KeyValuePair<string, CardRotateAnimState> kv in _cardRotateAnimations)
            {
                CardRotateAnimState state = kv.Value;
                if (state == null || now - state.startTime >= state.duration)
                {
                    if (completed == null)
                    {
                        completed = new List<string>();
                    }

                    completed.Add(kv.Key);
                }
            }

            if (completed != null)
            {
                for (int i = 0; i < completed.Count; i++)
                {
                    _cardRotateAnimations.Remove(completed[i]);
                }
            }

            if (_cardRotateAnimations.Count > 0)
            {
                Repaint();
            }
        }

        private static float GetQuantizedAnimatedPreviewTime()
        {
            double now = EditorApplication.timeSinceStartup;
            double step = 1.0 / AnimatedCardPreviewFps;
            double quantized = Math.Floor(now / step) * step;
            return (float)quantized;
        }

        private static void ApplyFaceMainTexSt(Renderer renderer, Vector4[] faceMainTexSt)
        {
            if (renderer == null || faceMainTexSt == null)
            {
                return;
            }

            Material[] shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0)
            {
                return;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            int count = Mathf.Min(shared.Length, faceMainTexSt.Length);
            for (int i = 0; i < count; i++)
            {
                renderer.GetPropertyBlock(block, i);
                block.SetVector(MainTexStShaderId, faceMainTexSt[i]);
                renderer.SetPropertyBlock(block, i);
            }
        }

        private static void ApplyEmissionForDefinition(Renderer renderer, BlockDefinition definition)
        {
            if (renderer == null)
            {
                return;
            }

            Material[] shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0)
            {
                return;
            }

            Color emissionColor =  Color.white;
            if (definition != null && definition.emitsLight)
            {
                emissionColor = Color.white;
            }

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int i = 0; i < shared.Length; i++)
            {
                Material mat = shared[i];
                if (mat == null || !mat.HasProperty(EmissionColorShaderId))
                {
                    continue;
                }

                block.Clear();
                renderer.GetPropertyBlock(block, i);
                block.SetColor(EmissionColorShaderId, emissionColor);
                renderer.SetPropertyBlock(block, i);
            }
        }

        private static void ConfigureRealtimeLight(Transform blockTransform, BlockDefinition definition, bool enablePointLight)
        {
            if (blockTransform == null)
            {
                return;
            }

            bool shouldEmit = enablePointLight && definition != null && definition.emitsLight;
            Transform child = blockTransform.Find(RealtimeLightChildName);
            if (!shouldEmit)
            {
                if (child != null)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

                return;
            }

            if (child == null)
            {
                GameObject lightGo = new GameObject(RealtimeLightChildName);
                lightGo.transform.SetParent(blockTransform, false);
                lightGo.transform.localPosition = Vector3.zero;
                child = lightGo.transform;
            }

            Light light = child.GetComponent<Light>();
            if (light == null)
            {
                light = child.gameObject.AddComponent<Light>();
            }

            Color c = definition.lightColor.maxColorComponent > 0.001f
                ? definition.lightColor
                : Color.white;
            light.type = LightType.Point;
            light.color = c;
            light.intensity = 1.1f;
            light.range = 6f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        private static Texture2D ResolvePreviewTexture(BlockDefinition definition)
        {
            string path = null;
            if (!definition.sideTexturePaths.TryGetValue("front", out path))
            {
                if (!definition.sideTexturePaths.TryGetValue("top", out path))
                {
                    foreach (string side in SideOrder)
                    {
                        if (definition.sideTexturePaths.TryGetValue(side, out path))
                        {
                            break;
                        }
                    }
                }
            }

            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string combined = Path.Combine(projectRoot, assetPath);
            return combined.Replace("\\", "/");
        }

        private void BuildCategories()
        {
            HashSet<string> categorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _allBlocks.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(_allBlocks[i].category))
                {
                    categorySet.Add(_allBlocks[i].category);
                }
            }

            List<string> categories = new List<string>(categorySet);
            categories.Sort(StringComparer.OrdinalIgnoreCase);
            categories.Insert(0, CategoryAll);
            categories.Insert(1, CategoryRecent);

            string current = _categories.Count > 0 && _selectedCategory < _categories.Count ? _categories[_selectedCategory] : CategoryAll;
            _categories = categories;
            int nextIndex = _categories.FindIndex(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase));
            _selectedCategory = nextIndex >= 0 ? nextIndex : 0;
        }

        private IEnumerable<BlockDefinition> EnumerateRecentBlocks()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _recentBlockIds.Count; i++)
            {
                string id = _recentBlockIds[i];
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    continue;
                }

                for (int j = 0; j < _allBlocks.Count; j++)
                {
                    if (string.Equals(_allBlocks[j].id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return _allBlocks[j];
                        break;
                    }
                }
            }
        }

        private void RegisterRecentPlaced(string blockId)
        {
            if (string.IsNullOrWhiteSpace(blockId))
            {
                return;
            }

            _recentBlockIds.RemoveAll(id => string.Equals(id, blockId, StringComparison.OrdinalIgnoreCase));
            _recentBlockIds.Insert(0, blockId);
        }

        public static bool TryPlaceBlockAtApi(Transform root, string blockId, Vector3Int position, bool replaceExisting = true, int rotationQuarter = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.TryPlaceBlockAtInternal(root, blockId, position, replaceExisting, rotationQuarter);
        }

        public static bool TryPlaceBlockOnTopApi(Transform root, string blockId, int x, int z, int baseY = 0, bool replaceExisting = true, int rotationQuarter = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.TryPlaceBlockOnTopInternal(root, blockId, x, z, baseY, replaceExisting, rotationQuarter);
        }

        public static int PlaceBlocksInBoundsApi(Transform root, string blockId, Vector3Int minInclusive, Vector3Int maxInclusive, bool replaceExisting = true, int rotationQuarter = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.PlaceBlocksInBoundsInternal(root, blockId, minInclusive, maxInclusive, replaceExisting, rotationQuarter) : 0;
        }

        public static bool EraseBlockAtApi(Transform root, Vector3Int position)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.EraseBlockAtInternal(root, position);
        }

        public static int EraseBlocksInBoundsApi(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.EraseBlocksInBoundsInternal(root, minInclusive, maxInclusive) : 0;
        }

        public static bool ReplaceBlockAtApi(Transform root, string blockId, Vector3Int position, int rotationQuarter = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.ReplaceBlockAtInternal(root, blockId, position, rotationQuarter);
        }

        public static int ReplaceBlocksInBoundsApi(Transform root, string blockId, Vector3Int minInclusive, Vector3Int maxInclusive, int rotationQuarter = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.ReplaceBlocksInBoundsInternal(root, blockId, minInclusive, maxInclusive, rotationQuarter) : 0;
        }

        public static bool RotateBlockAtApi(Transform root, Vector3Int position, int stepQuarter = 1)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.RotateBlockAtInternal(root, position, stepQuarter);
        }

        public static int RotateBlocksInBoundsApi(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive, int stepQuarter = 1)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.RotateBlocksInBoundsInternal(root, minInclusive, maxInclusive, stepQuarter) : 0;
        }

        public static bool TryGetBlockIdAtApi(Transform root, Vector3Int position, out string blockId)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            if (window == null)
            {
                blockId = null;
                return false;
            }

            return window.TryGetBlockIdAtInternal(root, position, out blockId);
        }

        public static bool ExistsAtApi(Transform root, Vector3Int position)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null && window.ExistsAtInternal(root, position);
        }

        public static int GetTopYApi(Transform root, int x, int z, int fallbackY = 0)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.GetTopYInternal(root, x, z, fallbackY) : fallbackY;
        }

        public static IReadOnlyList<string> GetAvailableBlockIdsApi()
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            return window != null ? window.GetAvailableBlockIdsInternal() : Array.Empty<string>();
        }

        public static void ReloadLibraryApi()
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            if (window != null)
            {
                window.ReloadBlockLibrary();
            }
        }

        public static bool IsTransparentApi(string blockId)
        {
            Box3BlocksBuilderWindow window = GetApiWindowInstance();
            if (window == null)
            {
                return false;
            }

            window.EnsureLibraryLoadedForApi();
            BlockDefinition def = window.FindDefinitionById(blockId);
            return def != null && def.transparent;
        }

        private static Box3BlocksBuilderWindow GetApiWindowInstance()
        {
            Box3BlocksBuilderWindow[] existing = Resources.FindObjectsOfTypeAll<Box3BlocksBuilderWindow>();
            if (existing != null && existing.Length > 0)
            {
                return existing[0];
            }

            Box3BlocksBuilderWindow created = CreateInstance<Box3BlocksBuilderWindow>();
            created.hideFlags = HideFlags.HideAndDontSave;
            created.ReloadBlockLibrary();
            return created;
        }

        private void EnsureLibraryLoadedForApi()
        {
            if (_allBlocks == null || _allBlocks.Count == 0)
            {
                ReloadBlockLibrary();
            }
        }

        private bool TryPlaceBlockAtInternal(Transform root, string blockId, Vector3Int position, bool replaceExisting, int rotationQuarter)
        {
            if (root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            EnsureLibraryLoadedForApi();
            BlockDefinition definition = FindDefinitionById(blockId);
            if (definition == null)
            {
                ReloadBlockLibrary();
                definition = FindDefinitionById(blockId);
                if (definition == null)
                {
                    return false;
                }
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                GameObject existing = FindBlockAt(position);
                if (existing != null)
                {
                    if (!replaceExisting)
                    {
                        return false;
                    }

                    Undo.DestroyObjectImmediate(existing);
                }

                int previousRotation = definition.placementRotationQuarter;
                definition.placementRotationQuarter = rotationQuarter & 3;
                bool placed = TryPlaceSingleBlock(definition, position);
                definition.placementRotationQuarter = previousRotation;
                if (placed)
                {
                    RegisterRecentPlaced(definition.id);
                }

                return placed;
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private bool TryPlaceBlockOnTopInternal(Transform root, string blockId, int x, int z, int baseY, bool replaceExisting, int rotationQuarter)
        {
            if (root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            int topY = int.MinValue;
            Box3BlocksPlacedBlock[] blocks = root.GetComponentsInChildren<Box3BlocksPlacedBlock>(true);
            for (int i = 0; i < blocks.Length; i++)
            {
                Box3BlocksPlacedBlock block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                Vector3Int p = Vector3Int.RoundToInt(block.transform.position);
                if (p.x == x && p.z == z && p.y > topY)
                {
                    topY = p.y;
                }
            }

            int y = topY == int.MinValue ? baseY : topY + 1;
            return TryPlaceBlockAtInternal(root, blockId, new Vector3Int(x, y, z), replaceExisting, rotationQuarter);
        }

        private int PlaceBlocksInBoundsInternal(Transform root, string blockId, Vector3Int minInclusive, Vector3Int maxInclusive, bool replaceExisting, int rotationQuarter)
        {
            if (root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return 0;
            }

            int minX = Mathf.Min(minInclusive.x, maxInclusive.x);
            int minY = Mathf.Min(minInclusive.y, maxInclusive.y);
            int minZ = Mathf.Min(minInclusive.z, maxInclusive.z);
            int maxX = Mathf.Max(minInclusive.x, maxInclusive.x);
            int maxY = Mathf.Max(minInclusive.y, maxInclusive.y);
            int maxZ = Mathf.Max(minInclusive.z, maxInclusive.z);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Blocks In Bounds");

            int placed = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (TryPlaceBlockAtInternal(root, blockId, new Vector3Int(x, y, z), replaceExisting, rotationQuarter))
                        {
                            placed++;
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(group);
            return placed;
        }

        private bool EraseBlockAtInternal(Transform root, Vector3Int position)
        {
            if (root == null)
            {
                return false;
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                GameObject existing = FindBlockAt(position);
                if (existing == null)
                {
                    return false;
                }

                UnregisterBlockInLookup(position, existing);
                Undo.DestroyObjectImmediate(existing);
                RefreshTransparentAround(position);
                RefreshOcclusionAround(position);
                return true;
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private int EraseBlocksInBoundsInternal(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive)
        {
            if (root == null)
            {
                return 0;
            }

            int minX = Mathf.Min(minInclusive.x, maxInclusive.x);
            int minY = Mathf.Min(minInclusive.y, maxInclusive.y);
            int minZ = Mathf.Min(minInclusive.z, maxInclusive.z);
            int maxX = Mathf.Max(minInclusive.x, maxInclusive.x);
            int maxY = Mathf.Max(minInclusive.y, maxInclusive.y);
            int maxZ = Mathf.Max(minInclusive.z, maxInclusive.z);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Erase Blocks In Bounds");
            int removed = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (EraseBlockAtInternal(root, new Vector3Int(x, y, z)))
                        {
                            removed++;
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(group);
            return removed;
        }

        private bool ReplaceBlockAtInternal(Transform root, string blockId, Vector3Int position, int rotationQuarter)
        {
            if (root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return false;
            }

            EnsureLibraryLoadedForApi();
            BlockDefinition definition = FindDefinitionById(blockId);
            if (definition == null)
            {
                ReloadBlockLibrary();
                definition = FindDefinitionById(blockId);
                if (definition == null)
                {
                    return false;
                }
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                GameObject existing = FindBlockAt(position);
                if (existing == null)
                {
                    return false;
                }

                Box3BlocksPlacedBlock marker = existing.GetComponent<Box3BlocksPlacedBlock>();
                if (marker != null && string.Equals(marker.BlockId, blockId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                UnregisterBlockInLookup(position, existing);
                Undo.DestroyObjectImmediate(existing);
                int previousRotation = definition.placementRotationQuarter;
                definition.placementRotationQuarter = rotationQuarter & 3;
                bool placed = TryPlaceSingleBlock(definition, position);
                definition.placementRotationQuarter = previousRotation;
                if (placed)
                {
                    RegisterRecentPlaced(definition.id);
                }

                return placed;
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private int ReplaceBlocksInBoundsInternal(Transform root, string blockId, Vector3Int minInclusive, Vector3Int maxInclusive, int rotationQuarter)
        {
            if (root == null || string.IsNullOrWhiteSpace(blockId))
            {
                return 0;
            }

            int minX = Mathf.Min(minInclusive.x, maxInclusive.x);
            int minY = Mathf.Min(minInclusive.y, maxInclusive.y);
            int minZ = Mathf.Min(minInclusive.z, maxInclusive.z);
            int maxX = Mathf.Max(minInclusive.x, maxInclusive.x);
            int maxY = Mathf.Max(minInclusive.y, maxInclusive.y);
            int maxZ = Mathf.Max(minInclusive.z, maxInclusive.z);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Blocks In Bounds");
            int replaced = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (ReplaceBlockAtInternal(root, blockId, new Vector3Int(x, y, z), rotationQuarter))
                        {
                            replaced++;
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(group);
            return replaced;
        }

        private bool RotateBlockAtInternal(Transform root, Vector3Int position, int stepQuarter)
        {
            if (root == null)
            {
                return false;
            }

            int q = stepQuarter % 4;
            if (q < 0)
            {
                q += 4;
            }

            if (q == 0)
            {
                return false;
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                GameObject existing = FindBlockAt(position);
                if (existing == null)
                {
                    return false;
                }

                Undo.RecordObject(existing.transform, "Rotate Block");
                existing.transform.Rotate(0f, q * 90f, 0f, Space.World);
                EditorUtility.SetDirty(existing.transform);
                UpdateTransparentBlockMesh(existing);
                return true;
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private int RotateBlocksInBoundsInternal(Transform root, Vector3Int minInclusive, Vector3Int maxInclusive, int stepQuarter)
        {
            if (root == null)
            {
                return 0;
            }

            int minX = Mathf.Min(minInclusive.x, maxInclusive.x);
            int minY = Mathf.Min(minInclusive.y, maxInclusive.y);
            int minZ = Mathf.Min(minInclusive.z, maxInclusive.z);
            int maxX = Mathf.Max(minInclusive.x, maxInclusive.x);
            int maxY = Mathf.Max(minInclusive.y, maxInclusive.y);
            int maxZ = Mathf.Max(minInclusive.z, maxInclusive.z);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rotate Blocks In Bounds");
            int rotated = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (RotateBlockAtInternal(root, new Vector3Int(x, y, z), stepQuarter))
                        {
                            rotated++;
                        }
                    }
                }
            }

            Undo.CollapseUndoOperations(group);
            return rotated;
        }

        private bool TryGetBlockIdAtInternal(Transform root, Vector3Int position, out string blockId)
        {
            blockId = null;
            if (root == null)
            {
                return false;
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                GameObject existing = FindBlockAt(position);
                if (existing == null)
                {
                    return false;
                }

                Box3BlocksPlacedBlock marker = existing.GetComponent<Box3BlocksPlacedBlock>();
                if (marker == null)
                {
                    return false;
                }

                blockId = marker.BlockId;
                return !string.IsNullOrWhiteSpace(blockId);
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private bool ExistsAtInternal(Transform root, Vector3Int position)
        {
            if (root == null)
            {
                return false;
            }

            Transform previousRoot = _root;
            try
            {
                _root = root;
                return FindBlockAt(position) != null;
            }
            finally
            {
                _root = previousRoot;
            }
        }

        private int GetTopYInternal(Transform root, int x, int z, int fallbackY)
        {
            if (root == null)
            {
                return fallbackY;
            }

            int topY = int.MinValue;
            Box3BlocksPlacedBlock[] blocks = root.GetComponentsInChildren<Box3BlocksPlacedBlock>(true);
            for (int i = 0; i < blocks.Length; i++)
            {
                Box3BlocksPlacedBlock block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                Vector3Int p = Vector3Int.RoundToInt(block.transform.position);
                if (p.x == x && p.z == z && p.y > topY)
                {
                    topY = p.y;
                }
            }

            return topY == int.MinValue ? fallbackY : topY;
        }

        private IReadOnlyList<string> GetAvailableBlockIdsInternal()
        {
            EnsureLibraryLoadedForApi();
            List<string> ids = new List<string>(_allBlocks.Count);
            for (int i = 0; i < _allBlocks.Count; i++)
            {
                if (_allBlocks[i] != null && !string.IsNullOrWhiteSpace(_allBlocks[i].id))
                {
                    ids.Add(_allBlocks[i].id);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

    }
}
