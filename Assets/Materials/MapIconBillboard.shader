// Constant-screen-size billboard icons drawn from one mesh.
// Each quad's 4 vertices share the icon's centre (object space); the corner
// offset is applied in clip space so icons stay a fixed pixel size at any zoom.
// Icons on the far side of the globe fade out using the sphere's own normal
// (the layer object must sit at the Earth's centre).
Shader "Custom/MapIconBillboard"
{
    Properties
    {
        _MainTex ("Icon Atlas (white glyphs, alpha)", 2D) = "white" {}
        _FadeStart ("Limb fade start (dot)", Range(0,0.5)) = 0.0
        _FadeEnd ("Limb fade end (dot)", Range(0,0.5)) = 0.18
        _IconScale ("Icon scale", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FadeStart, _FadeEnd;
            float _IconScale;   // global-ish multiplier set per layer (distance based), default 1

            struct appdata
            {
                float4 vertex : POSITION;   // icon centre, object space
                float2 uv     : TEXCOORD0;  // atlas uv
                float2 corner : TEXCOORD1;  // corner offset in pixels (already * size)
                fixed4 color  : COLOR;      // tint
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 centreWS = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                float3 earthWS  = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 n  = normalize(centreWS - earthWS);
                float3 toCam = normalize(_WorldSpaceCameraPos - centreWS);
                float facing = dot(n, toCam);
                float fade = smoothstep(_FadeStart, _FadeEnd, facing);

                float4 clip = UnityObjectToClipPos(v.vertex);
                float2 px = v.corner * (_IconScale > 0 ? _IconScale : 1.0);
                clip.xy += px * 2.0 / _ScreenParams.xy * clip.w;

                o.pos = clip;
                o.uv = v.uv;
                o.color = v.color;
                o.color.a *= fade;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.rgb *= i.color.rgb;
                c.a *= i.color.a;
                return c;
            }
            ENDCG
        }
    }
}
