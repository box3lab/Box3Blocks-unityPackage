using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    public static class BlockWorldJsonTemplateMenu
    {
        private const string Template = "{\n  \"blocks\": [\n    { \"id\": \"grass\", \"x\": 0, \"y\": 0, \"z\": 0 },\n    { \"id\": \"stone\", \"x\": 1, \"y\": 0, \"z\": 0 }\n  ]\n}\n";

        [MenuItem("Assets/Create/Block World MVP/Blocks JSON Template", priority = 2000)]
        public static void CreateTemplateJson()
        {
            string folder = GetActiveFolderPath();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, "BlocksData.json"));
            string fullPath = Path.GetFullPath(assetPath);

            File.WriteAllText(fullPath, Template);
            AssetDatabase.Refresh();

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string GetActiveFolderPath()
        {
            Object selected = Selection.activeObject;
            if (selected == null)
            {
                return "Assets";
            }

            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Assets";
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                return path;
            }

            return Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "Assets";
        }
    }
}
