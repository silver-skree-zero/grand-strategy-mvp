using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BorderLineMeshBuilder : MonoBehaviour
{
    [SerializeField] private TextAsset borderVectorsJson;
    [SerializeField] private float sphereRadius = 1f;      // must match RuntimeUVSphere.radius
    [SerializeField] private float surfaceOffset = 0.004f; // sit above the territory overlay shell

    private void Start()
    {
        BuildMesh();
    }

    private void BuildMesh()
{
    var data = JsonConvert.DeserializeObject<Dictionary<string, List<List<List<float>>>>>(borderVectorsJson.text);

    var vertices = new List<Vector3>();
    var uvs = new List<Vector2>();
    var tangents = new List<Vector4>();
    var triangles = new List<int>();

    float finalRadius = sphereRadius + surfaceOffset;

    foreach (var kvp in data)
    {
        foreach (var ring in kvp.Value)
        {
            int count = ring.Count;
            if (count < 2) continue;

            // Build one flattened, subdivided point list for the whole ring first,
            // so long sparse spans (e.g. a straight parallel-latitude border) get
            // broken into short enough hops that each chord hugs the sphere
            // surface instead of cutting through its interior.
            var subdivided = new List<(float lat, float lon)>();
            for (int i = 0; i < count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % count];
                AppendSubdivided(subdivided, a[1], a[0], b[1], b[0]);
            }

            int subCount = subdivided.Count;
            for (int i = 0; i < subCount; i++)
            {
                var p0ll = subdivided[i];
                var p1ll = subdivided[(i + 1) % subCount];

                Vector3 p0 = LatLonToPoint(p0ll.lat, p0ll.lon, finalRadius);
                Vector3 p1 = LatLonToPoint(p1ll.lat, p1ll.lon, finalRadius);
                Vector3 dir = (p1 - p0).normalized;

                int baseIndex = vertices.Count;
                vertices.Add(p0); uvs.Add(new Vector2(-1, 0)); tangents.Add(dir);
                vertices.Add(p0); uvs.Add(new Vector2(1, 0));  tangents.Add(dir);
                vertices.Add(p1); uvs.Add(new Vector2(-1, 0)); tangents.Add(dir);
                vertices.Add(p1); uvs.Add(new Vector2(1, 0));  tangents.Add(dir);

                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
            }
        }
    }

    var mesh = new Mesh
    {
        name = "BorderLines",
        indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16
    };
    mesh.SetVertices(vertices);
    mesh.SetUVs(0, uvs);
    mesh.SetTangents(tangents.ToArray());
    mesh.SetTriangles(triangles, 0);
    mesh.RecalculateBounds();

    GetComponent<MeshFilter>().mesh = mesh;
}

[SerializeField] private float maxSegmentDegrees = 1f; // tune: smaller = smoother/safer, more verts

private void AppendSubdivided(List<(float lat, float lon)> output, float lat0, float lon0, float lat1, float lon1)
{
    float dist = Mathf.Max(Mathf.Abs(lat1 - lat0), Mathf.Abs(lon1 - lon0));
    int steps = Mathf.Max(1, Mathf.CeilToInt(dist / maxSegmentDegrees));

    for (int s = 0; s < steps; s++)
    {
        float t = (float)s / steps;
        output.Add((Mathf.Lerp(lat0, lat1, t), Mathf.Lerp(lon0, lon1, t)));
    }
    // Note: the final point (t=1) is intentionally NOT added here -- it's
    // the next original ring point's own t=0, added on the next iteration,
    // avoiding duplicate coincident vertices at segment boundaries.
}
    private static Vector3 LatLonToPoint(float latDeg, float lonDeg, float radius)
    {
        float latRad = latDeg * Mathf.Deg2Rad;
        float lonRad = lonDeg * Mathf.Deg2Rad;
        float ringRadius = Mathf.Cos(latRad);
        float x = ringRadius * Mathf.Cos(lonRad);
        float z = ringRadius * Mathf.Sin(lonRad);
        float y = Mathf.Sin(latRad);
        return new Vector3(x, y, z) * radius;
    }
}