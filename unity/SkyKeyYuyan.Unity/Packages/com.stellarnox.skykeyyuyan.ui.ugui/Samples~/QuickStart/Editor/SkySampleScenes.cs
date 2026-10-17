using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkyKeyYuyan.Samples.Editor
{
    /// <summary>
    /// 为已安装的示例后端生成单人与双玩家场景。
    /// </summary>
    public static class SkySampleScenes
    {
        [MenuItem("Tools/SkyKeyYuyan/示例/单人 Legacy")]
        private static void SingleLegacy() => Create("Legacy", 1);
        [MenuItem("Tools/SkyKeyYuyan/示例/双玩家 Legacy")]
        private static void DualLegacy() => Create("Legacy", 2);
        [MenuItem("Tools/SkyKeyYuyan/示例/单人 Input System")]
        private static void SingleInputSystem() => Create("InputSystem", 1);
        [MenuItem("Tools/SkyKeyYuyan/示例/双玩家 Input System")]
        private static void DualInputSystem() => Create("InputSystem", 2);
        /// <summary>
        /// 在开发工程中生成四个场景，供批处理构建使用。
        /// </summary>
        public static void BuildAllScenes()
        {
            foreach (string backend in new[] { "Legacy", "InputSystem" })
                foreach (int count in new[] { 1, 2 }) Create(backend, count);
        }
        private static void Create(string backend, int count)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("SkyKeyYuyan.Samples.Sky" + backend + "QuickStart")).FirstOrDefault(value => value != null);
            if (type == null) throw new InvalidOperationException("所选示例输入后端尚不可用");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owner = new GameObject("SkyKeyYuyan Example"); var demo = (SkyQuickStart)owner.AddComponent(type); demo.PlayerCount = count;
            if (count == 1) demo.InitialText = "Hello，世界！\n用左摇杆移动光标。\n用右摇杆框选文本。";
            string script = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(demo));
            string directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.GetDirectoryName(script), "../Scenes"));
            Directory.CreateDirectory(directory);
            string relative = FileUtil.GetProjectRelativePath(directory.Replace('\\', '/'));
            if (!EditorSceneManager.SaveScene(scene, relative + "/" + (count == 1 ? "Single" : "Dual") + backend + ".unity")) throw new IOException("示例场景保存失败");
            AssetDatabase.Refresh();
        }
    }
}
