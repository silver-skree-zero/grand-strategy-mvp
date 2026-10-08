Shader "Custom/BorderLineScreenSpace"
{
    Properties
    {
        _Color ("Day Color", Color) = (0,0,0,1)
        _NightColor ("Night Color", Color) = (0.6,0.7,0.9,1)
        _WidthPixels ("Width In Pixels", Float) = 2.0
        _TerminatorSoftness ("Terminator Softness", Range(0.001,0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            // Deliberately no LightMode tag: an untagged pass is drawn by both
            // the Built-in pipeline and URP. The sun direction arrives through
            // the global _BorderSunDir (see SunDirectionBroadcaster.cs) instead
            // of Unity's per-pass light variables.
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _NightColor;
            float _WidthPixels;
            float _TerminatorSoftness;

            // Set globally by SunDirectionBroadcaster: world-space direction TO the sun.
            float4 _BorderSunDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 tangent : TANGENT;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
            };

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

                // The mesh sits on a sphere centered on this object's origin
                // (the border object is a child of the Earth, at local zero),
                // so the surface normal is just the vertex direction.
                o.worldNormal = UnityObjectToWorldNormal(normalize(v.vertex.xyz));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);

                // If nothing has set the sun direction, fall back to "all day"
                // so the border still shows its primary color.
                float sunLength = length(_BorderSunDir.xyz);
                float3 toSun = sunLength > 0.001 ? _BorderSunDir.xyz / sunLength : normal;

                float nDotL = dot(normal, toSun);
                float softness = max(_TerminatorSoftness, 0.0001);
                float day = smoothstep(-softness, softness, nDotL);

                return lerp(_NightColor, _Color, day);
            }
            ENDCG
        }
    }
}
