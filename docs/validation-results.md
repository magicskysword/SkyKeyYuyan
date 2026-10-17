# SkyKeyYuyan 验证记录

软件版本为 0.2.0，语言数据版本为 2026.10.2，词典格式为 1，键盘格式为 2。验证日期为 2026-10-17，宿主系统为 Windows 11 64 位，构建号 26200。

## 托管库与数据

| 检查 | 结果 |
| --- | --- |
| Release 构建，netstandard2.0 核心与三个语言库 | 通过，零警告、零错误 |
| 核心 NUnit 测试 | 56 / 56 |
| 中文 NUnit 测试 | 26 / 26 |
| 英语补全 NUnit 测试 | 20 / 20 |
| 韩语 NUnit 测试 | 17 / 17 |
| Node 数据转换测试 | 6 / 6 |
| 独立 NuGet 消费项目 | 四个发布包安装成功，六项输入流程通过 |

测试覆盖正文和草稿契约、代理对与选区、候选版本、组字历史、语言动态加入、运行时替换、共享及隔离词频、异步加载和保存，以及查询、引擎、写入和通知故障恢复。分页检查覆盖并列排序、词频、自定义词条、多个读音及极大偏移。韩语组合测试遍历现代音节组合；中文真实词库样本覆盖 `hen → 很`、连续全拼和多音词。键盘检查覆盖格式一和二、跨度、重叠、中心导航及三种语言的完整字母区。

生成资源包含中文 263274 条读音记录和英语 274937 条词条，三个语言包均附默认键盘。来源、读音推导数量及缺失读音样本记录在 `artifacts/Reports/language-data.json`，发布许可由固定来源清单合并生成。

## Unity 版本矩阵

| 编辑器 | EditMode | PlayMode | Input System | TMP / UGUI |
| --- | --- | --- | --- | --- |
| 2020.3.49f1 | 58 / 58 | 20 / 20 | 1.0.2 | TMP 3.0.9、UGUI 1.0.0 |
| 2022.3.62f3 | 58 / 58 | 20 / 20 | 1.19.0 | TMP 3.0.9、UGUI 1.0.0 |
| 6000.3.10f1 | 58 / 58 | 20 / 20 | 1.19.0 | UGUI 2.0.0 内的 TMP |

FairyGUI 使用官方 Unity SDK 提交 `8cc8f214cca79685532eef13372d0a4f74acdf08`。EditMode 包含原生文本验证、候选与键盘控制、目标发现、生命周期、输入路由及 Input System 虚拟设备。PlayMode 使用实际 Stage、控件排版和原生焦点，检查 UGUI 与 FairyGUI 双草稿、反向选区、保存、取消、验证拒绝，以及宽空格的方向导航、禁用功能键和焦点徽标。候选区检查单字与词组的字体宽度、长词完整显示、手柄选择跟随、查询更新后的重新排版及指针确认。中文候选和双玩家面板的实际渲染截图按 Unity 版本保存。

手柄检查覆盖两个输入后端的 Y 确认、Select 换语言、右摇杆浏览候选、摇杆按下切换正文焦点、左摇杆移动光标与右摇杆框选，以及回中门槛、重复输入和主方向判定。单行、多行最低行数、默认三行、五行内容、上下光标与选区、焦点退出后的端点保留、提示居中和输入后竖向折返均已检查。

Unity 2022.3 的依赖组合编译同时覆盖：不安装可选 UI 与输入依赖、仅 UGUI、仅 Input System、安装依赖并关闭全部 SKY 可选模块。各工程均成功退出，具体组合由 `prepare-unity-validation.mjs --profile` 重建。

## 分发包与 Windows Player

八个 UPM `.tgz` 已安装到独立、无 SKY 嵌入包的工程。验证工具从归档导入示例，使用 Unity 2022.3.62f3 构建高级托管裁剪的 Windows Mono Player，运行十项检查：

- Legacy Input 与 Input System 两个双玩家场景分别完成中文选择保存、韩语组合回退和玩家隔离及原框取消。
- FairyGUI 双草稿、反向选区、原生验证及保存取消。
- TMP 代理对选区替换和长度截断。
- PlayerPrefs 在一百、一千和一万条词频下的持久化回读及主线程耗时。
- 完整中文及英语词库的模块加载、托管内存与短前缀、完整拼写查询耗时。

全部检查通过。`unity-package-install.json` 记录八个包的实际解析来源、版本和 SDK 依赖；`player-package-mono.json` 记录运行平台、后端、用例及原始性能样本。

NuGet 消费项目使用 PackageReference 和独立还原缓存，实际从本次四个 `.nupkg` 加载资源并完成基础英语、中文单词和整句、英语补全与开关、韩语组合回退。消费结果保存在 `nuget-consumer.json`。

## 复现与结果位置

在仓库根目录运行 `node scripts/build.mjs`，生成语言数据，执行托管和数据测试，验证 NuGet 消费并产生分发包。Unity 版本矩阵使用[验证计划](validation-plan.md)中的独立工程和 Unity Test Framework 执行。

```powershell
node scripts/smoke-unity-packages.mjs --unity <Unity.exe路径> --fairygui <FairyGUI SDK仓库路径>
node scripts/benchmark.mjs
node scripts/check-performance.mjs --player-report artifacts/Reports/player-package-mono.json
```

产物位于 `artifacts/NuGet/` 和 `artifacts/UPM/`，原始 XML、TRX、JSON、日志及截图位于 `artifacts/Reports/`。性能测量方法、实际数值和预算见[性能基准](performance.md)。
