using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    public class BlockWorldBuilderWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3.blockworld-mvp/Assets/block";
        private const string BlockSpecPath = "Packages/com.box3.blockworld-mvp/Assets/block-spec.json";
        private const string BlockIdPath = "Packages/com.box3.blockworld-mvp/Assets/block-id.json";
        private const string GeneratedMaterialFolder = "Assets/BlockWorldGenerated/Materials";
        private const string CategoryAll = "All";
        private const string CategoryRecent = "Recent";
        private const string CategoryUncategorized = "Uncategorized";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex(@"^(.*)_(back|bottom|front|left|right|top)\.png$", RegexOptions.Compiled);
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly int MainTexStShaderId = Shader.PropertyToID("_MainTex_ST");

        private class BlockDefinition
        {
            public string id;
            public Dictionary<string, string> sideTexturePaths = new Dictionary<string, string>();
            public Dictionary<string, FaceAnimationSpec> sideAnimations = new Dictionary<string, FaceAnimationSpec>();
            public bool hasAnimation;
            public Texture2D previewTexture;
            public int numericId = -1;
            public string category = CategoryUncategorized;
            public bool emitsLight;
            public bool transparent;
            public Color lightColor = new Color(1f, 0.8f, 0.2f, 1f);
            public string displayName;
        }

        private class FaceAnimationSpec
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }

        private class BlockMetadata
        {
            public int numericId = -1;
            public string category;
            public bool emitsLight;
            public bool transparent;
            public Color lightColor = new Color(1f, 0.8f, 0.2f, 1f);
        }

        private enum EditTool
        {
            Place,
            Erase,
            Replace,
            Rotate
        }

        private Transform _root;
        private List<BlockDefinition> _allBlocks = new List<BlockDefinition>();
        private List<BlockDefinition> _filteredBlocks = new List<BlockDefinition>();
        private List<string> _recentBlockIds = new List<string>();
        private List<string> _categories = new List<string> { CategoryAll };
        private int _selectedIndex;
        private int _selectedCategory;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private EditTool _tool = EditTool.Place;
        private int _brushHorizontalSize = 1;
        private int _brushHeight = 1;
        private const float PreviewSize = 75f;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _subtleLabelStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _categoryTabStyle;
        private GUIStyle _categoryTabSelectedStyle;
        private readonly Dictionary<string, Mesh> _staticBlockMeshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _blockCardPreviewCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private PreviewRenderUtility _blockCardPreviewUtility;

        [MenuItem("Tools/Block World MVP/World Builder")]
        public static void Open()
        {
            GetWindow<BlockWorldBuilderWindow>(L("window.title"));
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            ReloadBlockLibrary();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
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
            return BlockWorldBuilderI18n.Get(key);
        }

        private static string Lf(string key, params object[] args)
        {
            return BlockWorldBuilderI18n.Format(key, args);
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

            return BlockWorldBuilderI18n.GetCategoryLabel(category.Trim());
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
            _root = (Transform)EditorGUILayout.ObjectField(L("root.root"), _root, typeof(Transform), true);

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

                if (GUILayout.Button(L("root.clean_materials"), _primaryButtonStyle))
                {
                    int deleted = CleanupUnusedGeneratedMaterials();
                    EditorUtility.DisplayDialog(L("window.title"), Lf("dialog.clean_materials_message", deleted), L("dialog.ok"));
                }
            }
        }

        private void DrawToolSection()
        {
            _tool = (EditTool)GUILayout.Toolbar((int)_tool, new[] { L("tool.place"), L("tool.erase"), L("tool.replace"), L("tool.rotate") });
            EditorGUILayout.Space(4f);

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
                EditorGUILayout.LabelField(L("tool.height"), _subtleLabelStyle, GUILayout.Width(70f));
                int heightInput = Mathf.Max(1, EditorGUILayout.IntField(_brushHeight, GUILayout.Width(52f)));
                float heightSliderMax = Mathf.Max(16f, heightInput);
                _brushHeight = Mathf.Max(1, Mathf.RoundToInt(GUILayout.HorizontalSlider(heightInput, 1f, heightSliderMax)));
            }
            EditorGUILayout.LabelField(Lf("tool.brush_volume", _brushHorizontalSize, _brushHorizontalSize, _brushHeight), _subtleLabelStyle);

            if (_root == null)
            {
                EditorGUILayout.HelpBox(L("tool.assign_root_help"), MessageType.Info);
            }
        }

        private void DrawBlockListSection()
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

            EditorGUILayout.Space(4f);
            if (_categories.Count > 0)
            {
                DrawCategoryTabsWrapped();
            }

            int columns = CalculateColumnCount();
            EditorGUILayout.LabelField(Lf("library.blocks_layout", _filteredBlocks.Count, columns), _subtleLabelStyle);
            EditorGUILayout.Space(2f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawBlockGrid(columns);

            EditorGUILayout.EndScrollView();
        }

        private void DrawBlockGrid(int columns)
        {
            float cellWidth = Mathf.Max(120f, (position.width - 40f) / columns);

            int index = 0;
            while (index < _filteredBlocks.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns; c++)
                    {
                        if (index < _filteredBlocks.Count)
                        {
                            DrawBlockCard(index, _filteredBlocks[index], cellWidth);
                            index++;
                        }
                        else
                        {
                            GUILayout.Space(cellWidth);
                        }
                    }
                }
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
            string title = BlockWorldBuilderI18n.GetBlockDisplayName(block.id, fallbackTitle);
            string categoryLabel = LocalizeCategoryLabel(block.category);
            string subtitle = string.IsNullOrWhiteSpace(info) ? categoryLabel : $"{categoryLabel} | {info}";
            GUIStyle cardStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = Mathf.Max(132f, PreviewSize + 60f),
                wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                fontSize = 11,
                padding = new RectOffset(6, 6, 8, 6)
            };

            bool clicked = GUILayout.Button(GUIContent.none, cardStyle, GUILayout.Width(width), GUILayout.Height(cardStyle.fixedHeight));
            Rect rect = GUILayoutUtility.GetLastRect();

            Rect previewRect = new Rect(
                rect.x + (rect.width - PreviewSize) * 0.5f,
                rect.y + 10f,
                PreviewSize,
                PreviewSize);
            Texture2D cardPreview = GetOrBuildBlockCardPreview(block);
            if (cardPreview != null)
            {
                GUI.DrawTexture(previewRect, cardPreview, ScaleMode.ScaleToFit, true);
            }

            Rect textRect = new Rect(rect.x + 6f, previewRect.yMax + 6f, rect.width - 12f, rect.height - (PreviewSize + 20f));
            GUIStyle textStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.UpperCenter,
                wordWrap = true,
                fontSize = 11
            };
            if (_selectedIndex == index)
            {
                textStyle.normal.textColor = new Color(0.78f, 1f, 0.8f, 1f);
                textStyle.fontStyle = FontStyle.Bold;
            }
            EditorGUI.LabelField(textRect, $"{title}\n{subtitle}", textStyle);

            if (_selectedIndex == index)
            {
                DrawSelectedCardFrame(rect);
            }

            if (block.emitsLight)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 6f, rect.yMax - 4f, rect.width - 12f, 2f), block.lightColor);
            }

            if (clicked)
            {
                _selectedIndex = index;
                Repaint();
            }
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

            Rect badgeRect = new Rect(rect.x + 6f, rect.y + 6f, 62f, 16f);
            EditorGUI.DrawRect(badgeRect, new Color(0.05f, 0.18f, 0.08f, 0.95f));
            EditorGUI.LabelField(badgeRect, L("card.selected"), new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.5f, 1f, 0.55f, 1f) },
                fontStyle = FontStyle.Bold
            });
        }

        private int CalculateColumnCount()
        {
            const float minCellWidth = 150f;
            float available = Mathf.Max(1f, position.width - 40f);
            return Mathf.Max(1, Mathf.FloorToInt(available / minCellWidth));
        }

        private void DrawCategoryTabsWrapped()
        {
            float availableWidth = Mathf.Max(120f, position.width - 32f);
            float lineWidth = 0f;
            const float padX = 10f;
            const float spacing = 4f;

            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < _categories.Count; i++)
            {
                string categoryKey = _categories[i];
                string label = LocalizeCategoryLabel(categoryKey);
                float buttonWidth = Mathf.Clamp(EditorStyles.miniButton.CalcSize(new GUIContent(label)).x + padX, 52f, 220f);

                if (lineWidth > 0f && lineWidth + buttonWidth > availableWidth)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    lineWidth = 0f;
                }

                bool selected = i == _selectedCategory;
                GUIStyle style = selected ? _categoryTabSelectedStyle : _categoryTabStyle;
                bool clicked = GUILayout.Toggle(selected, label, style, GUILayout.Width(buttonWidth));
                if (clicked && !selected)
                {
                    _selectedCategory = i;
                    ApplyFilter();
                }

                lineWidth += buttonWidth + spacing;
            }

            EditorGUILayout.EndHorizontal();
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

            if (!TryGetTargetPosition(e.mousePosition, out Vector3Int target, out PlacedBlock hitBlock))
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

        private bool TryGetTargetPosition(Vector2 mousePosition, out Vector3Int target, out PlacedBlock hitBlock)
        {
            target = default;
            hitBlock = null;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                hitBlock = hit.collider.GetComponentInParent<PlacedBlock>();
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
                if (TryFindClosestBlockFromScreen(mousePosition, out PlacedBlock closest))
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

        private bool TryFindClosestBlockFromScreen(Vector2 mousePosition, out PlacedBlock block)
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
                PlacedBlock candidate = child.GetComponent<PlacedBlock>();
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
            SceneView.RepaintAll();
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

            if (!BlockAssetFactory.TryGetFaceRenderData(definition.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData))
            {
                return false;
            }

            bool hasAnimatedFaces = HasAnimatedFaces(definition);
            Mesh meshToUse = hasAnimatedFaces
                ? BlockAssetFactory.GetOrCreateCubeMesh()
                : GetOrCreateStaticBlockMesh(definition, renderData);
            if (meshToUse == null || renderData == null || renderData.materials == null)
            {
                return false;
            }

            GameObject go = new GameObject($"{definition.id}_{position.x}_{position.y}_{position.z}");
            Undo.RegisterCreatedObjectUndo(go, "Place Block");

            go.transform.SetParent(_root);
            go.transform.position = position;

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = meshToUse;

            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            if (hasAnimatedFaces)
            {
                ConfigureRendererMaterials(meshRenderer, renderData, definition);
            }
            else
            {
                meshRenderer.sharedMaterial = renderData.materials[0];
            }

            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshToUse;

            PlacedBlock marker = go.AddComponent<PlacedBlock>();
            marker.BlockId = definition.id;
            marker.HasAnimation = hasAnimatedFaces;

            EditorUtility.SetDirty(go);
            return true;
        }

        private void EraseBlockBrush(PlacedBlock hitBlock, Vector3Int fallbackPosition)
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

                Undo.DestroyObjectImmediate(target);
            }
        }

        private void ReplaceBlockBrush(PlacedBlock hitBlock, Vector3Int fallbackPosition)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredBlocks.Count)
            {
                return;
            }

            BlockDefinition replacement = _filteredBlocks[_selectedIndex];
            Vector3Int origin = hitBlock != null ? Vector3Int.RoundToInt(hitBlock.transform.position) : fallbackPosition;
            List<Vector3Int> positions = BuildBrushPositions(origin);
            bool replacedAny = false;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3Int pos = positions[i];
                GameObject target = FindBlockAt(pos);
                if (target == null)
                {
                    continue;
                }

                PlacedBlock existing = target.GetComponent<PlacedBlock>();
                if (existing != null && string.Equals(existing.BlockId, replacement.id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(target);
                if (TryPlaceSingleBlock(replacement, pos))
                {
                    replacedAny = true;
                }
            }

            if (replacedAny)
            {
                RegisterRecentPlaced(replacement.id);
            }
        }

        private void RotateBlockBrush(PlacedBlock hitBlock, Vector3Int fallbackPosition)
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
            }
        }

        private GameObject FindBlockAt(Vector3Int position)
        {
            if (_root != null)
            {
                for (int i = 0; i < _root.childCount; i++)
                {
                    Transform child = _root.GetChild(i);
                    if (Vector3Int.RoundToInt(child.position) == position)
                    {
                        PlacedBlock marker = child.GetComponent<PlacedBlock>();
                        if (marker != null)
                        {
                            return child.gameObject;
                        }
                    }
                }
            }

            Collider[] colliders = Physics.OverlapBox(position, Vector3.one * 0.45f);
            for (int i = 0; i < colliders.Length; i++)
            {
                PlacedBlock marker = colliders[i].GetComponentInParent<PlacedBlock>();
                if (marker != null && marker.transform.parent == _root)
                {
                    return marker.gameObject;
                }
            }

            return null;
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

        private void CreateRoot()
        {
            GameObject go = new GameObject("BlockWorldRoot");
            Undo.RegisterCreatedObjectUndo(go, "Create Block Root");
            _root = go.transform;
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
            BlockAssetFactory.InvalidateCaches();
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
                }
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _filteredBlocks.Count - 1));
        }

        private static void ConfigureRendererMaterials(MeshRenderer renderer, BlockAssetFactory.FaceRenderData renderData, BlockDefinition definition)
        {
            if (renderer == null || renderData == null || renderData.materials == null || renderData.faceMainTexSt == null)
            {
                return;
            }

            renderer.sharedMaterials = renderData.materials;

            if (!definition.hasAnimation || definition.sideAnimations.Count == 0)
            {
                ApplyFaceMainTexSt(renderer, renderData.faceMainTexSt);
                BlockTextureAnimator existingAnimator = renderer.GetComponent<BlockTextureAnimator>();
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

            List<BlockTextureAnimator.FaceAnimation> runtimeAnimations = new List<BlockTextureAnimator.FaceAnimation>();
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

                runtimeAnimations.Add(new BlockTextureAnimator.FaceAnimation
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
                BlockTextureAnimator existingAnimator = renderer.GetComponent<BlockTextureAnimator>();
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

            BlockTextureAnimator animator = renderer.GetComponent<BlockTextureAnimator>();
            if (animator == null)
            {
                animator = renderer.gameObject.AddComponent<BlockTextureAnimator>();
            }

            animator.SetAnimations(runtimeAnimations.ToArray(), renderData.faceMainTexSt);
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

        private Mesh GetOrCreateStaticBlockMesh(BlockDefinition definition, BlockAssetFactory.FaceRenderData renderData)
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

            if (_blockCardPreviewCache.TryGetValue(block.id, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            if (!BlockAssetFactory.TryGetFaceRenderData(block.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData)
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

            Texture2D preview = RenderBlockCardPreview(mesh, renderData.materials[0]);
            if (preview != null)
            {
                _blockCardPreviewCache[block.id] = preview;
                return preview;
            }

            return block.previewTexture;
        }

        private Texture2D RenderBlockCardPreview(Mesh mesh, Material material)
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

            Texture2D onBlack = RenderPreviewWithBackground(mesh, material, new Color(0f, 0f, 0f, 1f));
            Texture2D onWhite = RenderPreviewWithBackground(mesh, material, new Color(1f, 1f, 1f, 1f));
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

        private Texture2D RenderPreviewWithBackground(Mesh mesh, Material material, Color backgroundColor)
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
            Vector3 camDir = viewRot * new Vector3(0f, 0f, 1f);
            cam.transform.position = target - camDir * radius * 4.6f;
            cam.transform.rotation = viewRot;
            cam.transform.LookAt(target);

            _blockCardPreviewUtility.DrawMesh(mesh, Matrix4x4.identity, material, 0);
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

        private static Dictionary<string, BlockMetadata> LoadBlockMetadata()
        {
            Dictionary<string, BlockMetadata> map = LoadFromBlockSpec();
            if (map.Count > 0)
            {
                return map;
            }

            return LoadFromBlockId();
        }

        private static Dictionary<string, BlockMetadata> LoadFromBlockSpec()
        {
            Dictionary<string, BlockMetadata> map = new Dictionary<string, BlockMetadata>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockSpecPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            Dictionary<string, string> objects = ExtractTopLevelObjectValues(json);
            foreach (KeyValuePair<string, string> pair in objects)
            {
                string name = pair.Key;
                string body = pair.Value;

                string category = ReadStringField(body, "category");
                int numericId = ParseIntSafe(ReadNumberField(body, "id"), -1);
                bool transparent = ReadBoolField(body, "transparent");
                Color emissiveColor = ReadColorArrayField(body, "emissive", InferLightColor(name));
                bool emitsLight = HasMeaningfulEmission(body) || HasLightKeyword(name);

                map[name] = new BlockMetadata
                {
                    numericId = numericId,
                    category = string.IsNullOrWhiteSpace(category) ? InferCategory(name) : category,
                    emitsLight = emitsLight,
                    transparent = transparent,
                    lightColor = emissiveColor
                };
            }

            return map;
        }

        private static Dictionary<string, BlockMetadata> LoadFromBlockId()
        {
            Dictionary<string, BlockMetadata> map = new Dictionary<string, BlockMetadata>(StringComparer.OrdinalIgnoreCase);
            string absPath = GetProjectAbsolutePath(BlockIdPath);
            if (!File.Exists(absPath))
            {
                return map;
            }

            string json = File.ReadAllText(absPath);
            MatchCollection flatMatches = FlatMapRegex.Matches(json);
            for (int i = 0; i < flatMatches.Count; i++)
            {
                Match m = flatMatches[i];
                string name = m.Groups["name"].Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                map[name] = new BlockMetadata
                {
                    numericId = ParseIntSafe(m.Groups["id"].Value, -1),
                    category = InferCategory(name),
                    emitsLight = HasLightKeyword(name),
                    transparent = IsTransparencyKeyword(name),
                    lightColor = InferLightColor(name)
                };
            }

            return map;
        }

        private static string ReadStringField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static string ReadNumberField(string text, string fieldName)
        {
            Match match = Regex.Match(text, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
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

        private static int ParseIntSafe(string text, int fallback)
        {
            return int.TryParse(text, out int value) ? value : fallback;
        }

        private static float ParseFloatSafe(string text, float fallback)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }

        private static bool HasLightKeyword(string id)
        {
            return id.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lava", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTransparencyKeyword(string id)
        {
            return id.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string InferCategory(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return CategoryUncategorized;
            }

            string lower = id.ToLowerInvariant();
            if (lower.Contains("light") || lower.Contains("lamp") || lower.Contains("lantern") || lower.Contains("led"))
            {
                return "Light";
            }

            if (lower.Contains("glass") || lower.Contains("window"))
            {
                return "Glass";
            }

            if (lower.Contains("grass") || lower.Contains("sand") || lower.Contains("dirt") || lower.Contains("rock") || lower.Contains("stone") || lower.Contains("leaf") || lower.Contains("water") || lower.Contains("snow") || lower.Contains("lava"))
            {
                return "Nature";
            }

            if (lower.Contains("board") || lower.Contains("plank") || lower.Contains("brick") || lower.Contains("wall") || lower.Contains("roof"))
            {
                return "Building";
            }

            if (lower.Length == 1 || lower.Contains("mark") || lower.Contains("slash") || lower.Contains("paren") || lower.Contains("brace") || lower.Contains("bracket"))
            {
                return "Symbol";
            }

            return "Misc";
        }

        private static Color InferLightColor(string id)
        {
            string lower = id.ToLowerInvariant();
            if (lower.Contains("red"))
            {
                return new Color(1f, 0.32f, 0.28f, 1f);
            }

            if (lower.Contains("blue"))
            {
                return new Color(0.35f, 0.7f, 1f, 1f);
            }

            if (lower.Contains("green") || lower.Contains("mint"))
            {
                return new Color(0.4f, 1f, 0.55f, 1f);
            }

            if (lower.Contains("yellow") || lower.Contains("warm"))
            {
                return new Color(1f, 0.9f, 0.3f, 1f);
            }

            if (lower.Contains("purple") || lower.Contains("indigo"))
            {
                return new Color(0.62f, 0.45f, 1f, 1f);
            }

            if (lower.Contains("pink"))
            {
                return new Color(1f, 0.52f, 0.8f, 1f);
            }

            return new Color(1f, 0.8f, 0.2f, 1f);
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
                sum += Mathf.Abs(ParseFloatSafe(parts[i], 0f));
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

            float r = ParseFloatSafe(parts[0], fallback.r);
            float g = ParseFloatSafe(parts[1], fallback.g);
            float b = ParseFloatSafe(parts[2], fallback.b);

            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max > 1f)
            {
                r /= max;
                g /= max;
                b /= max;
            }

            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
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
            System.Text.StringBuilder sb = null;
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
                        sb = new System.Text.StringBuilder();
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

    }
}
