using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace SkyKeyYuyan.Unity.Editor
{
    /// <summary>
    /// 检查项目依赖，并为当前构建平台配置可选适配和默认表现。
    /// </summary>
    public sealed class SkyModuleWindow : EditorWindow
    {
        private BuildTargetGroup _group;
        private HashSet<string> _symbols;
        private Dictionary<string, string> _packages;
        private Vector2 _scroll;
#if !UNITY_2022_1_OR_NEWER
        private UnityEditor.PackageManager.Requests.ListRequest _request;
#endif
        [MenuItem("Tools/SkyKeyYuyan/模块控制面板")]
        private static void Open() => GetWindow<SkyModuleWindow>("SkyKeyYuyan 模块");
        private void OnEnable() => Refresh();
        private void Refresh()
        {
            _group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (_group == BuildTargetGroup.Unknown) _group = BuildTargetGroup.Standalone;
            _symbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(_group).Split(';').Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
#if UNITY_2022_1_OR_NEWER
            _packages = PackageInfo.GetAllRegisteredPackages().GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First().version, StringComparer.Ordinal);
#else
            _packages = new Dictionary<string, string>(StringComparer.Ordinal);
            _request = UnityEditor.PackageManager.Client.List(true, true);
#endif
        }
#if !UNITY_2022_1_OR_NEWER
        private void Update()
        {
            if (_request == null || !_request.IsCompleted) return;
            if (_request.Status == UnityEditor.PackageManager.StatusCode.Success)
                _packages = _request.Result.GroupBy(value => value.name).ToDictionary(group => group.Key, group => group.First().version, StringComparer.Ordinal);
            _request = null; Repaint();
        }
#endif
        private void OnGUI()
        {
            if (_symbols == null || _packages == null) Refresh();
            EditorGUILayout.LabelField("SkyKeyYuyan", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("构建平台", _group.ToString());
            EditorGUILayout.HelpBox("适配同时满足依赖、平台能力和模块开关时生效。语言由宿主在初始化时显式注册。", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Installed("核心", "com.stellarnox.skykeyyuyan.core");
            Installed("中文全拼", "com.stellarnox.skykeyyuyan.chinese");
            Installed("英语补全", "com.stellarnox.skykeyyuyan.english");
            Installed("韩语两套式", "com.stellarnox.skykeyyuyan.korean");
            EditorGUILayout.Space();
            bool ugui = AtLeast("com.unity.ugui", 1), tmp = AtLeast("com.unity.textmeshpro", 2) || AtLeast("com.unity.ugui", 2);
            bool fairy = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "FairyGUI");
            int input = InputHandling();
            Toggle("UGUI 文本目标", ugui, "需要 UGUI", "SKY_DISABLE_UGUI");
            Toggle("TMP 文本目标", tmp, "需要 TMP 或 UGUI 的整合版本", "SKY_DISABLE_TMP");
            Toggle("Legacy Input", input == 0 || input == 2, "需要启用 Legacy Input", "SKY_DISABLE_LEGACY");
            Toggle("Input System", AtLeast("com.unity.inputsystem", 1) && input != 0, "需要安装并启用 Input System", "SKY_DISABLE_INPUT_SYSTEM");
            Toggle("FairyGUI 文本目标", fairy && Has("com.stellarnox.skykeyyuyan.fairygui"), "需要 FairyGUI SDK 和适配包", "SKY_FAIRYGUI", true);
            Toggle("UGUI 默认界面", ugui && !_symbols.Contains("SKY_DISABLE_UGUI") && Has("com.stellarnox.skykeyyuyan.ui.ugui"), "需要 UGUI 适配和表现包", "SKY_DISABLE_UGUI_UI");
            Toggle("FairyGUI 默认界面", fairy && _symbols.Contains("SKY_FAIRYGUI") && Has("com.stellarnox.skykeyyuyan.ui.fairygui"), "需要 FairyGUI 适配和表现包", "SKY_DISABLE_FAIRYGUI_UI");
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重新检测")) Refresh();
                if (GUILayout.Button("应用到当前平台"))
                {
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(_group, string.Join(";", _symbols.OrderBy(value => value, StringComparer.Ordinal)));
                    AssetDatabase.SaveAssets();
                }
            }
        }
        private void Installed(string label, string package)
        { EditorGUILayout.LabelField(label, _packages.TryGetValue(package, out var version) ? "已安装 " + version : "未安装"); }
        private void Toggle(string label, bool available, string missing, string symbol, bool positive = false)
        {
            bool requested = positive ? _symbols.Contains(symbol) : !_symbols.Contains(symbol);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool next;
                using (new EditorGUI.DisabledScope(!available && !requested)) next = EditorGUILayout.ToggleLeft(label, requested);
                EditorGUILayout.LabelField(available ? requested ? "可用" : "已关闭" : missing, EditorStyles.miniLabel);
                if (next == requested) return;
                if (next == positive) _symbols.Add(symbol); else _symbols.Remove(symbol);
            }
        }
        private bool Has(string package) => _packages.ContainsKey(package);
        private bool AtLeast(string package, int major)
        { return _packages.TryGetValue(package, out var value) && Version.TryParse(value.Split('-')[0], out var version) && version.Major >= major; }
        private static int InputHandling()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0) return 0;
            var settings = new SerializedObject(assets[0]); return settings.FindProperty("activeInputHandler")?.intValue ?? 0;
        }
    }
}
