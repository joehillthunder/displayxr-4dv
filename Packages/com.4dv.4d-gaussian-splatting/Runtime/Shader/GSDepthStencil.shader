Shader "Hidden/4DGS/GSDepthStencil"
{
   SubShader
   {
      Pass
      {
         ZWrite Off
         ZTest Always
         Cull Off
         Blend Off
         ColorMask 0

         Stencil
         {
            Ref 1
            Comp Always
            Pass Replace
         }

         
    
         HLSLPROGRAM

         #pragma vertex vert
         #pragma multi_compile_instancing
         #pragma fragment frag

         #pragma shader_feature UNITY_STEREO_INSTANCING_ENABLED

         #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
         #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
         #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
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

         TEXTURE2D_X(_SourceColorTex);
         SAMPLER(sampler_SourceColorTex);

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

         float4 frag (v2f i) : SV_Target
         {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float4 srcColor;
            float2 uv_src = float2(i.uv.x, 1.0 - i.uv.y);
        #ifdef UNITY_STEREO_INSTANCING_ENABLED
            uv_src = FoveatedRemapLinearToNonUniform(uv_src);
        #endif
            srcColor = SAMPLE_TEXTURE2D_X(_SourceColorTex, sampler_SourceColorTex, uv_src);
            if( srcColor.a <= 0.997 )
            {
               discard;
            }
            return float4(0,0,0,0);;
         }
         ENDHLSL
      }
   }
}
