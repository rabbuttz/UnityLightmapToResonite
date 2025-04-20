using UnityEngine;
using UnityEditor;

namespace ResoniteTools
{
    /// <summary>
    /// Custom editor for the Resonite Lightmapped Unlit shader
    /// Makes it easier to set up material settings for different rendering modes
    /// </summary>
    public class ResoniteLightmappedUnlitGUI : ShaderGUI
    {
        private enum RenderingMode
        {
            Opaque,
            Cutout,
            Transparent,
            Additive,
            Overlay
        }

        private static class Styles
        {
            public static readonly string[] renderingModeNames = {
                "Opaque",
                "Cutout",
                "Transparent",
                "Additive",
                "Overlay"
            };

            public static readonly GUIContent renderingModeText = new GUIContent("Rendering Mode", 
                "Controls how the material interacts with transparency and blending");
            public static readonly GUIContent albedoText = new GUIContent("Lightmap", 
                "Lightmap texture generated from Unity");
            public static readonly GUIContent emissionText = new GUIContent("Emission", 
                "Make the lightmap emissive");
            public static readonly GUIContent emissionStrengthText = new GUIContent("Emission Strength", 
                "How bright the emission should be");
            public static readonly GUIContent cutoutText = new GUIContent("Alpha Cutoff", 
                "Threshold for alpha cutout");
        }

        private MaterialProperty renderMode;
        private MaterialProperty mainTex;
        private MaterialProperty color;
        private MaterialProperty emission;
        private MaterialProperty emissionStrength;
        private MaterialProperty alphaCutout;
        private MaterialProperty cutoff;
        private MaterialProperty srcBlend;
        private MaterialProperty dstBlend;
        private MaterialProperty zWrite;
        private MaterialProperty cullMode;

        private MaterialEditor materialEditor;
        private bool firstTimeApply = true;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            this.materialEditor = materialEditor;
            Material material = materialEditor.target as Material;

            // Find properties
            mainTex = FindProperty("_MainTex", properties);
            color = FindProperty("_Color", properties);
            emission = FindProperty("_EnableEmission", properties);
            emissionStrength = FindProperty("_EmissionStrength", properties);
            alphaCutout = FindProperty("_AlphaCutout", properties);
            cutoff = FindProperty("_Cutoff", properties);
            srcBlend = FindProperty("_SrcBlend", properties);
            dstBlend = FindProperty("_DstBlend", properties);
            zWrite = FindProperty("_ZWrite", properties);
            cullMode = FindProperty("_CullMode", properties);

            // Detect rendering mode from material
            RenderingMode mode = GetRenderingMode(material);
            
            if (firstTimeApply)
            {
                firstTimeApply = false;
                SetupMaterialWithRenderingMode(material, mode, true);
            }

            EditorGUI.BeginChangeCheck();

            // Render the shader GUI
            EditorGUILayout.LabelField("Main Settings", EditorStyles.boldLabel);
            materialEditor.TexturePropertySingleLine(Styles.albedoText, mainTex, color);
            materialEditor.TextureScaleOffsetProperty(mainTex);

            EditorGUILayout.Space(10);
            
            // Rendering mode dropdown
            EditorGUI.BeginChangeCheck();
            mode = (RenderingMode)EditorGUILayout.Popup(Styles.renderingModeText, (int)mode, Styles.renderingModeNames);
            if (EditorGUI.EndChangeCheck())
            {
                materialEditor.RegisterPropertyChangeUndo("Rendering Mode");
                SetupMaterialWithRenderingMode(material, mode, false);
            }

            EditorGUILayout.Space(10);
            
            // Emission settings
            EditorGUILayout.LabelField("Emission", EditorStyles.boldLabel);
            materialEditor.ShaderProperty(emission, Styles.emissionText);
            if (emission.floatValue > 0)
            {
                EditorGUI.indentLevel++;
                materialEditor.ShaderProperty(emissionStrength, Styles.emissionStrengthText);
                EditorGUI.indentLevel--;
            }

            // Alpha cutout settings if applicable
            if (mode == RenderingMode.Cutout)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Alpha Cutout Settings", EditorStyles.boldLabel);
                materialEditor.ShaderProperty(cutoff, Styles.cutoutText);
            }

            EditorGUILayout.Space(10);
            
            // Advanced options
            bool showAdvanced = EditorGUILayout.Foldout(false, "Advanced Options");
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                materialEditor.ShaderProperty(cullMode, "Culling Mode");
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                // Set up material keywords and properties based on inspector UI
                if (emission.floatValue > 0)
                    material.EnableKeyword("_EMISSION");
                else
                    material.DisableKeyword("_EMISSION");

                if (mode == RenderingMode.Cutout)
                    material.EnableKeyword("_ALPHA_CUTOUT");
                else
                    material.DisableKeyword("_ALPHA_CUTOUT");
            }
        }

        // Determines which rendering mode the material is currently using
        private RenderingMode GetRenderingMode(Material material)
        {
            if (material.IsKeywordEnabled("_ALPHA_CUTOUT"))
                return RenderingMode.Cutout;
                
            int srcBlendValue = (int)material.GetFloat("_SrcBlend");
            int dstBlendValue = (int)material.GetFloat("_DstBlend");
            int zWriteValue = (int)material.GetFloat("_ZWrite");

            if (srcBlendValue == (int)UnityEngine.Rendering.BlendMode.One && 
                dstBlendValue == (int)UnityEngine.Rendering.BlendMode.Zero)
                return RenderingMode.Opaque;
                
            if (srcBlendValue == (int)UnityEngine.Rendering.BlendMode.SrcAlpha && 
                dstBlendValue == (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha)
                return RenderingMode.Transparent;
                
            if (srcBlendValue == (int)UnityEngine.Rendering.BlendMode.One && 
                dstBlendValue == (int)UnityEngine.Rendering.BlendMode.One)
                return RenderingMode.Additive;
                
            if (srcBlendValue == (int)UnityEngine.Rendering.BlendMode.OneMinusDstColor && 
                dstBlendValue == (int)UnityEngine.Rendering.BlendMode.One)
                return RenderingMode.Overlay;
                
            return RenderingMode.Opaque;
        }

        // Sets up the material with the correct blend mode, z writing, etc.
        private void SetupMaterialWithRenderingMode(Material material, RenderingMode mode, bool isInitialSetup)
        {
            switch (mode)
            {
                case RenderingMode.Opaque:
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetFloat("_ZWrite", 1.0f);
                    material.SetFloat("_AlphaCutout", 0);
                    material.DisableKeyword("_ALPHA_CUTOUT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    break;
                    
                case RenderingMode.Cutout:
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetFloat("_ZWrite", 1.0f);
                    material.SetFloat("_AlphaCutout", 1);
                    material.EnableKeyword("_ALPHA_CUTOUT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    break;
                    
                case RenderingMode.Transparent:
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0.0f);
                    material.SetFloat("_AlphaCutout", 0);
                    material.DisableKeyword("_ALPHA_CUTOUT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    break;
                    
                case RenderingMode.Additive:
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_ZWrite", 0.0f);
                    material.SetFloat("_AlphaCutout", 0);
                    material.DisableKeyword("_ALPHA_CUTOUT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    break;
                    
                case RenderingMode.Overlay:
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusDstColor);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                    material.SetFloat("_ZWrite", 0.0f);
                    material.SetFloat("_AlphaCutout", 0);
                    material.DisableKeyword("_ALPHA_CUTOUT");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    break;
            }
        }
    }
}