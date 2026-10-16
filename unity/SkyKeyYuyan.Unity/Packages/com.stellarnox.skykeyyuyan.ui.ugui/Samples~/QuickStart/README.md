# UGUI 完整输入示例

导入本示例后，打开 `Scenes` 中对应后端的 Single 或 Dual 场景并运行。Legacy 场景使用项目的 Input Manager 配置，可通过 `Tools/SkyKeyYuyan/添加 Legacy 默认输入轴` 补齐。Input System 场景需要安装并启用 Input System。

示例为每位玩家创建独立原框、草稿和虚拟键盘，绑定前两个手柄；实体键盘绑定首位玩家，默认关闭，可通过界面开关启用。鼠标可分别操作两块面板。完成或取消后，点击原框或“打开输入”再次编辑。

基础英语始终注册。已安装的中文、英语补全及韩语包通过程序集版本信息启用；默认注册中文和韩语，英语补全由示例组件上的 `EnglishCompletion` 选择。语言菜单按实际注册结果生成。

`MixedFont` 可配置宿主混合语言字体；为空时示例尝试系统字体。`Mode` 可切换原框和独立草稿模式。双玩家使用独立的用户标识保存词频。

场景由 `Tools/SkyKeyYuyan/示例` 菜单生成。核心接入流程位于 `SkyQuickStart`，后端绑定分别位于 `SkyLegacyQuickStart` 与 `SkyInputSystemQuickStart`。
