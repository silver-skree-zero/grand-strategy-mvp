Shader "Custom/CountryOverlayHighlight"
{
    Properties
    {
        _MainTex ("Overlay Visual", 2D) = "white" {}
        _IDTex ("ID Map", 2D) = "black" {}
        _HighlightID ("Highlighted ID", Float) = 0
        _HighlightColor ("Highlight Tint", Color) = (1,1,1,1)
        _HighlightStrength ("Highlight Strength", Range(0,1)) = 0.4
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
            sampler2D _IDTex;
            float _HighlightID;
            fixed4 _HighlightColor;
            float _HighlightStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 baseColor = tex2D(_MainTex, i.uv);
                float sampledID = tex2D(_IDTex, i.uv).r * 255.0;
                bool hit = _HighlightID > 0.5 && abs(sampledID - _HighlightID) < 0.5;

                fixed4 result = baseColor;
                if (hit)
                {
                    result.rgb = lerp(baseColor.rgb, _HighlightColor.rgb, _HighlightStrength);
                    result.a = max(baseColor.a, 0.5);
                }
                return result;
            }
            ENDCG
        }
    }
}