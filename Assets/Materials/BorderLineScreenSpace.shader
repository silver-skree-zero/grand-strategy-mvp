Shader "Custom/BorderLineScreenSpace"
{
    Properties
    {
        _Color ("Line Color", Color) = (0,0,0,1)
        _WidthPixels ("Width In Pixels", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _WidthPixels;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 tangent : TANGENT;
            };

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldDir = normalize(mul((float3x3)unity_ObjectToWorld, v.tangent.xyz));

                float4 clipPos = UnityWorldToClipPos(worldPos);
                float4 clipPosAhead = UnityWorldToClipPos(worldPos + worldDir * 0.001);

                float2 screenDir = normalize((clipPosAhead.xy / clipPosAhead.w) - (clipPos.xy / clipPos.w));
                float2 screenPerp = float2(-screenDir.y, screenDir.x);

                float side = v.uv.x;
                float pixelToClip = (_WidthPixels / _ScreenParams.y) * 2.0;
                clipPos.xy += screenPerp * side * pixelToClip * 0.5 * clipPos.w;

                o.pos = clipPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}