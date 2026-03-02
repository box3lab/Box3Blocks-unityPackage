using Box3Blocks.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器噪声生成示例窗口。
/// 该窗口用于演示如何在编辑模式下通过 <see cref="Box3Api"/> 批量放置方块。
/// <para/>
/// Editor noise-generation demo window (single feature).
/// Demonstrates batch block placement in Edit Mode via <see cref="Box3Api"/>.
/// </summary>
public sealed class Box3EditorApiDemoWindow : EditorWindow
{
    /// <summary>
    /// 方块根节点。
    /// 生成的方块将作为该节点子物体；为空时会自动创建名为 "Box3Root" 的对象。
    /// <para/>
    /// Block root transform.
    /// Generated blocks are parented under this transform; when null, a "Box3Root" object is created automatically.
    /// </summary>
    private Transform _root;

    /// <summary>
    /// 生成使用的方块 ID（例如 stone / grass / glass）。
    /// <para/>
    /// Block id used for generation (for example: stone / grass / glass).
    /// </summary>
    private string _blockId = "stone";
    private int _blockIdIndex;
    private readonly List<string> _availableBlockIds = new List<string>();

    /// <summary>
    /// 生成区域在 X 方向的尺寸（格）。
    /// <para/>
    /// Generation area size on X axis (in grid units).
    /// </summary>
    private int _sizeX = 24;

    /// <summary>
    /// 生成区域在 Z 方向的尺寸（格）。
    /// <para/>
    /// Generation area size on Z axis (in grid units).
    /// </summary>
    private int _sizeZ = 24;

    /// <summary>
    /// 列高上限（每个 x,z 的最大堆叠高度）。
    /// <para/>
    /// Maximum column height (max stacked blocks per x,z).
    /// </summary>
    private int _maxHeight = 8;

    /// <summary>
    /// 基础 Y 偏移（地形底部起始高度）。
    /// <para/>
    /// Base Y offset (starting height of terrain bottom).
    /// </summary>
    private int _baseY;

    /// <summary>
    /// 噪声采样缩放。
    /// 值越小，地形变化越平缓；值越大，地形起伏越频繁。
    /// <para/>
    /// Noise sampling scale.
    /// Smaller values create smoother terrain; larger values create more frequent variation.
    /// </summary>
    private float _noiseScale = 0.12f;

    /// <summary>
    /// 噪声种子（通过偏移采样坐标实现不同地形）。
    /// <para/>
    /// Noise seed (different terrains are produced by offsetting sample coordinates).
    /// </summary>
    private int _seed;

    /// <summary>
    /// 生成/重建碰撞体时使用的模式。
    /// <para/>
    /// Collider mode used by block placement and collider rebuild.
    /// </summary>
    private Box3ColliderMode _colliderMode = Box3ColliderMode.Full;

    /// <summary>
    /// 窗口底部状态文本。
    /// <para/>
    /// Status text shown at the bottom of the window.
    /// </summary>
    private string _status = "就绪。";

    /// <summary>
    /// 打开示例窗口。
    /// 菜单路径：Box3/Samples/Editor API Demo。
    /// <para/>
    /// Open demo window.
    /// Menu path: Box3/Samples/Editor API Demo.
    /// </summary>
    [MenuItem("Box3/编辑端示例/噪音生成", false, 230)]
    private static void Open()
    {
        GetWindow<Box3EditorApiDemoWindow>("Box3 噪声示例");
    }

    /// <summary>
    /// 启用时刷新方块 ID 列表。
    /// <para/>
    /// Refresh block-id list when window is enabled.
    /// </summary>
    private void OnEnable()
    {
        RefreshBlockIds();
    }

    /// <summary>
    /// 绘制窗口 UI。
    /// 该示例故意保持为最小交互：参数输入 + 一个生成按钮。
    /// <para/>
    /// Draw window UI.
    /// Intentionally minimal interaction: parameter inputs + one generate button.
    /// </summary>
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Box3 噪声地形示例", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("在编辑模式下生成简单噪声地形。", MessageType.Info);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _root = (Transform)EditorGUILayout.ObjectField("根节点", _root, typeof(Transform), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawBlockIdField();
             
            }
            _sizeX = Mathf.Max(1, EditorGUILayout.IntField("尺寸X", _sizeX));
            _sizeZ = Mathf.Max(1, EditorGUILayout.IntField("尺寸Z", _sizeZ));
            _maxHeight = Mathf.Max(1, EditorGUILayout.IntField("最大高度", _maxHeight));
            _baseY = EditorGUILayout.IntField("基础Y", _baseY);
            _noiseScale = Mathf.Max(0.001f, EditorGUILayout.FloatField("噪声缩放", _noiseScale));
            _seed = EditorGUILayout.IntField("种子", _seed);
            _colliderMode = (Box3ColliderMode)EditorGUILayout.EnumPopup("碰撞模式", _colliderMode);
        }

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("生成噪声地形"))
        {
            GenerateNoise();
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(_status, MessageType.None);
    }

    /// <summary>
    /// 绘制方块 ID 输入；有列表时显示下拉，无列表时显示文本输入。
    /// <para/>
    /// Draw block id selector: popup if list exists, text input otherwise.
    /// </summary>
    private void DrawBlockIdField()
    {
        if (_availableBlockIds.Count == 0)
        {
            _blockId = EditorGUILayout.TextField("方块ID", _blockId);
            return;
        }

        _blockIdIndex = Mathf.Clamp(_blockIdIndex, 0, _availableBlockIds.Count - 1);
        _blockIdIndex = EditorGUILayout.Popup("方块ID", _blockIdIndex, _availableBlockIds.ToArray());
        _blockId = _availableBlockIds[_blockIdIndex];
    }

    /// <summary>
    /// 从 Box3Api 读取可用方块 ID，并同步当前选中项。
    /// <para/>
    /// Load available block ids from Box3Api and sync current selection.
    /// </summary>
    private void RefreshBlockIds()
    {
        _availableBlockIds.Clear();
        IReadOnlyList<string> ids = Box3Api.GetAvailableBlockIds();
        if (ids != null)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(ids[i]))
                {
                    _availableBlockIds.Add(ids[i]);
                }
            }
        }

        if (_availableBlockIds.Count == 0)
        {
            return;
        }

        int index = _availableBlockIds.FindIndex(id => id == _blockId);
        _blockIdIndex = index >= 0 ? index : 0;
        _blockId = _availableBlockIds[_blockIdIndex];
    }

    /// <summary>
    /// 生成噪声地形。
    /// 流程：
    /// 1) 预热 Box3 资源；
    /// 2) 确保 root 可用；
    /// 3) 使用 PerlinNoise 计算每列高度；
    /// 4) 逐层放置方块；
    /// 5) 合并 Undo 记录，便于一次撤销。
    /// <para/>
    /// Generate noise terrain.
    /// Flow:
    /// 1) prepare Box3 assets;
    /// 2) ensure root exists;
    /// 3) compute per-column height with PerlinNoise;
    /// 4) place blocks layer by layer;
    /// 5) collapse Undo operations for one-step revert.
    /// </summary>
    private void GenerateNoise()
    {
        if (!Box3Api.PrepareGeneratedAssets())
        {
            _status = "资源准备失败，请查看 Console 中缺失 shader/资源的详细信息。";
            return;
        }

        if (_root == null)
        {
            GameObject go = GameObject.Find("Box3Root") ?? new GameObject("Box3Root");
            _root = go.transform;
        }

        float offsetX = _seed * 0.173f;
        float offsetZ = _seed * 0.271f;
        int placed = 0;

        Undo.SetCurrentGroupName("Box3 生成噪声地形");
        int group = Undo.GetCurrentGroup();

        for (int x = 0; x < _sizeX; x++)
        {
            for (int z = 0; z < _sizeZ; z++)
            {
                float n = Mathf.PerlinNoise(offsetX + x * _noiseScale, offsetZ + z * _noiseScale);
                int h = Mathf.Max(1, Mathf.RoundToInt(n * _maxHeight));

                for (int y = 0; y < h; y++)
                {
                    if (Box3Api.TryPlaceBlockAt(
                            _root,
                            _blockId,
                            new Vector3Int(x, _baseY + y, z),
                            true,
                            Box3QuarterTurn.R0,
                            null,
                            _colliderMode))
                    {
                        placed++;
                    }
                }
            }
        }

        Undo.CollapseUndoOperations(group);
        _status = placed > 0
            ? $"已生成噪声地形：{placed} 个方块。"
            : "生成完成，但放置数量为 0。请检查方块ID与 Console 日志。";
        Selection.activeTransform = _root;
    }
}
