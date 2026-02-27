using UnityEditor;
using UnityEngine;

namespace BlockWorldMVP.Editor
{
    [CustomEditor(typeof(BlockWorldGenerator))]
    public class BlockWorldGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            BlockWorldGenerator generator = (BlockWorldGenerator)target;
            if (GUILayout.Button("Build From JSON"))
            {
                Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Build Block World");
                generator.BuildFromInspectorJson();
                EditorUtility.SetDirty(generator.gameObject);
            }

            if (GUILayout.Button("Clear Generated"))
            {
                Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Clear Block World");
                generator.ClearGenerated();
                EditorUtility.SetDirty(generator.gameObject);
            }
        }
    }
}
