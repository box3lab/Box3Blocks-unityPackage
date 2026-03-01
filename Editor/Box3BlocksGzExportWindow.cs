using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Box3Blocks.Editor
{
    public sealed class Box3BlocksGzExportWindow : EditorWindow
    {
        private const string BlockIdPath = "Packages/com.box3lab.box3/Assets/block-id.json";
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

        private Transform _exportRoot;
        private string _exportGzPath = string.Empty;
        private string _status = string.Empty;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _textFieldStyle;

        [MenuItem("Box3/地形导出", false, 21)]
        public static void Open()
        {
            GetWindow<Box3BlocksGzExportWindow>(L("voxel.export.window.title"));
        }

        private void OnEnable()
        {
            _status = string.Empty;
        }

        private void OnGUI()
        {
            EnsureStyles();
            titleContent = new GUIContent(L("voxel.export.window.title"));

            DrawSection(L("voxel.section.export"), DrawExportSection);
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

            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(EditorStyles.textField) { fixedHeight = 22f };
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

            if (GUILayout.Button(L("voxel.export.run"), _primaryButtonStyle))
            {
                ExportGz();
            }
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(_status) ? "-" : _status, MessageType.None);
        }

        private void ExportGz()
        {
            try
            {
                if (_exportRoot == null)
                {
                    _status = L("voxel.export.err.no_root");
                    EditorUtility.DisplayDialog(L("voxel.export.window.title"), _status, L("dialog.ok"));
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
                    EditorUtility.DisplayDialog(L("voxel.export.window.title"), _status, L("dialog.ok"));
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
                    EditorUtility.DisplayDialog(L("voxel.export.window.title"), _status, L("dialog.ok"));
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
                EditorUtility.DisplayDialog(L("voxel.export.window.title"), done, L("dialog.ok"));
            }
            catch (Exception ex)
            {
                _status = ex.Message;
                EditorUtility.DisplayDialog(L("voxel.export.window.title"), ex.Message, L("dialog.ok"));
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

        private static Dictionary<string, int> LoadNameToBlockIdMap()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                    map[name] = id;
                }
            }

            return map;
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(root, assetPath).Replace("\\", "/");
        }
    }
}
