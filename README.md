# Box3Blocks（神岛材质包）-- Unity Package

[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![License](https://img.shields.io/badge/License-Apache--2.0-green.svg)](LICENSE.md)
[![Samples](https://img.shields.io/badge/Samples-Editor%20%26%20Runtime-orange)](Samples~)

[简体中文](README.md) | [English](README_EN.md)

一个 Unity 编辑器扩展包，用于导入、导出和编辑神岛方块世界。  
本包同时提供二次开发 API，可在**编辑器扩展**与**运行时脚本**中调用，统一执行方块放置、删除、替换、旋转与查询。

1. 让美术/关卡在 Unity / Minecraft 内编辑方块世界。
2. 让程序可以通过统一 API 在编辑期和运行期操作方块。
3. 将纹理图集、网格、材质等生成流程标准化，减少人工维护。

## 环境要求

- Unity 版本：`2022.3`（LTS）
- 目标平台：Editor + Runtime
- 依赖：无额外外部包硬依赖（使用 Unity 内置能力）

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

- 包内路径：`Packages/com.box3lab.box3/Editor/SourceAssets/block-id.json`
- 用途：方块 ID、分类、列表展示来源。

2. `block-spec.json`（行为/渲染规则）

- 包内路径：`Packages/com.box3lab.box3/Editor/SourceAssets/block-spec.json`
- 用途：透明、发光等规则。

### 生成资源位置

工具运行后会在项目 `Assets/Box3` 下生成资源：

- `Assets/Box3/Textures`
- `Assets/Box3/Materials`
- `Assets/Box3/Meshes`

## 二次开发 API

可通过 Package Manager 导入示例：

1. 打开 `Window > Package Manager`
2. 选中 `Box3 Blocks`
3. 进入 `Samples` 标签页
4. 点击导入 Demo

### Editor API（编辑器）

命名空间：

```csharp
using Box3Blocks.Editor;
using UnityEngine;
```

入口类型：

- `Box3Blocks.Editor.Box3Api`
- `Box3Blocks.Editor.Box3QuarterTurn`

核心方法（Editor）：

- 放置：`TryPlaceBlockAt`、`TryPlaceBlockOnTop`、`PlaceBlocksInBounds`
- 删除：`EraseBlockAt`、`EraseBlocksInBounds`
- 替换：`ReplaceBlockAt`、`ReplaceBlocksInBounds`
- 旋转：`RotateBlockAt`、`RotateBlocksInBounds`
- 查询：`TryGetBlockIdAt`、`ExistsAt`、`GetTopY`、`GetAvailableBlockIds`、`IsTransparent`
- 资源：`PrepareGeneratedAssets`
- Chunk 构建：`BuildChunkFromRoot`
- 发光默认策略：`SetSpawnRealtimeLightForEmissive`、`GetSpawnRealtimeLightForEmissive`
- 碰撞体模式：放置/替换相关 API 支持 `colliderMode`（`None` / `TopOnly` / `Full`）

### Runtime API（运行时）

命名空间：

```csharp
using Box3Blocks;
using UnityEngine;
```

入口类型：

- `Box3Blocks.Box3Api`
- `Box3Blocks.Box3QuarterTurn`

核心方法（Runtime）：

- 放置：`TryPlaceBlockAt`、`TryPlaceBlockOnTop`、`PlaceBlocksInBounds`
- 删除：`EraseBlockAt`、`EraseBlocksInBounds`
- 替换：`ReplaceBlockAt`、`ReplaceBlocksInBounds`
- 旋转：`RotateBlockAt`、`RotateBlocksInBounds`
- 查询：`TryGetBlockIdAt`、`ExistsAt`、`GetTopY`、`GetAvailableBlockIds`、`IsTransparent`
- 资源检查：`PrepareGeneratedAssets`
- 发光默认策略：`SetSpawnRealtimeLightForEmissive`、`GetSpawnRealtimeLightForEmissive`
- 运行时目录：`SetDefaultRuntimeCatalog`、`GetDefaultRuntimeCatalog`
- 碰撞体模式：
  - 放置/替换相关 API 支持 `colliderMode`（`None` / `TopOnly` / `Full`）
  - 可设置默认模式：`SetDefaultColliderMode`、`GetDefaultColliderMode`

旋转参数说明（Editor/Runtime 通用 `Box3QuarterTurn`）：

- `R0 = 0°`
- `R90 = 90°`
- `R180 = 180°`
- `R270 = 270°`

## 📄 许可证

本项目采用 [Apache License 2.0](LICENSE.md) 许可证。

## 🙏 致谢

- 神奇代码岛提供的方块，神岛实验室移植

## 星历史

[![Star History Chart](https://api.star-history.com/svg?repos=box3lab/Box3Blocks-unityPackage&type=date&legend=top-left)](https://www.star-history.com/#box3lab/Box3Blocks-unityPackage&type=date&legend=top-left)
