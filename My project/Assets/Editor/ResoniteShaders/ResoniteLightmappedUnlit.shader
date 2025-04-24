Shader "Resonite/LightmappedUnlit" {
    Properties {
        _MainTex ("Lightmap", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Toggle(_EMISSION)] _EnableEmission ("Enable Emission", Float) = 0
        _EmissionStrength ("Emission Strength", Range(0,5)) = 1
        [Toggle(_ALPHA_CUTOUT)] _AlphaCutout ("Alpha Cutout", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Toggle] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2 // Back face culling by default
    }

    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 100
        
        // Set culling, blending, and depth writing based on properties
        Cull [_CullMode]
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma shader_feature_local _ _EMISSION
            #pragma shader_feature_local _ _ALPHA_CUTOUT
            #include "UnityCG.cginc"
            
            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0; // Uses UV1 for lightmap
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _EmissionStrength;
            fixed _Cutoff;
            
            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Use UV1 for the lightmap (standard Resonite behavior)
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.texcoord) * _Color;
                
                // Handle alpha cutout if enabled
                #ifdef _ALPHA_CUTOUT
                    clip(col.a - _Cutoff);
                #endif
                
                // Handle emission if enabled
                #ifdef _EMISSION
                    col.rgb *= _EmissionStrength;
                #endif
                
                return col;
            }
            ENDCG
        }
    }
    
    // Fallback for compatibility
    FallBack "Unlit/Texture"
    
    // Custom Editor for easier setting up
    CustomEditor "ResoniteTools.ResoniteLightmappedUnlitGUI"
}