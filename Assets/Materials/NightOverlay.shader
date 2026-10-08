Shader "Custom/NightOverlay"
{
    Properties
    {
        _MainTex ("Night Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _TerminatorSoftness ("Terminator Softness", Range(0.001,0.5)) = 0.08
        _TerminatorOffset ("Terminator Offset", Range(-1,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            float _TerminatorSoftness;
            float _TerminatorOffset;

            // Set globally: world-space direction TO the sun.
            float4 _BorderSunDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);

                float sunLength = length(_BorderSunDir.xyz);
                float3 toSun = sunLength > 0.001 ? _BorderSunDir.xyz / sunLength : normal;

                float softness = max(_TerminatorSoftness, 0.0001);
                float day = smoothstep(-softness, softness, dot(normal, toSun) - _TerminatorOffset);
                float night = 1.0 - day;

                fixed4 tex = tex2D(_MainTex, i.uv) * _Tint;
                return fixed4(tex.rgb, tex.a * night);
            }
            ENDCG
        }
    }
}