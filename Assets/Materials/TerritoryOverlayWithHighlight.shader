Shader "Custom/TerritoryOverlayWithHighlight"
{
    Properties
    {
        _IDTex ("ID Map", 2D) = "black" {}
        _FillAlpha ("Fill Alpha", Range(0,1)) = 0.35
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        _Saturation ("Fill Saturation", Range(0,1)) = 0.35
        _Value ("Fill Value", Range(0,1)) = 0.9

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

            sampler2D _IDTex;
            float4 _IDTex_TexelSize;
            float _FillAlpha;
            fixed4 _BorderColor;
            float _Saturation;
            float _Value;

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

            float SampleID16(float2 uv)
            {
                fixed4 c = tex2D(_IDTex, uv);
                return round(c.r * 255.0) + round(c.g * 255.0) * 256.0;
            }

            fixed3 HsvToRgb(float3 hsv)
            {
                float3 rgb = clamp(abs(fmod(hsv.x * 6.0 + float3(0,4,2), 6.0) - 3.0) - 1.0, 0.0, 1.0);
                return hsv.z * lerp(float3(1,1,1), rgb, hsv.y);
            }

            fixed3 IDToColor(float id)
            {
                float hue = frac(sin(id * 12.9898) * 43758.5453);
                return HsvToRgb(float3(hue, _Saturation, _Value));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float id = SampleID16(i.uv);
                if (id < 0.5) return fixed4(0,0,0,0);

                float idR = SampleID16(i.uv + float2(_IDTex_TexelSize.x, 0));
                float idL = SampleID16(i.uv - float2(_IDTex_TexelSize.x, 0));
                float idU = SampleID16(i.uv + float2(0, _IDTex_TexelSize.y));
                float idD = SampleID16(i.uv - float2(0, _IDTex_TexelSize.y));

                bool isBorder = abs(idR - id) > 0.5 || abs(idL - id) > 0.5
                             || abs(idU - id) > 0.5 || abs(idD - id) > 0.5;

                fixed4 result = isBorder
                    ? _BorderColor
                    : fixed4(IDToColor(id), _FillAlpha);

                // Layer the highlight on top of whatever fill/border color
                // was already decided above -- works correctly whether the
                // hovered pixel is interior fill or a border pixel.
                bool hit = _HighlightID > 0.5 && abs(id - _HighlightID) < 0.5;
                if (hit)
                {
                    result.rgb = lerp(result.rgb, _HighlightColor.rgb, _HighlightStrength);
                    result.a = max(result.a, 0.5);
                }

                return result;
            }
            ENDCG
        }
    }
}