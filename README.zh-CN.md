# SkyKeyYuyan

[English](https://github.com/magicskysword/SkyKeyYuyan/blob/main/README.md) | **简体中文**

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/magicskysword/SkyKeyYuyan/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/magicskysword/SkyKeyYuyan)](https://github.com/magicskysword/SkyKeyYuyan/releases)
[![OpenUPM](https://img.shields.io/npm/v/com.stellarnox.skykeyyuyan.core?label=OpenUPM&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.stellarnox.skykeyyuyan.core/)

SkyKeyYuyan（简称 SKY）是一个纯 C# 的游戏内输入法框架。它自己完成拼音组字、候选排序和文本编辑，不依赖操作系统 IME，安装语言包后即可离线运行。玩家可以只用手柄，在 UGUI、TextMeshPro 或 FairyGUI 文本框里输入简体中文、英文和韩文。

![UGUI 默认面板的拼音组字与候选](https://media.githubusercontent.com/media/magicskysword/SkyKeyYuyan/main/ui/Screenshots/ugui-composition.png)

*UGUI 默认面板：已确认“你好”，`shi'jie` 待转换，首选候选为“世界”。*

## 为什么选择 SKY

- **不依赖系统 IME。** 在主机、全屏游戏等没有或不便使用系统输入法的环境里，SKY 仍能提供完整的组字与候选流程。
- **手柄能完成全部操作。** 选键、输入、选词、切换语言、移动光标、框选和保存都可以用手柄完成，键鼠玩家也能直接使用。
- **支持本地多人。** 每位玩家拥有独立的设备、面板、会话和词频存档。默认界面已验证双手柄、双面板同时输入。
- **纯 C#，兼容 IL2CPP 和 AOT。** 核心与语言库以 .NET Standard 2.0 构建，语言模块通过显式工厂注册，不含原生插件。
- **中文输入够用。** 支持连续全拼、首字母简拼、全简混输、末音节补全、整句候选、分段选词和按历史回退。词库约 26 万条读音记录。
- **组件可替换。** 语言引擎、输入设备、文本目标、UI 和存档 Provider 都通过接口接入。可以直接使用默认面板，也可以只用语言引擎自己画 UI。
- **按需安装。** 核心、各语言、Unity 适配、FairyGUI 适配和两套默认 UI 分别发布，只安装用得到的部分。
- **有测试和基准。** Unity 2020.3、2022.3 和 6000.3 的 EditMode / PlayMode 测试全部通过，发布包会在 Windows Player 上做安装验证并检查性能预算。

## 语言能力

| 语言 | 语言标识 | 能力 |
| --- | --- | --- |
| 简体中文拼音 | `zh-Hans` | 连续全拼（`nihaoshijie`）、简拼（`nhsj`）、全简混输（`nhao`）、末音节补全（`nihaoshij`）、手动音节分隔（`xi'an`）、单字和词语候选、整句候选、分段选词、回退 |
| 基础英语 | `en` | 直接输入，内置于核心包 |
| 英语前缀补全 | `en` | 根据前缀给出单词候选，可在会话中随时开关；约 27 万词条 |
| 韩语 | `ko` | 现代两套式，支持复合元音、双辅音、收音组合与重新分配，支持按字母回退 |

候选排序综合使用词库基础权重、宿主自定义权重和玩家个人词频。个人词频按用户分别保存，同一用户在多个运行时实例之间共享。

## 适用场景

- 需要玩家用手柄输入角色名、聊天或搜索内容的 Unity 游戏
- 在系统 IME 不可用或体验不佳的平台上提供中文、韩文输入
- 同屏多人游戏中，多位玩家各自独立输入
- 在 .NET 程序（工具、服务端、其他引擎）里复用拼音转换和候选查询

## 包一览

### NuGet（.NET 宿主）

| 包 | 内容 |
| --- | --- |
| `SkyKeyYuyan.Core` | 核心接口、输入运行时、基础英语、词频契约 |
| `SkyKeyYuyan.Languages.Chinese` | 简体中文拼音引擎与词库 |
| `SkyKeyYuyan.Languages.English` | 英语前缀补全引擎与词表 |
| `SkyKeyYuyan.Languages.Korean` | 韩语两套式引擎与规则 |

语言资源以 `contentFiles` 携带，构建时复制到输出目录的 `sky-data/`，默认从该目录加载。

### UPM（Unity）

| 包 | 内容 | 依赖 |
| --- | --- | --- |
| `com.stellarnox.skykeyyuyan.core` | 核心托管程序集 | — |
| `com.stellarnox.skykeyyuyan.chinese` | 中文引擎与词库 | core |
| `com.stellarnox.skykeyyuyan.english` | 英语补全引擎与词表 | core |
| `com.stellarnox.skykeyyuyan.korean` | 韩语引擎与规则 | core |
| `com.stellarnox.skykeyyuyan.unity` | Unity 适配：UGUI、TMP、Legacy Input、Input System | core |
| `com.stellarnox.skykeyyuyan.fairygui` | FairyGUI `GTextInput` 适配 | unity，FairyGUI SDK 由项目自行提供 |
| `com.stellarnox.skykeyyuyan.ui.ugui` | UGUI 默认输入面板、虚拟键盘、草稿框，附 Quick Start 示例 | unity |
| `com.stellarnox.skykeyyuyan.ui.fairygui` | FairyGUI 默认输入面板、虚拟键盘、草稿框 | fairygui |

所有包共用同一个版本号，请安装相同版本的组合。

**怎么选：** 使用 UGUI 或 TMP 的项目，安装 `ui.ugui` 加所需语言包即可，其余依赖会自动解析。使用 FairyGUI 的项目，安装 `ui.fairygui` 加语言包。只需要引擎、打算自己做界面的项目，安装 `unity` 加语言包。

## 安装

### .NET

从 [GitHub Releases](https://github.com/magicskysword/SkyKeyYuyan/releases) 下载同一版本的 `.nupkg` 文件，放入 .NET 项目的 `packages/` 目录。将该目录作为本地 NuGet 源，与 nuget.org 一同使用：

```bash
dotnet new nugetconfig
dotnet nuget add source ./packages --name SkyKeyYuyanLocal --configfile nuget.config
dotnet add package SkyKeyYuyan.Core
dotnet add package SkyKeyYuyan.Languages.Chinese
dotnet add package SkyKeyYuyan.Languages.English
dotnet add package SkyKeyYuyan.Languages.Korean
```

只需要安装用到的语言包。

### Unity：通过 OpenUPM（推荐）

使用 [openupm-cli](https://openupm.com/docs/getting-started-cli.html)：

```bash
openupm add com.stellarnox.skykeyyuyan.ui.ugui com.stellarnox.skykeyyuyan.chinese com.stellarnox.skykeyyuyan.english com.stellarnox.skykeyyuyan.korean
```

也可以手动编辑 `Packages/manifest.json`，添加 Scoped Registry 和依赖。将 `<version>` 替换为 [OpenUPM 包页面](https://openupm.com/packages/com.stellarnox.skykeyyuyan.core/)列出的版本，所有 SKY 包使用同一版本：

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.stellarnox.skykeyyuyan"]
    }
  ],
  "dependencies": {
    "com.stellarnox.skykeyyuyan.ui.ugui": "<version>",
    "com.stellarnox.skykeyyuyan.chinese": "<version>",
    "com.stellarnox.skykeyyuyan.english": "<version>",
    "com.stellarnox.skykeyyuyan.korean": "<version>"
  }
}
```

### Unity：本地 .tgz（备选）

1. 从 [GitHub Releases](https://github.com/magicskysword/SkyKeyYuyan/releases) 下载同一版本的 `.tgz` 包。
2. 在 Package Manager 中选择 **+ → Add package from tarball…**，按依赖顺序安装：`core` → 语言包 → `unity` → `fairygui`（按需）→ `ui.ugui` / `ui.fairygui`。

### 可选依赖

- **TextMeshPro / Input System：** 不是硬依赖，项目装了哪个就启用哪个的适配，没装的部分不参与编译。
- **FairyGUI：** 先安装 FairyGUI 官方 Unity SDK，再在对应平台的 Scripting Define Symbols 中加入 `SKY_FAIRYGUI`。
- **Legacy Input：** 菜单 `Tools/SkyKeyYuyan/添加 Legacy 默认输入轴` 会补齐 `SkyPad1`–`SkyPad8` 的默认手柄轴。
- **模块控制面板：** 菜单 `Tools/SkyKeyYuyan/模块控制面板` 显示依赖检测结果，可以分别开关各个模块。

## 快速上手

### Unity：运行示例

安装 `ui.ugui` 后，在 Package Manager 中选中 **SkyKeyYuyan UGUI Default UI**，从 **Samples** 区域导入 **UGUI Quick Start**。示例包含 Legacy Input 和 Input System 两种后端的单人、双人场景，导入后打开即可运行。

试一下：按 Y 切换到简体中文，输入 `hen`，按 X 确认候选，再按 Start 保存。

### Unity：接入自己的项目

下面以 Input System 和 UGUI 为例。SKY 会自动发现 `PlayerRoot` 下的 `InputField` / `TMP_InputField`，玩家开始编辑时弹出默认面板。

```csharp
using System.Threading;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.UI.UGUI;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.InputSystem;
using SkyKeyYuyan.Unity.UGUI;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class SkyBootstrap : MonoBehaviour
{
    public RectTransform PlayerRoot;   // 玩家 UI 根节点，内部的文本框会被自动发现
    public Font MixedFont;             // 能覆盖中、英、韩文字形的字体

    private SkyRuntime _runtime;
    private SkyUnityHost _host;

    private async void Start()
    {
        // 1. 注册语言并初始化（资源、主线程调度和 PlayerPrefs 存档由 CreateOptions 配好）
        _runtime = new SkyRuntime();
        _runtime.RegisterLanguage(new BasicEnglishModule());
        _runtime.RegisterLanguage(new ChinesePinyinModule());
        var initialized = await _runtime.InitializeAsync(SkyUnityServices.Shared.CreateOptions(), CancellationToken.None);
        if (!initialized.Succeeded) { Debug.LogError(initialized.Result.Reason); return; }

        // 2. 创建宿主和玩家，并在打开输入前创建默认面板
        _host = gameObject.AddComponent<SkyInputHost>().Configure(SkyInputBackend.InputSystem);
        _host.Runtime = _runtime;
        var player = _host.AddPlayer(new SkyPlayerOptions { PlayerId = "player", InitialLanguage = "zh-Hans" },
            new UguiTargetDiscovery(PlayerRoot));
        UguiSkyPanel.Create(PlayerRoot, player, MixedFont);
        player.Completed += result => Debug.Log("输入结束：" + result.Reason);

        // 3. 把设备绑定给玩家
        var driver = new SkyInputSystemDriver(_host.Router);
        _host.SetDriver(driver);
        if (Gamepad.current != null) driver.Bind(Gamepad.current, player.PlayerId);
        if (Keyboard.current != null) driver.Bind(Keyboard.current, player.PlayerId);
    }

    private void OnDestroy()
    {
        _host?.Dispose();
        _runtime?.Dispose();
    }
}
```

本地多人时，为每位玩家分别调用一次 `AddPlayer`，各自使用独立的 UI 根节点和 `UserId`，再把不同手柄绑定给对应玩家。使用 FairyGUI 时，把 `UguiTargetDiscovery` / `UguiSkyPanel` 换成 `FairyTargetDiscovery` / `FairySkyPanel`。

### .NET：直接调用语言引擎

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;

using var runtime = new SkyRuntime();
runtime.RegisterLanguage(new BasicEnglishModule());
runtime.RegisterLanguage(new ChinesePinyinModule());
var initialized = runtime.Initialize(new RuntimeOptions());   // 从输出目录的 sky-data/ 离线加载
if (!initialized.Succeeded)
    throw new InvalidOperationException(initialized.Result.Reason);

var target = new MemoryTextTarget();                            // 也可以实现自己的 ITextTarget
var opened = runtime.OpenSession(new SessionOptions { InitialLanguage = "zh-Hans" }, target);
using var session = opened.Session ?? throw new InvalidOperationException(opened.Reason);
var candidatesReady = new TaskCompletionSource<CandidatePage>(TaskCreationOptions.RunContinuationsAsynchronously);
var completed = new TaskCompletionSource<SessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

session.StateChanged += snapshot =>
{
    if (snapshot.Querying || snapshot.Candidates == null || snapshot.Candidates.Candidates.Count == 0) return;
    Console.WriteLine($"{snapshot.Composition.DisplayText} → " +
        string.Join(" ", snapshot.Candidates.Candidates.Take(5).Select(c => c.Text)));
    candidatesReady.TrySetResult(snapshot.Candidates);
};
session.Completed += result => completed.TrySetResult(result);
session.Process(InputOperation.Insert("nihaoshijie"));
```

候选到达后，用当前查询标识确认候选，然后完成会话，结果写回文本目标：

```csharp
var page = await candidatesReady.Task;
session.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id));   // 首选“你好世界”
session.Process(InputOperation.Finish());
await completed.Task;
Console.WriteLine(target.ReadSnapshot().Text);                  // 你好世界
```

`InputOperation` 还提供退格、光标导航、全选、换行、切换语言、开关补全、取消、重试转换等操作。完整接口见[接口与数据契约](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/api-contracts.md)。

## 默认操作

### 手柄

| 输入 | 行为 |
| --- | --- |
| 左摇杆 / 方向键 | 在虚拟键盘上选键；进入正文编辑后移动光标 |
| A | 按下当前虚拟键 |
| B | 回退组字；没有组字时退格 |
| X | 确认当前候选 |
| LB / RB | 选择前一个 / 后一个候选 |
| 右摇杆 | 浏览候选；进入正文编辑后扩展选区 |
| Y | 切换语言 |
| L3 / R3 | 在虚拟键盘和正文编辑之间切换 |
| Start | 保存并关闭面板 |

宿主可以替换绑定和键盘布局。手柄断开时会话暂停，重新连接后继续。

### 键盘

键盘输入有三种模式：**Automatic**（默认，平台支持时使用系统 IME，否则使用 SKY）、**SystemIme**、**SkyIme**。SKY 模式下单独点按 Shift 切换语言，空格或 Enter 确认候选，数字 1–9 选择当前页候选。

## 兼容性

| 项目 | 支持情况 |
| --- | --- |
| .NET | .NET Standard 2.0 |
| Unity | 最低 2020.3。在 2020.3.49f1、2022.3.62f3、6000.3.10f1 上验证 |
| 脚本后端 | Mono、IL2CPP；高级托管代码裁剪已验证 |
| 文本框 | UGUI `InputField`、TMP `TMP_InputField`、FairyGUI `GTextInput` |
| 输入后端 | Legacy Input Manager、Input System（可同时启用） |
| 编辑模式 | 直接编辑原框，或在独立草稿中编辑、完成后回填（手柄默认使用草稿） |
| 操作系统 | 目前在 Windows 上完成验收；架构上面向 Windows、Android、iOS、Linux、macOS |

各版本的测试结果见[验证记录](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/validation-results.md)。

## 性能

以下数据来自 0.2.0，完整词库，Intel i7-13700KF，.NET 10。

| 指标 | 结果 |
| --- | --- |
| 中文全拼 `nihaoshijie` 查询 P95 | 0.053 ms |
| 中文简拼 `nhsj` 查询 P95 | 0.545 ms |
| 最差短前缀（中文 `zh`）查询 P95 | 2.6 ms |
| 输入到状态更新 P95 | 0.021 ms |
| 完整中英词库初始化 | 约 0.7–0.9 s，常驻托管内存约 116 MiB |

查询在后台执行，不阻塞主线程。Unity Windows Mono Player 上的中文查询 P95 不超过 5.2 ms。测量方法、全部数据和版本预算见[性能基准](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/performance.md)。

## 文档

更深入的说明在 [`docs/`](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/README.md)：

- [快速开始](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/getting-started.md)
- [Unity 接入与默认表现](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/unity-integration.md)
- [接口与数据契约](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/api-contracts.md)
- [系统架构](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/architecture.md)
- [会话与文本编辑](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/session-and-editing.md)
- [语言引擎](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/language-engines.md) · [中文拼音匹配](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/chinese-pinyin-matching.md)
- [默认虚拟键盘](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/keyboard-design.md)
- [恢复与词频存储](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/recovery-and-persistence.md)
- [分发与构建](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/distribution-and-build.md)

## 从源码构建

需要 Git LFS、Node.js 24，以及 [`global.json`](https://github.com/magicskysword/SkyKeyYuyan/blob/main/global.json) 中指定的 .NET SDK。

```bash
git lfs pull
node scripts/build.mjs
```

构建会生成语言资源，编译并测试托管库，打包 NuGet 和 UPM，结果输出到 `artifacts/`。Unity 开发工程位于 `unity/SkyKeyYuyan.Unity`。提交规范见[贡献指南](https://github.com/magicskysword/SkyKeyYuyan/blob/main/CONTRIBUTING.md)。

## 许可

SKY 的代码以 [MIT](https://github.com/magicskysword/SkyKeyYuyan/blob/main/LICENSE) 许可发布。语言数据来自以下 MIT 许可项目，发布包附带整合后的许可声明：

- 中文词条与词频：[jieba](https://github.com/fxsjy/jieba)
- 中文词语读音：[phrase-pinyin-data](https://github.com/mozillazg/phrase-pinyin-data)、[python-pinyin](https://github.com/mozillazg/python-pinyin)
- 英语词表：[an-array-of-english-words](https://github.com/words/an-array-of-english-words)

详见[语言数据与许可](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/language-data-and-licensing.md)。
