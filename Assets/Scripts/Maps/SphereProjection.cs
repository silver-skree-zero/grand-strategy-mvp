using UnityEngine;

public static class SphereProjection
{
    /// <summary>
    /// Converts a world-space point (e.g. a raycast hit) into the same
    /// equirectangular UV space used by RuntimeUVSphere.
    /// </summary>
    public static Vector2 WorldPointToUV(Vector3 worldPoint, Transform sphereTransform, bool flipV)
    {
        Vector3 local = sphereTransform.InverseTransformPoint(worldPoint).normalized;

        float latAngleRad = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
        float lonAngleRad = Mathf.Atan2(local.z, local.x);

        float u = (lonAngleRad / (2f * Mathf.PI)) + 0.5f;
        float v = (latAngleRad / Mathf.PI) + 0.5f;

        if (flipV) v = 1f - v;

        return new Vector2(u, v);
    }
}