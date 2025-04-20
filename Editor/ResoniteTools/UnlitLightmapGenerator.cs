using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace ResoniteTools
{
    /// <summary>
    /// Unity Editor extension to generate Unlit meshes with lightmaps applied to UV1
    /// for compatibility with platforms like Resonite that don't support UV2 lightmaps.
    /// </summary>
    public partial class UnlitLightmapGenerator : EditorWindow
    {
        // φ ← function that returns JP when Editor が日本語, otherwise EN
        private static string φ(string en, string jp)
        {
            // Unity 2021+ は EditorPrefs に "EditorLanguage" が入る。
            // なければ OS 言語を fallback に
            string lang = EditorPrefs.GetString("EditorLanguage",
                               Application.systemLanguage.ToString());
            return lang.StartsWith("Japanese") ? jp : en;
        }

        // Configuration Options
        private GameObject targetRoot;
        private float vertexOffset = 0.001f;
        private bool groupGeneratedObjects = true;
        private string groupSuffix = "_UnlitLM_Group";
        private string objectSuffix = "_UnlitLM";

        // UI States
        private Vector2 scrollPosition;
        private bool showAdvancedOptions = true;
        private bool processRecursively = true;
        
        // デバッグオプション
        private bool showDebugOptions = false;
        private bool verboseLogging = false;
        private bool saveTexturesToDisk = false;
        private string debugSavePath = "Assets/Debug/Lightmaps";
        
        // ★★★ テクスチャ出力設定
        private int maxLightmapSize = 2048;
        private enum LightmapExportFormat { PNG, EXR16, EXR32 }
        private LightmapExportFormat texFormat = LightmapExportFormat.PNG;

        // ★★★ 追加オプション
        private enum VertexOffsetMode { Absolute, RelativeToScale }
        private VertexOffsetMode vertexOffsetMode = VertexOffsetMode.Absolute;
        private bool shareMaterials = true;
        private bool addNoise = false;
        private float noiseStrength = 0.01f;
        private bool recalcNormalsFirst = false;  // 法線を事前に再計算するオプション
        
        // 階層構造とオブジェクト結合のオプション
        private bool preserveHierarchy = true;    // 元のオブジェクト階層を維持する
        private bool combineAllMeshes = false;    // すべてのメッシュを1つに結合する

        // ★ NEW ★ ― 同じライトマップ(Material)単位で結合
        //   Exporter が無効化された今、Generator 側で実装する
        private bool combineByLightmapMaterial = true;

        // ★★★ マテリアルキャッシュ
        private static readonly Dictionary<int, Material> materialCache = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Texture2D> readableLightmapCache = new Dictionary<int, Texture2D>();

        /// <summary>
        /// Renderer が Baked Lightmap を保持しているか判定し、
        /// 有効なら index を返す
        /// </summary>
        private bool HasBakedLightmap(Renderer r, out int index)
        {
            index = r.lightmapIndex;

            // -1 なら未割当
            if (index < 0) return false;

            // 範囲外 or Lightmap 自体が null の場合も無効
            if (index >= LightmapSettings.lightmaps.Length ||
                LightmapSettings.lightmaps[index].lightmapColor == null)
                return false;

            return true;
        }

        // Add menu item
        // [MenuItem("Tools/Generate Unlit Lightmap Overlays")]
        public static void ShowWindow()
        {
            GetWindow<UnlitLightmapGenerator>("Unlit Lightmap Generator");
        }

        // GUI
        private void OnGUI()
        {
            EditorGUILayout.LabelField(φ("Unlit Lightmap Generator",
                                        "ライトマップ → Unlit 変換ツール"),
                                      EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(φ("This tool generates UV1-based unlit meshes from lightmapped objects for platforms like Resonite.",
                                      "このツールはUV2ベースのライトマップをUV1ベースのUnlitメッシュに変換し、Resoniteなどのプラットフォームで使用できるようにします。"), 
                                    MessageType.Info);
            
            // モデル出力機能が無効化されていることを表示
            EditorGUILayout.HelpBox(φ("Model export feature is currently unavailable.", 
                                      "モデル出力機能は現在利用できません。"), 
                                    MessageType.None);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(φ("Target Settings", "ターゲット設定"), EditorStyles.boldLabel);
            
            targetRoot = EditorGUILayout.ObjectField(φ("Target Root Object", "ターゲットルートオブジェクト"), targetRoot, typeof(GameObject), true) as GameObject;
            processRecursively = EditorGUILayout.Toggle(φ("Process Child Objects", "子オブジェクトも処理する"), processRecursively);

            EditorGUILayout.Space(10);
            showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, φ("Advanced Options", "詳細設定"));
            
            if (showAdvancedOptions)
            {
                EditorGUI.indentLevel++;
                vertexOffset = EditorGUILayout.Slider(φ("Vertex Offset", "頂点オフセット"), vertexOffset, 0.0001f, 0.02f);
                vertexOffsetMode = (VertexOffsetMode)EditorGUILayout.EnumPopup(φ("Offset Mode", "オフセットモード"), vertexOffsetMode);
                recalcNormalsFirst = EditorGUILayout.Toggle(φ("Recalculate Normals First", "法線を先に再計算"), recalcNormalsFirst);
                shareMaterials = EditorGUILayout.ToggleLeft(φ("Share Materials (same lightmap)", "マテリアルを共有 (同じライトマップ)"), shareMaterials);
                addNoise = EditorGUILayout.ToggleLeft(φ("Add Dither Noise", "ディザノイズを追加"), addNoise);
                if (addNoise)
                    noiseStrength = EditorGUILayout.Slider(φ("Noise Strength", "ノイズ強度"), noiseStrength, 0.001f, 0.05f);
                groupGeneratedObjects = EditorGUILayout.Toggle(φ("Group Generated Objects", "生成オブジェクトをグループ化"), groupGeneratedObjects);
                if (groupGeneratedObjects)
                {
                    groupSuffix = EditorGUILayout.TextField(φ("Group Name Suffix", "グループ名の接尾辞"), groupSuffix);
                }
                objectSuffix = EditorGUILayout.TextField(φ("Object Name Suffix", "オブジェクト名の接尾辞"), objectSuffix);
                
                // 階層構造と結合オプション
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(φ("Hierarchy Options", "階層構造オプション"), EditorStyles.boldLabel);
                preserveHierarchy = EditorGUILayout.Toggle(φ("Preserve Original Hierarchy", "元の階層構造を維持"), preserveHierarchy);
                
                // 階層構造を維持しない場合のみ、メッシュ結合オプションを表示
                if (!preserveHierarchy)
                {
                    EditorGUI.indentLevel++;
                    combineAllMeshes = EditorGUILayout.Toggle(φ("Combine All Meshes", "すべてのメッシュを結合"), combineAllMeshes);
                    combineByLightmapMaterial = EditorGUILayout.Toggle(
                        φ("Combine Same‑Lightmap Meshes", "同じライトマップのメッシュを結合"), combineByLightmapMaterial);
                    EditorGUI.indentLevel--;
                }
                
                // ── Texture Settings ─────────────────────────────────────────────
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField(φ("Texture Settings", "テクスチャ設定"), EditorStyles.miniBoldLabel);

                EditorGUI.indentLevel++;
                maxLightmapSize = EditorGUILayout.IntPopup(
                    φ("Max Lightmap Size", "最大ライトマップサイズ"),
                    maxLightmapSize,
                    new[] { "256", "512", "1024", "2048", "4096" },
                    new[] { 256, 512, 1024, 2048, 4096 });

                texFormat = (LightmapExportFormat)EditorGUILayout.EnumPopup(
                    φ("Texture Format", "テクスチャ形式"),
                    texFormat);
                EditorGUI.indentLevel--;
                
                EditorGUI.indentLevel--;
            }
            
            // デバッグオプション
            EditorGUILayout.Space(10);
            showDebugOptions = EditorGUILayout.Foldout(showDebugOptions, φ("Debug Options", "デバッグオプション"));
            
            if (showDebugOptions)
            {
                EditorGUI.indentLevel++;
                verboseLogging = EditorGUILayout.Toggle(φ("Verbose Logging", "詳細ログ記録"), verboseLogging);
                saveTexturesToDisk = EditorGUILayout.Toggle(φ("Save Textures To Disk", "テクスチャをディスクに保存"), saveTexturesToDisk);
                if (saveTexturesToDisk)
                {
                    debugSavePath = EditorGUILayout.TextField(φ("Debug Save Path", "デバッグ保存パス"), debugSavePath);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(20);

            GUI.enabled = targetRoot != null;
            if (GUILayout.Button(φ("Generate Unlit Lightmap Objects", "Unlitライトマップオブジェクトを生成")))
            {
                GenerateUnlitLightmaps();
            }
            
            if (GUILayout.Button(φ("Debug Lightmap Information", "ライトマップ情報をデバッグ")))
            {
                DebugLightmapInfo();
            }
            GUI.enabled = true;

            EditorGUILayout.EndScrollView();
        }
        
        // デバッグ用：ライトマップ情報を表示
        private void DebugLightmapInfo()
        {
            if (targetRoot == null)
            {
                Debug.LogWarning("No target root object selected");
                return;
            }

            Renderer[] renderers;
            if (processRecursively)
                renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            else
                renderers = targetRoot.GetComponents<Renderer>();

            Debug.Log($"Found {renderers.Length} renderers in {targetRoot.name}");
            foreach (var renderer in renderers)
            {
                Debug.Log($"{renderer.name}  baked={renderer.lightmapIndex}  realtime={renderer.realtimeLightmapIndex}");
            }

            if (LightmapSettings.lightmaps != null)
            {
                Debug.Log($"Lightmap count: {LightmapSettings.lightmaps.Length}");
                for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
                {
                    var lm = LightmapSettings.lightmaps[i];
                    if (lm.lightmapColor != null)
                        Debug.Log($"Lightmap {i}: {lm.lightmapColor.width}x{lm.lightmapColor.height}");
                }
            }
            else
            {
                Debug.Log("No lightmaps found in scene");
            }
        }
        
        // デバッグ用：ライトマップをディスクに保存
        private void SaveLightmapToDisk(int lightmapIndex, string objectName)
        {
            if (lightmapIndex < 0 || lightmapIndex >= LightmapSettings.lightmaps.Length)
                return;
                
            var lightmap = LightmapSettings.lightmaps[lightmapIndex];
            if (lightmap.lightmapColor == null)
                return;
                
            // 保存先ディレクトリの作成
            string directory = debugSavePath;
            if (!System.IO.Directory.Exists(Application.dataPath + "/../" + directory))
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/../" + directory);
            }
            
            try
            {
                // 読み取り可能なコピーの作成
                Texture2D readableCopy = CreateReadableTextureCopy(lightmap.lightmapColor);
                if (readableCopy != null)
                {
                    string path;
                    byte[] bytes;
                    
                    // 指定されたフォーマットで保存
                    switch (texFormat)
                    {
                        case LightmapExportFormat.EXR32:
                        case LightmapExportFormat.EXR16:
                            // EXRとして保存
                            bytes = readableCopy.EncodeToEXR();
                            path = $"{directory}/lightmap_{lightmapIndex}_{objectName}.exr";
                            break;
                            
                        case LightmapExportFormat.PNG:
                        default:
                            // PNGとして保存
                            bytes = readableCopy.EncodeToPNG();
                            path = $"{directory}/lightmap_{lightmapIndex}_{objectName}.png";
                            break;
                    }
                    
                    System.IO.File.WriteAllBytes(Application.dataPath + "/../" + path, bytes);
                    Debug.Log($"Lightmap saved to: {path}");
                    
                    // 使用後破棄
                    Object.DestroyImmediate(readableCopy);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error saving lightmap to disk: {e.Message}");
            }
        }

        // Main Generation Method
        private void GenerateUnlitLightmaps()
        {
            if (targetRoot == null)
            {
                Debug.LogError("Target root object is not set!");
                return;
            }

            // Start recording Undo
            Undo.RegisterCompleteObjectUndo(targetRoot, "Generate Unlit Lightmap Objects");
            
            // Create a parent container for all generated objects if needed
            GameObject unlitGroup = null;
            Transform parentTransform = null;
            
            if (groupGeneratedObjects)
            {
                unlitGroup = new GameObject(targetRoot.name + groupSuffix);
                
                // Store the target root's absolute transform values
                Vector3 worldPosition = targetRoot.transform.position;
                Quaternion worldRotation = targetRoot.transform.rotation;
                Vector3 worldScale = targetRoot.transform.lossyScale;
                
                // Set the group as a direct child of the scene
                unlitGroup.transform.parent = null;
                
                // Apply absolute transforms to ensure the group has the same world-space position/rotation as the target
                unlitGroup.transform.position = worldPosition;
                unlitGroup.transform.rotation = worldRotation;
                
                // Attempt to match the world scale (though this is approximate due to how Unity handles non-uniform scaling)
                // For exact scaling, individual objects will maintain their own world scale
                unlitGroup.transform.localScale = worldScale;
                
                parentTransform = unlitGroup.transform;
                Undo.RegisterCreatedObjectUndo(unlitGroup, "Create Unlit Group");
                
                Debug.Log($"Created output group '{unlitGroup.name}' with world position {worldPosition}, rotation {worldRotation.eulerAngles}, scale {worldScale}");
            }

            // メッシュ結合用の準備
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            Dictionary<Material, List<CombineInstance>> materialToCombineInstances = new Dictionary<Material, List<CombineInstance>>();
            
            int processedCount = 0;
            int skippedCount = 0;

            // Get all renderers
            Renderer[] renderers;
            if (processRecursively)
            {
                renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                renderers = targetRoot.GetComponents<Renderer>();
            }

            Debug.Log($"Found {renderers.Length} renderers to process");
            Debug.Log($"Available lightmaps: {LightmapSettings.lightmaps.Length}");
            
            // デバッグ用に各ライトマップを事前に処理して保存
            if (saveTexturesToDisk)
            {
                PreprocessAndSaveLightmaps();
            }
            
            // 階層を維持する場合は、階層ごとに親オブジェクトを作成するための辞書
            Dictionary<Transform, Transform> originalToClonedParents = new Dictionary<Transform, Transform>();
            if (preserveHierarchy && parentTransform != null)
            {
                // 元のルートの階層構造をコピーして新しい親の下に再作成
                CreateHierarchyStructure(targetRoot.transform, parentTransform, originalToClonedParents);
            }
            
            // Process each renderer
            foreach (Renderer renderer in renderers)
            {
                bool processed;
                
                // 階層構造を保持するかどうかで処理を分岐
                if (preserveHierarchy)
                {
                    // 階層構造を保持する場合はProcessRendererWithHierarchyを呼び出す
                    Transform clonedParent = parentTransform;
                    
                    // rendererの親を取得してマッピングされた複製先を調べる
                    if (originalToClonedParents.TryGetValue(renderer.transform.parent, out Transform mappedParent))
                    {
                        clonedParent = mappedParent;
                    }
                    
                    if (ProcessRendererWithHierarchy(renderer, clonedParent, out processed))
                    {
                        if (processed)
                            processedCount++;
                        else
                            skippedCount++;
                    }
                }
                else if (combineAllMeshes)
                {
                    // すべてのメッシュを結合する場合は、結合用インスタンスを作成
                    if (ProcessRendererForCombining(renderer, combineInstances, materialToCombineInstances, out processed))
                    {
                        if (processed)
                            processedCount++;
                        else
                            skippedCount++;
                    }
                }
                else
                {
                    // 従来どおりの処理
                    if (ProcessRenderer(renderer, parentTransform, out processed))
                    {
                        if (processed)
                            processedCount++;
                        else
                            skippedCount++;
                    }
                }
            }
            
            // メッシュを結合する場合の後処理
            if (combineAllMeshes && combineInstances.Count > 0)
            {
                string combinedObjectName = targetRoot.name + "_Combined" + objectSuffix;
                
                if (shareMaterials)
                {
                    // マテリアルごとに結合
                    List<Material> materials = new List<Material>();
                    GameObject combinedObject = new GameObject(combinedObjectName);
                    combinedObject.transform.parent = parentTransform;
                    combinedObject.transform.localPosition = Vector3.zero;
                    combinedObject.transform.localRotation = Quaternion.identity;
                    combinedObject.transform.localScale = Vector3.one;
                    
                    // マテリアルごとに個別のメッシュを作成してマージ
                    List<CombineInstance> finalCombineInstances = new List<CombineInstance>();
                    int materialIndex = 0;
                    
                    foreach (var kvp in materialToCombineInstances)
                    {
                        if (kvp.Value.Count > 0)
                        {
                            materials.Add(kvp.Key);
                            
                            if (verboseLogging)
                            {
                                Debug.Log($"Processing material '{kvp.Key.name}' with {kvp.Value.Count} meshes");
                            }
                            
                            // このマテリアル用のメッシュを作成
                            Mesh submesh = new Mesh();
                            submesh.name = $"SubMesh_{materialIndex}_{kvp.Key.name}";
                            
                            // このマテリアルのすべてのメッシュを結合（マージする）
                            submesh.CombineMeshes(kvp.Value.ToArray(), true);
                            
                            // 結合したメッシュをサブメッシュとして追加
                            CombineInstance ci = new CombineInstance();
                            ci.mesh = submesh;
                            ci.subMeshIndex = 0;
                            ci.transform = Matrix4x4.identity;
                            finalCombineInstances.Add(ci);
                            
                            materialIndex++;
                        }
                    }
                    
                    // サブメッシュとして結合（マージしない）
                    Mesh finalMesh = new Mesh();
                    finalMesh.name = "CombinedMesh";
                    finalMesh.CombineMeshes(finalCombineInstances.ToArray(), false);
                    
                    MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
                    MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
                    meshFilter.sharedMesh = finalMesh;
                    meshRenderer.sharedMaterials = materials.ToArray();
                    
                    if (verboseLogging)
                    {
                        Debug.Log($"Final combined mesh has {finalMesh.subMeshCount} submeshes and {finalMesh.vertexCount} vertices");
                        Debug.Log($"Materials array has {materials.Count} materials");
                    }
                    
                    Debug.Log($"Created combined mesh '{combinedObjectName}' with {materialIndex} submeshes and {materials.Count} materials");
                }
                else
                {
                    // 単一メッシュに結合（単一マテリアル）
                    GameObject combinedObject = new GameObject(combinedObjectName);
                    combinedObject.transform.parent = parentTransform;
                    combinedObject.transform.localPosition = Vector3.zero;
                    combinedObject.transform.localRotation = Quaternion.identity;
                    combinedObject.transform.localScale = Vector3.one;
                    
                    MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
                    MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
                    
                    Mesh combinedMesh = new Mesh();
                    combinedMesh.name = "CombinedMesh";
                    combinedMesh.CombineMeshes(combineInstances.ToArray(), true);
                    
                    meshFilter.sharedMesh = combinedMesh;
                    
                    // 最初のマテリアルを使用
                    if (combineInstances.Count > 0 && materialToCombineInstances.Count > 0)
                    {
                        meshRenderer.sharedMaterial = materialToCombineInstances.Keys.First();
                    }
                    
                    Debug.Log($"Created combined mesh '{combinedObjectName}' with single material");
                }
            }

            // ★ NEW ★ ───────────
            if (combineByLightmapMaterial && groupGeneratedObjects)
            {
                MergeMeshesByLightmap(parentTransform ?? unlitGroup.transform);
            }

            // Log results
            if (processedCount > 0)
            {
                Debug.Log($"Successfully processed {processedCount} objects. Skipped {skippedCount} objects (missing UV2 or lightmap).");
            }
            else if (skippedCount > 0)
            {
                Debug.LogWarning($"No objects were processed. Skipped {skippedCount} objects (missing UV2 or lightmap).");
            }
            else
            {
                Debug.LogError("No valid lightmapped objects found in the selection!");
            }
        }
        
        // デバッグ用：全ライトマップを事前処理して保存
        private void PreprocessAndSaveLightmaps()
        {
            Debug.Log("Preprocessing all lightmaps...");
            
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                var lightmap = LightmapSettings.lightmaps[i];
                if (lightmap.lightmapColor != null)
                {
                    Debug.Log($"Preprocessing lightmap {i}: {lightmap.lightmapColor.name}");
                    
                    // 読み取り可能なコピーの作成と保存
                    Texture2D readableCopy = CreateReadableTextureCopy(lightmap.lightmapColor);
                    if (readableCopy != null)
                    {
                        if (saveTexturesToDisk)
                        {
                            // 保存先ディレクトリの作成
                            if (!System.IO.Directory.Exists(Application.dataPath + "/../" + debugSavePath))
                            {
                                System.IO.Directory.CreateDirectory(Application.dataPath + "/../" + debugSavePath);
                            }
                            
                            // 指定フォーマットで保存
                            try {
                                string extension;
                                byte[] bytes;
                                
                                switch (texFormat)
                                {
                                    case LightmapExportFormat.EXR32:
                                    case LightmapExportFormat.EXR16:
                                        bytes = readableCopy.EncodeToEXR();
                                        extension = ".exr";
                                        break;
                                    case LightmapExportFormat.PNG:
                                    default:
                                        bytes = readableCopy.EncodeToPNG();
                                        extension = ".png";
                                        break;
                                }
                                
                                string path = $"{debugSavePath}/lightmap_{i}_preprocessed{extension}";
                                System.IO.File.WriteAllBytes(Application.dataPath + "/../" + path, bytes);
                                Debug.Log($"Preprocessed lightmap saved to: {path}");
                            }
                            catch (System.Exception e) {
                                Debug.LogError($"Error saving preprocessed lightmap: {e.Message}");
                            }
                        }
                        
                        // アセットとして保存（テストのため）
                        try {
                            string assetPath = $"{debugSavePath}/lightmap_{i}_asset.asset";
                            AssetDatabase.CreateAsset(readableCopy, assetPath);
                            AssetDatabase.SaveAssets();
                            Debug.Log($"Lightmap texture saved as asset: {assetPath}");
                        }
                        catch (System.Exception e) {
                            Debug.LogError($"Error saving lightmap as asset: {e.Message}");
                            // 使用後破棄
                            Object.DestroyImmediate(readableCopy);
                        }
                    }
                    else
                    {
                        Debug.LogError($"Failed to create readable copy of lightmap {i}");
                    }
                }
            }
        }

        /// <summary>
        /// Returns a CPU‑readable copy of <paramref name="sourceTexture"/> while
        /// preserving HDR precision (RGBAHalf / RGBAFloat when applicable).
        /// </summary>
        private Texture2D CreateReadableTextureCopy(Texture2D sourceTexture)
        {
            if (sourceTexture == null) return null;

            // テクスチャフォーマットの選択
            TextureFormat dstFmt;
            RenderTextureFormat rtFmt;
            
            // テクスチャフォーマット設定に基づいて変更
            switch (texFormat)
            {
                case LightmapExportFormat.EXR32:
                    dstFmt = TextureFormat.RGBAFloat;
                    rtFmt = RenderTextureFormat.ARGBFloat;
                    break;
                case LightmapExportFormat.EXR16:
                    dstFmt = TextureFormat.RGBAHalf;
                    rtFmt = RenderTextureFormat.ARGBHalf;
                    break;
                case LightmapExportFormat.PNG:
                default:
                    // 元のフォーマットを考慮
                    switch (sourceTexture.format)
                    {
                        case TextureFormat.RGBAFloat:
                        case TextureFormat.RGBA64:
                            dstFmt = TextureFormat.RGBAFloat;
                            rtFmt = RenderTextureFormat.ARGBFloat;
                            break;
                        case TextureFormat.RGBAHalf:
                            dstFmt = TextureFormat.RGBAHalf;
                            rtFmt = RenderTextureFormat.ARGBHalf;
                            break;
                        default:
                            dstFmt = TextureFormat.RGBA32;
                            rtFmt = RenderTextureFormat.ARGB32;
                            break;
                    }
                    break;
            }

            // リサイズのための計算
            int width = sourceTexture.width;
            int height = sourceTexture.height;
            
            // maxLightmapSizeを使用して最大サイズを制限
            if (width > maxLightmapSize || height > maxLightmapSize)
            {
                if (width > height)
                {
                    height = Mathf.RoundToInt((float)height * maxLightmapSize / width);
                    width = maxLightmapSize;
                }
                else
                {
                    width = Mathf.RoundToInt((float)width * maxLightmapSize / height);
                    height = maxLightmapSize;
                }
                
                if (verboseLogging)
                {
                    Debug.Log($"Resizing lightmap from {sourceTexture.width}x{sourceTexture.height} to {width}x{height}");
                }
            }

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, rtFmt, RenderTextureReadWrite.Linear);
            Graphics.Blit(sourceTexture, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(width, height, dstFmt, false, true);
            readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            
            return readable;
        }
        
        /// <summary>
        /// Applies dithering noise to a texture
        /// </summary>
        private void ApplyNoise(Texture2D texture)
        {
            if (texture == null || noiseStrength <= 0f) return;
            
            Color[] px = texture.GetPixels();
            System.Random rng = new System.Random(texture.GetHashCode());
            for (int i = 0; i < px.Length; ++i)
            {
                float n = (float)rng.NextDouble() * noiseStrength - noiseStrength * 0.5f;
                px[i].r += n; px[i].g += n; px[i].b += n;
            }
            texture.SetPixels(px);
            texture.Apply();
        }

        /// <summary>
        /// Converts one renderer into an unlit clone that uses UV1‑based lightmaps.
        /// </summary>
        private bool ProcessRenderer(Renderer renderer, Transform parentTransform, out bool processed)
        {
            processed = false;
            if (!HasBakedLightmap(renderer, out int lmIndex)) return true; // skip silently

            if (verboseLogging)
                Debug.Log($"Processing {renderer.name} with lightmap index {lmIndex}");

            // ------------------------------------------------ cache readable lightmap
            if (!readableLightmapCache.TryGetValue(lmIndex, out Texture2D readableLightmap))
            {
                Texture2D src = LightmapSettings.lightmaps[lmIndex].lightmapColor;
                
                bool needNoise = addNoise && noiseStrength > 0f;

                if (!needNoise)
                {
                    // noise 無し要求 → 先にキャッシュを検索
                    if (!readableLightmapCache.TryGetValue(lmIndex, out readableLightmap))
                        readableLightmap = CreateReadableTextureCopy(src);
                }
                else
                {
                    // noise 有り要求 → 毎回新規コピー (または noisy キーでキャッシュ)
                    readableLightmap = CreateReadableTextureCopy(src);
                }

                if (needNoise) ApplyNoise(readableLightmap);
                
                if (readableLightmap == null) return false;
                readableLightmapCache[lmIndex] = readableLightmap;
            }

            // ------------------------------------------------ cache material
            if (!materialCache.TryGetValue(lmIndex, out Material unlitMat))
            {
                var shaderType = UnlitLightmapUtilities.DetectShaderType(renderer.sharedMaterial);
                unlitMat = UnlitLightmapUtilities.CreateUnlitMaterial(readableLightmap, shaderType, renderer.sharedMaterial);
                materialCache[lmIndex] = unlitMat;
            }

            // ------------------------------------------------ obtain source mesh
            Mesh srcMesh = null;
            if (renderer is MeshRenderer mr)
                srcMesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
            else if (renderer is SkinnedMeshRenderer smr)
                srcMesh = smr.sharedMesh;
            if (srcMesh == null || srcMesh.uv2 == null || srcMesh.uv2.Length == 0) return true; // nothing to do

            // Pass the renderer directly to CreateModifiedMesh for world-space transformations
            Mesh mesh = CreateModifiedMesh(srcMesh, renderer.lightmapScaleOffset, renderer);
            if (mesh == null) return false;

            // ------------------------------------------------ create cloned object
            GameObject clone = new GameObject(renderer.gameObject.name + objectSuffix);
            
            // First set the world position/rotation of the new object to match the original
            clone.transform.position = renderer.transform.position;
            clone.transform.rotation = renderer.transform.rotation;
            
            // worldPositionStays=true ensures the clone maintains the exact world position, rotation,
            // and scale of the original object, even when placed under a different parent hierarchy.
            // This preserves the visual appearance regardless of parent transformations.
            clone.transform.SetParent(parentTransform, true);  // ワールド姿勢を維持
            
            // 位置・回転・スケールは SetParent(parent, true) で自動的に維持されるので不要

            MeshFilter mf = clone.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer newMr = clone.AddComponent<MeshRenderer>();
            newMr.sharedMaterial = unlitMat;

            processed = true;
            return true;
        }

        /// <summary>
        /// Copies UV2 into UV1, applies vertex offset and recalculates bounds / normals.
        /// Returns <c>null</c> if the source mesh has no UV2 channel.
        /// </summary>
        /// <param name="original">The source mesh to copy</param>
        /// <param name="lmSO">Lightmap UV scale and offset</param>
        /// <param name="renderer">The renderer for world-space transformations</param>
        private Mesh CreateModifiedMesh(Mesh original, Vector4 lmSO, Renderer renderer)
        {
            if (original.uv2 == null || original.uv2.Length == 0) return null;

            Mesh m = Object.Instantiate(original);
            m.name = original.name + "_UV1LM";
            
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            
            // ── 1. "元の法線" が信用できないならリセット
            if (vertexOffsetMode == VertexOffsetMode.RelativeToScale && recalcNormalsFirst)
                m.RecalculateNormals();
                
            // Get updated normals if needed
            if (recalcNormalsFirst)
                n = m.normals;
                
            for (int i = 0; i < v.Length; ++i)
            {
                // ── 2. ローカル→ワールドへ
                Vector3 wPos = renderer.transform.TransformPoint(v[i]);
                Vector3 wNrm = renderer.transform.TransformDirection(n[i]).normalized;
                
                // ── 3. ワールド空間で mm 単位オフセット
                wPos += wNrm * vertexOffset; // ← scaleFactor いらない
                
                // ── 4. ローカルへ戻す
                v[i] = renderer.transform.InverseTransformPoint(wPos);
            }
            
            m.vertices = v;
            m.uv = MakeUV1FromUV2(original.uv2, lmSO);
            m.RecalculateBounds();
            m.RecalculateNormals(); // 押し出し後にもう一度
            return m;
        }
        
        /// <summary>
        /// Converts UV2 coordinates to UV1 with lightmap scale and offset applied
        /// </summary>
        private Vector2[] MakeUV1FromUV2(Vector2[] uv2, Vector4 lmScaleOffset)
        {
            Vector2[] uv1 = new Vector2[uv2.Length];
            for (int i = 0; i < uv2.Length; ++i)
            {
                uv1[i] = new Vector2(
                    uv2[i].x * lmScaleOffset.x + lmScaleOffset.z,
                    uv2[i].y * lmScaleOffset.y + lmScaleOffset.w);
            }
            return uv1;
        }

        // Add context menu for quick access
        // [MenuItem("GameObject/Generate Unlit Lightmap Overlay", false, 20)]
        private static void GenerateFromContextMenu(MenuCommand command)
        {
            GameObject selection = command.context as GameObject;
            if (selection == null)
                return;
                
            UnlitLightmapGenerator window = GetWindow<UnlitLightmapGenerator>();
            window.targetRoot = selection;
            window.Focus();
        }

        /// <summary>
        /// 元の階層構造を再作成する
        /// </summary>
        private void CreateHierarchyStructure(Transform originalRoot, Transform clonedParent, Dictionary<Transform, Transform> originalToCloned)
        {
            // ルート自体をマッピング
            originalToCloned[originalRoot] = clonedParent;
            
            // 子オブジェクトの階層を再帰的に作成
            foreach (Transform child in originalRoot)
            {
                // 複製用のゲームオブジェクトを作成
                GameObject clonedChild = new GameObject(child.name + "_hierarchy");
                
                // コピー元のTransformを適用
                clonedChild.transform.SetParent(clonedParent);
                clonedChild.transform.localPosition = child.localPosition;
                clonedChild.transform.localRotation = child.localRotation;
                clonedChild.transform.localScale = child.localScale;
                
                // マッピングに追加
                originalToCloned[child] = clonedChild.transform;
                
                // 再帰的に下の階層を処理
                CreateHierarchyStructure(child, clonedChild.transform, originalToCloned);
            }
        }
        
        /// <summary>
        /// 階層構造を保持したまま、レンダラーを処理する
        /// </summary>
        private bool ProcessRendererWithHierarchy(Renderer renderer, Transform clonedParent, out bool processed)
        {
            processed = false;
            if (!HasBakedLightmap(renderer, out int lmIndex)) return true; // skip silently
            
            if (verboseLogging)
                Debug.Log($"Processing {renderer.name} with lightmap index {lmIndex} for hierarchy preservation");
            
            // ------------------------------------------------ cache readable lightmap
            if (!readableLightmapCache.TryGetValue(lmIndex, out Texture2D readableLightmap))
            {
                Texture2D src = LightmapSettings.lightmaps[lmIndex].lightmapColor;
                
                bool needNoise = addNoise && noiseStrength > 0f;

                if (!needNoise)
                {
                    // noise 無し要求 → 先にキャッシュを検索
                    if (!readableLightmapCache.TryGetValue(lmIndex, out readableLightmap))
                        readableLightmap = CreateReadableTextureCopy(src);
                }
                else
                {
                    // noise 有り要求 → 毎回新規コピー (または noisy キーでキャッシュ)
                    readableLightmap = CreateReadableTextureCopy(src);
                }

                if (needNoise) ApplyNoise(readableLightmap);
                
                if (readableLightmap == null) return false;
                readableLightmapCache[lmIndex] = readableLightmap;
            }

            // ------------------------------------------------ cache material
            if (!materialCache.TryGetValue(lmIndex, out Material unlitMat))
            {
                var shaderType = UnlitLightmapUtilities.DetectShaderType(renderer.sharedMaterial);
                unlitMat = UnlitLightmapUtilities.CreateUnlitMaterial(readableLightmap, shaderType, renderer.sharedMaterial);
                materialCache[lmIndex] = unlitMat;
            }

            // ------------------------------------------------ obtain source mesh
            Mesh srcMesh = null;
            if (renderer is MeshRenderer mr)
                srcMesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
            else if (renderer is SkinnedMeshRenderer smr)
                srcMesh = smr.sharedMesh;
            if (srcMesh == null || srcMesh.uv2 == null || srcMesh.uv2.Length == 0) return true; // nothing to do
            
            // Pass the renderer directly to CreateModifiedMesh for world-space transformations
            Mesh mesh = CreateModifiedMesh(srcMesh, renderer.lightmapScaleOffset, renderer);
            if (mesh == null) return false;
            
            // ------------------------------------------------ create cloned object with name matching original
            GameObject clone = new GameObject(renderer.gameObject.name + objectSuffix);
            
            // 既存の階層下に配置 - 元のオブジェクトのローカル座標を使用
            clone.transform.SetParent(clonedParent);
            clone.transform.localPosition = renderer.transform.localPosition;
            clone.transform.localRotation = renderer.transform.localRotation;
            clone.transform.localScale = renderer.transform.localScale;
            
            MeshFilter mf = clone.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer newMr = clone.AddComponent<MeshRenderer>();
            newMr.sharedMaterial = unlitMat;
            
            // 元のコンポーネントからデータをコピー (Staticフラグなど)
            CopyRendererSettings(renderer, newMr);
            
            processed = true;
            return true;
        }
        
        /// <summary>
        /// メッシュ結合のためにレンダラーを処理する
        /// </summary>
        private bool ProcessRendererForCombining(
            Renderer renderer, 
            List<CombineInstance> combineInstances, 
            Dictionary<Material, List<CombineInstance>> materialToCombineInstances,
            out bool processed)
        {
            processed = false;
            if (!HasBakedLightmap(renderer, out int lmIndex)) return true; // skip silently
            
            if (verboseLogging)
                Debug.Log($"Processing {renderer.name} with lightmap index {lmIndex} for mesh combining");
            
            // ------------------------------------------------ cache readable lightmap
            if (!readableLightmapCache.TryGetValue(lmIndex, out Texture2D readableLightmap))
            {
                Texture2D src = LightmapSettings.lightmaps[lmIndex].lightmapColor;
                
                bool needNoise = addNoise && noiseStrength > 0f;

                if (!needNoise)
                {
                    // noise 無し要求 → 先にキャッシュを検索
                    if (!readableLightmapCache.TryGetValue(lmIndex, out readableLightmap))
                        readableLightmap = CreateReadableTextureCopy(src);
                }
                else
                {
                    // noise 有り要求 → 毎回新規コピー (または noisy キーでキャッシュ)
                    readableLightmap = CreateReadableTextureCopy(src);
                }

                if (needNoise) ApplyNoise(readableLightmap);
                
                if (readableLightmap == null) return false;
                readableLightmapCache[lmIndex] = readableLightmap;
            }

            // ------------------------------------------------ cache material
            if (!materialCache.TryGetValue(lmIndex, out Material unlitMat))
            {
                var shaderType = UnlitLightmapUtilities.DetectShaderType(renderer.sharedMaterial);
                unlitMat = UnlitLightmapUtilities.CreateUnlitMaterial(readableLightmap, shaderType, renderer.sharedMaterial);
                materialCache[lmIndex] = unlitMat;
            }

            // ------------------------------------------------ obtain source mesh
            Mesh srcMesh = null;
            if (renderer is MeshRenderer mr)
                srcMesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
            else if (renderer is SkinnedMeshRenderer smr)
                srcMesh = smr.sharedMesh;
            if (srcMesh == null || srcMesh.uv2 == null || srcMesh.uv2.Length == 0) return true; // nothing to do
            
            // Pass the renderer directly to CreateModifiedMesh for world-space transformations
            Mesh mesh = CreateModifiedMesh(srcMesh, renderer.lightmapScaleOffset, renderer);
            if (mesh == null) return false;
            
            // ワールド座標での変換行列を作成
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            
            // 結合インスタンスを作成
            CombineInstance ci = new CombineInstance();
            ci.mesh = mesh;
            ci.transform = matrix;
            
            // マテリアルでグループ化する場合
            if (shareMaterials)
            {
                if (!materialToCombineInstances.ContainsKey(unlitMat))
                {
                    materialToCombineInstances[unlitMat] = new List<CombineInstance>();
                    if (verboseLogging)
                    {
                        Debug.Log($"Created new combine group for material '{unlitMat.name}' from renderer '{renderer.name}'");
                    }
                }
                materialToCombineInstances[unlitMat].Add(ci);
                if (verboseLogging)
                {
                    Debug.Log($"Added mesh from '{renderer.name}' to material group '{unlitMat.name}', now has {materialToCombineInstances[unlitMat].Count} meshes");
                }
            }
            
            // 全体のリストにも追加
            combineInstances.Add(ci);
            
            processed = true;
            return true;
        }
        
        /// <summary>
        /// レンダラーの設定をコピーする
        /// </summary>
        private void CopyRendererSettings(Renderer source, Renderer target)
        {
            // StaticEditorFlagsをコピー
            GameObjectUtility.SetStaticEditorFlags(
                target.gameObject, 
                GameObjectUtility.GetStaticEditorFlags(source.gameObject)
            );
            
            // その他のレンダラー設定
            target.lightProbeUsage = source.lightProbeUsage;
            target.reflectionProbeUsage = source.reflectionProbeUsage;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.motionVectorGenerationMode = source.motionVectorGenerationMode;
            target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        }

        // ★ NEW ★ ───────────
        /// <summary>
        /// _UnlitLM_Group 配下のメッシュを「同一 sharedMaterial ごと」に結合
        /// </summary>
        private void MergeMeshesByLightmap(Transform groupRoot)
        {
            // null ガード
            if (groupRoot == null) return;

            // 子 MeshRenderer を収集
            var renderers = groupRoot.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;

            // マテリアル → MeshFilter[] へ振り分け
            var buckets = new Dictionary<Material, List<MeshFilter>>();
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var m = r.sharedMaterial;
                if (!buckets.ContainsKey(m)) buckets[m] = new List<MeshFilter>();
                buckets[m].Add(mf);
            }

            int created = 0;
            foreach (var kv in buckets)
            {
                var mat  = kv.Key;
                var list = kv.Value;
                if (list.Count <= 1) continue; // 1 個だけなら結合不要

                // CombineInstance 配列
                var ci = new CombineInstance[list.Count];
                int verts = 0;
                for (int i = 0; i < list.Count; ++i)
                {
                    ci[i].mesh      = list[i].sharedMesh;
                    ci[i].transform = list[i].transform.localToWorldMatrix;
                    verts += list[i].sharedMesh.vertexCount;
                }

                // 結合メッシュ
                var combined = new Mesh { name = $"{mat.name}_LM_Combine_{created}" };
                if (verts > 65535)
                    combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                combined.CombineMeshes(ci, mergeSubMeshes:true, useMatrices:true, hasLightmapData:false);

                // 新 GO
                var go = new GameObject(combined.name);
                go.transform.SetParent(groupRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh   = combined;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                Undo.RegisterCreatedObjectUndo(go, "Combine LM");

                // 元オブジェクトを削除
                foreach (var mf in list) Undo.DestroyObjectImmediate(mf.gameObject);

                Debug.Log($"[LM‑Combine] {list.Count} → 1  ({verts} verts, mat='{mat.name}')");
                created++;
            }

            if (created == 0)
                Debug.Log("[LM‑Combine] 結合対象なし（マテリアル共有が無かった可能性）");
                
            // ── 5. 不要になった空ノードを再帰的に削除
            RemoveEmptyGameObjects(groupRoot);
        }

        /// <summary>子孫を走査し「レンダラーもメッシュも無い」オブジェクトを破棄</summary>
        private void RemoveEmptyGameObjects(Transform t)
        {
            // 先に子を処理（後置き再帰）
            for (int i = t.childCount - 1; i >= 0; --i)
                RemoveEmptyGameObjects(t.GetChild(i));
            
            // MeshFilter / SkinnedMeshRenderer / MeshRenderer を一切持っていなければ削除
            if (t.childCount == 0 &&
                t.GetComponent<MeshFilter>()            == null &&
                t.GetComponent<SkinnedMeshRenderer>()   == null &&
                t.GetComponent<MeshRenderer>()          == null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
            }
        }
    }
}