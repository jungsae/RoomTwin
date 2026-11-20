using RoomTwin.Geometry;
using UnityEditor;
using UnityEngine;

namespace RoomTwin.Geometry.EditorTools
{
    [CustomEditor(typeof(RoomGeometryController))]
    public sealed class RoomGeometryControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            if (GUILayout.Button("Rebuild Room"))
            {
                changed = true;
            }

            var controller = (RoomGeometryController)target;
            if (changed)
            {
                EditorApplication.delayCall += () =>
                {
                    if (controller == null)
                    {
                        return;
                    }

                    controller.Rebuild();
                    EditorUtility.SetDirty(controller);
                    Repaint();
                };
            }

            if (!string.IsNullOrEmpty(controller.LastError))
            {
                EditorGUILayout.HelpBox(controller.LastError, MessageType.Error);
            }
            else if (controller.HasValidMesh)
            {
                EditorGUILayout.HelpBox(
                    "Valid convex room. Corners are room-local meters (x = X, y = Z), separate from AR session space.",
                    MessageType.Info);
            }
        }
    }
}
