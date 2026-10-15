#ifndef GAUSSIAN_SPLAT_HLSL
#define GAUSSIAN_SPLAT_HLSL
struct SplatData
{
    float3 pos;
    float3 fdc;
    #if defined(SH_1) || defined(SH_2) || defined(SH_3)
    float3 sh1, sh2, sh3;
    #endif
    #if defined(SH_2) || defined(SH_3)
    float3 sh4, sh5, sh6, sh7, sh8;
    #endif
    #if defined(SH_3)
    float3 sh9, shA, shB, shC, shD, shE, shF;
    #endif
    float3 scale;
    float4 rot;
    float opacity;
    float t_center;
    float t_scale;
    float3 motion;
};

struct SplatViewData
{
    half4 position;
    half3 axis1;
    half cutoffSigma;
    half3 axis2;
    half _pad;
    half4 color;
};

// We will consider about packin' later
struct PooledSplatData
{
    float3 pos;
    float splatSize;
    float3 scale;
    float cutoffSigma;
    float4 rot;
    float4 color;
};

struct SplatSHData
{
    float3 col;
    #if defined(SH_1) || defined(SH_2) || defined(SH_3)
    float3 sh1, sh2, sh3;
    #endif
    #if defined(SH_2) || defined(SH_3)
    float3 sh4, sh5, sh6, sh7, sh8;
    #endif
    #if defined(SH_3)
    float3 sh9, sh10, sh11, sh12, sh13, sh14, sh15;
    #endif
};

struct PooledSplatSetupInfo
{
    float4x4 matO2W;
    float4x4 matW2O;
    float4x4 matV;
    float4x4 matInvV;
    float4 camPosWorld;
    float4 tintColor;
    float4 color;
    float3 objScale;
    float transparency;
    float3 localPosition;
    float currentTime;
    float splatSize;
    float cutoffSigma;
    float tScale;
    float splatColorPower;
};

float3x3 CalcMatrixFromRotationScale(float4 rot, float3 scale)
{
    float3x3 ms = float3x3(
        scale.x, 0, 0,
        0, scale.y, 0,
        0, 0, scale.z
    );
    float x = rot.x;
    float y = rot.y;
    float z = rot.z;
    float w = rot.w;
    float3x3 mrl = float3x3(
        1-2*(y*y + z*z),   2*(x*y + w*z),   2*(x*z - w*y),
          2*(x*y - w*z), 1-2*(x*x + z*z),   2*(y*z + w*x),
          2*(x*z + w*y),   2*(y*z - w*x), 1-2*(x*x + y*y)
    );
    
    return mul(ms, mrl);
}

void CalcCovariance3D(float3x3 rotMat, out float3 sigma0, out float3 sigma1)
{
    float3x3 sig = mul(transpose(rotMat), rotMat);
    sigma0 = float3(sig._m00, sig._m01, sig._m02);
    sigma1 = float3(sig._m11, sig._m12, sig._m22);
}

void DecomposeCovariance(float3 cov2d, float sampledSigma, out float2 v1, out float2 v2, out float rotation)
{
    float mid = 0.5f * (cov2d.x + cov2d.z);
    float det = cov2d.x * cov2d.z - cov2d.y * cov2d.y;
    float radius = sqrt(max(mid * mid - det, 0.0));
    float lambda1 = mid + radius;
    float lambda2 = mid - radius;

    float2 diagVec = float2(cov2d.y, lambda1 - cov2d.x);
    if( abs(diagVec.x) < 1e-16 && abs(diagVec.y) < 1e-16 )
    {
        diagVec = float2(1.0, 0.0);
    }
    diagVec = normalize(diagVec);
    
    // This size is in meter now
    float maxSize = 100.0;
    // Clamping the eigen values so we won't get a NaN problem.
    lambda1 = max(lambda1, 0.0);
    lambda2 = max(lambda2, 0.0);
    // Should make this a parameter for artists to tweak.
    v1 = min(sqrt(sampledSigma * lambda1), maxSize) * diagVec;
    v2 = min(sqrt(sampledSigma * lambda2), maxSize) * float2(diagVec.y, -diagVec.x);
    rotation = degrees(atan2(cov2d.y,lambda1 - cov2d.x));
}


static const float SH_C1 = 0.4886025;
static const float SH_C2[] = { 1.0925484, -1.0925484, 0.3153916, -1.0925484, 0.5462742 };
static const float SH_C3[] = { -0.5900436, 2.8906114, -0.4570458, 0.3731763, -0.4570458, 1.4453057, -0.5900436 };

float3 ShadeSH(SplatSHData splat, float3 dir)
{
    dir.x *= -1;
    dir.y *= -1;
    dir.z *= 1;

    float x = dir.x, y = dir.y, z = dir.z;

    // ambient band
    float3 res = splat.col; // col = sh0 * SH_C0 + 0.5 is already precomputed

    #if defined(SH_1) || defined(SH_2) || defined(SH_3)
    res += SH_C1 * (-splat.sh1 * y + splat.sh2 * z - splat.sh3 * x);
    #endif
        // 2nd degree
    #if defined(SH_2) || defined(SH_3)
    float xx = x * x, yy = y * y, zz = z * z;
    float xy = x * y, yz = y * z, xz = x * z;
    res +=
        (SH_C2[0] * xy) * splat.sh4 +
        (SH_C2[1] * yz) * splat.sh5 +
        (SH_C2[2] * (2 * zz - xx - yy)) * splat.sh6 +
        (SH_C2[3] * xz) * splat.sh7 +
        (SH_C2[4] * (xx - yy)) * splat.sh8;
            // 3rd degree
    #endif
    #if defined(SH_3)
    res +=
        (SH_C3[0] * y * (3 * xx - yy)) * splat.sh9 +
        (SH_C3[1] * xy * z) * splat.sh10 +
        (SH_C3[2] * y * (4 * zz - xx - yy)) * splat.sh11 +
        (SH_C3[3] * z * (2 * zz - 3 * xx - 3 * yy)) * splat.sh12 +
        (SH_C3[4] * x * (4 * zz - xx - yy)) * splat.sh13 +
        (SH_C3[5] * z * (xx - yy)) * splat.sh14 +
        (SH_C3[6] * x * (xx - 3 * yy)) * splat.sh15;
    #endif
    // res = normalize(dir) * 0.5 + 0.5;
            
    return saturate(res);
}

PooledSplatData SetupPooledSplat(SplatData splatData, PooledSplatSetupInfo setupInfo)
{
    PooledSplatData splat = (PooledSplatData)0;

    float3 newPos = setupInfo.localPosition + splatData.motion * (setupInfo.currentTime - splatData.t_center);
    float4 centerWorldPos = mul(setupInfo.matO2W, float4(newPos.x, newPos.y, newPos.z, 1.0f));
    splat.pos = centerWorldPos.xyz;

    float4 centerViewPos = mul(setupInfo.matV, centerWorldPos);

    float3 worldViewDir = setupInfo.camPosWorld.xyz - centerWorldPos.xyz;
    float3 objViewDir = mul((float3x3)setupInfo.matW2O, worldViewDir);
    objViewDir = normalize(objViewDir);

    // Since training was done in sRGB, blending will also be required to be in sRGB, not linear.
    SplatSHData sh = {
        splatData.fdc
        #if defined(SH_1) || defined(SH_2) || defined(SH_3)
        ,splatData.sh1, splatData.sh2, splatData.sh3
        #endif
        #if defined(SH_2) || defined(SH_3)
        ,splatData.sh4, splatData.sh5, splatData.sh6, splatData.sh7, splatData.sh8
        #endif
        #if defined(SH_3)
        ,splatData.sh9, splatData.shA, splatData.shB, splatData.shC, splatData.shD, splatData.shE, splatData.shF
        #endif
    };

    float4 color = setupInfo.color;
    float4 tintColor = setupInfo.tintColor;
    // color.rgb = saturate(tintColor.rgb * lerp(color.rgb, splatData.fdc, setupInfo.splatColorPower));
    color.rgb = saturate(tintColor.rgb * lerp(color.rgb, ShadeSH(sh, objViewDir), setupInfo.splatColorPower));

    float tScale = splatData.t_scale * setupInfo.tScale;
    float ratio = exp(-0.5 * pow((setupInfo.currentTime - splatData.t_center), 2) / (tScale * tScale));
    color.a = saturate(ratio * splatData.opacity * setupInfo.transparency * 1.414);
    splat.color = color;

    splat.scale = splatData.scale * setupInfo.objScale * setupInfo.objScale;
    splat.rot = splatData.rot;
    splat.splatSize = setupInfo.splatSize;
    splat.cutoffSigma = setupInfo.cutoffSigma;

    return splat;
}

float3 Unpack111011(uint packData)
{
    const float inv11 = 1.0 / 2047.0;
    const float inv10 = 1.0 / 1023.0;
    float3 unpacked;
    unpacked.x = (float)((packData >> 21) & 0x7FFu) * inv11;
    unpacked.y = (float)((packData >> 11) & 0x3FFu) * inv10;
    unpacked.z = (float)(packData & 0x7FFu) * inv11;
    return unpacked;
}

float4 Unpack8888(uint packData)
{
    const float inv8 = 1.0 / 255.0;
    float4 unpacked;
    unpacked.x = (float)((packData >> 24) & 0xFFu) * inv8;
    unpacked.y = (float)((packData >> 16) & 0xFFu) * inv8;
    unpacked.z = (float)((packData >> 8) & 0xFFu) * inv8;
    unpacked.w = (float)(packData & 0xFFu) * inv8;
    return unpacked;
}

float4 UnpackRotation(uint packData)
{
    const float inv10 = 1.0 / 1023.0;
    const float halfSqrt2 = sqrt(2) * 0.5;
    uint largest_idx = (packData >> 30) & 0x3u;
    uint3 i;
    i.x = (packData >> 20) & 0x3FFu;
    i.y = (packData >> 10) & 0x3FFu;
    i.z = packData & 0x3FFu;

    float3 c;
    c.x = ((float)i.x * inv10 - 0.5f) / halfSqrt2;
    c.y = ((float)i.y * inv10 - 0.5f) / halfSqrt2;
    c.z = ((float)i.z * inv10 - 0.5f) / halfSqrt2;

    float largest = sqrt(1.0f - saturate(dot(c, c)));

    float4 q;
    if (largest_idx == 0) q = float4(largest, c.x, c.y, c.z);
    else if (largest_idx == 1) q = float4(c.x, largest, c.y, c.z);
    else if (largest_idx == 2) q = float4(c.x, c.y, largest, c.z);
    else  q = float4(c.x, c.y, c.z, largest);
    return normalize(q);
}

float4 UnpackPaddedSHPara(uint packData)
{
    uint4 u;
    u.x = packData & 0xFFu;         // R
    u.y = (packData >> 8) & 0xFFu;  // G
    u.z = (packData >> 16) & 0xFFu; // B
    u.w = (packData >> 24) & 0xFFu; // Padding

    float4 f = (float4) u;

    // sh_quantized = (sh / 8 + 0.5) * 256
    // sh_quantized = torch.clamp(torch.truc(sh), 0, 255)
    return (f) * 0.03125f - 4.0f;
}
void UnpackSH(uint idx, int shOrder, inout SplatData splat)
{
    // 1->12, 2->32, 3->60
    int shCount = ((shOrder + 1) * (shOrder + 1) - 1) * 4;
    int stride = shCount * 1; // each SH is uchar
    uint baseSHAddr = idx * stride;
    #if defined(SH_1) || defined(SH_2) || defined(SH_3)
    splat.sh1 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr)).xyz;
    splat.sh2 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 4)).xyz;
    splat.sh3 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 8)).xyz;
    #endif

    #if defined(SH_2) || defined(SH_3)
    splat.sh4 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 12)).xyz;
    splat.sh5 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 16)).xyz;
    splat.sh6 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 20)).xyz;
    splat.sh7 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 24)).xyz;
    splat.sh8 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 28)).xyz;
    #endif

    #if defined(SH_3)
    splat.sh9 = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 32)).xyz;
    splat.shA = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 36)).xyz;
    splat.shB = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 40)).xyz;
    splat.shC = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 44)).xyz;
    splat.shD = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 48)).xyz;
    splat.shE = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 52)).xyz;
    splat.shF = UnpackPaddedSHPara(_CurrentSHDataBuffer.Load(baseSHAddr + 56)).xyz;
    #endif

    
}
float3 Dequantize(float3 qData, float3 minValue, float3 maxValue)
{
    return lerp(minValue, maxValue, qData);
}
float4 Dequantize(float4 qData, float4 minValue, float4 maxValue)
{
    return lerp(minValue, maxValue, qData);
}
float Dequantize(float qData, float minValue, float maxValue)
{
    return lerp(minValue, maxValue, qData);
}
void UnpackChunk(uint idx, inout SplatData splat)
{
    uint baseVertexAddr = idx * 24;
    // 6 uint in 1 vertex
    uint baseChunkAddr = idx / 256 * 28 * 4;
    // 1 chunk => 256 splats, 28 float in 1 chunk
    float3 minPos = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr));
    float3 maxPos = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 12));
    float3 minScale = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 24));
    float3 maxScale = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 36));
    float3 minRGB = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 48));
    float3 maxRGB = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 60));
    float3 minMotion = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 72));
    float3 maxMotion = asfloat(_CurrentChunkDataBuffer.Load3(baseChunkAddr + 84));
    float minTimeScale = asfloat(_CurrentChunkDataBuffer.Load(baseChunkAddr + 96));
    float maxTimeScale = asfloat(_CurrentChunkDataBuffer.Load(baseChunkAddr + 100));
    float minTime = asfloat(_CurrentChunkDataBuffer.Load(baseChunkAddr + 104));
    float maxTime = asfloat(_CurrentChunkDataBuffer.Load(baseChunkAddr + 108));

    splat.pos = Dequantize(Unpack111011(_CurrentVertexDataBuffer.Load(baseVertexAddr)), minPos, maxPos);
    splat.rot = UnpackRotation(_CurrentVertexDataBuffer.Load(baseVertexAddr + 4));
    splat.scale = Dequantize(Unpack111011(_CurrentVertexDataBuffer.Load(baseVertexAddr + 8)), minScale, maxScale);
    float4 color = Unpack8888(_CurrentVertexDataBuffer.Load(baseVertexAddr + 12));
    splat.fdc = Dequantize(color.xyz, minRGB, maxRGB);
    splat.opacity = color.w;
    splat.motion = Dequantize(Unpack111011(_CurrentVertexDataBuffer.Load(baseVertexAddr + 16)), minMotion, maxMotion);
    float3 time = Unpack111011(_CurrentVertexDataBuffer.Load(baseVertexAddr + 20));
    splat.t_scale = Dequantize(time.x, minTimeScale, maxTimeScale);
    splat.t_center = Dequantize(time.y, minTime, maxTime);

    // preprocess
    splat.scale = exp(splat.scale);
    splat.t_scale = exp(splat.t_scale);

    splat.rot = normalize(splat.rot).yzwx;

    // splat.fdc = splat.fdc * 0.2820948f + 0.5f;
    // splat.opacity = 1.0f / (1.0f + exp(-splat.opacity));

    splat.rot.x = -splat.rot.x;
    splat.rot.y = -splat.rot.y;
    splat.pos.z = -splat.pos.z;
    splat.motion.z = - splat.motion.z;
}
void splatSet(uint idx, int shOrder, inout SplatData splat)
{
    uint baseVertexAddr = idx * 76;
    // 19 float in 1 vertex
    splat.pos = asfloat(_CurrentVertexDataBuffer.Load3(baseVertexAddr));
    splat.fdc = asfloat(_CurrentVertexDataBuffer.Load3(baseVertexAddr + 12));
    splat.scale = asfloat(_CurrentVertexDataBuffer.Load3(baseVertexAddr + 24));
    splat.rot = asfloat(_CurrentVertexDataBuffer.Load4(baseVertexAddr + 36));
    splat.opacity = asfloat(_CurrentVertexDataBuffer.Load(baseVertexAddr + 52));
    splat.t_center = asfloat(_CurrentVertexDataBuffer.Load(baseVertexAddr + 56));
    splat.t_scale = asfloat(_CurrentVertexDataBuffer.Load(baseVertexAddr + 60));
    splat.motion = asfloat(_CurrentVertexDataBuffer.Load3(baseVertexAddr + 64));
    
    // preprocess
    splat.scale = exp(splat.scale);
    splat.t_scale = exp(splat.t_scale);

    splat.rot = normalize(splat.rot).yzwx;

    splat.fdc = splat.fdc * 0.2820948f + 0.5f;
    splat.opacity = 1.0f / (1.0f + exp(-splat.opacity));

    splat.rot.x = -splat.rot.x;
    splat.rot.y = -splat.rot.y;
    splat.pos.z = -splat.pos.z;
    splat.motion.z = - splat.motion.z;

    if (shOrder > 0)
    {
        int shCount = ((shOrder + 1) * (shOrder + 1) - 1) * 3;
        int stride = shCount * 4; // each SH is float
        uint baseSHAddr = idx * stride;
        #if defined(SH_1) || defined(SH_2) || defined(SH_3)
        splat.sh1 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr));
        splat.sh2 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 12));
        splat.sh3 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 24));
        #endif

        #if defined(SH_2) || defined(SH_3)
        splat.sh4 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 36));
        splat.sh5 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 48));
        splat.sh6 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 60));
        splat.sh7 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 72));
        splat.sh8 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 84));
        #endif
  
        #if defined(SH_3)
        splat.sh9 = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 96));
        splat.shA = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 108));
        splat.shB = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 120));
        splat.shC = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 132));
        splat.shD = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 144));
        splat.shE = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 156));
        splat.shF = asfloat(_CurrentSHDataBuffer.Load3(baseSHAddr + 168));
        #endif
    }
}

#endif