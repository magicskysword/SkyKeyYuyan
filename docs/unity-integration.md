# SkyKeyYuyan Unity 接入与默认表现

Unity 适配把宿主设备、文本框和生命周期连接到 SKY 输入运行时。默认表现分别提供 UGUI 和 FairyGUI 的完整手柄输入界面。

## 模块组成

| 模块 | 内容 |
| --- | --- |
| Unity 转换核心 | 运行时初始化、主线程调度、玩家上下文、文本目标和生命周期桥接 |
| Legacy Input 适配 | 将宿主选择的 Legacy Input 设备事件转换为统一操作 |
| Input System 适配 | 将动作、设备和玩家绑定转换为统一操作 |
| UGUI 适配 | InputField 文本、选区、验证、导航及编辑态 |
| TMP 适配 | TMP_InputField 文本、选区、验证、导航及编辑态 |
| FairyGUI 适配 | GTextInput 目标及 FairyGUI 焦点和生命周期桥接 |
| UGUI 默认表现 | UGUI 输入面板、虚拟键盘和草稿正文 |
| FairyGUI 默认表现 | FairyGUI 输入面板、虚拟键盘和草稿正文 |

Unity 主适配包包含 UGUI、TMP、Legacy Input 和 Input System 接入代码。FairyGUI 适配及两套默认表现分别安装。具体包依赖见[分发与构建](distribution-and-build.md)。

## 接入流程

1. 安装 SKY 核心、Unity 适配、所需语言和表现包，以及宿主所需 UI 依赖。
2. 配置输入后端、玩家标识、用户标识（单用户游戏可只配置默认用户）、设备分配和各玩家 UI 根节点。
3. 显式注册语言模块及实现，手动调用同步或异步初始化，处理初始化结果。初始化成功的运行时实例默认成为全局默认实例。
4. 指定启用设备，默认启用手柄。
5. 配置目标自动发现与编辑态跟随，或通过宿主调用显式打开输入。打开输入默认使用全局默认实例，显式打开时也可以传入其他运行时实例。
6. 订阅会话结果，在完成后处理宿主业务，并按宿主生命周期释放服务。

Unity 转换核心以宿主身份创建和释放默认的 PlayerPrefs 存档 Provider 和资源加载器。更换语言数据或实现配置时，初始化新的运行时实例，见[系统架构](architecture.md#运行时实例)。

SkyUnityServices.Shared 提供进程共享的 PumpExecutionContext。宿主在 Unity 主线程调用 CreateOptions，将返回的配置用于运行时初始化。常驻 SkyUnityDispatcher 在主循环排空队列，编辑器中也可显式调用 Context.Drain。

UnityResourceProvider 从 `Resources/SkyKeyYuyan/` 加载语言包的 TextAsset。异步读取将 Unity API 调用投递到主线程，词典解析和索引准备在后台进行。PlayerPrefsFrequencyStore 在主线程读写版本化词频，将大快照拆为完整字符分块，最后更新存档头并集中调用 PlayerPrefs.Save。

同一设备事件选择一个接入来源。Unity 同时启用多个输入后端时，SKY 仍按宿主配置路由一次输入操作。

## 设备启用策略

| 键盘 | 手柄 | 行为 |
| --- | --- | --- |
| 启用 | 启用 | 两类设备均通过 SKY 路由 |
| 关闭 | 启用 | 手柄通过 SKY，键盘由宿主管理 |
| 启用 | 关闭 | 键盘通过 SKY，手柄由宿主管理 |
| 自定义 | 自定义 | 宿主按自己的设备和交互策略发送统一操作 |

表现层或宿主配置策略，平台适配执行实际事件路由。被 SKY 接管的输入同时与目标控件的原有输入路径协调，保证一次按键产生一次编辑。

`SkyNativeInputCoordination` 合并活动玩家的接管状态，在原生 EventSystem 前排空已由 SKY 处理的键盘事件。UGUI 适配保存并暂时关闭 EventSystem 导航，最后一个使用手柄的会话结束后还原原值；Legacy 输入能力启用时同步协调 IME 模式。键盘接管仍由每位玩家的启用设置决定。

## 文本框发现和编辑态

标准接入在指定玩家 UI 根节点内发现支持的文本框，覆盖运行期间创建的目标。特殊控件可通过显式注册文本目标接入。

`SkyInputHost.Configure` 创建指定后端的 `SkyUnityHost`，由组件推进非缩放时间的主循环。通过 `AddPlayer` 提供玩家配置和 `UguiTargetDiscovery`、`TmpTargetDiscovery` 或 `FairyTargetDiscovery`，再绑定设备并安装相应驱动。宿主持有玩家和发现器的生命周期，显式 `RegisterTarget` 的目标由调用方持有。`SkyTextTargetSettings` 可覆盖单个控件的默认模式与语言。

目标发现限定在传入的 UI 根内，并排除 `SkyInputSurface` 标记的面板内部控件。自动跟随原生编辑态的进入变化；原文被宿主修改而结束输入后，继续保持同一编辑态不会重新打开会话。玩家切换到新的原生目标时，先按当前模式取消旧会话，再打开新目标。应用焦点、应用暂停和宿主组件停用分别保留其暂停条件。

进入编辑态的交互由宿主决定。默认行为在已启用设备对应的文本框进入编辑态时打开 SKY；关闭此行为后，宿主负责显式打开。

虚拟键盘、候选列表和功能按键之间的焦点转移属于面板内部交互。目标销毁、宿主中断和外部正文变化通过相应会话结束原因处理。

## 手柄默认绑定

| 输入 | 默认行为 |
| --- | --- |
| 左摇杆及方向键 | 键盘模式中导航虚拟按键；正文模式中移动光标 |
| A | 激活当前虚拟按键 |
| B | 组字回退；组字为空时退格 |
| LB | 选择前一个候选 |
| RB | 选择后一个候选 |
| L3 | 切换正文编辑焦点 |
| R3 | 切换正文编辑焦点 |
| X | 确认当前有效候选 |
| Y | 按会话语言列表循环切换 |
| Start | 完成编辑并保存，关闭输入面板 |
| 右摇杆 | 键盘模式中浏览候选；正文模式中扩展选区 |

虚拟键盘提供取消、全选、向前删除、清空、换行及语言所需功能键。英语补全实现提供实时开关，字面输入状态提供重试转换。

方向重复、摇杆阈值和布局导航由输入及表现配置提供。宿主可替换布局和绑定。

连续竖向选键保持同一个逻辑列中心，横向选键或指针激活后重新确定该中心。每次摇杆采样只采用主方向，切换正文焦点后先等待摇杆回中，再接纳对应方向动作。

两套默认表现采用同一份六排对齐键盘布局，加宽键帽按逻辑列跨度排版。手柄焦点使用薄荷色填充、双层轮廓和 A 徽标；候选选中单独高亮。禁用功能键保留位置并降低明度，方向导航跳过这些按键。组字在正文光标处预览，候选浮窗随光标定位，面板随玩家区域缩放。尺寸、色彩和布局约定见[默认虚拟键盘](keyboard-design.md)。

## 多玩家和焦点

每个玩家具有独立设备路由、UI 根节点、活动会话及面板状态。宿主维护设备到玩家的绑定；词频归属使用对应用户标识，未指定时使用默认用户。

`SkyPlayerContext` 固定一位玩家的会话默认值，显式 `Open` 可传入运行时、编辑模式和初始语言。`DraftTargetFactory` 连接默认面板的原生草稿，`Panel` 提供共用的按键焦点、Shift、候选确认和正文操作逻辑。`Opened` 与 `Completed` 用于显示和关闭相应玩家面板。

`SkyPlayerOptions.TextInput` 配置默认正文类型，`Open(..., textInput: new SkyTextInputOptions(SkyTextInputType.MultiLine, 3))` 可按次覆盖。`SingleLine` 使用单行视口；`MultiLine` 的最低显示行数默认为三，限制在一至五。`SkyTargetBinding.TextInput` 和原生文本框上的 `SkyTextTargetSettings` 可覆盖单个目标。配置在打开会话前确定，表现层的草稿工厂可以读取当前 `SkyPlayerContext.TextInput`。

`SkyInputRouter` 在构造时固定事件后端。注册玩家后，将设备标识绑定到玩家，使用 `Route` 发送动作；设备不能同时绑定给两位玩家。后端提供递增设备事件序号时，重复事件保持无效。`KeyboardEnabled` 和 `GamepadEnabled` 分别控制路由。设备断开和应用失焦分别占用暂停原因，全部恢复后会话才继续活动。

宿主支持鼠标或触摸操作时，可绑定 `SkyDeviceKind.Pointer` 维持对应面板的输入可用状态。指针绑定独立于键盘与手柄开关，通过解除绑定停用。

安装并启用 Input System 后，`SkyInputSystemDriver` 位于独立程序集。宿主将具体 Gamepad 或 Keyboard 绑定给玩家，在主循环以非缩放时间调用 `Poll`。手柄采用默认绑定，方向和退格支持可配置的按住重复。键盘使用系统文本事件保留实际键盘布局和完整代理对，方向键编辑正文，Shift 扩展选区，Escape 取消，Tab 切换语言，Ctrl+A 全选。正文焦点中的多行输入使用 Enter 换行、Ctrl+Enter 完成，其余状态使用 Enter 完成。Legacy 键盘使用相同约定。键盘默认仍需显式启用。

启用 Legacy Input 时，`SkyLegacyInputDriver` 按一到八号手柄槽位绑定玩家。`LegacyGamepadProfile` 指定摇杆、方向键、扳机的轴名称和按钮索引，`ILegacyInputSource` 可替换平台采样。开发工程包含 `SkyPad1` 至 `SkyPad8` 的默认轴；其他工程可通过 `Tools/SkyKeyYuyan/添加 Legacy 默认输入轴` 补齐。默认轴采用 Windows 手柄配置，宿主可以在 Input Manager 中调整实际轴映射。菜单保留已有的同名轴设置。键盘由系统文本帧提供字符，同一帧重复采样保持一次输入。

默认界面支持双手柄、双面板和独立草稿。两套 UI 的验证都包含此场景。原框模式依赖对应目标适配的选区与焦点处理，并按控件逐项验证。

目标编辑权由运行时统一管理。第二个会话打开同一目标时收到目标占用结果，宿主可先结束原会话再转交编辑权。

设备断开时保留会话并暂停；重新绑定后继续。应用失焦按相同保留原则处理。

## 文本框适配

适配器以控件实际正文作为权威状态，并转换选区、行导航、验证结果和长度计数。三个框架使用相同的 SKY 文本编辑契约。

`UguiTextTarget` 和 `TmpTextTarget` 分别绑定已有的 InputField 和 TMP_InputField。调用 `Poll` 同步原生选区并发现未通知的正文变化；每次执行统一操作前，会话也重新读取当前编辑目标。`SetKeyboardOwned` 保存并临时接管原生输入配置，释放适配器时还原；`EndEditing` 退出原生编辑态。适配器的 `Diagnostic` 单独报告控件验证和通知异常。

多行竖直导航使用 UGUI TextGenerator 或 TMP 的实际排版信息，并在连续上下移动时保留期望横坐标。选区统一使用 UTF-16 偏移。文本写入只通知一次，已接受的同一操作重放返回缓存回执；完成会话后释放对应的操作记录。

FairyGUI SDK 使用名为 `FairyGUI` 的程序集。安装 SDK 后，在相应平台的 Scripting Define Symbols 中启用 `SKY_FAIRYGUI`，编译文本目标适配和默认表现。`FairyTextTarget` 绑定 GTextInput，按 SDK 的字符计数处理长度，并在统一接口边界转换 UTF-16 选区。适配器写入选区时保持当前全局焦点，独立会话的正文导航不会夺取其他文本框的焦点。

FairyGUI 公开的选区接口无法表达反向选区，因此适配器缓存读取其选区字段与验证方法；包内 `link.xml` 保留相应成员供 IL2CPP 使用。构造目标时检查所需成员，SDK 结构不兼容时明确报告失败。

原框插入保留未选中的正文前后缀；草稿最终回填使用全文替换。适配实现根据具体版本的控件行为组织验证、写入和通知顺序，并返回真实结果。

SKY 自己的写入带有操作归属，适配器用它区分宿主外部修改。外部正文修改和目标失效（例如控件被销毁）通过文本目标的 Changed 事件上报，会话按规则终止编辑，保存宿主最新文本。

组字在正文当前选区显示为临时预览，并带有下划线和显示光标。`SessionSnapshot.Text` 及原生正文目标继续反映已接受文本，`SkyCompositionText` 提供合并后的显示内容和临时范围。候选确认后，显示层恢复为原生正文和选区。`CommitComposition()` 提交当前组字并继续编辑，指针重新定位前使用这一操作。

## 默认表现

默认面板包含：

- 正文或草稿正文区域，以及光标、选区和行内组字预览。
- 跟随光标的候选浮窗、候选选择状态及查询处理中反馈。
- 可替换布局的虚拟键盘。
- 语言切换、完成、取消和对应语言功能。
- 恢复状态、字面输入和重试转换入口。

面板读取不可变会话快照，交互发送统一操作。开发者可使用相同接口创建自己的 UI。

写入结果需要确认时，虚拟键盘显示取消、使用当前正文和再次提交三个选项，初始焦点为取消。手柄方向导航与 A 键可以完成该决策，处理后恢复原语言布局。

`UguiSkyPanel.Create(parent, player, mixedFont)` 在指定 RectTransform 下创建并绑定默认界面。每位玩家使用独立父节点；面板按父节点尺寸缩放，在会话打开时显示，在关闭时隐藏。面板须在打开会话前绑定，以便安装原生草稿工厂。`DraftField` 允许宿主调整正文样式，`SetLanguageFont` 配置各语言的展示字体。

UGUI 草稿控件为每位玩家保留独立编辑态，虚拟按钮不会改变 EventSystem 的文本选择。候选按钮可直接确认，前后候选按钮支持跨页浏览。保存被宿主验证拒绝时保持面板打开；写入结果需要人工确认时显示对应处理入口。

`FairySkyPanel.Create(parent, player, mixedFont)` 从包内 Resources 加载默认界面，为每位玩家绑定独立的 GComponent 父节点。`View` 和 `DraftField` 暴露原生组件，`SetLanguageFont` 使用 FairyGUI 字体名称配置语言展示区。释放宿主时调用面板的 `Dispose`。非全局焦点草稿仍按自身 TextField 排版绘制光标和双向选区。

FairyGUI 原生源工程位于 `ui/FairyGUI/SkyKeyYuyan.fairy`，包含 Panel、Keyboard、Candidates 和 Key 四个组件。导出的 `SkyKeyYuyan_fui.bytes` 位于表现包的 `Runtime/Resources/SkyKeyYuyan/UI/FairyGUI/`。调整源工程后将发布目录指向此位置，保持资源与原生组件同步。

## 字体

候选浮窗和虚拟键盘文字允许按语言指定字体。正文、草稿和行内组字沿用宿主配置的混合语言字体及回退方案。

默认配置使用 UGUI、TMP 或 FairyGUI 对应组件的默认字体设置。宿主负责资源中的中文字形、韩文字形及混合文本覆盖。语言切换仅调整语言相关展示区的字体配置。

## 模块控制面板

编辑器面板显示依赖检测结果、配置开关和模块可用状态。模块启用同时满足依赖可用、平台能力可用及用户开关条件。

通过 `Tools/SkyKeyYuyan/模块控制面板` 检测已安装语言、UI 依赖和输入能力，并为当前构建平台应用开关。面板保留项目其他编译符号；UGUI、TMP、两个输入后端和两套表现可分别关闭。FairyGUI 适配在 SDK 可用后显式启用。

可选接入代码按程序集隔离，通过编译条件及包版本信息启用。Unity 的 Version Defines 和 Define Constraints 提供相应能力；具体条件需覆盖目标版本与依赖组合。[Unity 程序集定义文档](https://docs.unity3d.com/2020.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html)

FairyGUI SDK 的安装和自身依赖由宿主管理。SKY FairyGUI 包提供与所验证 SDK 版本对应的适配。
