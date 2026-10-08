Shader "Custom/Earth Atmosphere Volume"
{
    Properties
    {
        _Color ("Atmosphere Color", Color) = (0.08, 0.35, 1.0, 1)

        _Intensity ("Intensity", Range(0, 10)) = 1.5
        _Opacity ("Opacity", Range(0, 5)) = 1.0

        _OuterRadius ("Outer Radius", Range(1.001, 1.2)) = 1.04

        _Samples ("Ray Samples", Range(4, 32)) = 12

        _SunInfluence ("Sun Influence", Range(0, 1)) = 1.0

        _NightLight ("Night Side Light", Range(0, 0.2)) = 0.02

        _SunDirection ("Sun Direction", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        // Render both sides of the atmosphere sphere.
        Cull Front

        ZWrite Off
        ZTest Always

        Blend SrcAlpha OneMinusSrcAlpha

        Lighting Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            fixed4 _Color;

            float _Intensity;
            float _Opacity;
            float _OuterRadius;
            float _Samples;
            float _SunInfluence;
            float _NightLight;

            float4 _SunDirection;


            struct appdata
            {
                float4 vertex : POSITION;
            };


            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
            };


            v2f vert(appdata v)
            {
                v2f o;

                o.vertex =
                    UnityObjectToClipPos(v.vertex);

                o.worldPosition =
                    mul(
                        unity_ObjectToWorld,
                        v.vertex
                    ).xyz;

                return o;
            }


            // --------------------------------------------------------
            // Ray / sphere intersection.
            //
            // Returns both intersections measured from rayOrigin.
            // --------------------------------------------------------

            bool RaySphere(
                float3 rayOrigin,
                float3 rayDirection,
                float3 sphereCenter,
                float sphereRadius,
                out float tNear,
                out float tFar
            )
            {
                float3 oc =
                    rayOrigin - sphereCenter;

                float b =
                    dot(oc, rayDirection);

                float c =
                    dot(oc, oc) -
                    sphereRadius * sphereRadius;

                float discriminant =
                    b * b - c;

                if (discriminant < 0.0)
                {
                    tNear = 0.0;
                    tFar = 0.0;

                    return false;
                }

                float sqrtD =
                    sqrt(discriminant);

                tNear =
                    -b - sqrtD;

                tFar =
                    -b + sqrtD;

                return true;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 cameraPosition =
                    _WorldSpaceCameraPos;


                float3 rayDirection =
                    normalize(
                        i.worldPosition -
                        cameraPosition
                    );


                // ----------------------------------------------------
                // Atmosphere center.
                // ----------------------------------------------------

                float3 center =
                    mul(
                        unity_ObjectToWorld,
                        float4(0, 0, 0, 1)
                    ).xyz;


                // ----------------------------------------------------
                // Determine actual world-space atmosphere radius.
                //
                // Unity sphere radius = 0.5.
                // ----------------------------------------------------

                float3 worldEdge =
                    mul(
                        unity_ObjectToWorld,
                        float4(0.5, 0, 0, 1)
                    ).xyz;

                float outerRadius =
                    length(
                        worldEdge - center
                    );


                // ----------------------------------------------------
                // Earth radius.
                // ----------------------------------------------------

                float innerRadius =
                    outerRadius /
                    _OuterRadius;


                // ----------------------------------------------------
                // Find the camera ray's intersection with the
                // OUTER atmosphere sphere.
                // ----------------------------------------------------

                float outerNear;
                float outerFar;

                bool outerHit =
                    RaySphere(
                        cameraPosition,
                        rayDirection,
                        center,
                        outerRadius,
                        outerNear,
                        outerFar
                    );

                if (!outerHit)
                    discard;


                // ----------------------------------------------------
                // Determine whether the camera is inside the
                // atmosphere.
                // ----------------------------------------------------

                float cameraDistance =
                    length(
                        cameraPosition -
                        center
                    );


                float tStart;

                if (cameraDistance < outerRadius)
                {
                    // Camera is already inside the atmosphere.
                    tStart = 0.0;
                }
                else
                {
                    // Camera is outside.
                    tStart = max(
                        outerNear,
                        0.0
                    );
                }


                // ----------------------------------------------------
                // Find the camera ray's intersection with Earth.
                // ----------------------------------------------------

                float innerNear;
                float innerFar;

                bool innerHit =
                    RaySphere(
                        cameraPosition,
                        rayDirection,
                        center,
                        innerRadius,
                        innerNear,
                        innerFar
                    );


                // ----------------------------------------------------
                // Determine where the atmospheric ray ends.
                //
                // Earth surface takes precedence if we hit Earth.
                // Otherwise we march to the far side of the
                // atmosphere.
                // ----------------------------------------------------

                float tEnd;

                if (innerHit &&
                    innerNear > tStart)
                {
                    tEnd = innerNear;
                }
                else
                {
                    tEnd = outerFar;
                }


                // ----------------------------------------------------
                // Make sure the interval is valid.
                // ----------------------------------------------------

                float pathLength =
                    max(
                        0.0,
                        tEnd - tStart
                    );

                if (pathLength <= 0.0)
                    discard;


                // ----------------------------------------------------
                // Ray march.
                // ----------------------------------------------------

                float accumulated = 0.0;

                float samples =
                    max(
                        4.0,
                        _Samples
                    );

                float stepLength =
                    pathLength /
                    samples;


                for (int sample = 0;
                     sample < 32;
                     sample++)
                {
                    if (sample >= samples)
                        break;


                    float t =
                        tStart +
                        stepLength *
                        (sample + 0.5);


                    float3 position =
                        cameraPosition +
                        rayDirection *
                        t;


                    float distanceFromCenter =
                        length(
                            position -
                            center
                        );


                    // ------------------------------------------------
                    // Atmospheric density.
                    // ------------------------------------------------

                    float density =
                        1.0 -
                        (
                            (distanceFromCenter - innerRadius) /
                            (outerRadius - innerRadius)
                        );

                    density =
                        saturate(density);


                    // ------------------------------------------------
                    // Sun illumination.
                    // ------------------------------------------------

                    float3 radial =
                        normalize(
                            position -
                            center
                        );

                    float3 sunDirection =
                        normalize(
                            _SunDirection.xyz
                        );


                    float sunlight =
                        saturate(
                            dot(
                                radial,
                                sunDirection
                            )
                        );


                    sunlight =
                        max(
                            sunlight *
                            _SunInfluence,
                            _NightLight
                        );


                    accumulated +=
                        density *
                        sunlight *
                        stepLength;
                }


                // ----------------------------------------------------
                // Convert density into opacity.
                // ----------------------------------------------------

                float alpha =
                    accumulated *
                    _Opacity;

                alpha =
                    saturate(alpha);


                // ----------------------------------------------------
                // Final color.
                // ----------------------------------------------------

                float3 color =
                    _Color.rgb *
                    _Intensity;


                return fixed4(
                    color,
                    alpha
                );
            }

            ENDCG
        }
    }

    FallBack Off
}