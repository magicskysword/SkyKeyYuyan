# SkyKeyYuyan

**English** | [简体中文](https://github.com/magicskysword/SkyKeyYuyan/blob/main/README.zh-CN.md)

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/magicskysword/SkyKeyYuyan/blob/main/LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/magicskysword/SkyKeyYuyan)](https://github.com/magicskysword/SkyKeyYuyan/releases)
[![OpenUPM](https://img.shields.io/npm/v/com.stellarnox.skykeyyuyan.core?label=OpenUPM&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.stellarnox.skykeyyuyan.core/)

SkyKeyYuyan (SKY) is an in-game input method framework written in pure C#. It handles pinyin composition, candidate ranking and text editing itself, without relying on the operating system's IME, and runs fully offline once a language package is installed. Players can type Simplified Chinese, English and Korean into UGUI, TextMeshPro or FairyGUI text fields using only a gamepad.

![UGUI pinyin composition and candidates](https://media.githubusercontent.com/media/magicskysword/SkyKeyYuyan/main/ui/Screenshots/ugui-composition.png)

*UGUI default panel: “你好” has been converted, `shi'jie` is still being composed, and “世界” is the first candidate.*

## Why SKY

- **No system IME required.** On consoles, in fullscreen games and anywhere a system input method is unavailable or awkward, SKY still provides the full compose-and-pick workflow.
- **Everything works on a gamepad.** Picking keys, typing, choosing candidates, switching languages, moving the caret, selecting text and saving can all be done with a controller. Keyboard and mouse work too.
- **Local multiplayer.** Each player has their own devices, panel, session and word-frequency profile. The default UI is verified with two gamepads typing into two panels at the same time.
- **Pure C#, IL2CPP and AOT friendly.** The core and language libraries target .NET Standard 2.0. Language modules are registered through explicit factories, and there are no native plugins.
- **Capable Chinese input.** Continuous full pinyin, initial-letter abbreviations, mixed full and abbreviated spelling, completion of the last syllable, sentence candidates, partial selection and step-by-step undo. The lexicon has about 260,000 pronunciation entries.
- **Every part is replaceable.** Language engines, input devices, text targets, UI and save providers all plug in through interfaces. Use the default panels, or use only the language engine and draw your own UI.
- **Install only what you need.** Core, each language, the Unity adapter, the FairyGUI adapter and the two default UIs are published as separate packages.
- **Tested and benchmarked.** EditMode and PlayMode suites pass on Unity 2020.3, 2022.3 and 6000.3. Release packages are install-tested in a Windows Player and checked against performance budgets.

## Languages

| Language | Language ID | Features |
| --- | --- | --- |
| Simplified Chinese (pinyin) | `zh-Hans` | Continuous full pinyin (`nihaoshijie`), abbreviations (`nhsj`), mixed spelling (`nhao`), last-syllable completion (`nihaoshij`), manual syllable breaks (`xi'an`), character and word candidates, sentence candidates, partial selection, undo |
| Basic English | `en` | Direct text entry, built into the core package |
| English completion | `en` | Prefix-based word candidates that can be toggled during a session; about 270,000 words |
| Korean | `ko` | Modern 2-set (Dubeolsik) layout with compound vowels, double consonants, final-consonant composition and reassignment, and per-letter undo |

Candidate ranking combines the lexicon's base weight, custom weights supplied by your game and each player's personal selection history. Personal frequencies are stored per user and shared across runtime instances for the same user.

## Use cases

- Unity games where players enter character names, chat or search text with a gamepad
- Chinese and Korean input on platforms where the system IME is missing or a poor fit
- Couch multiplayer where several players type independently at the same time
- Reusing pinyin conversion and candidate lookup in .NET tools, servers or other engines

## Packages

### NuGet (.NET hosts)

| Package | Contents |
| --- | --- |
| `SkyKeyYuyan.Core` | Core interfaces, input runtime, basic English, frequency contracts |
| `SkyKeyYuyan.Languages.Chinese` | Simplified Chinese pinyin engine and lexicon |
| `SkyKeyYuyan.Languages.English` | English prefix-completion engine and word list |
| `SkyKeyYuyan.Languages.Korean` | Korean 2-set engine and rules |

Language data ships as `contentFiles` and is copied to `sky-data/` in your output directory, which is where it is loaded from by default.

### UPM (Unity)

| Package | Contents | Depends on |
| --- | --- | --- |
| `com.stellarnox.skykeyyuyan.core` | Core managed assemblies | — |
| `com.stellarnox.skykeyyuyan.chinese` | Chinese engine and lexicon | core |
| `com.stellarnox.skykeyyuyan.english` | English completion engine and word list | core |
| `com.stellarnox.skykeyyuyan.korean` | Korean engine and rules | core |
| `com.stellarnox.skykeyyuyan.unity` | Unity adapter: UGUI, TMP, Legacy Input, Input System | core |
| `com.stellarnox.skykeyyuyan.fairygui` | FairyGUI `GTextInput` adapter | unity; you provide the FairyGUI SDK |
| `com.stellarnox.skykeyyuyan.ui.ugui` | Default UGUI panel, virtual keyboard and draft field, with a Quick Start sample | unity |
| `com.stellarnox.skykeyyuyan.ui.fairygui` | Default FairyGUI panel, virtual keyboard and draft field | fairygui |

All packages share one version number. Install a matching set.

**Which ones do I need?** For UGUI or TMP projects, install `ui.ugui` plus the languages you want; the other dependencies resolve automatically. For FairyGUI projects, install `ui.fairygui` plus languages. If you only want the engine and will build your own UI, install `unity` plus languages.

## Installation

### .NET

Download the `.nupkg` files for one version from [GitHub Releases](https://github.com/magicskysword/SkyKeyYuyan/releases) into a `packages/` folder in your .NET project. Add that folder as a local NuGet source alongside nuget.org:

```bash
dotnet new nugetconfig
dotnet nuget add source ./packages --name SkyKeyYuyanLocal --configfile nuget.config
dotnet add package SkyKeyYuyan.Core
dotnet add package SkyKeyYuyan.Languages.Chinese
dotnet add package SkyKeyYuyan.Languages.English
dotnet add package SkyKeyYuyan.Languages.Korean
```

Add only the language packages you use.

### Unity via OpenUPM (recommended)

With [openupm-cli](https://openupm.com/docs/getting-started-cli.html):

```bash
openupm add com.stellarnox.skykeyyuyan.ui.ugui com.stellarnox.skykeyyuyan.chinese com.stellarnox.skykeyyuyan.english com.stellarnox.skykeyyuyan.korean
```

Or edit `Packages/manifest.json` by hand to add the scoped registry and dependencies. Replace `<version>` with a version listed on the [OpenUPM package page](https://openupm.com/packages/com.stellarnox.skykeyyuyan.core/), using the same version for all SKY packages:

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

### Unity via local .tgz (alternative)

1. Download the `.tgz` files for one version from [GitHub Releases](https://github.com/magicskysword/SkyKeyYuyan/releases).
2. In Package Manager choose **+ → Add package from tarball…** and install them in dependency order: `core` → languages → `unity` → `fairygui` (if needed) → `ui.ugui` / `ui.fairygui`.

### Optional dependencies

- **TextMeshPro / Input System:** not hard dependencies. Each adapter is enabled only when the matching package is installed; the rest is left out of compilation.
- **FairyGUI:** install the official FairyGUI Unity SDK, then add `SKY_FAIRYGUI` to Scripting Define Symbols for your target platforms.
- **Legacy Input:** the menu item `Tools/SkyKeyYuyan/添加 Legacy 默认输入轴` ("Add default Legacy input axes") adds default gamepad axes `SkyPad1`–`SkyPad8`.
- **Module control panel:** `Tools/SkyKeyYuyan/模块控制面板` ("Module control panel") shows detected dependencies and lets you switch each module on or off.

## Quick start

### Unity: run the sample

After installing `ui.ugui`, select **SkyKeyYuyan UGUI Default UI** in Package Manager and import **UGUI Quick Start** from the **Samples** section. It contains single-player and two-player scenes for both Legacy Input and Input System.

Try it: press Y to switch to Simplified Chinese, type `hen`, press X to pick a candidate, then press Start to save.

### Unity: add SKY to your project

This example uses Input System and UGUI. SKY discovers every `InputField` / `TMP_InputField` under `PlayerRoot` and shows the default panel when the player starts editing.

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
    public RectTransform PlayerRoot;   // Player UI root; text fields inside are discovered automatically
    public Font MixedFont;             // A font that covers Chinese, English and Korean glyphs

    private SkyRuntime _runtime;
    private SkyUnityHost _host;

    private async void Start()
    {
        // 1. Register languages and initialize (CreateOptions sets up resources, main-thread dispatch and PlayerPrefs storage)
        _runtime = new SkyRuntime();
        _runtime.RegisterLanguage(new BasicEnglishModule());
        _runtime.RegisterLanguage(new ChinesePinyinModule());
        var initialized = await _runtime.InitializeAsync(SkyUnityServices.Shared.CreateOptions(), CancellationToken.None);
        if (!initialized.Succeeded) { Debug.LogError(initialized.Result.Reason); return; }

        // 2. Create the host and a player, and create the default panel before any input opens
        _host = gameObject.AddComponent<SkyInputHost>().Configure(SkyInputBackend.InputSystem);
        _host.Runtime = _runtime;
        var player = _host.AddPlayer(new SkyPlayerOptions { PlayerId = "player", InitialLanguage = "zh-Hans" },
            new UguiTargetDiscovery(PlayerRoot));
        UguiSkyPanel.Create(PlayerRoot, player, MixedFont);
        player.Completed += result => Debug.Log("Input finished: " + result.Reason);

        // 3. Bind devices to the player
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

For local multiplayer, call `AddPlayer` once per player with a separate UI root and `UserId`, then bind each gamepad to its player. For FairyGUI, replace `UguiTargetDiscovery` / `UguiSkyPanel` with `FairyTargetDiscovery` / `FairySkyPanel`.

### .NET: use the language engine directly

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;

using var runtime = new SkyRuntime();
runtime.RegisterLanguage(new BasicEnglishModule());
runtime.RegisterLanguage(new ChinesePinyinModule());
var initialized = runtime.Initialize(new RuntimeOptions());   // Loads offline from sky-data/ in the output directory
if (!initialized.Succeeded)
    throw new InvalidOperationException(initialized.Result.Reason);

var target = new MemoryTextTarget();                            // Or implement your own ITextTarget
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

Once candidates arrive, confirm one using the current query ID, then finish the session to write the result back to the text target:

```csharp
var page = await candidatesReady.Task;
session.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id));   // Top candidate: 你好世界
session.Process(InputOperation.Finish());
await completed.Task;
Console.WriteLine(target.ReadSnapshot().Text);                  // 你好世界
```

`InputOperation` also covers backspace, caret navigation, select all, newline, language switching, toggling completion, cancel, retrying conversion and more. See [API and data contracts](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/api-contracts.md) for the full interface.

## Default controls

### Gamepad

| Input | Action |
| --- | --- |
| Left stick / D-pad | Move between virtual keys; move the caret while editing text |
| A | Press the focused virtual key |
| B | Undo composition; backspace when nothing is being composed |
| X | Confirm the current candidate |
| LB / RB | Previous / next candidate |
| Right stick | Browse candidates; extend the selection while editing text |
| Y | Switch language |
| L3 / R3 | Toggle between the virtual keyboard and text editing |
| Start | Save and close the panel |

Bindings and keyboard layouts can be replaced. If a gamepad disconnects the session pauses, and it resumes when the gamepad reconnects.

### Keyboard

Keyboard input has three modes: **Automatic** (default; uses the system IME where the platform supports it, otherwise SKY), **SystemIme** and **SkyIme**. In SKY mode, tapping Shift on its own switches language, Space or Enter confirms a candidate, and the number keys 1–9 pick from the current page.

## Compatibility

| Item | Support |
| --- | --- |
| .NET | .NET Standard 2.0 |
| Unity | 2020.3 or later. Verified on 2020.3.49f1, 2022.3.62f3 and 6000.3.10f1 |
| Scripting backends | Mono and IL2CPP; verified with high managed code stripping |
| Text fields | UGUI `InputField`, TMP `TMP_InputField`, FairyGUI `GTextInput` |
| Input backends | Legacy Input Manager and Input System (both can be enabled at once) |
| Editing modes | Edit the original field directly, or edit in a separate draft and write back on finish (the gamepad default) |
| Operating systems | Currently verified on Windows. The architecture targets Windows, Android, iOS, Linux and macOS |

Per-version test results are in the [validation results](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/validation-results.md).

## Performance

Measured on 0.2.0 with the full lexicons, Intel i7-13700KF, .NET 10.

| Metric | Result |
| --- | --- |
| Chinese full pinyin `nihaoshijie` query, P95 | 0.053 ms |
| Chinese abbreviation `nhsj` query, P95 | 0.545 ms |
| Worst short prefix (Chinese `zh`) query, P95 | 2.6 ms |
| Keystroke to state update, P95 | 0.021 ms |
| Initializing full Chinese and English lexicons | about 0.7–0.9 s, about 116 MiB resident managed memory |

Queries run in the background and do not block the main thread. In a Unity Windows Mono Player, Chinese query P95 stays at or below 5.2 ms. Methodology, full data and release budgets are in [performance benchmarks](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/performance.md).

## Documentation

Detailed documentation lives in [`docs/`](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/README.md) and is currently written in Chinese:

- [Getting started](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/getting-started.md)
- [Unity integration and default UI](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/unity-integration.md)
- [API and data contracts](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/api-contracts.md)
- [Architecture](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/architecture.md)
- [Sessions and text editing](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/session-and-editing.md)
- [Language engines](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/language-engines.md) · [Chinese pinyin matching](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/chinese-pinyin-matching.md)
- [Default virtual keyboard](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/keyboard-design.md)
- [Recovery and frequency storage](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/recovery-and-persistence.md)
- [Distribution and build](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/distribution-and-build.md)

## Building from source

You need Git LFS, Node.js 24 and the .NET SDK pinned in [`global.json`](https://github.com/magicskysword/SkyKeyYuyan/blob/main/global.json).

```bash
git lfs pull
node scripts/build.mjs
```

The build generates language data, compiles and tests the managed libraries, and packs NuGet and UPM packages into `artifacts/`. The Unity development project is in `unity/SkyKeyYuyan.Unity`. See the [contributing guide](https://github.com/magicskysword/SkyKeyYuyan/blob/main/CONTRIBUTING.md) for commit conventions.

## License

SKY's code is released under the [MIT License](https://github.com/magicskysword/SkyKeyYuyan/blob/main/LICENSE). Language data comes from the following MIT-licensed projects, and release packages include a combined license notice:

- Chinese words and frequencies: [jieba](https://github.com/fxsjy/jieba)
- Chinese word pronunciations: [phrase-pinyin-data](https://github.com/mozillazg/phrase-pinyin-data), [python-pinyin](https://github.com/mozillazg/python-pinyin)
- English word list: [an-array-of-english-words](https://github.com/words/an-array-of-english-words)

See [language data and licensing](https://github.com/magicskysword/SkyKeyYuyan/blob/main/docs/language-data-and-licensing.md) for details.
