using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkyKeyYuyan.Validation
{
    /// <summary>
    /// 在独立 Player 内验证示例、资源、语言与可选适配。
    /// </summary>
    public sealed class SkyPlayerValidation : MonoBehaviour
    {
        [Serializable]
        private sealed class Report { public string unity, platform, backend, error; public bool passed; public string[] cases; }
        private sealed class CheckEntry { internal string Name; internal Func<Task> Run; }
        private static readonly List<CheckEntry> Additional = new List<CheckEntry>();
        private readonly List<string> _cases = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Additional.Clear();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            if (!Environment.GetCommandLineArgs().Contains("--sky-validation")) return;
            var owner = new GameObject("Player Validation"); DontDestroyOnLoad(owner); owner.AddComponent<SkyPlayerValidation>();
        }
        /// <summary>
        /// 为安装的可选适配登记独立 Player 检查。
        /// </summary>
        public static void Register(string name, Func<Task> check) => Additional.Add(new CheckEntry { Name = name, Run = check });
        /// <summary>
        /// 等待主线程状态变化，超时返回明确失败。
        /// </summary>
        public static async Task Wait(Func<bool> predicate, float seconds = 30)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!predicate()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Player 状态等待超时"); await Task.Yield(); }
        }
        /// <summary>
        /// 检查当前行为是否满足验收条件。
        /// </summary>
        public static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private async void Start()
        {
            var report = new Report { unity = Application.unityVersion, platform = Application.platform.ToString() };
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            try
            {
                await CheckScene();
                foreach (var entry in Additional) { await entry.Run(); _cases.Add(entry.Name); }
                if (Application.CanStreamedLevelBeLoaded("DualInputSystem"))
                {
                    SceneManager.LoadScene("DualInputSystem"); await Task.Yield(); await CheckScene();
                }
                report.passed = true;
            }
            catch (Exception ex) { report.error = ex.ToString(); Debug.LogException(ex); }
            report.cases = _cases.ToArray();
            var arguments = Environment.GetCommandLineArgs(); int index = Array.IndexOf(arguments, "--sky-report");
            if (index >= 0 && index + 1 < arguments.Length)
            {
                string path = Path.GetFullPath(arguments[index + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            Debug.Log("SKY PLAYER VALIDATION " + (report.passed ? "PASSED" : "FAILED")); Application.Quit(report.passed ? 0 : 1);
        }
        private async Task CheckScene()
        {
            SkyInputHost component = null; SkyUnityHost host = null; SkyPlayerContext first = null, second = null;
            await Wait(() =>
            {
                component = FindObjectOfType<SkyInputHost>(); host = component?.Host; if (host == null) return false;
                host.SetApplicationFocused(true);
                return host.Router.TryGetPlayer("sample-pointer-0", out first) && host.Router.TryGetPlayer("sample-pointer-1", out second) && first.Session != null && second.Session != null;
            }, 90);
            string name = SceneManager.GetActiveScene().name; var firstTarget = first.Target; var secondTarget = second.Target;
            Require(first.Session.Snapshot.Lifecycle == SessionLifecycle.Active, "首位玩家没有进入活动状态");
            first.Handle(SkyControl.SelectAll); first.Handle(SkyControl.Clear); first.Session.Process(InputOperation.SwitchLanguage("zh-Hans")); first.Handle(SkyControl.Text, text: "hen");
            await Wait(() => !first.Session.Snapshot.Querying && first.Session.Snapshot.Candidates != null);
            var page = first.Session.Snapshot.Candidates; var candidate = page.Candidates.FirstOrDefault(value => value.Text == "很"); Require(candidate != null, "中文候选缺少很");
            first.Session.Process(InputOperation.ConfirmCandidate(page.Query.Id, candidate.Id)); await Wait(() => first.Session.Snapshot.Composition.IsEmpty);
            Require(firstTarget.ReadSnapshot().Text == "Hello，世界！", "草稿提前写入原框");
            Require(second.Session.Snapshot.LanguageId == "en" && second.Session.Snapshot.Text.Text == "Hello，世界！", "双玩家状态互相影响");
            first.Handle(SkyControl.Finish); await Wait(() => first.Session == null); Require(firstTarget.ReadSnapshot().Text == "很", "中文草稿回填结果错误");
            second.Handle(SkyControl.SelectAll); second.Handle(SkyControl.Clear); second.Session.Process(InputOperation.SwitchLanguage("ko")); second.Handle(SkyControl.Text, text: "rksk");
            Require(second.Session.Snapshot.Composition.DisplayText == "가나", "韩语组合结果错误"); second.Handle(SkyControl.Backspace);
            Require(second.Session.Snapshot.Composition.DisplayText == "간", "韩语回退结果错误"); second.Handle(SkyControl.Finish); await Wait(() => second.Session == null);
            Require(secondTarget.ReadSnapshot().Text == "간", "韩语草稿回填结果错误");
            Require(first.Open(firstTarget, SkyRuntime.Default, EditingMode.Direct, "en").Succeeded, "原框会话打开失败"); first.Handle(SkyControl.Text, text: "!"); first.Handle(SkyControl.Cancel);
            Require(firstTarget.ReadSnapshot().Text == "很!", "原框取消未保留已接受文本");
            _cases.Add(name + " 中文全拼与保存"); _cases.Add(name + " 韩语组合与回退"); _cases.Add(name + " 双玩家隔离与原框取消");
        }
    }
}
