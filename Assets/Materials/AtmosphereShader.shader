Shader "Custom/Earth Atmosphere Volume"
{
    Properties
    {
        _Color ("Atmosphere Color", Color) = (0.08, 0.35, 1.0, 1)

        _Intensity ("Intensity", Range(0, 10)) = 1.5
        _Opacity ("Opacity", Range(0, 5)) = 1.0

        // Atmosphere radius / Earth radius.
        // 1.04 means the atmosphere extends 4% above the surface.
        _OuterRadius ("Outer Radius", Range(1.001, 1.2)) = 1.04

        // Number of samples through the atmosphere.
        _Samples ("Ray Samples", Range(4, 32)) = 12

        // How strongly sunlight affects the atmosphere.
        _SunInfluence ("Sun Influence", Range(0, 1)) = 1.0

        // Small amount of atmospheric light on the night side.
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

        // Render the front-facing side of the outer atmosphere sphere.
        Cull Back

        ZWrite Off
        ZTest LEqual

        // Normal alpha blending rather than additive blending.
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

                o.vertex = UnityObjectToClipPos(v.vertex);

                o.worldPosition =
                    mul(unity_ObjectToWorld, v.vertex).xyz;

                return o;
            }


            // Returns the far intersection distance of a ray
            // with a sphere.
            //
            // Assumes the ray starts outside the sphere.
            float RaySphereFar(
                float3 rayOrigin,
                float3 rayDirection,
                float3 sphereCenter,
                float sphereRadius,
                out bool hit
            )
            {
                float3 oc = rayOrigin - sphereCenter;

                float b = dot(oc, rayDirection);
                float c = dot(oc, oc) -
                          sphereRadius * sphereRadius;

                float discriminant =
                    b * b - c;

                hit = discriminant >= 0.0;

                if (!hit)
                    return 0.0;

                float sqrtD = sqrt(discriminant);

                float tNear = -b - sqrtD;
                float tFar  = -b + sqrtD;

                return tFar;
            }


            // Returns the first intersection of a ray with a sphere.
            float RaySphereNear(
                float3 rayOrigin,
                float3 rayDirection,
                float3 sphereCenter,
                float sphereRadius,
                out bool hit
            )
            {
                float3 oc = rayOrigin - sphereCenter;

                float b = dot(oc, rayDirection);
                float c = dot(oc, oc) -
                          sphereRadius * sphereRadius;

                float discriminant =
                    b * b - c;

                hit = discriminant >= 0.0;

                if (!hit)
                    return 0.0;

                float sqrtD = sqrt(discriminant);

                float tNear = -b - sqrtD;

                return tNear;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float3 cameraPosition = _WorldSpaceCameraPos;

                float3 rayDirection =
                    normalize(i.worldPosition - cameraPosition);

                // Center of the atmosphere object.
                float3 center =
                    mul(
                        unity_ObjectToWorld,
                        float4(0, 0, 0, 1)
                    ).xyz;


                // ----------------------------------------------------
                // Determine the actual world-space outer radius.
                //
                // Unity's default Sphere mesh has radius 0.5,
                // so measuring from the object's center to its
                // transformed +X vertex gives us the real radius.
                // ----------------------------------------------------

                float3 worldEdge =
                    mul(
                        unity_ObjectToWorld,
                        float4(0.5, 0, 0, 1)
                    ).xyz;

                float outerRadius =
                    length(worldEdge - center);


                // Earth radius is derived from the requested
                // atmosphere radius ratio.
                //
                // Example:
                //
                // Outer = 1.04
                // Earth = 1.00
                //
                // Therefore:
                //
                // Earth / Outer = 1 / 1.04
                //

                float innerRadius =
                    outerRadius / _OuterRadius;


                // ----------------------------------------------------
                // The current fragment is the near intersection with
                // the outer atmosphere sphere because Cull Back is
                // being used.
                // ----------------------------------------------------

                float3 fragmentToCamera =
                    cameraPosition - i.worldPosition;

                float tStart =
                    length(fragmentToCamera);


                // ----------------------------------------------------
                // Find where this camera ray exits the outer sphere.
                // ----------------------------------------------------

                bool outerHit;

                float tOuterFar =
                    RaySphereFar(
                        cameraPosition,
                        rayDirection,
                        center,
                        outerRadius,
                        outerHit
                    );


                if (!outerHit)
                    discard;


                // ----------------------------------------------------
                // Find where the ray first intersects the Earth.
                //
                // If it hits Earth, the atmosphere in front of Earth
                // ends at the Earth's surface.
                //
                // If it doesn't hit Earth, the ray travels through
                // the entire atmospheric shell and exits the far
                // side of the atmosphere.
                // ----------------------------------------------------

                bool innerHit;

                float tInner =
                    RaySphereNear(
                        cameraPosition,
                        rayDirection,
                        center,
                        innerRadius,
                        innerHit
                    );


                float tEnd;

                if (innerHit && tInner > tStart)
                {
                    tEnd = tInner;
                }
                else
                {
                    tEnd = tOuterFar;
                }


                float pathLength =
                    max(0.0, tEnd - tStart);

                if (pathLength <= 0.0)
                    discard;


                // ----------------------------------------------------
                // Ray march through the atmospheric volume.
                // ----------------------------------------------------

                float accumulated = 0.0;

                float samples =
                    max(4.0, _Samples);

                float stepLength =
                    pathLength / samples;


                for (int sample = 0; sample < 32; sample++)
                {
                    if (sample >= samples)
                        break;


                    // Sample at the center of each interval.
                    float t =
                        tStart +
                        stepLength * (sample + 0.5);


                    float3 position =
                        cameraPosition +
                        rayDirection * t;


                    float distanceFromCenter =
                        length(position - center);


                    // ------------------------------------------------
                    // Atmospheric density.
                    //
                    // At Earth surface:
                    //
                    //     radius = innerRadius
                    //     density = 1
                    //
                    // At atmosphere edge:
                    //
                    //     radius = outerRadius
                    //     density = 0
                    //
                    // This is the linear falloff you described.
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
                    //
                    // The radial direction represents the local
                    // "surface normal" of the atmosphere.
                    // ------------------------------------------------

                    float3 radial =
                        normalize(position - center);

                    float3 sunDirection =
                        normalize(_SunDirection.xyz);


                    float sunlight =
                        saturate(
                            dot(radial, sunDirection)
                        );


                    // Allow a small amount of atmospheric light
                    // on the night side.
                    sunlight =
                        max(
                            sunlight * _SunInfluence,
                            _NightLight
                        );


                    // Density × illumination × distance traveled.
                    accumulated +=
                        density *
                        sunlight *
                        stepLength;
                }


                // ----------------------------------------------------
                // Convert accumulated atmospheric density into
                // pixel opacity.
                //
                // _Opacity controls the overall strength without
                // changing the density profile.
                // ----------------------------------------------------

                float alpha =
                    accumulated *
                    _Opacity;


                alpha =
                    saturate(alpha);


                // ----------------------------------------------------
                // Color/intensity.
                // ----------------------------------------------------

                float3 color =
                    _Color.rgb *
                    _Intensity;


                return fixed4(color, alpha);
            }

            ENDCG
        }
    }

    FallBack Off
}