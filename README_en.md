# Box3Blocks（神岛材质包）-- Unity Package

[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![License](https://img.shields.io/badge/License-Apache--2.0-green.svg)](LICENSE.md)
[![Samples](https://img.shields.io/badge/Samples-Editor%20%26%20Runtime-orange)](Samples~)

[简体中文](README.md) | [English](README_EN.md)

A Unity editor extension package for importing, exporting, and editing Box3 block worlds.  
It also provides reusable APIs for both **Editor extensions** and **Runtime scripts**, with a unified workflow for placing, erasing, replacing, rotating, and querying blocks.

1. Enable artists/level designers to edit block worlds directly in Unity / Minecraft workflows.
2. Provide unified APIs for developers in both edit-time and runtime.
3. Standardize atlas, mesh, and material generation to reduce manual maintenance.

## Requirements

- Unity version: `2022.3` (LTS)
- Target: Editor + Runtime
- Dependencies: No extra external package dependencies

## Installation (Package Manager)

1. Open `Window > Package Manager`
2. Click `+` in the top-left corner
3. Select `Add package from git URL...`
4. Enter:

```text
https://github.com/box3lab/Box3Blocks-unityPackage.git
```

## Quick Start

After installation, open these menus in Unity:

- `Box3/方块库` (Block Library): build and edit blocks
- `Box3/地形导入` (Terrain Import): import `.gz`
- `Box3/地形导出` (Terrain Export): export `.gz`

## Data & Generated Assets

### Where to find blockId data

1. `block-id.json` (main block ID table)

- Package path: `Packages/com.box3lab.box3/Editor/SourceAssets/block-id.json`
- Usage: block IDs, categories, list source.

2. `block-spec.json` (behavior/render rules)

- Package path: `Packages/com.box3lab.box3/Editor/SourceAssets/block-spec.json`
- Usage: transparency, emissive rules, etc.

### Generated asset output path

Generated resources are written to project `Assets/Box3`:

- `Assets/Box3/Textures`
- `Assets/Box3/Materials`
- `Assets/Box3/Meshes`

## Extensibility API

You can import samples from Package Manager:

1. Open `Window > Package Manager`
2. Select `Box3 Blocks`
3. Open the `Samples` tab
4. Import demo samples

### Editor API

Namespace:

```csharp
using Box3Blocks.Editor;
using UnityEngine;
```

Entry types:

- `Box3Blocks.Editor.Box3Api`
- `Box3Blocks.Editor.Box3QuarterTurn`

Core methods (Editor):

- Place: `TryPlaceBlockAt`, `TryPlaceBlockOnTop`, `PlaceBlocksInBounds`
- Erase: `EraseBlockAt`, `EraseBlocksInBounds`
- Replace: `ReplaceBlockAt`, `ReplaceBlocksInBounds`
- Rotate: `RotateBlockAt`, `RotateBlocksInBounds`
- Query: `TryGetBlockIdAt`, `ExistsAt`, `GetTopY`, `GetAvailableBlockIds`, `IsTransparent`
- Resources: `PrepareGeneratedAssets`
- Emissive light default: `SetSpawnRealtimeLightForEmissive`, `GetSpawnRealtimeLightForEmissive`
- Collider mode: placement/replacement APIs support `colliderMode` (`None` / `TopOnly` / `Full`)

### Runtime API

Namespace:

```csharp
using Box3Blocks;
using UnityEngine;
```

Entry types:

- `Box3Blocks.Box3Api`
- `Box3Blocks.Box3QuarterTurn`

Core methods (Runtime):

- Place: `TryPlaceBlockAt`, `TryPlaceBlockOnTop`, `PlaceBlocksInBounds`
- Erase: `EraseBlockAt`, `EraseBlocksInBounds`
- Replace: `ReplaceBlockAt`, `ReplaceBlocksInBounds`
- Rotate: `RotateBlockAt`, `RotateBlocksInBounds`
- Query: `TryGetBlockIdAt`, `ExistsAt`, `GetTopY`, `GetAvailableBlockIds`, `IsTransparent`
- Resource validation: `PrepareGeneratedAssets`
- Runtime catalog: `SetDefaultRuntimeCatalog`, `GetDefaultRuntimeCatalog`
- Collider mode:
  - placement/replacement APIs support `colliderMode` (`None` / `TopOnly` / `Full`)
  - default mode APIs: `SetDefaultColliderMode`, `GetDefaultColliderMode`

Rotation enum (shared by Editor/Runtime `Box3QuarterTurn`):

- `R0 = 0°`
- `R90 = 90°`
- `R180 = 180°`
- `R270 = 270°`

## Documentation

- Detailed package docs: `Packages/com.box3lab.box3/Documentation~/index.md`
- In Package Manager, click `View documentation` to open it.
