using UnityEditor;
using UnityEngine;

namespace ResoniteTools
{
    /// <summary>
    /// Inspector now targets Renderer instead of every GameObject to avoid performance hits.
    /// </summary>
    // [CustomEditor(typeof(Renderer))]
    public class UnlitLightmapGeneratorInspector : Editor
    {
        private bool showUnlitLightmapTools = false;
        private float vertexOffset = 0.001f;
        private bool preserveOriginalMaterials = false;
        private bool addLightmapExportMenu = false;

        public override void OnInspectorGUI()
        {
            // Draw the default inspector
            DrawDefaultInspector();
            
            // Add our custom UI
            EditorGUILayout.Space(10);
            showUnlitLightmapTools = EditorGUILayout.Foldout(showUnlitLightmapTools, "Unlit Lightmap Tools");
            
            if (showUnlitLightmapTools)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Convert lightmapped objects to use UV1 for Resonite compatibility", MessageType.Info);
                
                // Options
                vertexOffset = EditorGUILayout.Slider("Vertex Offset", vertexOffset, 0.0001f, 0.01f);
                preserveOriginalMaterials = EditorGUILayout.Toggle("Preserve Original Materials", preserveOriginalMaterials);
                
                EditorGUILayout.Space(5);
                
                // Main buttons
                if (GUILayout.Button("Generate Unlit Lightmap Overlay"))
                {
                    Renderer renderer = target as Renderer;
                    if (renderer != null)
                    {
                        GenerateUnlitLightmap(renderer.gameObject);
                    }
                }
                
                EditorGUILayout.Space(5);
                
                // Export tools
                addLightmapExportMenu = EditorGUILayout.Foldout(addLightmapExportMenu, "Export Tools");
                if (addLightmapExportMenu)
                {
                    EditorGUI.indentLevel++;
                    
                    if (GUILayout.Button("Export Lightmap Textures..."))
                    {
                        UnlitLightmapUtilities.ExportLightmapsToFolder("", UnlitLightmapUtilities.LightmapExportFormat.PNG);
                    }
                    
                    if (GUILayout.Button("Create Resonite Shader"))
                    {
                        UnlitLightmapUtilities.CreateResoniteShaderAsset();
                        EditorUtility.DisplayDialog("Shader Created", "Resonite-compatible shader has been created in Assets/Editor/ResoniteShaders folder.", "OK");
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private void GenerateUnlitLightmap(GameObject targetObject)
        {
            // Get or create the Unlit Lightmap Generator window
            UnlitLightmapGenerator window = EditorWindow.GetWindow<UnlitLightmapGenerator>();
            
            // Configure it
            System.Type windowType = window.GetType();
            System.Reflection.FieldInfo targetRootField = windowType.GetField("targetRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.FieldInfo vertexOffsetField = windowType.GetField("vertexOffset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (targetRootField != null)
                targetRootField.SetValue(window, targetObject);
                
            if (vertexOffsetField != null)
                vertexOffsetField.SetValue(window, vertexOffset);
            
            // Call the generation method
            System.Reflection.MethodInfo generateMethod = windowType.GetMethod("GenerateUnlitLightmaps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (generateMethod != null)
            {
                generateMethod.Invoke(window, null);
            }
            else
            {
                // Fallback - just show the window
                window.Show();
            }
        }
    }
}