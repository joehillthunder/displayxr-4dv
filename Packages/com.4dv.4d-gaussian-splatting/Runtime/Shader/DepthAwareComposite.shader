Shader "Hidden/4DGS/DepthAwareComposite"
{
   SubShader
   {
      Pass
      {
         ZWrite Off
         ZTest Always
         Cull Off
         Blend One OneMinusSrcAlpha
         // Blend Off
    
         HLSLPROGRAM

         #pragma vertex vert
         #pragma multi_compile_instancing
         #pragma fragment frag

         #pragma shader_feature UNITY_STEREO_INSTANCING_ENABLED

         #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
         #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
         #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
         #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
         // #include "HLSLSupport.cginc"
         #include "UnityInstancing.cginc"

        static const float4 _Pos[3] = {
                float4(-1.0, -1.0, 0.5, 1.0),
                float4(3.0, -1.0, 0.5, 1.0),
                float4(-1.0, 3.0, 0.5, 1.0)
            };

        static const float2 _UV[3] = {
                float2(0.0, 0.0),
                float2(2.0, 0.0),
                float2(0.0, 2.0)
            };

         uint _GammaToLinear;

         TEXTURE2D_X(_SourceColorTex);
         SAMPLER(sampler_SourceColorTex);

         TEXTURE2D_X(_SourceDepthTex);
         SAMPLER(sampler_SourceDepthTex);

         TEXTURE2D_X(_CameraDepthTex);
         SAMPLER(sampler_CameraDepthTex);

         struct appdata
         {
            uint vtxID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
         };

         struct v2f
         {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID 
            UNITY_VERTEX_OUTPUT_STEREO
         };

         struct FragOutput
         {
            float4 color : SV_Target;
            // float depth : SV_Depth;
         };

         v2f vert (appdata v)
         {
            v2f o;

            UNITY_SETUP_INSTANCE_ID(v);
            ZERO_INITIALIZE(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

           uint vtxID = v.vtxID;
           o.vertex = _Pos[vtxID];
           o.uv = _UV[vtxID];

           return o;
         }

         FragOutput frag (v2f i) : SV_Target
         {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            FragOutput o;
            float depthRaw = 0.0;
            float4 srcColor;
            float2 uv_src = float2(i.uv.x, 1.0 - i.uv.y);
        #ifdef UNITY_STEREO_INSTANCING_ENABLED
            uv_src = FoveatedRemapLinearToNonUniform(uv_src);
        #endif
            srcColor = SAMPLE_TEXTURE2D_X(_SourceColorTex, sampler_SourceColorTex, uv_src);
            depthRaw = SAMPLE_TEXTURE2D_X(_CameraDepthTex, sampler_CameraDepthTex, uv_src).r;
            float safeAlpha = max(srcColor.a, 0.0001);
            float3 res = SRGBToLinear(srcColor.rgb);
            
         #if defined(UNITY_STEREO_INSTANCING_ENABLED) && defined(SHADER_API_METAL)
            static const float3x3 sRGBtoP3 = float3x3(
               0.8225, 0.1774, 0.0000,
               0.0332, 0.9669, 0.0000,
               0.0171, 0.0724, 0.9108
            );
            o.color = float4(mul(sRGBtoP3, res), srcColor.a);
         #else
            o.color = float4(res, srcColor.a);
         #endif

            return o;
         }
         ENDHLSL
      }
   }
}
