Shader "Custom/WaterShader"
{
    Properties
    {
        _Color ("Color", Color) = (0.15,0.5,0.85,0.8)
        _SpecColor ("Specular Color", Color) = (0.9,0.9,0.9,1)
        _Shininess ("Shininess", Range(0.01, 1)) = 0.7
        _WaveScale ("Wave Scale", Range(0.01, 1)) = 0.05
        _WaveSpeed ("Wave Speed", Range(0, 10)) = 1.0
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.5
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200
        
        CGPROGRAM
        #pragma surface surf BlinnPhong alpha:fade vertex:vert
        
        struct Input
        {
            float2 uv_MainTex;
            float3 worldRefl;
            float3 viewDir;
            float4 screenPos;
            INTERNAL_DATA
        };
        
        float4 _Color;
        float _Shininess;
        float _WaveScale;
        float _WaveSpeed;
        float _ReflectionStrength;
        
        void vert(inout appdata_full v)
        {
            // Simple wave animation
            float t = _Time.y * _WaveSpeed;
            float waveX = sin(t + v.vertex.x * _WaveScale) * _WaveScale;
            float waveZ = sin(t + v.vertex.z * _WaveScale) * _WaveScale;
            v.vertex.y += (waveX + waveZ) * 0.5;
        }
        
        void surf(Input IN, inout SurfaceOutput o)
        {
            // Base color
            o.Albedo = _Color.rgb;
            
            // Fresnel effect (more reflection at glancing angles)
            float fresnel = pow(1.0 - saturate(dot(normalize(IN.viewDir), o.Normal)), 5.0);
            o.Specular = _Shininess;
            o.Gloss = fresnel * _ReflectionStrength + 0.1;
            o.Alpha = _Color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}