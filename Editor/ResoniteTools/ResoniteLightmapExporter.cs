using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Reflection;

namespace ResoniteTools
{
    /// <summary>
    /// Advanced tool for exporting Unity lightmapped scenes to Resonite-compatible format
    /// Extends the basic UnlitLightmapGenerator with batch processing and export features
    /// </summary>
    public partial class ResoniteLightmapExporter : EditorWindow
    {
        private enum ExportFormat { GLTF, FBX, UnityPrefab } // デフォルトを GLTF (glb) へ

        private GameObject targetRoot;
        private bool processRecursively = true;
        private float vertexOffset = 0.001f;
        private bool enableVertexOffset = true; // 頂点オフセット処理を有効/無効にするフラグ
        private bool autoExport = false;
        private ExportFormat exportFormat = ExportFormat.GLTF;   // ← 既定値を GLTF
        private string exportPath = "";

        // --- Advanced --------------------------------------------------------
        private bool preserveOriginalMaterials = false;
        private bool mergeByLightmap = true;
        private bool optimizeMeshes = true;
        private bool combineByLightmapMaterial = true; // 同じライトマップを参照するメッシュを結合
        private bool exportLightmapTextures = true;
        private int  maxLightmapSize = 2048;
        private bool useCustomShader = true;

        // Generator‑specific options -----------------------------------------
        private bool shareMaterials  = true;
        private bool addNoise        = false;
        private float noiseStrength  = 0.01f;

        // Make sure this has the same values in the same order as the enum in UnlitLightmapGenerator
        private enum VertexOffsetMode { Absolute, RelativeToScale }
        private VertexOffsetMode vertexOffsetMode = VertexOffsetMode.Absolute;
        private bool verboseLogging = false;
        private bool recalcNormalsFirst = false; // 法線の事前再計算オプション
        
        // 階層構造とオブジェクト結合のオプション
        private bool preserveHierarchy = true;    // 元のオブジェクト階層を維持する
        private bool combineAllMeshes = false;    // すべてのメッシュを1つに結合する

        // Processing status
        private bool isProcessing = false;
        private float processingProgress = 0f;
        private string processingStatus = "";
        
        // ★★★ 追加列挙 ----------
        private UnlitLightmapUtilities.LightmapExportFormat texFormat =
            UnlitLightmapUtilities.LightmapExportFormat.PNG;

        // --------------------------------------------------------------------
        [MenuItem("Tools/Resonite Lightmap Processor")]
        public static void ShowWindow() => GetWindow<ResoniteLightmapExporter>("Resonite Lightmap Processor");

        // --------------------------------------------------------------------
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Resonite Lightmap Processor", EditorStyles.boldLabel);

            // --- Target ------------------------------------------------------
            targetRoot = EditorGUILayout.ObjectField("Target Root Object", targetRoot, typeof(GameObject), true) as GameObject;
            processRecursively = EditorGUILayout.Toggle("Process Child Objects", processRecursively);

            // --- Advanced ----------------------------------------------------
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Advanced Settings", EditorStyles.boldLabel);
            enableVertexOffset = EditorGUILayout.Toggle("Enable Vertex Offset", enableVertexOffset);
            if (enableVertexOffset)
            {
                EditorGUI.indentLevel++;
                vertexOffset = EditorGUILayout.Slider("Vertex Offset", vertexOffset, 0.0001f, 0.02f);
                vertexOffsetMode = (VertexOffsetMode)EditorGUILayout.EnumPopup("Offset Mode", vertexOffsetMode);
                EditorGUI.indentLevel--;
            }

            // Mesh Optimization section
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Mesh Optimization", EditorStyles.miniBoldLabel);
            recalcNormalsFirst = EditorGUILayout.Toggle("Recalculate Normals First", recalcNormalsFirst);
            
            // Mesh Combining Options section
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Mesh Combining", EditorStyles.miniBoldLabel);
            
            // 同じライトマップのメッシュを結合するオプション
            combineByLightmapMaterial = EditorGUILayout.Toggle("Combine Same Lightmap Meshes", combineByLightmapMaterial);
            if (combineByLightmapMaterial)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Combines meshes sharing the same lightmap material into single objects. Reduces draw calls significantly.", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            
            mergeByLightmap = EditorGUILayout.Toggle("Merge By Lightmap", mergeByLightmap);
            if (mergeByLightmap)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("This option only affects generation, not post-processing.", MessageType.Info);
                EditorGUI.indentLevel--;
            }

            // Material Options section
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Material Options", EditorStyles.miniBoldLabel);
            preserveOriginalMaterials = EditorGUILayout.Toggle("Preserve Original Materials", preserveOriginalMaterials);
            shareMaterials = EditorGUILayout.Toggle("Share Materials (same lightmap)", shareMaterials);

            // Texture Settings section
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Texture Settings", EditorStyles.miniBoldLabel);

            maxLightmapSize = EditorGUILayout.IntPopup(
                "Max Lightmap Size",
                maxLightmapSize,
                new[] { "256", "512", "1024", "2048", "4096" },
                new[] { 256, 512, 1024, 2048, 4096 });

            texFormat = (UnlitLightmapUtilities.LightmapExportFormat)EditorGUILayout.EnumPopup(
                "Texture Format",
                texFormat);

            // Noise options
            addNoise = EditorGUILayout.Toggle("Add Dither Noise", addNoise);
            if (addNoise)
            {
                EditorGUI.indentLevel++;
                noiseStrength = EditorGUILayout.Slider("Noise Strength", noiseStrength, 0.001f, 0.05f);
                EditorGUI.indentLevel--;
            }
            verboseLogging = EditorGUILayout.Toggle("Verbose Logging", verboseLogging);
            
            // 階層構造と結合オプション
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Hierarchy Options", EditorStyles.boldLabel);
            preserveHierarchy = EditorGUILayout.Toggle("Preserve Original Hierarchy", preserveHierarchy);
            
            // 階層構造を維持しない場合のみ、メッシュ結合オプションを表示
            if (!preserveHierarchy)
            {
                EditorGUI.indentLevel++;
                combineAllMeshes = EditorGUILayout.Toggle("Combine All Meshes", combineAllMeshes);
                EditorGUI.indentLevel--;
            }

            useCustomShader = EditorGUILayout.Toggle("Use Custom Resonite Shader", useCustomShader);

            // --- Buttons -----------------------------------------------------
            GUI.enabled = targetRoot && !isProcessing;
            if (GUILayout.Button("Process Lightmapped Objects")) ProcessLightmappedObjects();
            GUI.enabled = true;

            if (isProcessing)
            {
                EditorGUILayout.Space(8);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18f), processingProgress, processingStatus);
            }
        }

        // --------------------------------------------------------------------
        private void ProcessLightmappedObjects()
        {
            if (!targetRoot)
            {
                EditorUtility.DisplayDialog("Error", "No target root object selected!", "OK");
                return;
            }

            isProcessing = true;
            processingProgress = 0f;
            processingStatus = "Initializing…";

            if (useCustomShader)
            {
                UnlitLightmapUtilities.EnsureCustomShadersExist();
                UnlitLightmapUtilities.CreateResoniteShaderAsset();
            }

            EditorApplication.update += ProcessingUpdate;
        }

        // --------------------------------------------------------------------
        private void ProcessingUpdate()
        {
            // This simulates a coroutine in EditorWindow since actual coroutines aren't available
            if (!isProcessing)
            {
                EditorApplication.update -= ProcessingUpdate;
                return;
            }
            
            // Process in steps
            if (processingProgress < 0.1f)
            {
                // Step 1: Find all lightmapped renderers
                processingStatus = "Finding lightmapped renderers...";
                processingProgress = 0.1f;
                Repaint();
            }
            else if (processingProgress < 0.3f)
            {
                processingStatus = "Processing lightmapped renderers…";
                processingProgress = 0.3f;

                var generator = ScriptableObject.CreateInstance<UnlitLightmapGenerator>();
                var t = generator.GetType();

                // ---- フィールド設定 ---------------------------------------
                void Set(string name, object value)
                {
                    var f = t.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null) f.SetValue(generator, value);
                }
                
                // Special method to handle enum conversion properly
                void SetEnum<TSource, TTarget>(string fieldName, TSource value) where TSource : Enum where TTarget : Enum
                {
                    var field = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        // Convert by using the name of the enum value rather than a direct cast
                        TTarget targetValue = (TTarget)Enum.Parse(typeof(TTarget), value.ToString());
                        field.SetValue(generator, targetValue);
                    }
                }
                
                Set("targetRoot", targetRoot);
                Set("vertexOffset", vertexOffset);
                Set("enableVertexOffset", enableVertexOffset);
                Set("processRecursively", processRecursively);
                
                // Use enum name-based conversion instead of direct casting
                var targetEnumType = t.GetNestedType("VertexOffsetMode", System.Reflection.BindingFlags.NonPublic);
                if (targetEnumType != null)
                {
                    object targetValue = Enum.Parse(targetEnumType, vertexOffsetMode.ToString());
                    var field = t.GetField("vertexOffsetMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null) field.SetValue(generator, targetValue);
                }
                else
                {
                    // Fallback to integer-based conversion
                    Set("vertexOffsetMode", (int)vertexOffsetMode);
                }
                
                Set("shareMaterials", shareMaterials);
                Set("addNoise", addNoise);
                Set("noiseStrength", noiseStrength);
                Set("verboseLogging", verboseLogging);
                Set("recalcNormalsFirst", recalcNormalsFirst);
                Set("preserveHierarchy", preserveHierarchy);
                Set("combineAllMeshes", combineAllMeshes);
                Set("mergeByLightmap", mergeByLightmap);
                Set("combineByLightmapMaterial", combineByLightmapMaterial);
                
                // テクスチャ設定の転送
                Set("maxLightmapSize", maxLightmapSize);

                // Generator 側 enum に合わせて文字列で変換してセット
                {
                    var field = t.GetField("texFormat", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var textureFormatEnumType = field.FieldType;                       // ← Generator 内部 enum
                        object valueToSet  = Enum.Parse(textureFormatEnumType, texFormat.ToString());
                        field.SetValue(generator, valueToSet);
                    }
                }

                // ---- 実行 ---------------------------------------------------
                var generateMethod = t.GetMethod("GenerateUnlitLightmaps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (generateMethod != null)
                {
                    generateMethod.Invoke(generator, null);
                    
                    // キャッシュをクリア
                    System.Reflection.FieldInfo readableLightmapCacheField = t.GetField("readableLightmapCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    System.Reflection.FieldInfo materialCacheField = t.GetField("materialCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    
                    if (readableLightmapCacheField != null)
                    {
                        var cache = readableLightmapCacheField.GetValue(null) as System.Collections.IDictionary;
                        if (cache != null) cache.Clear();
                    }
                    
                    if (materialCacheField != null)
                    {
                        var cache = materialCacheField.GetValue(null) as System.Collections.IDictionary;
                        if (cache != null) cache.Clear();
                    }
                }

                DestroyImmediate(generator);
                Repaint();
            }
            else if (processingProgress < 0.5f)
            {
                // Step 3: Optimize if requested (skip optimizeMeshes as it breaks things)
                if (combineByLightmapMaterial)
                {
                    processingStatus = "Combining meshes by lightmap...";
                    MergeMeshesByLightmap();   // 同じライトマップのメッシュを結合する
                }
                processingProgress = 0.5f;
                Repaint();
            }
            else if (processingProgress < 0.9f)
            {
                // 完了処理へ
                processingProgress = 0.9f;
                Repaint();
            }
            else
            {
                // Finished
                processingStatus = "Processing complete!";
                processingProgress = 1f;
                isProcessing = false;
                Repaint();
                
                EditorUtility.DisplayDialog("Processing Complete", "Lightmap processing completed successfully!", "OK");
                EditorApplication.update -= ProcessingUpdate;
            }
        }
        
        private void ExportProcessedObjects()
        {
            if (string.IsNullOrEmpty(exportPath))
            {
                exportPath = EditorUtility.SaveFolderPanel("Select Export Folder", Application.dataPath, "");
                if (string.IsNullOrEmpty(exportPath))
                {
                    return;
                }
            }
            
            // Find the generated lightmap group (assuming it follows the naming convention)
            Transform lightmapGroup = null;
            string groupName = targetRoot.name + "_UnlitLM_Group";
            
            // First try to find the group as a direct child of the scene (root objects)
            foreach (GameObject rootObj in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObj.name == groupName)
                {
                    lightmapGroup = rootObj.transform;
                    break;
                }
            }
            
            // If not found at root level, fallback to looking under the target root
            if (lightmapGroup == null && targetRoot != null)
            {
                foreach (Transform child in targetRoot.transform)
                {
                    if (child.name == groupName)
                    {
                        lightmapGroup = child;
                        break;
                    }
                }
            }
            
            if (lightmapGroup == null)
            {
                EditorUtility.DisplayDialog("Export Error", "Could not find the processed lightmap group! Please process the lightmapped objects first.", "OK");
                return;
            }
            
            string exportFileName = targetRoot.name + "_ResoniteUnlit";
            string fullPath = Path.Combine(exportPath, exportFileName);
            
            // Ensure we're preserving absolute transforms during export
            Debug.Log($"Exporting lightmapped group '{lightmapGroup.name}' with world position {lightmapGroup.position}, " +
                      $"rotation {lightmapGroup.rotation.eulerAngles}, scale {lightmapGroup.lossyScale}");
            
            switch (exportFormat)
            {
                case ExportFormat.FBX:
                    ExportAsFBX(lightmapGroup.gameObject, fullPath + ".fbx");
                    break;
                    
                case ExportFormat.GLTF:
                    ExportAsGLTF(lightmapGroup.gameObject, fullPath);
                    break;
                    
                case ExportFormat.UnityPrefab:
                    ExportAsPrefab(lightmapGroup.gameObject, fullPath + ".prefab");
                    break;
            }
        }
        
        private void ExportAsFBX(GameObject target, string path)
        {
            // Check if the FBX Exporter package is available
            // Unity 2020.1以降で、FBX Exporterパッケージがインストールされている場合のみ実行
            #if UNITY_EDITOR && UNITY_2020_1_OR_NEWER && FBXEXPORTER_PRESENT
            if (UnityEditor.Formats.Fbx.Exporter.ModelExporter.ExportObjects(new UnityEngine.Object[] { target }, path))
            {
                EditorUtility.DisplayDialog("Export Successful", "Model exported successfully to:\n" + path, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Export Failed", "Failed to export model. Make sure you have the FBX Exporter package installed.", "OK");
            }
            #else
            // FBX Exporterパッケージがインストールされていない場合
            EditorUtility.DisplayDialog("Export Failed", "FBX export requires Unity 2020.1 or newer with the FBX Exporter package installed.", "OK");
            #endif
        }
        
        private void ExportAsGLTF(GameObject target, string path)
        {
            // Check if glTFast or similar package is available
            // This is just a placeholder - actual implementation depends on the GLTF package you're using
            EditorUtility.DisplayDialog("GLTF Export", "GLTF export requires a third-party package such as glTFast or UnityGLTF.\n\nPlease install one of these packages and modify this script to use it.", "OK");
        }
        
        private void ExportAsPrefab(GameObject target, string path)
        {
            // Create a prefab from the target GameObject
            string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(target, relativePath);
            
            if (prefab != null)
            {
                EditorUtility.DisplayDialog("Export Successful", "Prefab created successfully at:\n" + relativePath, "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Export Failed", "Failed to create prefab!", "OK");
            }
        }

        /// <summary>
        /// _UnlitLM_Group 配下の Mesh を 1 つに結合する。
        /// 必ず同じマテリアル（同じライトマップ）を使っている前提。
        /// </summary>
        private void MergeMeshesIntoSingleMesh()
        {
            // ── 1. 処理対象グループを取得
            if (targetRoot == null) return;
            string groupName = targetRoot.name + "_UnlitLM_Group";
            
            // まずは子オブジェクトとして探す
            Transform group = targetRoot.transform.Find(groupName);
            
            // 見つからなければシーンのルートから探す
            if (group == null)
            {
                foreach (GameObject rootObj in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (rootObj.name == groupName)
                    {
                        group = rootObj.transform;
                        break;
                    }
                }
            }
            
            if (group == null)
            {
                Debug.LogWarning("MergeMeshes: group not found → " + groupName);
                return;
            }

            // ── 2. 子 MeshFilter を収集
            MeshFilter[] mfs = group.GetComponentsInChildren<MeshFilter>();
            if (mfs.Length == 0) return;

            // ── 3. CombineInstance 配列を作成
            var combines = new CombineInstance[mfs.Length];
            int totalVerts = 0;
            for (int i = 0; i < mfs.Length; ++i)
            {
                combines[i].mesh      = mfs[i].sharedMesh;
                combines[i].transform = mfs[i].transform.localToWorldMatrix;
                totalVerts += mfs[i].sharedMesh.vertexCount;
            }

            // ── 4. 新しいメッシュを生成
            Mesh combined = new Mesh { name = targetRoot.name + "_CombinedLM" };
            
            // 頂点数で適切なインデックスフォーマットを設定（65,535頂点を超える場合は32ビットインデックスを使用）
            if (totalVerts > 65535)
            {
#if UNITY_2017_3_OR_NEWER
                combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                Debug.Log($"Using 32-bit index format for combined mesh with {totalVerts} vertices (exceeds 16-bit limit of 65,535)");
#else
                Debug.LogWarning($"Combined mesh will have {totalVerts} vertices which exceeds the 16-bit limit of 65,535. " +
                                 "This requires Unity 2017.3 or newer for 32-bit index support. The mesh may not be created correctly.");
#endif
            }
            
            combined.CombineMeshes(combines, true, true, false);

            // ── 5. 新しい GameObject を作成して親にぶら下げる
            GameObject combinedGO = new GameObject(targetRoot.name + "_CombinedLM");
            combinedGO.transform.SetParent(group, false);

            var mfNew  = combinedGO.AddComponent<MeshFilter>();
            var mrNew  = combinedGO.AddComponent<MeshRenderer>();
            mfNew.sharedMesh = combined;

            // すべて同じマテリアルを使っている前提で先頭のを流用
            mrNew.sharedMaterial = mfs[0].GetComponent<MeshRenderer>().sharedMaterial;

            // ── 6. 旧オブジェクトを削除（Undo 対応）
            foreach (var mf in mfs)
                Undo.DestroyObjectImmediate(mf.gameObject);

            // ── 7. 生成物を記録
            Undo.RegisterCreatedObjectUndo(combinedGO, "Merge Unlit LM Meshes");
            
#if UNITY_2017_3_OR_NEWER
            string indexFormatInfo = totalVerts > 65535 ? "using 32-bit indices" : "using 16-bit indices";
#else
            string indexFormatInfo = "using 16-bit indices" + (totalVerts > 65535 ? " (CAUTION: vertex count exceeds limit!)" : "");
#endif
            Debug.Log($"Successfully merged {mfs.Length} meshes into a single mesh '{combinedGO.name}' with {combined.vertexCount} vertices and {combined.triangles.Length/3} triangles ({indexFormatInfo}).");
        }

        private void MergeMeshesByLightmap()
        {
            // ── 1. 処理対象グループを取得
            if (targetRoot == null) return;
            string groupName = targetRoot.name + "_UnlitLM_Group";
            
            // まずは子オブジェクトとして探す
            Transform group = targetRoot.transform.Find(groupName);
            
            // 見つからなければシーンのルートから探す
            if (group == null)
            {
                foreach (GameObject rootObj in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (rootObj.name == groupName)
                    {
                        group = rootObj.transform;
                        break;
                    }
                }
            }
            
            if (group == null)
            {
                Debug.LogWarning("MergeMeshesByLightmap: group not found → " + groupName);
                return;
            }

            // ── 2. すべてのメッシュレンダラーを取得
            MeshRenderer[] renderers = group.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("MergeMeshesByLightmap: No mesh renderers found");
                return;
            }
            
            // ── 3. マテリアル別にグループ化
            Dictionary<Material, List<MeshFilter>> meshFiltersByMaterial = new Dictionary<Material, List<MeshFilter>>();
            
            foreach (MeshRenderer renderer in renderers)
            {
                // メッシュフィルターを取得
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;
                
                // マテリアルでグループ化
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                
                if (!meshFiltersByMaterial.ContainsKey(material))
                {
                    meshFiltersByMaterial[material] = new List<MeshFilter>();
                }
                meshFiltersByMaterial[material].Add(meshFilter);
            }
            
            // ── 4. マテリアルごとにメッシュを結合
            int combinedCount = 0;
            foreach (var entry in meshFiltersByMaterial)
            {
                Material material = entry.Key;
                List<MeshFilter> meshFilters = entry.Value;
                
                // 結合する必要がない場合はスキップ
                if (meshFilters.Count <= 1) continue;
                
                // CombineInstanceの配列を作成
                CombineInstance[] combines = new CombineInstance[meshFilters.Count];
                int totalVerts = 0;
                
                for (int i = 0; i < meshFilters.Count; i++)
                {
                    combines[i].mesh = meshFilters[i].sharedMesh;
                    combines[i].transform = meshFilters[i].transform.localToWorldMatrix;
                    totalVerts += meshFilters[i].sharedMesh.vertexCount;
                }
                
                // 新しいメッシュを作成
                Mesh combinedMesh = new Mesh { name = targetRoot.name + "_LightmapMaterial_" + combinedCount };
                
                // 頂点数で適切なインデックスフォーマットを設定
                if (totalVerts > 65535)
                {
#if UNITY_2017_3_OR_NEWER
                    combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    Debug.Log($"Using 32-bit index format for combined mesh with {totalVerts} vertices (exceeds 16-bit limit of 65,535)");
#else
                    Debug.LogWarning($"Combined mesh will have {totalVerts} vertices which exceeds the 16-bit limit of 65,535. " +
                                    "This requires Unity 2017.3 or newer for 32-bit index support. The mesh may not be created correctly.");
#endif
                }
                
                // メッシュを結合
                combinedMesh.CombineMeshes(combines, true);
                
                // 新しいGameObjectを作成
                GameObject combinedGO = new GameObject($"{material.name}_Combined_{combinedCount}");
                combinedGO.transform.SetParent(group, false);
                
                MeshFilter mf = combinedGO.AddComponent<MeshFilter>();
                MeshRenderer mr = combinedGO.AddComponent<MeshRenderer>();
                
                mf.sharedMesh = combinedMesh;
                mr.sharedMaterial = material;
                
                // 元のオブジェクトを削除
                foreach (MeshFilter filter in meshFilters)
                {
                    Undo.DestroyObjectImmediate(filter.gameObject);
                }
                
                // Undoに登録
                Undo.RegisterCreatedObjectUndo(combinedGO, "Combine Meshes By Lightmap Material");
                
                combinedCount++;
                
#if UNITY_2017_3_OR_NEWER
                string indexFormatInfo = totalVerts > 65535 ? "using 32-bit indices" : "using 16-bit indices";
#else
                string indexFormatInfo = "using 16-bit indices" + (totalVerts > 65535 ? " (CAUTION: vertex count exceeds limit!)" : "");
#endif
                Debug.Log($"Successfully combined {meshFilters.Count} meshes with same material '{material.name}' into one mesh with {combinedMesh.vertexCount} vertices ({indexFormatInfo})");
            }
            
            if (combinedCount == 0)
            {
                Debug.LogWarning("No meshes were combined - there might not be any shared materials.");
            }
            else
            {
                Debug.Log($"Total materials/objects after combining: {combinedCount} (reduced from {renderers.Length} objects)");
            }
        }
    }
}