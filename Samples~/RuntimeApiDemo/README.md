# Runtime API Demo

这个示例演示如何在运行时调用 `Box3Api`：

- 按键`1`：在锚点周围一次生成 4 种测试方块
- 包含：静态不透明、静态透明、动画不透明、动画透明
- 按键`2`：删除这 4 个测试方块
- 按键`3`：旋转这 4 个测试方块（+90°）
- 在摄像机前方生成

## 使用步骤

1. 在 Package Manager 导入 `Runtime API Demo` Sample。
2. 新建一个空物体，挂载 `RuntimeBlockApiExample`。
3. 先确保已生成 `Assets/Box3` 资源（方块库工具会生成）。
4. 执行菜单：`Box3/运行时/构建 UV 目录`。
5. 将 `Assets/Box3/Runtime/Box3BlocksCatalog.asset` 赋给脚本的 `catalog` 字段（打包必需）。
6. 按需修改脚本中的 4 个 `blockId` 字段（静态/透明/动画/透明动画）。
7. 运行场景，按键盘上的`1/2/3` 测试 4 类方块的放置/删除/旋转。
