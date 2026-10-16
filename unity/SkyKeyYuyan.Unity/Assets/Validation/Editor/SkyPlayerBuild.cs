using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SkyKeyYuyan.Validation.Editor
{
    /// <summary>
    /// 构建包含两个输入后端的 Windows 验收 Player。
    /// </summary>
    public static class SkyPlayerBuild
    {
        /// <summary>
        /// 通过 --sky-backend Mono 或 IL2CPP 选择编译后端。
        /// </summary>
        public static void Build()
        {
            var arguments = Environment.GetCommandLineArgs(); int index = Array.IndexOf(arguments, "--sky-backend");
            bool il2cpp = index >= 0 && index + 1 < arguments.Length && arguments[index + 1] == "IL2CPP";
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            index = Array.IndexOf(arguments, "--sky-output");
            string directory = index >= 0 && index + 1 < arguments.Length ? Path.GetFullPath(arguments[index + 1])
                : Path.Combine(root, "artifacts/Player", il2cpp ? "IL2CPP" : "Mono");
            Directory.CreateDirectory(directory);
            var previous = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            var stripping = PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Standalone);
            bool background = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.High); PlayerSettings.runInBackground = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/SkyKeyYuyanSamples/Scenes/DualLegacy.unity", "Assets/SkyKeyYuyanSamples/Scenes/DualInputSystem.unity" },
                    target = BuildTarget.StandaloneWindows64, locationPathName = Path.Combine(directory, "SkyKeyYuyan.exe"), options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Windows Player 构建失败：" + report.summary.result);
                Debug.Log("SKY Player built: " + directory);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previous); PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, stripping); PlayerSettings.runInBackground = background;
            }
        }
    }
}
