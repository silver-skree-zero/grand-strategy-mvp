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

        _BorderSDF ("Border Distance Field", 2D) = "white" {}
        _BorderThreshold ("Border Threshold", Range(0,1)) = 0.15

        _StatePaletteTex ("State Palette (256x256)", 2D) = "black" {}
        _CountryPaletteTex ("Country Palette (256x256)", 2D) = "black" {}
        _OwnerTex ("State -> Current Owner (256x256)", 2D) = "black" {}
        _ControllerTex ("State -> Current Controller (256x256)", 2D) = "black" {}
        _ColorizeByCountry ("0=State, 1=Country", Range(0,1)) = 0
        _PulseSoftness ("Occupation Pulse Softness", Range(0,1)) = 0.25
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

            sampler2D _BorderSDF;
            sampler2D _StatePaletteTex;
            sampler2D _CountryPaletteTex;
            sampler2D _OwnerTex;
            sampler2D _ControllerTex;

            float _ColorizeByCountry;
            float _BorderThreshold;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            
            float _Pulse;

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

            float2 IdToPaletteUV(float id)
            {
                float idxR = fmod(id, 256.0);
                float idxG = floor(id / 256.0);
                return (float2(idxR, idxG) + 0.5) / 256.0;
            }

            fixed3 IDToColor(float stateId)
            {
                float2 stateUV = IdToPaletteUV(stateId);

                if (_ColorizeByCountry < 0.5)
                    return tex2Dlod(_StatePaletteTex, float4(stateUV, 0, 0)).rgb;

                // National mode: state id -> current owner's country id -> country color
                fixed4 ownerTexel = tex2Dlod(_OwnerTex, float4(stateUV, 0, 0));
                fixed4 ctrlTexel  = tex2Dlod(_ControllerTex, float4(stateUV, 0, 0));
                float countryId = round(ownerTexel.r * 255.0) + round(ownerTexel.g * 255.0) * 256.0;
                float controllerId = round(ctrlTexel.r * 255.0) + round(ctrlTexel.g * 255.0) * 256.0;
                float occupied = any(abs(ownerTexel.rg - ctrlTexel.rg) > 0.002) ? 1.0 : 0.0;

                // Unowned/unresolved state: fall back to its own color rather than black
                if (countryId < 0.5)
                    return tex2Dlod(_StatePaletteTex, float4(stateUV, 0, 0)).rgb;

                fixed3 ownerCol = tex2Dlod(_CountryPaletteTex, float4(IdToPaletteUV(countryId), 0, 0)).rgb;
                fixed3 ctrlCol = tex2Dlod(_CountryPaletteTex, float4(IdToPaletteUV(controllerId), 0, 0)).rgb;

                //return tex2Dlod(_CountryPaletteTex, float4(IdToPaletteUV(countryId), 0, 0)).rgb;
                //return ctrlTexel.rgb;
                return lerp(ownerCol, ctrlCol, occupied * _Pulse);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float id = SampleID16(i.uv);
                if (id < 0.5) return fixed4(0,0,0,0);

                float idR = SampleID16(i.uv + float2(_IDTex_TexelSize.x, 0));
                float idL = SampleID16(i.uv - float2(_IDTex_TexelSize.x, 0));
                float idU = SampleID16(i.uv + float2(0, _IDTex_TexelSize.y));
                float idD = SampleID16(i.uv - float2(0, _IDTex_TexelSize.y));

                bool isBorder = (idR - id) > 0.5 || (idL - id) > 0.5
                             || (idU - id) > 0.5 || (idD - id) > 0.5;
                isBorder = false;

                fixed4 result = isBorder
                    ? _BorderColor
                    : fixed4(IDToColor(id), _FillAlpha);

                

                /*
                float dist = tex2D(_BorderSDF, i.uv).r;
                float signedDist = dist - _BorderThreshold; // keep _BorderThreshold small — see below
                float w = fwidth(signedDist);               // screen-space-correct band width, no manual multiplier
                float borderCoverage = 1.0 - smoothstep(0.0, w, signedDist);

                fixed4 result = lerp(fixed4(IDToColor(id), _FillAlpha), _BorderColor, borderCoverage);
                */

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