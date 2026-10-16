using System;
using SkyKeyYuyan.Unity.Legacy;
using UnityEditor;

namespace SkyKeyYuyan.Unity.Editor
{
    /// <summary>
    /// 为 Windows 手柄配置添加缺少的 Legacy 轴。
    /// </summary>
    public static class SkyLegacyInputSetup
    {
        /// <summary>
        /// 仅添加 SKY 默认轴，保留宿主已有的同名配置。
        /// </summary>
        [MenuItem("Tools/SkyKeyYuyan/添加 Legacy 默认输入轴")]
        public static void ConfigureDefaults()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
            var axes = settings.FindProperty("m_Axes");
            for (int joystick = 1; joystick <= 8; joystick++)
            {
                var profile = new LegacyGamepadProfile(joystick);
                Add(axes, profile.LeftX, joystick, 1); Add(axes, profile.LeftY, joystick, 2, true);
                Add(axes, profile.RightX, joystick, 4); Add(axes, profile.RightY, joystick, 5, true);
                Add(axes, profile.DpadX, joystick, 6); Add(axes, profile.DpadY, joystick, 7);
                Add(axes, profile.LeftTrigger, joystick, 3);
            }
            settings.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
        }
        private static void Add(SerializedProperty axes, string name, int joystick, int axis, bool invert = false)
        {
            for (int index = 0; index < axes.arraySize; index++) if (axes.GetArrayElementAtIndex(index).FindPropertyRelative("m_Name").stringValue == name) return;
            axes.InsertArrayElementAtIndex(axes.arraySize);
            var entry = axes.GetArrayElementAtIndex(axes.arraySize - 1);
            entry.FindPropertyRelative("m_Name").stringValue = name;
            foreach (string property in new[] { "descriptiveName", "descriptiveNegativeName", "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton" }) entry.FindPropertyRelative(property).stringValue = "";
            entry.FindPropertyRelative("gravity").floatValue = 0; entry.FindPropertyRelative("dead").floatValue = 0.19f; entry.FindPropertyRelative("sensitivity").floatValue = 1;
            entry.FindPropertyRelative("snap").boolValue = false; entry.FindPropertyRelative("invert").boolValue = invert;
            entry.FindPropertyRelative("type").intValue = 2; entry.FindPropertyRelative("axis").intValue = axis - 1; entry.FindPropertyRelative("joyNum").intValue = joystick;
        }
    }
}
