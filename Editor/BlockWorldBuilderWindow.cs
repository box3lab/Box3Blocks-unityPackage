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
        private static readonly string[] SideOrder = { "back", "bottom", "front", "left", "right", "top" };
        private static readonly Regex SideRegex = new Regex(@"^(.*)_(back|bottom|front|left|right|top)\.png$", RegexOptions.Compiled);
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

        private class BlockDefinition
        {
            public string id;
            public Dictionary<string, string> sideTexturePaths = new Dictionary<string, string>();
            public Dictionary<string, FaceAnimationSpec> sideAnimations = new Dictionary<string, FaceAnimationSpec>();
            public bool hasAnimation;
            public Texture2D previewTexture;
            public int numericId = -1;
            public string category = "Uncategorized";
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
        private List<string> _categories = new List<string> { "All" };
        private int _selectedIndex;
        private int _selectedCategory;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private EditTool _tool = EditTool.Place;
        private int _brushHorizontalSize = 1;
        private int _brushHeight = 1;
        private const float PreviewSize = 75f;

        [MenuItem("Tools/Block World MVP/World Builder")]
        public static void Open()
        {
            GetWindow<BlockWorldBuilderWindow>("Block World Builder");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            ReloadBlockLibrary();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            DrawRootSection();
            EditorGUILayout.Space(6f);
            DrawToolSection();
            EditorGUILayout.Space(6f);
            DrawBlockListSection();
        }

        private void DrawRootSection()
        {
            EditorGUILayout.LabelField("World Root", EditorStyles.boldLabel);
            _root = (Transform)EditorGUILayout.ObjectField("Root", _root, typeof(Transform), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Root"))
                {
                    CreateRoot();
                }

                if (GUILayout.Button("Clear Root"))
                {
                    ClearRoot();
                }

                if (GUILayout.Button("Clean Materials"))
                {
                    int deleted = CleanupUnusedGeneratedMaterials();
                    EditorUtility.DisplayDialog("Block World Builder", $"Removed {deleted} unused generated materials.", "OK");
                }
            }
        }

        private void DrawToolSection()
        {
            EditorGUILayout.LabelField("Editor Tool", EditorStyles.boldLabel);
            _tool = (EditTool)GUILayout.Toolbar((int)_tool, new[] { "Place", "Erase", "Replace", "Rotate" });
            _brushHorizontalSize = Mathf.Max(1, EditorGUILayout.IntField("Horizontal Size (X/Z)", _brushHorizontalSize));
            _brushHeight = Mathf.Max(1, EditorGUILayout.IntField("Height (Y)", _brushHeight));
            EditorGUILayout.LabelField($"Brush Volume: {_brushHorizontalSize}x{_brushHorizontalSize}x{_brushHeight}");

            if (_root == null)
            {
                EditorGUILayout.HelpBox("Assign or create Root first. Scene click edit works only when Root exists.", MessageType.Info);
            }
        }

        private void DrawBlockListSection()
        {
            EditorGUILayout.LabelField("Block Library", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                string newSearch = EditorGUILayout.TextField("Search", _search);
                if (!string.Equals(newSearch, _search, StringComparison.Ordinal))
                {
                    _search = newSearch;
                    ApplyFilter();
                }

                if (GUILayout.Button("Reload", GUILayout.Width(70)))
                {
                    ReloadBlockLibrary();
                }
            }

            if (_categories.Count > 0)
            {
                DrawCategoryTabsWrapped();
            }

            int columns = CalculateColumnCount();
            EditorGUILayout.LabelField($"Blocks: {_filteredBlocks.Count}  |  Layout: {columns} columns");
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
                info = "Anim";
            }

            if (block.emitsLight)
            {
                info = string.IsNullOrEmpty(info) ? "Glow" : $"{info} | Glow";
            }
            if (block.transparent)
            {
                info = string.IsNullOrEmpty(info) ? "Transparent" : $"{info} | Transparent";
            }

            string title = string.IsNullOrWhiteSpace(block.displayName) ? block.id : block.displayName;
            string subtitle = string.IsNullOrWhiteSpace(info) ? block.category : $"{block.category} | {info}";
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
            if (block.previewTexture != null)
            {
                EditorGUI.DrawPreviewTexture(previewRect, block.previewTexture, null, ScaleMode.ScaleToFit);
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
            EditorGUI.LabelField(badgeRect, "SELECTED", new GUIStyle(EditorStyles.miniLabel)
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
                string label = _categories[i];
                float buttonWidth = Mathf.Clamp(EditorStyles.miniButton.CalcSize(new GUIContent(label)).x + padX, 52f, 220f);

                if (lineWidth > 0f && lineWidth + buttonWidth > availableWidth)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    lineWidth = 0f;
                }

                bool selected = i == _selectedCategory;
                bool clicked = GUILayout.Toggle(selected, label, EditorStyles.miniButton, GUILayout.Width(buttonWidth));
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

            Material[] materials = BlockAssetFactory.GetFaceMaterials(definition.sideTexturePaths, definition.transparent);
            Mesh cubeMesh = BlockAssetFactory.GetOrCreateCubeMesh();
            if (cubeMesh == null || materials == null)
            {
                return false;
            }

            GameObject go = new GameObject($"{definition.id}_{position.x}_{position.y}_{position.z}");
            Undo.RegisterCreatedObjectUndo(go, "Place Block");

            go.transform.SetParent(_root);
            go.transform.position = position;

            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = cubeMesh;

            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            ConfigureRendererMaterials(meshRenderer, materials, definition);

            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = cubeMesh;

            PlacedBlock marker = go.AddComponent<PlacedBlock>();
            marker.BlockId = definition.id;
            marker.HasAnimation = definition.hasAnimation;

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
            IEnumerable<BlockDefinition> source = string.Equals(selectedCategory, "Recent", StringComparison.OrdinalIgnoreCase)
                ? EnumerateRecentBlocks()
                : _allBlocks;

            foreach (BlockDefinition block in source)
            {
                bool matchSearch = string.IsNullOrWhiteSpace(_search)
                    || block.id.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (!string.IsNullOrWhiteSpace(block.displayName) && block.displayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
                bool matchCategory = selectedCategory == "All"
                    || string.Equals(selectedCategory, "Recent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(block.category, selectedCategory, StringComparison.OrdinalIgnoreCase);
                if (matchSearch && matchCategory)
                {
                    _filteredBlocks.Add(block);
                }
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _filteredBlocks.Count - 1));
        }

        private static void ConfigureRendererMaterials(MeshRenderer renderer, Material[] baseMaterials, BlockDefinition definition)
        {
            if (renderer == null)
            {
                return;
            }

            if (!definition.hasAnimation || definition.sideAnimations.Count == 0)
            {
                renderer.sharedMaterials = baseMaterials;
                return;
            }

            Material[] instanceMaterials = new Material[baseMaterials.Length];
            Array.Copy(baseMaterials, instanceMaterials, baseMaterials.Length);
            List<BlockTextureAnimator.FaceAnimation> runtimeAnimations = new List<BlockTextureAnimator.FaceAnimation>();

            for (int i = 0; i < SideOrder.Length && i < instanceMaterials.Length; i++)
            {
                string side = SideOrder[i];
                if (!definition.sideAnimations.TryGetValue(side, out FaceAnimationSpec spec))
                {
                    continue;
                }

                if (spec == null || spec.frameCount <= 1 || instanceMaterials[i] == null)
                {
                    continue;
                }

                Material instance = new Material(instanceMaterials[i])
                {
                    name = instanceMaterials[i].name + "_anim"
                };
                instanceMaterials[i] = instance;

                runtimeAnimations.Add(new BlockTextureAnimator.FaceAnimation
                {
                    materialIndex = i,
                    frameCount = spec.frameCount,
                    frameDuration = Mathf.Max(0.01f, spec.frameDuration),
                    frames = spec.frames
                });
            }

            if (runtimeAnimations.Count == 0)
            {
                renderer.sharedMaterials = baseMaterials;
                return;
            }

            renderer.sharedMaterials = instanceMaterials;
            BlockTextureAnimator animator = renderer.gameObject.AddComponent<BlockTextureAnimator>();
            animator.SetAnimations(runtimeAnimations.ToArray());
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
            categories.Insert(0, "All");
            categories.Insert(1, "Recent");

            string current = _categories.Count > 0 && _selectedCategory < _categories.Count ? _categories[_selectedCategory] : "All";
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
                return "Uncategorized";
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

        private static class BlockAssetFactory
        {
            private static readonly string GeneratedRoot = "Assets/BlockWorldGenerated";
            private static readonly string MeshFolder = "Assets/BlockWorldGenerated/Meshes";
            private static readonly string MaterialFolder = "Assets/BlockWorldGenerated/Materials";
            private static readonly string MeshAssetPath = "Assets/BlockWorldGenerated/Meshes/BlockCube.asset";

            public static Mesh GetOrCreateCubeMesh()
            {
                EnsureFolders();
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
                if (mesh != null)
                {
                    return mesh;
                }

                mesh = BuildCubeMesh();
                AssetDatabase.CreateAsset(mesh, MeshAssetPath);
                AssetDatabase.SaveAssets();
                return mesh;
            }

            public static Material[] GetFaceMaterials(Dictionary<string, string> sideTexturePaths, bool transparent)
            {
                EnsureFolders();

                Material[] materials = new Material[SideOrder.Length];
                for (int i = 0; i < SideOrder.Length; i++)
                {
                    string side = SideOrder[i];
                    string texturePath;
                    if (!sideTexturePaths.TryGetValue(side, out texturePath))
                    {
                        texturePath = GetFallbackTexturePath(sideTexturePaths);
                    }

                    materials[i] = GetOrCreateMaterial(texturePath, transparent);
                }

                return materials;
            }

            private static string GetFallbackTexturePath(Dictionary<string, string> sideTexturePaths)
            {
                foreach (string side in SideOrder)
                {
                    if (sideTexturePaths.TryGetValue(side, out string path))
                    {
                        return path;
                    }
                }

                return null;
            }

            private static Material GetOrCreateMaterial(string texturePath, bool transparent)
            {
                if (string.IsNullOrWhiteSpace(texturePath))
                {
                    return null;
                }

                EnsureCrispTextureImport(texturePath, transparent);

                string fileName = Path.GetFileNameWithoutExtension(texturePath);
                string variant = transparent ? "trans" : "opaque";
                string materialAssetPath = $"{MaterialFolder}/{fileName}_{variant}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
                if (material != null)
                {
                    RefreshTextureSampling(material.mainTexture as Texture2D);
                    return material;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture != null)
                {
                    RefreshTextureSampling(texture);
                }

                Shader shader = transparent ? Shader.Find("Unlit/Transparent") : Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader)
                {
                    name = fileName,
                    mainTexture = texture
                };

                if (transparent)
                {
                    SetupTransparentMaterial(material);
                }

                AssetDatabase.CreateAsset(material, materialAssetPath);
                return material;
            }

            private static void EnsureCrispTextureImport(string texturePath, bool transparent)
            {
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer == null)
                {
                    return;
                }

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

                if (transparent && !importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            private static void RefreshTextureSampling(Texture2D texture)
            {
                if (texture == null)
                {
                    return;
                }

                texture.filterMode = FilterMode.Point;
                texture.anisoLevel = 0;
            }

            private static void SetupTransparentMaterial(Material material)
            {
                if (material == null)
                {
                    return;
                }

                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            private static void EnsureFolders()
            {
                if (!AssetDatabase.IsValidFolder(GeneratedRoot))
                {
                    AssetDatabase.CreateFolder("Assets", "BlockWorldGenerated");
                }

                if (!AssetDatabase.IsValidFolder(MeshFolder))
                {
                    AssetDatabase.CreateFolder(GeneratedRoot, "Meshes");
                }

                if (!AssetDatabase.IsValidFolder(MaterialFolder))
                {
                    AssetDatabase.CreateFolder(GeneratedRoot, "Materials");
                }
            }

            private static Mesh BuildCubeMesh()
            {
                Mesh mesh = new Mesh
                {
                    name = "BlockCube"
                };

                Vector3[] vertices = new Vector3[24]
                {
                    // Back
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3(0.5f, -0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f, -0.5f),
                    // Bottom
                    new Vector3(-0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, -0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    // Front
                    new Vector3(0.5f, -0.5f, 0.5f),
                    new Vector3(-0.5f, -0.5f, 0.5f),
                    new Vector3(-0.5f, 0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, 0.5f),
                    // Left
                    new Vector3(-0.5f, -0.5f, 0.5f),
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f, 0.5f),
                    // Right
                    new Vector3(0.5f, -0.5f, -0.5f),
                    new Vector3(0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f),
                    // Top
                    new Vector3(-0.5f, 0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f, 0.5f),
                    new Vector3(-0.5f, 0.5f, 0.5f)
                };

                Vector2[] uvs = new Vector2[24]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
                };

                mesh.vertices = vertices;
                mesh.uv = uvs;
                mesh.subMeshCount = 6;

                mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                mesh.SetTriangles(new[] { 4, 6, 5, 4, 7, 6 }, 1);
                mesh.SetTriangles(new[] { 8, 10, 9, 8, 11, 10 }, 2);
                mesh.SetTriangles(new[] { 12, 14, 13, 12, 15, 14 }, 3);
                mesh.SetTriangles(new[] { 16, 18, 17, 16, 19, 18 }, 4);
                mesh.SetTriangles(new[] { 20, 22, 21, 20, 23, 22 }, 5);

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
