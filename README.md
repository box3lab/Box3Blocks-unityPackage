# Box3 Blocks for Unity

一个 Unity 编辑器扩展包，用于导入、导出和编辑神岛方块世界。

## 环境要求

- Unity 2022.3 LTS
- 在 `Editor` 模式下使用本包工具

## 安装Package Manager

1. 打开 `Window > Package Manager`
2. 点击左上角 `+`
3. 选择 `Add package from git URL...`
4. 输入：

```text
https://github.com/box3lab/Box3Blocks-unityPackage.git
```

## 快速使用

安装完成后可在 Unity 菜单中打开：

- `Box3/方块库`：方块搭建与编辑
- `Box3/地形导入`：导入 `.gz`
- `Box3/地形导出`：导出 `.gz`

## 数据与资源说明

### blockId 数据在哪里看

1. `block-id.json`（方块 ID 主表）

- 包内路径：`Packages/com.box3lab.box3/Assets/block-id.json`
- 用途：方块 ID、分类、列表展示来源。

2. `block-spec.json`（行为/渲染规则）

- 包内路径：`Packages/com.box3lab.box3/Assets/block-spec.json`
- 用途：透明、发光等规则。

### 生成资源位置

工具运行后会在项目 `Assets/Box3` 下生成资源：

- `Assets/Box3/Textures`
- `Assets/Box3/Materials`
- `Assets/Box3/Meshes`

## 二次开发 API

命名空间：

```csharp
using Box3Blocks.Editor;
using UnityEngine;
```

入口类型：

- `Box3Blocks.Editor.Box3Api`
- `Box3Blocks.Editor.Box3QuarterTurn`

### 使用前准备

```csharp
[UnityEditor.MenuItem("Tools/Box3/API Example/Init")]
private static void Init()
{
    var rootGo = GameObject.Find("Box3Root") ?? new GameObject("Box3Root");
    Box3Api.PrepareGeneratedAssets();
    Debug.Log($"Block count: {Box3Api.GetAvailableBlockIds().Count}");
}
```

### 放置 API

- `TryPlaceBlockAt(root, blockId, position, replaceExisting = true, rotationQuarter = Box3QuarterTurn.R0)`
- `TryPlaceBlockOnTop(root, blockId, x, z, baseY = 0, replaceExisting = true, rotationQuarter = Box3QuarterTurn.R0)`
- `PlaceBlocksInBounds(root, blockId, minInclusive, maxInclusive, replaceExisting = true, rotationQuarter = Box3QuarterTurn.R0)`

示例：

```csharp
var root = GameObject.Find("Box3Root")?.transform;
if (root == null) return;

Box3Api.PrepareGeneratedAssets();

Box3Api.TryPlaceBlockAt(root, "stone", new Vector3Int(10, 5, 10), true, Box3QuarterTurn.R90);
Box3Api.TryPlaceBlockOnTop(root, "blue_decorative_light", 12, 12, 0, true, Box3QuarterTurn.R0);

int placed = Box3Api.PlaceBlocksInBounds(
    root,
    "grass",
    new Vector3Int(0, 0, 0),
    new Vector3Int(4, 2, 4),
    true,
    Box3QuarterTurn.R0);

Debug.Log($"Placed: {placed}");
```

### 删除 / 替换 / 旋转 API

- `EraseBlockAt(root, position)`
- `EraseBlocksInBounds(root, minInclusive, maxInclusive)`
- `ReplaceBlockAt(root, blockId, position, rotationQuarter = Box3QuarterTurn.R0)`
- `ReplaceBlocksInBounds(root, blockId, minInclusive, maxInclusive, rotationQuarter = Box3QuarterTurn.R0)`
- `RotateBlockAt(root, position, stepQuarter = Box3QuarterTurn.R90)`
- `RotateBlocksInBounds(root, minInclusive, maxInclusive, stepQuarter = Box3QuarterTurn.R90)`

旋转参数说明（`Box3QuarterTurn`）：

- `R0 = 0°`
- `R90 = 90°`
- `R180 = 180°`
- `R270 = 270°`

### 查询 API

- `TryGetBlockIdAt(root, position, out blockId)`
- `ExistsAt(root, position)`
- `GetTopY(root, x, z, fallbackY = 0)`
- `GetAvailableBlockIds()`
- `IsTransparent(blockId)`
- `PrepareGeneratedAssets()`：主动生成 `Assets/Box3` 下的网格/图集/材质

示例：

```csharp
var root = GameObject.Find("Box3Root")?.transform;
if (root == null) return;

var p = new Vector3Int(5, 2, 5);
if (Box3Api.TryGetBlockIdAt(root, p, out var id))
{
    Debug.Log($"Block at {p}: {id}, transparent={Box3Api.IsTransparent(id)}");
}

int topY = Box3Api.GetTopY(root, 5, 5, 0);
Debug.Log($"TopY(5,5) = {topY}");
```
