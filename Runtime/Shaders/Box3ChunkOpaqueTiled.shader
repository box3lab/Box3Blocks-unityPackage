Shader "Box3Blocks/ChunkOpaqueTiled"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1
        _MetallicGlossMap ("Metallic", 2D) = "black" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.15
        _GlossMapScale ("Gloss Map Scale", Range(0,1)) = 0.15
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Back
        ZWrite On

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #pragma shader_feature _NORMALMAP
        #pragma shader_feature _METALLICGLOSSMAP
        #pragma shader_feature _EMISSION

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        sampler2D _EmissionMap;

        fixed4 _Color;
        half _BumpScale;
        half _Metallic;
        half _Glossiness;
        half _GlossMapScale;
        fixed4 _EmissionColor;
        float4 _MainTex_TexelSize;

        struct Input
        {
            float2 uv_MainTex;
            float2 atlasMin;
            float2 atlasSize;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.uv_MainTex = v.texcoord.xy;
            o.atlasMin = v.texcoord1.xy;
            o.atlasSize = v.texcoord2.xy;
        }

        inline float2 GetAtlasUv(Input IN)
        {
            float2 tile = frac(IN.uv_MainTex);
            float2 inset = _MainTex_TexelSize.xy * 0.5;
            float2 uvMin = IN.atlasMin + inset;
            float2 uvMax = IN.atlasMin + IN.atlasSize - inset;
            return lerp(uvMin, uvMax, tile);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 atlasUv = GetAtlasUv(IN);
            fixed4 albedo = tex2D(_MainTex, atlasUv) * _Color;
            o.Albedo = albedo.rgb;
            o.Alpha = 1;

            #if defined(_NORMALMAP)
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, atlasUv), _BumpScale);
            #endif

            #if defined(_METALLICGLOSSMAP)
            fixed4 metallicTex = tex2D(_MetallicGlossMap, atlasUv);
            o.Metallic = saturate(metallicTex.r * _Metallic);
            o.Smoothness = saturate(metallicTex.a * _GlossMapScale);
            #else
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            #endif

            #if defined(_EMISSION)
            o.Emission = tex2D(_EmissionMap, atlasUv).rgb * _EmissionColor.rgb;
            #endif
        }
        ENDCG
    }

    FallBack "Standard"
}
