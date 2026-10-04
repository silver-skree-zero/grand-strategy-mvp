Shader "Custom/TerritoryOverlayNeutral"
{
    Properties
    {
        _IDTex ("ID Map", 2D) = "black" {}
        _FillAlpha ("Fill Alpha", Range(0,1)) = 0.35
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        _Saturation ("Fill Saturation", Range(0,1)) = 0.35
        _Value ("Fill Value", Range(0,1)) = 0.9
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Decodes the 16-bit state ID packed across R (low byte) and G (high byte)
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

            // Deterministic pseudo-random hue per ID -- same ID always
            // gives the same color, with no lookup table needed.
            fixed3 IDToColor(float id)
            {
                float hue = frac(sin(id * 12.9898) * 43758.5453);
                return HsvToRgb(float3(hue, _Saturation, _Value));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float id = SampleID16(i.uv);
                if (id < 0.5) return fixed4(0,0,0,0); // no territory here

                float idR = SampleID16(i.uv + float2(_IDTex_TexelSize.x, 0));
                float idL = SampleID16(i.uv - float2(_IDTex_TexelSize.x, 0));
                float idU = SampleID16(i.uv + float2(0, _IDTex_TexelSize.y));
                float idD = SampleID16(i.uv - float2(0, _IDTex_TexelSize.y));

                bool isBorder = abs(idR - id) > 0.5 || abs(idL - id) > 0.5
                             || abs(idU - id) > 0.5 || abs(idD - id) > 0.5;

                if (isBorder) return _BorderColor;

                return fixed4(IDToColor(id), _FillAlpha);
            }
            ENDCG
        }
    }
}