# 快速开始

## 本地开发

安装 Git LFS、Node.js 24 和 `global.json` 指定的 .NET SDK，执行：

```powershell
git lfs pull
node scripts/build.mjs
```

构建会生成离线语言资源、编译和测试托管库、打包并验证 NuGet 消费、生成八个 UPM 包，并将示例安装到 Unity 开发工程。输出位于 `artifacts/NuGet`、`artifacts/UPM` 和 `artifacts/Reports`。

用 Unity 打开 `unity/SkyKeyYuyan.Unity`。`Assets/SkyKeyYuyanSamples/Scenes` 提供两种输入后端的单人和双玩家示例。模块状态可在 `Tools/SkyKeyYuyan/模块控制面板` 查看。

## 独立 .NET 宿主

安装 `SkyKeyYuyan.Core` 和所需语言包。语言资源随 NuGet 包复制到输出目录，可离线初始化：

```csharp
using System;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;

using var runtime = new SkyRuntime();
runtime.RegisterLanguage(new BasicEnglishModule());
runtime.RegisterLanguage(new ChinesePinyinModule());
var initialized = runtime.Initialize(new RuntimeOptions());
if (!initialized.Succeeded)
    throw new InvalidOperationException(initialized.Result.Reason);

var target = new MemoryTextTarget();
using var session = runtime.OpenSession(
    new SessionOptions { InitialLanguage = "zh-Hans" }, target).Session;
session.Process(InputOperation.Insert("hen"));
```

通过 `StateChanged` 展示组字和候选，使用当前查询身份与候选身份确认选择。`Finish` 完成草稿回填，`Cancel` 丢弃草稿。接口结果及执行上下文见[接口与数据契约](api-contracts.md)。

## Unity 宿主

从本地 `.tgz` 或已配置的注册表安装核心、Unity 适配、语言及默认表现包。安装 UGUI 默认表现后，在 Package Manager 的 Samples 区域导入 UGUI Quick Start，即可运行示例。

宿主使用 `SkyUnityServices.Shared.CreateOptions()` 配置资源、主线程上下文和存档，显式注册语言并初始化运行时。通过 `SkyInputHost` 管理玩家与后端，在打开输入前为各玩家创建 `UguiSkyPanel` 或 `FairySkyPanel`。

UGUI、TMP 和 FairyGUI 文本目标可以自动发现，也可显式注册。设备绑定、字体、双玩家和生命周期接入见[Unity 接入](unity-integration.md)。

## 打包与发布

`node scripts/pack-upm.mjs --repository https://github.com/owner/repository` 生成 UPM 包及对应 OpenUPM 元数据；仓库地址使用实际 GitHub 地址。GitHub Actions 构建标签对应版本后创建包含全部包的草稿 Release。发布该 Release 后，OpenUPM 可按 `githubRelease` 模式跟踪对应附件。

所有 SKY 包共用版本号。标签、NuGet、UPM 和内部依赖版本在打包时核对，数据与存档使用各自的格式版本。发布前按[验证计划](validation-plan.md)检查实际环境及结果。
