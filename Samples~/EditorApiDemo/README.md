# Editor API Demo

这个示例只演示一个功能：在编辑模式下用噪声生成方块地形。
内部通过 `Box3Blocks.Editor.Box3Api.TryPlaceBlockAt(...)` 放置方块。

## 使用步骤

1. 在 Package Manager 导入 `Editor API Demo`。
2. 打开菜单：`Box3/Samples/Editor API Demo`。
3. 在窗口中设置参数：`Root`、`Block Id`、`Size X/Z`、`Max Height`、`Base Y`、`Noise Scale`、`Seed`。
4. 点击 `Generate Noise` 生成地形。

## 说明

- 该示例仅用于编辑模式（`EditorWindow`）。
- 若 `Root` 为空，点击生成时会自动创建 `Box3Root`。
- 示例脚本路径：`Samples~/EditorApiDemo/Editor/Box3EditorApiDemoWindow.cs`
