using UnityEngine;

/// <summary>
/// Generates a UV sphere mesh at runtime with explicit, uniform lat/lon
/// vertex spacing and equirectangular UVs matching standard lon/lat mapping.
/// Attach to your Earth GameObject in place of the default Sphere primitive.
/// Requires MeshFilter and MeshRenderer components.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RuntimeUVSphere : MonoBehaviour
{
    [Header("Resolution")]
    [Tooltip("Number of segments around the equator (longitude divisions).")]
    [SerializeField] private int lonSegments = 128;
    [Tooltip("Number of segments from pole to pole (latitude divisions).")]
    [SerializeField] private int latSegments = 64;

    [Header("Shape")]
    [SerializeField] private float radius = 1f;

    [Header("UV")]
    [Tooltip("Match this to whatever flipV setting you used in EquirectRectangleTest.")]
    [SerializeField] private bool flipV = false;

    [Header("Collider")]
    [Tooltip("Adds a MeshCollider matching the generated mesh. Leave off if Earth clicks aren't needed at the sphere level.")]
    [SerializeField] private bool addMeshCollider = false;

    private void Awake()
    {
        GenerateSphere();
    }

    public void GenerateSphere()
    {
        Mesh mesh = BuildMesh();
        GetComponent<MeshFilter>().mesh = mesh;

        if (addMeshCollider)
        {
            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }
    }

    private Mesh BuildMesh()
    {
        int vertRowCount = latSegments + 1; // rings from south pole to north pole, inclusive
        int vertColCount = lonSegments + 1; // +1 so the seam at lon=180/-180 has matching UVs on both edges

        var vertices = new Vector3[vertRowCount * vertColCount];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];

        for (int lat = 0; lat < vertRowCount; lat++)
        {
            // v: 0 at south pole, 1 at north pole (before flipV)
            float v = (float)lat / latSegments;
            float latAngleRad = (v - 0.5f) * Mathf.PI; // -PI/2 .. +PI/2

            float y = Mathf.Sin(latAngleRad);
            float ringRadius = Mathf.Cos(latAngleRad);

            for (int lon = 0; lon < vertColCount; lon++)
            {
                float u = (float)lon / lonSegments; // 0..1 around the equator
                float lonAngleRad = (u - 0.5f) * 2f * Mathf.PI; // -PI .. +PI

                float x = ringRadius * Mathf.Cos(lonAngleRad);
                float z = ringRadius * Mathf.Sin(lonAngleRad);

                int index = lat * vertColCount + lon;
                vertices[index] = new Vector3(x, y, z) * radius;
                normals[index] = new Vector3(x, y, z); // unit sphere, so position == normal direction

                float finalV = flipV ? 1f - v : v;
                uvs[index] = new Vector2(u, finalV);
            }
        }

        // Triangles: skip degenerate rows at poles isn't needed here since we're
        // just letting the pole ring's triangles collapse naturally (all verts at
        // that ring share the same position but different UVs, which is correct
        // for texture seams and is fine for a static, non-deformed sphere).
        int quadRows = latSegments;
        int quadCols = lonSegments;
        var triangles = new int[quadRows * quadCols * 6];
        int t = 0;

        for (int lat = 0; lat < quadRows; lat++)
        {
            for (int lon = 0; lon < quadCols; lon++)
            {
                int i0 = lat * vertColCount + lon;
                int i1 = i0 + 1;
                int i2 = i0 + vertColCount;
                int i3 = i2 + 1;

                // Two triangles per quad, wound for outward-facing normals
                triangles[t++] = i0;
                triangles[t++] = i2;
                triangles[t++] = i1;

                triangles[t++] = i1;
                triangles[t++] = i2;
                triangles[t++] = i3;
            }
        }

        var mesh = new Mesh
        {
            name = "RuntimeUVSphere",
            indexFormat = (vertices.Length > 65535)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        // Not calling RecalculateNormals() since we computed exact analytic
        // normals above — more accurate than Unity's approximation for a sphere.

        return mesh;
    }
}