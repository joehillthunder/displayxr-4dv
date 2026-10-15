Shader "Unlit/GaussianSplat4D"
{
    Properties {}
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        // Blend One OneMinusSrcAlpha // Back to Front
        // Blend OneMinusDstAlpha One // Front to Back
        Cull Off
        ZWrite Off
        ZTest LEqual

        Stencil
        {
            Ref 1
            Comp NotEqual
            Pass Keep
        }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
        // #include "HLSLSupport.cginc"
        #include "UnityInstancing.cginc"

        // #include "GaussianSplat4D.hlsl"
        struct SplatViewData
        {
            half4 position;
            half3 axis1;
            half cutoffSigma;
            half3 axis2;
            half _pad;
            half4 color;
        };
        ByteAddressBuffer _SplatPositionSigma;
        ByteAddressBuffer _SplatPackedAxis;
        ByteAddressBuffer _SplatPackedColor;

        // StructuredBuffer<SplatViewData> _SplatViewDataBuffer;
        StructuredBuffer<uint> _OrderBuffer; 
        StructuredBuffer<uint> _ParamBuffer;
        int _SplatPerBatch;
        int _SplitIndex;
        float _YFactor;

        // // Soft particle
        // TEXTURE2D_X(_CameraDepthTexture);
        // SAMPLER(sampler_CameraDepthTexture);

        float4 UnpackColor(uint packedInput)
        {
            float4 color;

            float scale = 1.0 / 255.0;

            color.r = (float)(packedInput & 0xFF) * scale;
            color.g = (float)((packedInput >> 8) & 0xFF) * scale;
            color.b = (float)((packedInput >> 16) & 0xFF) * scale;
            color.a = (float)((packedInput >> 24) & 0xFF) * scale;

            return color;
        }

        struct appdata
        {
            uint vertexID : SV_VertexID;
        #ifdef UNITY_STEREO_INSTANCING_ENABLED
            UNITY_VERTEX_INPUT_INSTANCE_ID
        #else
            uint instanceID : SV_InstanceID;
        #endif
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float4 col : COLOR0;
            float2 uv  : TEXCOORD0;
            float  sigma : TEXCOORD1;
            float4 screenPos : TEXCOORD2;

            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert(appdata v)
        {
            v2f o;

            UNITY_SETUP_INSTANCE_ID(v);
            ZERO_INITIALIZE(v2f, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

            uint splatPerSplit = _ParamBuffer[10];

        #ifdef UNITY_STEREO_INSTANCING_ENABLED
            uint batchId = UNITY_GET_INSTANCE_ID(v) >> 1;
        #else
            uint batchId = v.instanceID;
        #endif

            uint splitSplatId = batchId * _SplatPerBatch + + v.vertexID / 6;
            if( splitSplatId >= splatPerSplit )
            {
                // Will be clipped
                o.pos = float4(-2.0, -2.0, -2.0, 1.0);
                return o;
            }

            // uint splatIdOffset = _ParamBuffer[3];
            uint splatIdOffset = _ParamBuffer[10] * _SplitIndex;
            uint splatCountThisFrame = _ParamBuffer[11];
            uint splatId = splitSplatId + splatIdOffset;
            if( splatId >= splatCountThisFrame )
            {
                // Will be clipped
                o.pos = float4(-2.0, -2.0, -2.0, 1.0);
                return o;
            }

            uint orderIndex = _OrderBuffer[splatId];

        #ifdef UNITY_STEREO_INSTANCING_ENABLED
            float4x4 w2p = unity_StereoMatrixVP[unity_StereoEyeIndex];
            float3 camPos = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
        #else
            float4x4 w2p = UNITY_MATRIX_VP;
            float3 camPos = _WorldSpaceCameraPos;
        #endif

            // SplatViewData view = _SplatViewDataBuffer[orderIndex];

            float2 offsets[6];
            offsets[0] = float2(-1, -1); // Bottom-left
            offsets[1] = float2(-1,  1); // Top-left
            offsets[2] = float2( 1,  1); // Top-right

            offsets[3] = float2(-1, -1); // Bottom-left
            offsets[4] = float2( 1,  1); // Top-right
            offsets[5] = float2( 1, -1); // Bottom-right

            uint float4Address = orderIndex * 16;
            uint half4Address = orderIndex * 8;
            uint uintAddress = orderIndex * 4;

            uint4 rawPosSig = _SplatPositionSigma.Load4(float4Address);
            float4 positionSig = asfloat(rawPosSig);
            // positionSig.x = f16tof32(rawPosSig.x);
            // positionSig.y = f16tof32(rawPosSig.x >> 16);
            // positionSig.z = f16tof32(rawPosSig.y);
            // positionSig.w = f16tof32(rawPosSig.y >> 16);
            float4 position = float4(positionSig.xyz, 1.0);
            float cutoffSigma = positionSig.w;

            uint2 rawPackedAxis = _SplatPackedAxis.Load2(half4Address);
            half4 packedAxis;
            packedAxis.x = f16tof32(rawPackedAxis.x);
            packedAxis.y = f16tof32(rawPackedAxis.x >> 16);
            packedAxis.z = f16tof32(rawPackedAxis.y);
            packedAxis.w = f16tof32(rawPackedAxis.y >> 16);
            half3 axis1 = packedAxis.xyz;
            half3 lookAtVec = normalize(positionSig.xyz - camPos);
            half3 axis2 = cross(lookAtVec, axis1) * packedAxis.w;

            uint rawColor = _SplatPackedColor.Load(uintAddress);
            float4 color = UnpackColor(rawColor);

            float2 offset = offsets[v.vertexID % 6];
            o.uv = offset * sqrt(cutoffSigma);
            o.sigma = cutoffSigma;

            o.col = color;
            o.pos = mul(w2p, position + offset.x * float4(axis1,0.0) + offset.y * float4(axis2, 0.0));
            o.pos.y *= _YFactor;

            o.screenPos = ComputeScreenPos(o.pos);
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float uvSqr = dot(i.uv, i.uv);
            if( uvSqr > i.sigma )
            {
                discard;
            }

            float power = -0.5 * uvSqr;

            float alpha = exp(power);
            if (i.col.a >= 0) alpha = saturate(alpha * i.col.a);
            if (alpha < 1.0/255.0) discard;

            // // Soft particle
            // float2 screenUV = i.screenPos.xy / i.screenPos.w;
            // float rawDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, screenUV);
            // float sceneZ = LinearEyeDepth(rawDepth, _ZBufferParams);
            // float fragZ = i.screenPos.w;
            // float fade = saturate((sceneZ - fragZ) / 0.1);
            // alpha *= fade;

            float4 res = float4(i.col.rgb * alpha, alpha);

            return res;
        }

        ENDHLSL

        Pass
        {
            Name "Gaussian_FrontToBack"
            Blend OneMinusDstAlpha One // Front to Back
            
            HLSLPROGRAM
            // #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            // #pragma require compute
            // #pragma use_dxc
            #pragma multi_compile_instancing

            #pragma shader_feature UNITY_STEREO_INSTANCING_ENABLED


            ENDHLSL
        }

        Pass
        {
            Name "Gaussian_BackToFront"
            Blend One OneMinusSrcAlpha // Back to Front

            HLSLPROGRAM
            // #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            // #pragma require compute
            // #pragma use_dxc
            #pragma multi_compile_instancing

            #pragma shader_feature UNITY_STEREO_INSTANCING_ENABLED


            ENDHLSL
        }
    }
}


