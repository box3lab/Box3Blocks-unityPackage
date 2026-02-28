using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockWorldMVP.Editor
{
    public class BlockWorldBuilderWindow : EditorWindow
    {
        private const string BlockTextureFolder = "Packages/com.box3.blockworld-mvp/Assets/block";
        private const string BlockSpecPath = "Packages/com.box3.blockworld-mvp/Assets/block-spec.json";
        private const string BlockIdPath = "Packages/com.box3.blockworld-mvp/Assets/block-id.json";
        private const string GeneratedMaterialFolder = "Assets/BlockWorldGenerated/Materials";
        private const string GeneratedMeshFolder = "Assets/BlockWorldGenerated/Meshes";
        private const string GeneratedChunkMeshFolder = "Assets/BlockWorldGenerated/Meshes/ChunkConverted";
        private const string ChunkMergedRootName = "__ChunkMerged";
        private const string CategoryAll = "All";
        private const string CategoryRecent = "Recent";
        private const string CategoryUncategorized = "Uncategorized";
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Vector3Int[] NeighborDirections =
        {
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0)
        };
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
            public int placementRotationQuarter;
        }

        private class FaceAnimationSpec
        {
            public int frameCount = 1;
            public float frameDuration = 0.05f;
            public int[] frames = Array.Empty<int>();
        }

        private sealed class AnimatedPreviewCacheEntry
        {
            public int signature;
            public Texture2D texture;
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
        private int _chunkConvertSize = 16;
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
        private readonly Dictionary<string, AnimatedPreviewCacheEntry> _animatedBlockCardPreviewCache = new Dictionary<string, AnimatedPreviewCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private PreviewRenderUtility _blockCardPreviewUtility;
        private double _nextAnimatedPreviewRepaintTime;

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
            RequestAnimatedCardPreviewRepaint();
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

            Rect rect = GUILayoutUtility.GetRect(width, cardStyle.fixedHeight, GUILayout.Width(width), GUILayout.Height(cardStyle.fixedHeight));
            GUI.Box(rect, GUIContent.none, cardStyle);
            Rect rotateRect = DrawCardRotateButton(rect, block);

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
                    block.placementRotationQuarter = (block.placementRotationQuarter + 1) & 3;
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
                GUI.Box(buttonRect, "R0", EditorStyles.miniButton);
                return buttonRect;
            }

            int degrees = (block.placementRotationQuarter & 3) * 90;
            string label = Lf("card.rotate_badge", degrees);
            GUI.Box(buttonRect, label, EditorStyles.miniButton);
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
                meshRenderer.sharedMaterial = renderData.materials[0];
            }

            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshToUse;

            PlacedBlock marker = go.AddComponent<PlacedBlock>();
            marker.BlockId = definition.id;
            marker.HasAnimation = hasAnimatedFaces;

            RefreshTransparentAround(position);
            EditorUtility.SetDirty(go);
            return true;
        }

        private void EraseBlockBrush(PlacedBlock hitBlock, Vector3Int fallbackPosition)
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

                Undo.DestroyObjectImmediate(target);
                refresh.Add(positions[i]);
            }

            foreach (Vector3Int pos in refresh)
            {
                RefreshTransparentAround(pos);
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
            HashSet<Vector3Int> refresh = new HashSet<Vector3Int>();
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
                UpdateTransparentBlockMesh(target);
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

        private void RefreshTransparentAround(Vector3Int position)
        {
            UpdateTransparentBlockAt(position);
            for (int i = 0; i < NeighborDirections.Length; i++)
            {
                UpdateTransparentBlockAt(position + NeighborDirections[i]);
            }
        }

        private void UpdateTransparentBlockAt(Vector3Int position)
        {
            GameObject target = FindBlockAt(position);
            if (target == null)
            {
                return;
            }

            UpdateTransparentBlockMesh(target);
        }

        private void UpdateTransparentBlockMesh(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            PlacedBlock marker = target.GetComponent<PlacedBlock>();
            if (marker == null)
            {
                return;
            }

            BlockDefinition definition = FindDefinitionById(marker.BlockId);
            if (definition == null || !definition.transparent)
            {
                return;
            }

            if (HasAnimatedFaces(definition))
            {
                return;
            }

            if (!BlockAssetFactory.TryGetFaceRenderData(definition.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData))
            {
                return;
            }

            Vector3Int pos = Vector3Int.RoundToInt(target.transform.position);
            float yRot = NormalizeYRotation(target.transform.eulerAngles.y);
            int rotQuarter = Mathf.RoundToInt(yRot / 90f) & 3;
            Mesh mesh = BuildCulledTransparentMesh(pos, rotQuarter, renderData.faceMainTexSt);
            if (mesh == null)
            {
                return;
            }

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mf.sharedMesh = mesh;
            }

            MeshCollider mc = target.GetComponent<MeshCollider>();
            if (mc != null)
            {
                mc.sharedMesh = mesh;
            }
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

        private Mesh BuildCulledTransparentMesh(Vector3Int position, int rotationQuarter, Vector4[] faceMainTexSt)
        {
            if (faceMainTexSt == null || faceMainTexSt.Length < SideOrder.Length)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>(24);
            List<Vector2> uvs = new List<Vector2>(24);
            List<int> triangles = new List<int>(36);
            Quaternion rotation = Quaternion.Euler(0f, (rotationQuarter & 3) * 90f, 0f);

            for (int face = 0; face < SideOrder.Length; face++)
            {
                Vector3 dir = rotation * FaceNormals[face];
                Vector3Int neighbor = position + ToVector3Int(dir);
                if (FindBlockAt(neighbor) != null)
                {
                    continue;
                }

                Vector4 st = faceMainTexSt[face];
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
                name = $"CulledTransparent_{position.x}_{position.y}_{position.z}"
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

        private static float NormalizeYRotation(float y)
        {
            float n = y % 360f;
            if (n < 0f)
            {
                n += 360f;
            }

            return n;
        }

        private void CreateRoot()
        {
            GameObject go = new GameObject("BlockWorldRoot");
            Undo.RegisterCreatedObjectUndo(go, "Create Block Root");
            _root = go.transform;
            EnsureRuntimeCuller(_root);
            Selection.activeObject = go;
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

        private void ConvertRootToChunkMerged(bool deleteOriginal)
        {
            if (_root == null)
            {
                EditorUtility.DisplayDialog(L("window.title"), L("tool.assign_root_help"), L("dialog.ok"));
                return;
            }

            if (_allBlocks.Count == 0)
            {
                ReloadBlockLibrary();
            }

            Dictionary<string, BlockDefinition> blockMap = BuildBlockMap();
            List<PlacedBlock> sources = CollectPlacedBlocksFromRoot();
            if (sources.Count == 0)
            {
                EditorUtility.DisplayDialog(L("window.title"), L("dialog.convert_chunk_empty"), L("dialog.ok"));
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
            Undo.RegisterCreatedObjectUndo(mergedRootGo, "Convert Root To Chunk");
            mergedRootGo.transform.SetParent(_root, false);

            Dictionary<ChunkMatKey, List<CombineInstance>> buckets = new Dictionary<ChunkMatKey, List<CombineInstance>>(512);
            Dictionary<int, Material> materialById = new Dictionary<int, Material>(64);
            int accepted = 0;
            int skippedUnknown = 0;
            int skippedAnimated = 0;
            int skippedInvalid = 0;

            for (int i = 0; i < sources.Count; i++)
            {
                PlacedBlock placed = sources[i];
                if (placed == null)
                {
                    continue;
                }

                if (!blockMap.TryGetValue(placed.BlockId, out BlockDefinition definition) || definition == null)
                {
                    skippedUnknown++;
                    continue;
                }

                if (HasAnimatedFaces(definition))
                {
                    skippedAnimated++;
                    continue;
                }

                if (!BlockAssetFactory.TryGetFaceRenderData(definition.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData)
                    || renderData == null
                    || renderData.materials == null
                    || renderData.materials.Length == 0
                    || renderData.materials[0] == null)
                {
                    skippedInvalid++;
                    continue;
                }

                Mesh mesh = GetOrCreateStaticBlockMesh(definition, renderData);
                if (mesh == null)
                {
                    skippedInvalid++;
                    continue;
                }

                Material material = renderData.materials[0];
                int materialId = material.GetInstanceID();
                materialById[materialId] = material;

                Vector3Int p = Vector3Int.RoundToInt(placed.transform.position);
                int cx = FloorDiv(p.x, Mathf.Max(1, _chunkConvertSize));
                int cy = FloorDiv(p.y, Mathf.Max(1, _chunkConvertSize));
                int cz = FloorDiv(p.z, Mathf.Max(1, _chunkConvertSize));
                ChunkMatKey key = new ChunkMatKey(cx, cy, cz, materialId);
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
                accepted++;
            }

            int createdRenderers = 0;
            foreach (KeyValuePair<ChunkMatKey, List<CombineInstance>> kv in buckets)
            {
                ChunkMatKey key = kv.Key;
                List<CombineInstance> list = kv.Value;
                if (list == null || list.Count == 0 || !materialById.TryGetValue(key.materialId, out Material material))
                {
                    continue;
                }

                GameObject go = new GameObject($"chunk_{key.x}_{key.y}_{key.z}_m{key.materialId}");
                go.transform.SetParent(mergedRootGo.transform, false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;

                Mesh mesh = new Mesh
                {
                    name = $"Chunk_{key.x}_{key.y}_{key.z}_{createdRenderers}",
                    indexFormat = IndexFormat.UInt32
                };
                mesh.CombineMeshes(list.ToArray(), true, true, false);
                mesh.RecalculateBounds();

                string meshPath = $"{GeneratedChunkMeshFolder}/{SanitizeAssetName(_root.name)}_{key.x}_{key.y}_{key.z}_{createdRenderers}.asset";
                AssetDatabase.CreateAsset(mesh, meshPath);
                mf.sharedMesh = mesh;
                createdRenderers++;
            }

            EnsureRuntimeCuller(mergedRootGo.transform);

            if (deleteOriginal)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    PlacedBlock placed = sources[i];
                    if (placed != null)
                    {
                        Undo.DestroyObjectImmediate(placed.gameObject);
                    }
                }
            }

            EditorUtility.SetDirty(mergedRootGo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                L("window.title"),
                Lf("dialog.convert_chunk_summary", sources.Count, accepted, buckets.Count, createdRenderers, skippedUnknown, skippedAnimated, skippedInvalid),
                L("dialog.ok"));
        }

        private List<PlacedBlock> CollectPlacedBlocksFromRoot()
        {
            List<PlacedBlock> list = new List<PlacedBlock>();
            if (_root == null)
            {
                return list;
            }

            for (int i = 0; i < _root.childCount; i++)
            {
                Transform child = _root.GetChild(i);
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

            string cacheKey = BuildCardPreviewCacheKey(block);

            if (HasAnimatedFaces(block))
            {
                return GetOrBuildAnimatedBlockCardPreview(block);
            }

            if (_blockCardPreviewCache.TryGetValue(cacheKey, out Texture2D cached) && cached != null)
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

            Texture2D preview = RenderBlockCardPreview(mesh, renderData.materials[0], block.placementRotationQuarter);
            if (preview != null)
            {
                _blockCardPreviewCache[cacheKey] = preview;
                return preview;
            }

            return block.previewTexture;
        }

        private Texture2D GetOrBuildAnimatedBlockCardPreview(BlockDefinition block)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.id))
            {
                return null;
            }

            if (!BlockAssetFactory.TryGetFaceRenderData(block.sideTexturePaths, out BlockAssetFactory.FaceRenderData renderData)
                || renderData == null
                || renderData.materials == null
                || renderData.materials.Length == 0
                || renderData.materials[0] == null
                || renderData.faceMainTexSt == null
                || renderData.faceMainTexSt.Length < SideOrder.Length)
            {
                return block.previewTexture;
            }

            float now = Time.realtimeSinceStartup;
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

        private Texture2D RenderBlockCardPreview(Mesh mesh, Material material, int rotationQuarter)
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

            Texture2D onBlack = RenderPreviewWithBackground(mesh, material, new Color(0f, 0f, 0f, 1f), rotationQuarter);
            Texture2D onWhite = RenderPreviewWithBackground(mesh, material, new Color(1f, 1f, 1f, 1f), rotationQuarter);
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

        private Texture2D RenderPreviewWithBackground(Mesh mesh, Material material, Color backgroundColor, int rotationQuarter)
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

            Quaternion modelRot = Quaternion.Euler(0f, (rotationQuarter & 3) * 90f, 0f);
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
            bool hasAnimatedVisible = false;
            for (int i = 0; i < _filteredBlocks.Count; i++)
            {
                if (HasAnimatedFaces(_filteredBlocks[i]))
                {
                    hasAnimatedVisible = true;
                    break;
                }
            }

            if (!hasAnimatedVisible)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextAnimatedPreviewRepaintTime)
            {
                return;
            }

            _nextAnimatedPreviewRepaintTime = now + (1.0 / 12.0);
            Repaint();
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
