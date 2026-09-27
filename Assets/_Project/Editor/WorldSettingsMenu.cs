using MedievalZen.World;
using UnityEditor;
using UnityEngine;

namespace MedievalZen.EditorTools
{
    /// <summary>Menu items for <see cref="WorldSettings"/> assets.</summary>
    internal static class WorldSettingsMenu
    {
        /// <summary>Folder that holds the project's WorldSettings assets.</summary>
        public const string SettingsFolder = "Assets/_Project/Settings";

        /// <summary>Path of the default WorldSettings asset.</summary>
        public const string DefaultSettingsPath = SettingsFolder + "/WorldSettings.asset";

        [MenuItem("MedievalZen/Create Default World Settings")]
        private static void CreateDefaultWorldSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(DefaultSettingsPath);
            if (settings == null)
            {
                EnsureFolder(SettingsFolder);
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Created {DefaultSettingsPath}", settings);
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
