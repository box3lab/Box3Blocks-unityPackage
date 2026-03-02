using Box3Blocks;
using UnityEngine;

/// <summary>
/// 运行时 API 示例：按键触发放置/删除/旋转。
/// 生成位置取主摄像机前方，并自动对齐到网格坐标。
/// <para/>
/// Runtime API demo: keyboard-driven place/erase/rotate.
/// Spawn position is in front of main camera and snapped to grid.
/// </summary>
public class RuntimeBlockApiExample : MonoBehaviour
{
    // 四个测试方块相对锚点的偏移位置（十字形分布）。
    // Relative offsets around anchor for four test blocks (cross layout).
    private static readonly Vector3Int[] Offsets =
    {
        new Vector3Int(-1, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),
    };

    /// <summary>
    /// 运行时目录资源（必须在 Inspector 赋值，确保打包后也可用）。
    /// <para/>
    /// Runtime catalog asset (assign in Inspector to ensure it is available in builds).
    /// </summary>
    public Box3BlocksRuntimeCatalog catalog;

    /// <summary>
    /// 不透明静态方块 ID。
    /// <para/>
    /// Block id for opaque static block.
    /// </summary>
    public string opaqueStaticId = "stone";

    /// <summary>
    /// 透明静态方块 ID。
    /// <para/>
    /// Block id for transparent static block.
    /// </summary>
    public string transparentStaticId = "bamboo";

    /// <summary>
    /// 不透明动画方块 ID。
    /// <para/>
    /// Block id for opaque animated block.
    /// </summary>
    public string opaqueAnimatedId = "lava01";

    /// <summary>
    /// 透明动画方块 ID。
    /// <para/>
    /// Block id for transparent animated block.
    /// </summary>
    public string transparentAnimatedId = "blue_decorative_light";

    /// <summary>
    /// 摄像机前方锚点距离（世界单位）。
    /// <para/>
    /// Camera-front anchor distance in world units.
    /// </summary>
    public float cameraAnchorDistance = 3f;

    // 运行时方块根节点，所有生成对象将作为它的子节点。
    // Runtime root transform that owns all spawned block objects.
    private Transform _runtimeRoot;

    /// <summary>
    /// 初始化运行时根节点，并在有 catalog 时注册为默认目录。
    /// <para/>
    /// Initialize runtime root and register catalog as default when assigned.
    /// </summary>
    private void Awake()
    {
        GameObject root = new GameObject("Box3_RuntimeRoot");
        root.transform.SetParent(transform, false);
        _runtimeRoot = root.transform;

        // 打包环境下建议显式指定 catalog，避免自动查找失败。
        // In player builds, explicitly setting catalog is recommended to avoid auto-resolve failure.
        if (catalog != null)
        {
            Box3Api.SetDefaultRuntimeCatalog(catalog);
        }
    }

    /// <summary>
    /// 键盘测试入口：
    /// 1 放置，2 删除，3 旋转。
    /// <para/>
    /// Keyboard test entry:
    /// 1 place, 2 erase, 3 rotate.
    /// </summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            PlaceTestBlocks();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            EraseTestBlocks();
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            RotateTestBlocks();
        }

       
    }

    /// <summary>
    /// 放置四类测试方块（静态不透明、静态透明、动画不透明、动画透明）。
    /// <para/>
    /// Place four test blocks (opaque static, transparent static, opaque animated, transparent animated).
    /// </summary>
    [ContextMenu("Place 4 Test Blocks")]
    public void PlaceTestBlocks()
    {
        if (!IsReady())
        {
            return;
        }

        Vector3Int targetAnchor = ResolveAnchor();
        Box3Api.TryPlaceBlockAt(_runtimeRoot, opaqueStaticId, targetAnchor + Offsets[0], true);
        Box3Api.TryPlaceBlockAt(_runtimeRoot, transparentStaticId, targetAnchor + Offsets[1], true);
        Box3Api.TryPlaceBlockAt(_runtimeRoot, opaqueAnimatedId, targetAnchor + Offsets[2], true);
        Box3Api.TryPlaceBlockAt(_runtimeRoot, transparentAnimatedId, targetAnchor + Offsets[3], true);
    }

    /// <summary>
    /// 删除四类测试方块。
    /// <para/>
    /// Erase the four test blocks.
    /// </summary>
    [ContextMenu("Erase 4 Test Blocks")]
    public void EraseTestBlocks()
    {
        if (!IsReady())
        {
            return;
        }

        Vector3Int targetAnchor = ResolveAnchor();
        for (int i = 0; i < Offsets.Length; i++)
        {
            Box3Api.EraseBlockAt(_runtimeRoot, targetAnchor + Offsets[i]);
        }
    }

    /// <summary>
    /// 旋转四类测试方块（+90°）。
    /// <para/>
    /// Rotate the four test blocks by +90 degrees.
    /// </summary>
    [ContextMenu("Rotate 4 Test Blocks")]
    public void RotateTestBlocks()
    {
        if (!IsReady())
        {
            return;
        }

        Vector3Int targetAnchor = ResolveAnchor();
        for (int i = 0; i < Offsets.Length; i++)
        {
            Box3Api.RotateBlockAt(_runtimeRoot, targetAnchor + Offsets[i], Box3QuarterTurn.R90);
        }
    }

    /// <summary>
    /// 解析本次操作使用的锚点。
    /// 若存在主摄像机，取其前方指定距离；否则回退到当前物体位置。
    /// <para/>
    /// Resolve action anchor.
    /// Uses main-camera-front position when available; otherwise falls back to current transform position.
    /// </summary>
    /// <returns>本次操作锚点。 / Anchor for current action.</returns>
    private Vector3Int ResolveAnchor()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return Vector3Int.RoundToInt(transform.position);
        }

        float distance = Mathf.Max(0.1f, cameraAnchorDistance);
        Vector3 world = cam.transform.position + cam.transform.forward * distance;
        return Vector3Int.RoundToInt(world);
    }

    /// <summary>
    /// 检查示例是否具备运行条件（仅要求运行时根节点已初始化）。
    /// <para/>
    /// Validate demo readiness (runtime root initialized).
    /// </summary>
    /// <returns>可执行返回 true。 / Returns true when ready.</returns>
    private bool IsReady()
    {
        return _runtimeRoot != null;
    }
}
