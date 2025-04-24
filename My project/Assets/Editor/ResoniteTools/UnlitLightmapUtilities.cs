using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace ResoniteTools
{
    /// <summary>
    /// Additional utilities for the Unlit Lightmap Generator
    /// - Handles special material types (transparent, cutout)
    /// - Provides export functionality for Resonite
    /// </summary>
    public static partial class UnlitLightmapUtilities
    {
        // Available shader types for generated materials
        public enum UnlitShaderType
        {
            Basic,
            Transparent,
            Cutout
        }

        /* ----------------- ★★★ 追加 : ExportFormat ------------------ */
        public enum LightmapExportFormat
        {
            PNG,
            EXR16,   // HALF float
            EXR32    // FULL float
        }

        /// <summary>
        /// Creates an appropriate unlit material based on the specified shader type
        /// </summary>
        public static Material CreateUnlitMaterial(Texture2D lightmapTexture, UnlitShaderType shaderType, Material originalMaterial = null)
        {
            Material result = null;
            
            switch (shaderType)
            {
                case UnlitShaderType.Basic:
                    result = new Material(Shader.Find("Unlit/Texture"));
                    break;
                    
                case UnlitShaderType.Transparent:
                    result = new Material(Shader.Find("Unlit/Transparent"));
                    break;
                    
                case UnlitShaderType.Cutout:
                    // For cutout we'll use a custom shader or fall back to transparent if not available
                    Shader cutoutShader = Shader.Find("Custom/Unlit/Cutout");
                    if (cutoutShader != null)
                        result = new Material(cutoutShader);
                    else
                        result = new Material(Shader.Find("Unlit/Transparent"));
                    break;
            }
            
            if (result != null)
            {
                result.name = "UnlitLightmap_" + lightmapTexture.name;
                result.mainTexture = lightmapTexture;
                
                // Copy relevant properties from the original material if provided
                if (originalMaterial != null)
                {
                    // Alpha cutoff for cutout shader
                    if (shaderType == UnlitShaderType.Cutout && result.HasProperty("_Cutoff") && originalMaterial.HasProperty("_Cutoff"))
                    {
                        result.SetFloat("_Cutoff", originalMaterial.GetFloat("_Cutoff"));
                    }
                    
                    // Copy color if available (to tint the lightmap)
                    if (result.HasProperty("_Color") && originalMaterial.HasProperty("_Color"))
                    {
                        result.SetColor("_Color", originalMaterial.GetColor("_Color"));
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// Detects the most appropriate shader type based on the original material
        /// </summary>
        public static UnlitShaderType DetectShaderType(Material material)
        {
            if (material == null)
                return UnlitShaderType.Basic;
                
            // Check for transparent/cutout rendering mode
            if (material.HasProperty("_Mode"))
            {
                int mode = (int)material.GetFloat("_Mode");
                if (mode == 1) // Cutout
                    return UnlitShaderType.Cutout;
                else if (mode == 2 || mode == 3) // Fade or Transparent
                    return UnlitShaderType.Transparent;
            }
            
            // Check rendering type by shader keywords or render queue
            if (material.IsKeywordEnabled("_ALPHATEST_ON"))
                return UnlitShaderType.Cutout;
            else if (material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                return UnlitShaderType.Transparent;
            
            // Check by render queue as fallback
            if (material.renderQueue >= 3000) // Transparent queue
                return UnlitShaderType.Transparent;
            else if (material.renderQueue >= 2450) // AlphaTest queue
                return UnlitShaderType.Cutout;
                
            return UnlitShaderType.Basic;
        }

        /// <summary>
        /// Creates a custom shader for cutout materials if needed
        /// </summary>
        public static void EnsureCustomShadersExist()
        {
            // Only create if it doesn't exist
            string customShaderPath = "Assets/Editor/UnlitLightmapShaders";
            if (!AssetDatabase.IsValidFolder(customShaderPath))
            {
                Directory.CreateDirectory(Application.dataPath + "/Editor/UnlitLightmapShaders");
                AssetDatabase.Refresh();
            }
            
            string cutoutShaderPath = customShaderPath + "/UnlitCutout.shader";
            if (!File.Exists(Application.dataPath + "/../" + cutoutShaderPath))
            {
                // Create a simple unlit cutout shader
                string shaderCode = @"Shader ""Custom/Unlit/Cutout"" {
    Properties {
        _MainTex (""Base (RGB) Trans (A)"", 2D) = ""white"" {}
        _Cutoff (""Alpha cutoff"", Range(0,1)) = 0.5
    }

    SubShader {
        Tags {""Queue""=""AlphaTest"" ""IgnoreProjector""=""True"" ""RenderType""=""TransparentCutout""}
        LOD 100
        
        Lighting Off
        
        Pass {
            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0
                #include ""UnityCG.cginc""
                
                struct appdata_t {
                    float4 vertex : POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };
                
                struct v2f {
                    float4 vertex : SV_POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_VERTEX_OUTPUT_STEREO
                };
                
                sampler2D _MainTex;
                float4 _MainTex_ST;
                fixed _Cutoff;
                
                v2f vert (appdata_t v)
                {
                    v2f o;
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                    return o;
                }
                
                fixed4 frag (v2f i) : SV_Target
                {
                    fixed4 col = tex2D(_MainTex, i.texcoord);
                    clip(col.a - _Cutoff);
                    return col;
                }
            ENDCG
        }
    }

    }";
                File.WriteAllText(Application.dataPath + "/../" + cutoutShaderPath, shaderCode);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Gets all the lightmap textures used in the scene for batch export
        /// </summary>
        public static Dictionary<int, Texture2D> GetAllLightmapTextures()
        {
            Dictionary<int, Texture2D> result = new Dictionary<int, Texture2D>();
            
            if (LightmapSettings.lightmaps == null || LightmapSettings.lightmaps.Length == 0)
                return result;
                
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                if (LightmapSettings.lightmaps[i].lightmapColor != null)
                {
                    result[i] = LightmapSettings.lightmaps[i].lightmapColor;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Exports all baked lightmaps in the scene preserving HDR precision.
        /// </summary>
        public static void ExportLightmapsToFolder(string folderPath, LightmapExportFormat format)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = EditorUtility.SaveFolderPanel("Export Lightmaps", Application.dataPath, "Lightmaps");
                if (string.IsNullOrEmpty(folderPath)) return;
            }

            Dictionary<int, Texture2D> lightmaps = GetAllLightmapTextures();
            if (lightmaps.Count == 0)
            {
                EditorUtility.DisplayDialog("Export Error", "No lightmaps found to export.", "OK");
                return;
            }

            foreach (var kvp in lightmaps)
            {
                Texture2D src = kvp.Value;

                // Choose appropriate RT / Texture formats to keep HDR when requested
                RenderTextureFormat rtFmt;
                TextureFormat texFmt;
                switch (format)
                {
                    case LightmapExportFormat.EXR32:
                        rtFmt  = RenderTextureFormat.ARGBFloat;
                        texFmt = TextureFormat.RGBAFloat;
                        break;
                    case LightmapExportFormat.EXR16:
                        rtFmt  = RenderTextureFormat.ARGBHalf;
                        texFmt = TextureFormat.RGBAHalf;
                        break;
                    default:
                        rtFmt  = RenderTextureFormat.ARGB32;
                        texFmt = TextureFormat.RGBA32;
                        break;
                }

                RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, rtFmt, RenderTextureReadWrite.Linear);
                Graphics.Blit(src, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;

                Texture2D tex = new Texture2D(src.width, src.height, texFmt, false, true);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                byte[] bytes;
                string ext;
                switch (format)
                {
                    case LightmapExportFormat.EXR32:
                        bytes = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                        ext = ".exr";
                        break;
                    case LightmapExportFormat.EXR16:
                        bytes = tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                        ext = ".exr";
                        break;
                    default:
                        bytes = tex.EncodeToPNG();
                        ext = ".png";
                        break;
                }

                File.WriteAllBytes($"{folderPath}/Lightmap_{kvp.Key}{ext}", bytes);
                Object.DestroyImmediate(tex);
            }

            EditorUtility.DisplayDialog("Export Complete", $"Exported {lightmaps.Count} lightmap textures to\n{folderPath}", "OK");
        }

        /// <summary>
        /// Generates a Resonite‑compatible unlit shader that supports tint, cutoff and emission.
        /// </summary>
        public static void CreateResoniteShaderAsset()
        {
            string dir = "Assets/Editor/ResoniteShaders";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(Application.dataPath + "/Editor/ResoniteShaders");
                AssetDatabase.Refresh();
            }

            string shaderPath = dir + "/ResoniteLightmappedUnlit.shader";
            if (File.Exists(Application.dataPath + "/../" + shaderPath)) return; // already present

            const string shaderCode = @"Shader ""Resonite/LightmappedUnlit"" {
Properties {
    _MainTex (""Lightmap"", 2D) = ""white"" {}
    _Color   (""Tint"", Color) = (1,1,1,1)
    _Cutoff  (""Alpha Cutoff"", Range(0,1)) = 0.5
    _EnableEmission (""Enable Emission"", Float) = 0
    _EmissionStrength (""Emission Strength"", Range(0,10)) = 1
}
SubShader {
    Tags { ""RenderType"" = ""Opaque"" }
    LOD 100
    Pass {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_instancing
        #include ""UnityCG.cginc""
        struct appdata_t {
            float4 vertex : POSITION;
            float2 uv     : TEXCOORD0;
        };
        struct v2f {
            float4 pos : SV_POSITION;
            float2 uv  : TEXCOORD0;
        };
        sampler2D _MainTex;
        float4    _MainTex_ST;
        fixed4    _Color;
        half      _Cutoff;
        half      _EnableEmission;
        half      _EmissionStrength;
        v2f vert (appdata_t v) {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
            return o;
        }
        fixed4 frag (v2f i) : SV_Target {
            fixed4 col = tex2D(_MainTex, i.uv) * _Color;
            if (_Cutoff > 0.0) clip(col.a - _Cutoff);
            if (_EnableEmission > 0.0) col.rgb *= _EmissionStrength;
            return col;
        }
        ENDCG
    }
}
Fallback ""Unlit/Texture""
}";

            File.WriteAllText(Application.dataPath + "/../" + shaderPath, shaderCode);
            AssetDatabase.Refresh();
        }
    }
}