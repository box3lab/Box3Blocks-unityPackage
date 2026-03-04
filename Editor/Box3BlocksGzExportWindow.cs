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
        private const string BlockIdPath = "Packages/com.box3lab.box3/Editor/SourceAssets/block-id.json";
        private static readonly Regex FlatMapRegex = new Regex("\"(?<id>\\d+)\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

        [Serializable]
        private sealed class VoxelPayload
        {
        
            public string formatVersion;
            public int[] shape;
            public int[] dir;
            public int[] indices;
            public int[] data;
            public int[] rot;
            public int[] lightIndices;
            public int[] lightFlags;
            public float[] lightIntensity;
            public float[] lightRange;
            public float[] lightColorRgb;
            public float[] lightOffsetXyz;
        }

        private Transform _exportRoot;
        private string _exportGzPath = string.Empty;
        private bool _exportRealtimeLightData = true;
        private string _status = string.Empty;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _primaryButtonStyle;
        private GUIStyle _dangerButtonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _subtleLabelStyle;
        private GUIStyle _insetPanelStyle;

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
                _textFieldStyle = new GUIStyle(EditorStyles.textField) { fixedHeight = 22f };
            }

            if (_subtleLabelStyle == null)
            {
                _subtleLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.72f, 0.72f, 0.72f, 1f) }
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
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
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

                string previewPath = string.IsNullOrWhiteSpace(_exportGzPath) ? "-" : _exportGzPath;
                EditorGUILayout.LabelField(previewPath, _subtleLabelStyle);
                _exportRealtimeLightData = EditorGUILayout.ToggleLeft(L("voxel.export.include_realtime_light_data"), _exportRealtimeLightData);

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(_exportRoot == null);
                    if (GUILayout.Button(L("voxel.export.run"), _primaryButtonStyle))
                    {
                        ExportGz();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button(L("voxel.run.cancel"), _dangerButtonStyle))
                    {
                        _exportGzPath = string.Empty;
                        _status = string.Empty;
                    }
                }
            }
        }

        private void DrawStatusSection()
        {
            using (new EditorGUILayout.VerticalScope(_insetPanelStyle))
            {
                EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(_status) ? "-" : _status, MessageType.None);
            }
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
                List<bool> blockHasLight = _exportRealtimeLightData ? new List<bool>(allBlocks.Count) : null;
                List<Color> blockLightColor = _exportRealtimeLightData ? new List<Color>(allBlocks.Count) : null;
                List<float> blockLightIntensity = _exportRealtimeLightData ? new List<float>(allBlocks.Count) : null;
                List<float> blockLightRange = _exportRealtimeLightData ? new List<float>(allBlocks.Count) : null;
                List<Vector3> blockLightOffset = _exportRealtimeLightData ? new List<Vector3>(allBlocks.Count) : null;
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
                    if (_exportRealtimeLightData)
                    {
                        if (TryReadBlockRealtimeLightData(block.transform, out Color lightColor, out float intensity, out float range, out Vector3 localOffset))
                        {
                            blockHasLight.Add(true);
                            blockLightColor.Add(lightColor);
                            blockLightIntensity.Add(intensity);
                            blockLightRange.Add(range);
                            blockLightOffset.Add(localOffset);
                        }
                        else
                        {
                            blockHasLight.Add(false);
                            blockLightColor.Add(Color.black);
                            blockLightIntensity.Add(0f);
                            blockLightRange.Add(0f);
                            blockLightOffset.Add(Vector3.zero);
                        }
                    }
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

                int[] lightIndices = null;
                int[] lightFlags = null;
                float[] lightIntensity = null;
                float[] lightRange = null;
                float[] lightColorRgb = null;
                float[] lightOffsetXyz = null;
                if (_exportRealtimeLightData)
                {
                    List<int> compactLightIndices = new List<int>(count);
                    List<int> compactLightFlags = new List<int>(count);
                    List<float> compactLightIntensity = new List<float>(count);
                    List<float> compactLightRange = new List<float>(count);
                    List<float> compactLightColorRgb = new List<float>(count * 3);
                    List<float> compactLightOffsetXyz = new List<float>(count * 3);
                    for (int i = 0; i < count; i++)
                    {
                        if (!blockHasLight[i])
                        {
                            continue;
                        }

                        compactLightIndices.Add(indices[i]);
                        compactLightFlags.Add(1);
                        compactLightIntensity.Add(blockLightIntensity[i]);
                        compactLightRange.Add(blockLightRange[i]);
                        Color c = blockLightColor[i];
                        compactLightColorRgb.Add(c.r);
                        compactLightColorRgb.Add(c.g);
                        compactLightColorRgb.Add(c.b);
                        Vector3 o = blockLightOffset[i];
                        compactLightOffsetXyz.Add(o.x);
                        compactLightOffsetXyz.Add(o.y);
                        compactLightOffsetXyz.Add(o.z);
                    }

                    lightIndices = compactLightIndices.ToArray();
                    lightFlags = compactLightFlags.ToArray();
                    lightIntensity = compactLightIntensity.ToArray();
                    lightRange = compactLightRange.ToArray();
                    lightColorRgb = compactLightColorRgb.ToArray();
                    lightOffsetXyz = compactLightOffsetXyz.ToArray();
                }

                VoxelPayload payload = new VoxelPayload
                {
                    formatVersion = "unity",
                    shape = new[] { shapeX, shapeY, shapeZ },
                    dir = new[] { 1, 1, 1 },
                    indices = indices,
                    data = data,
                    rot = rot,
                    lightIndices = _exportRealtimeLightData ? lightIndices : null,
                    lightFlags = _exportRealtimeLightData ? lightFlags : null,
                    lightIntensity = _exportRealtimeLightData ? lightIntensity : null,
                    lightRange = _exportRealtimeLightData ? lightRange : null,
                    lightColorRgb = _exportRealtimeLightData ? lightColorRgb : null,
                    lightOffsetXyz = _exportRealtimeLightData ? lightOffsetXyz : null
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

        private static bool TryReadBlockRealtimeLightData(Transform blockTransform, out Color color, out float intensity, out float range, out Vector3 localOffset)
        {
            color = Color.white;
            intensity = 0f;
            range = 0f;
            localOffset = Vector3.zero;

            if (blockTransform == null)
            {
                return false;
            }

            Light[] lights = blockTransform.GetComponentsInChildren<Light>(true);
            if (lights == null || lights.Length == 0)
            {
                return false;
            }

            Light chosen = null;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type != LightType.Point)
                {
                    continue;
                }

                if (light.transform == blockTransform
                    || string.Equals(light.gameObject.name, "__VoxelLight", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(light.gameObject.name, "__BlockLight", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = light;
                    break;
                }

                if (chosen == null)
                {
                    chosen = light;
                }
            }

            if (chosen == null)
            {
                return false;
            }

            color = chosen.color;
            intensity = Mathf.Max(0f, chosen.intensity);
            range = Mathf.Max(0f, chosen.range);
            localOffset = blockTransform.InverseTransformPoint(chosen.transform.position);
            return true;
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
