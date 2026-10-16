# SkyKeyYuyan 分发与构建

SKY 使用同一套纯 C# 核心与语言库构建独立 .NET 分发物和 Unity 分发物。NuGet 面向 .NET 宿主，OpenUPM 面向 Unity 按需安装。

## 分发边界

| 逻辑模块 | NuGet 内容 | UPM 包名 | UPM 内容与依赖 |
| --- | --- | --- | --- |
| 核心 | 核心接口、运行时、基础英语、词频契约 | com.stellarnox.skykeyyuyan.core | 核心托管程序集及开发辅助文件 |
| 中文 | 中文引擎和语言资源 | com.stellarnox.skykeyyuyan.chinese | 对应库、资源和 Unity 资源定位封装；依赖核心包 |
| 韩语 | 韩语引擎和规则资源 | com.stellarnox.skykeyyuyan.korean | 对应库、资源和 Unity 资源定位封装；依赖核心包 |
| 英语补全 | 前缀补全引擎和词表 | com.stellarnox.skykeyyuyan.english | 对应库、资源和 Unity 资源定位封装；依赖核心包 |
| Unity 适配 | 由 Unity 分发提供 | com.stellarnox.skykeyyuyan.unity | 转换核心、UGUI、TMP、Legacy Input、Input System；依赖核心包 |
| FairyGUI 适配 | 由 Unity 分发提供 | com.stellarnox.skykeyyuyan.fairygui | FairyGUI 接入；依赖 Unity 适配，宿主提供 FairyGUI SDK |
| UGUI 默认表现 | 由 Unity 分发提供 | com.stellarnox.skykeyyuyan.ui.ugui | 默认面板、键盘和草稿框；依赖 Unity 适配 |
| FairyGUI 默认表现 | 由 Unity 分发提供 | com.stellarnox.skykeyyuyan.ui.fairygui | 默认面板、键盘和草稿框；依赖 FairyGUI 适配 |

NuGet 包标识与对应的 C# 项目名一致，例如 SkyKeyYuyan.Core。

语言 UPM 包封装独立语言库及资源定位描述，通过核心资源契约与 Unity 加载器连接。默认表现读取已注册语言提供的布局和能力。

## 托管程序集

核心及语言库以 netstandard2.0 构建。NuGet 和 UPM 采用同一次构建得到的托管程序集，UPM 同时提供 DLL、PDB 和 XML 接口文档。

核心程序集由核心包统一提供，各语言库引用该程序集。Unity 适配与表现代码以源码程序集接入，编辑器工具与运行时程序集分别组织。

`dotnet build` 完成后，由 MSBuild 目标把程序集及 `data/generated/` 中的语言资源复制到 Unity 工程中对应的 UPM 嵌入包。因此修改 C# 代码后重新构建，Unity 工程即可使用新的程序集。复制得到的文件不提交到 git；对应的 `.meta` 文件需要提交，以固定 GUID 和导入设置。

复制规则定义在 `dotnet/Directory.Build.targets`：库项目用 `SkyUpmPackage` 指定目标包，语言库再用 `SkyLanguageData` 指定 `data/generated/` 下的资源目录。程序集（DLL、PDB、XML）复制到包内 `Plugins/`，语言资源复制到包内 `Data/Resources/SkyKeyYuyan/语言目录/`，由 Unity 随 Player 提供。

NuGet 语言包以 contentFiles 携带 `sky-data/语言目录/`，随引用项目复制到输出目录。默认 DirectoryResourceProvider 从应用输出目录的 `sky-data/` 读取资源。资源文件 `lexicon.bytes` 是带版本头的 UTF-8 制表文本，`keyboard.bytes` 提供平台无关键盘布局。

测试项目使用 NUnit，目标框架为 net10.0，与 Unity Test Framework 采用同一测试框架。

语言工厂和资源处理采用显式引用，构建验证覆盖 IL2CPP、裁剪和 AOT 场景。Unity 的 Mono 与 IL2CPP 使用不同编译方式，实际兼容性由对应构建验证。[Unity .NET 运行环境文档](https://docs.unity3d.com/2020.3/Documentation/Manual/overview-of-dot-net-in-unity.html)

## UPM 可选模块

可选接入按程序集隔离。模块可用性由依赖存在、依赖版本、相关 Unity 编译能力和配置开关共同决定。

TMP、Input System 和 FairyGUI 的类型引用只存在于其适配程序集内。编辑器面板根据当前项目显示可用状态，宿主可调整启用配置。

Unity 适配提供通用资源加载器，语言包提供资源位置和描述，宿主通过显式语言注册将两者连接。

## 包版本与 OpenUPM

所有 SKY 包使用统一版本号，每次发布打一个与版本号一致的 semver 标签，一次发布全部包。发布元数据统一记录包名称、发布版本、Unity 最低版本、依赖版本、仓库地址、许可证和说明文档。

UPM 包清单为所需依赖声明明确版本。每次发布维护一组一起验证的 SKY 包版本及对应第三方组件版本。[Unity 包清单文档](https://docs.unity3d.com/2020.3/Documentation/Manual/upm-manifestPkg.html)

UPM 包通过 OpenUPM 分发，宿主项目添加 OpenUPM 的 Scoped Registry（`https://package.openupm.com`），作用域为 `com.stellarnox.skykeyyuyan`。[Unity Scoped Registry 文档](https://docs.unity3d.com/2020.3/Documentation/Manual/upm-scoped.html)

OpenUPM 不执行自定义构建，而核心 DLL 和语言资源都需要构建生成，因此采用 GitHub Release 跟踪模式：

- CI 构建后把每个 UPM 包打成 `.tgz`，附加到与版本标签同名的 GitHub Release 上。
- 每个 UPM 包在 OpenUPM 单独登记，使用 `githubRelease` 跟踪模式，并以包名作为 `githubReleaseAssetName` 前缀，从同一个 Release 中选出自己的 `.tgz`。
- 主分支不提交构建产物。

OpenUPM 只收录托管在 GitHub 上的开源仓库。[OpenUPM 收录说明](https://openupm.com/docs/adding-upm-package.html)

语言数据格式版本单独维护，由引擎声明可读取范围。接口、资源格式和存档格式分别使用其版本信息处理兼容。

## 仓库目录

```text
SkyKeyYuyan.sln                 根解决方案：dotnet build / test / pack
global.json                     锁定 .NET SDK 版本
LICENSE
.gitignore
.gitattributes
.mcp.json                       项目级 MCP 配置（FairyGUI-MCP-Headless）
docs/                           开发文档
dotnet/                         C# 核心与语言库
  Directory.Build.props         C# 公共构建设置，只作用于本目录
  Directory.Build.targets       构建后复制到 UPM 嵌入包
  SkyKeyYuyan.Core/
  SkyKeyYuyan.Core.Tests/
  SkyKeyYuyan.Languages.Chinese/
  SkyKeyYuyan.Languages.Chinese.Tests/
  SkyKeyYuyan.Languages.Korean/
  SkyKeyYuyan.Languages.Korean.Tests/
  SkyKeyYuyan.Languages.English/
  SkyKeyYuyan.Languages.English.Tests/
scripts/                        Node 工具脚本
  package.json
data/
  sources/                      上游数据
  supplements/                  补充与修订
  generated/                    生成的语言资源（不提交）
  manifests/                    版本和来源清单
unity/
  SkyKeyYuyan.Unity/            Unity 2022.3 开发工程
    Assets/                     示例与验证场景
    Packages/
      manifest.json
      com.stellarnox.skykeyyuyan.core/
      com.stellarnox.skykeyyuyan.chinese/
      com.stellarnox.skykeyyuyan.korean/
      com.stellarnox.skykeyyuyan.english/
      com.stellarnox.skykeyyuyan.unity/
      com.stellarnox.skykeyyuyan.fairygui/
      com.stellarnox.skykeyyuyan.ui.ugui/
      com.stellarnox.skykeyyuyan.ui.fairygui/
ui/
  FairyGUI/                     FairyGUI 编辑器工程和发布配置
artifacts/                      构建产物（不提交）
  NuGet/
  UPM/
  Reports/
```

### 目录约束

| 约束 | 原因 |
| --- | --- |
| 根目录只放解决方案、SDK 版本锁定和仓库级文件（许可证、忽略规则） | 根目录保持一眼可读，各类代码各归其位 |
| `Directory.Build.props` 等 MSBuild 公共设置放在 `dotnet/` 内，不放根目录 | MSBuild 会逐级向上查找这些文件；放在根目录时，Unity 生成的 csproj 在 IDE 中构建也会导入它们 |
| 根解决方案只包含 `dotnet/` 下的项目，不包含 Unity 生成的项目 | `dotnet build`、`test`、`pack` 只负责 C# 库；Unity 工程由 Unity 自己管理 |
| 每个 C# 项目旁边放同名的 `.Tests` 测试项目，测试不放进被测项目目录 | SDK 风格项目默认编译目录下所有 `.cs` 文件，放在内部会把测试编进程序集 |
| Node 脚本全部放在 `scripts/`，依赖由 `scripts/package.json` 管理 | 数据转换和打包工具集中维护，不污染根目录 |
| UPM 开发包作为嵌入包放在 Unity 工程的 `Packages/` 下，一个发布模块对应一个包目录 | Unity 自动识别嵌入包，开发工程直接打包，不需要修改 manifest |
| 构建复制进 UPM 包的 DLL、PDB、XML 和语言资源，以及 `data/generated/`、`artifacts/`，都不提交到 git | 构建产物由源码和数据重新生成，发布内容走 GitHub Release |
| Unity 工程使用 2022.3 开发，但 UPM 包中的 Unity 代码必须能在 2020.3 编译（最高 C# 8） | 最低支持版本为 2020.3，由验证计划中的 2020.3 编译检查保证 |

Unity 开发工程额外安装 uGUI、TextMeshPro、Input System 和 Test Framework，并同时启用 Legacy Input 与 Input System，用于验证各类接入。这些是开发工程的依赖，不是 SKY 包的硬依赖。

FairyGUI 工程维护默认界面源资源，导出内容进入对应表现包。工程可以用 FairyGUI 编辑器打开，也可以通过仓库 `.mcp.json` 中配置的 [FairyGUI-MCP-Headless](https://github.com/magicskysword/FairyGUI-MCP-Headless) 查询、编辑、校验和发布；后者需要全局安装 `@magicskysword/fairygui-mcp-headless`（Node.js 24），动态预览还需要 Playwright Chromium。

## 构建流程

| 入口 | 位置 | 作用 |
| --- | --- | --- |
| 完整本地构建 | `node scripts/build.mjs` | 生成数据、构建和测试托管库、验证 NuGet 消费、同步 Unity 资源、安装示例并打包 UPM |
| 数据生成 | `scripts/` 中的 Node 脚本 | 读取 `data/` 中的清单、上游数据和补充数据，生成语言资源到 `data/generated/`，并输出质量报告 |
| 构建与测试 | 根目录 `dotnet build` / `dotnet test` | 构建 netstandard2.0 程序集并运行测试；构建后把程序集和语言资源复制到 UPM 嵌入包 |
| NuGet 打包 | 根目录 `dotnet pack` | 生成 NuGet 包到 `artifacts/NuGet/` |
| UPM 打包 | `scripts/` 中的 Node 脚本 | 把嵌入包打成 `.tgz`，输出到 `artifacts/UPM/` |
| NuGet 安装验证 | `node scripts/smoke-nuget.mjs` | 使用独立缓存安装本次四个包，检查资源与完整语言输入流程 |
| UPM 安装验证 | `node scripts/smoke-unity-packages.mjs --unity <Unity.exe路径>` | 从八个 `.tgz` 创建干净工程、导入包内示例、构建并运行 Windows Player；可用 `--fairygui <SDK仓库路径>` 加入 FairyGUI 检查 |

克隆仓库后，先运行 `node scripts/generate-data.mjs`，再运行 `dotnet build`。`npm --prefix scripts test` 验证数据转换规则，`node scripts/sync-unity-meta.mjs` 为新增 Unity 资源创建导入元数据。`npm --prefix scripts run fetch-data` 按来源清单重新取得上游数据；常规构建使用仓库内的数据文件。

完整发布流程：

1. 读取版本和来源清单，准备本次采用的数据。
2. 生成规范化词条、稳定标识映射、索引和键盘布局资源。
3. 执行数据结构、读音和候选质量检查，报告输出到 `artifacts/Reports/`。
4. 构建 netstandard2.0 核心与语言程序集，运行独立库测试，并复制到 UPM 嵌入包。
5. 生成 NuGet 包。
6. 运行 Unity 编译、交互和 Windows 构建验证。
7. 整理整合 MIT 声明、接口文档、版本矩阵及验证记录。
8. 打包 UPM `.tgz`，打统一版本标签；CI 归档 NuGet，并创建附带全部 `.tgz` 和 OpenUPM 元数据的草稿 GitHub Release。发布后供 OpenUPM 收录。

`scripts/pack-upm.mjs --repository owner/repository` 以实际 GitHub 仓库地址生成 OpenUPM 登记文件，输出到 `artifacts/OpenUPM/`。CI 使用 `GITHUB_REPOSITORY` 自动提供地址，并检查标签是否与包版本一致。八个包均包含整合许可和 `Documentation~`，打包报告记录版本与压缩体积。

UPM 安装验证使用本次归档中的清单及示例，包依赖均指向 `.tgz`，验证工程不含 SKY 嵌入包。工具检查 Package Manager 的解析来源，结果保存到 `artifacts/Reports/unity-package-install.json`。NuGet 消费项目只有 PackageReference，结果保存到同目录的 `nuget-consumer.json`。

运行时读取转换后的资源，构建工具只在开发和发布流程中执行。

## 发布内容

每个发布模块附带其接口说明、必要资源、版本信息和许可声明。默认表现包提供可导入示例，覆盖初始化、注册语言、打开目标、接收结果和释放资源。

集成示例使用实际发布包标识与版本。OpenUPM Scoped Registry 的配置示例由发布元数据生成，使包名和版本与本次发布保持一致。

## 发布记录

发布记录包含：

- SKY 包、语言数据及格式版本。
- Unity 精确版本、操作系统、脚本后端和第三方组件版本。
- 通过的输入后端、文本目标、编辑模式及可选依赖组合。
- 语言质量、性能和恢复测试结果。
- NuGet 与 UPM 产物位置及整合许可清单。

发布完成以 NuGet 和 OpenUPM 的实际可获取状态为准，验证范围按构建记录呈现。
