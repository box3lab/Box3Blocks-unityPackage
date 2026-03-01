# Box3 使用说明

这是一个给关卡编辑者使用的 Unity 方块世界工具。  
它的目标是：**在编辑器里快速搭建、导入、编辑、导出方块地形**。

## 这个工具能做什么

- 在 Scene 视图中像“笔刷”一样搭建方块世界
- 支持 `Place / Erase / Replace / Rotate` 四种编辑模式
- 支持批量尺寸编辑（水平与高度）
- 支持按分类/搜索选择方块，支持最近使用
- 识别透明方块、发光方块、动画方块
- 支持从 `.gz` 体素数据导入地形（含旋转）
- 支持把场景方块导出为 `.gz`
- 自动生成/复用图集与材质，减少手工处理

## 适用场景

- 快速原型：白盒地图、玩法验证
- 内容制作：体素地形、建筑拼装
- 数据互通：与外部 voxel/gz 流程互导

## 快速开始

1. 安装包（UPM）
```json
{
  "dependencies": {
    "com.box3lab.box3": "https://github.com/box3lab/Box3Blocks-unityPackage.git"
  }
}
```

2. 打开工具菜单
- `Box3/方块库`：可视化搭建
- `Box3/地形导入`：导入 `.gz`
- `Box3/地形导出`：导出 `.gz`

3. 在 `方块库` 中创建或指定 Root，然后直接在 Scene 里开始搭建

## 方块编辑功能

- `Place`：放置方块
- `Erase`：删除方块
- `Replace`：把命中的方块替换为当前选中方块
- `Rotate`：按 90° 旋转方块
- 尺寸控制：
- `Horizontal (X/Z)` 控制水平范围
- `Height (Y)` 控制垂直范围

## 导入 / 导出功能

### 导入（`Box3/地形导入`）
- 读取 `.gz` 体素数据
- 支持 `Chunk` 模式（性能优先）与 `Single Block` 模式（编辑优先）
- 可选择碰撞体策略（表面/完整）

### 导出（`Box3/地形导出`）
- 从 Root 下已放置方块导出 `.gz`
- 保留 block id、位置与旋转信息

## 资源与规则

包内默认资源目录：

- `Packages/com.box3lab.box3/Assets/block`
- `Packages/com.box3lab.box3/Assets/block-spec.json`
- `Packages/com.box3lab.box3/Assets/block-id.json`

方块贴图命名（六面）：

- `{id}_back.png`
- `{id}_bottom.png`
- `{id}_front.png`
- `{id}_left.png`
- `{id}_right.png`
- `{id}_top.png`

可选：

- `spec_{id}_block.png`：兜底贴图
- `*.png.mcmeta`：动画配置

## 特性说明

- `transparent: true`：按透明方块渲染
- `emissive/glow`：启用发光表现
- `fluid: true`：作为流体方块处理（如水流动）

## 生成内容位置

工具自动生成的资源在：

- `Assets/Box3/Textures/Atlases`
- `Assets/Box3/Materials`
- `Assets/Box3/Meshes`

## 多语言（i18n）

UI 文案：

- `Packages/com.box3lab.box3/Editor/I18n/blockworld-ui.zh-CN.json`
- `Packages/com.box3lab.box3/Editor/I18n/blockworld-ui.en.json`

方块名：

- `Packages/com.box3lab.box3/Editor/I18n/block-names.zh-CN.json`
- `Packages/com.box3lab.box3/Editor/I18n/block-names.en.json`

说明：窗口已不再使用硬编码 fallback 文案，新增文案请同步维护到 i18n JSON。
