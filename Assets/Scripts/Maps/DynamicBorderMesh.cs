using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

/// <summary>
/// Rebuilds the visible border line mesh from state_border_segments.json
/// whenever state ownership changes.
///
/// Each segment in the file is the exact shared boundary between two states
/// (a, b), or a state's outer boundary (b == 0: coastline, etc.). A shared
/// segment is drawn only if the two states currently have different owners,
/// so borders appear and vanish as states change hands. Outer boundaries
/// are always drawn.
///
/// Uses the same mesh layout as BorderLineMeshBuilder (uv.x = side, tangent
/// = segment direction), so the existing Custom/BorderLineScreenSpace
/// material works unchanged. Put this object under the Earth transform so
/// it rotates with it, and match sphereRadius / surfaceOffset to your
/// existing border mesh.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class DynamicBorderMesh : MonoBehaviour
{
    [SerializeField] private TextAsset segmentsJson;           // state_border_segments.json
    [SerializeField] private StateOwnershipRenderer ownership;
    [SerializeField] private float sphereRadius = 1f;
    [SerializeField] private float surfaceOffset = 0.004f;
    [SerializeField] private float maxSegmentDegrees = 1f;     // keeps long straight spans on the sphere surface
    [SerializeField] private bool drawAllStateBorders = false; // true: every state border; false: only where owners differ
    [SerializeField] float simplifyTolerance = 0.03f;

    private class SegmentData
    {
        public int a;
        public int b;
        public List<List<float[]>> lines;
    }

    private class SegmentFile
    {
        public List<SegmentData> segments;
    }

    private class Segment
    {
        public int a;
        public int b;
        public List<Vector3[]> polylines = new List<Vector3[]>();
    }

    private readonly List<Segment> _segments = new List<Segment>();
    private Mesh _mesh;

    // Reused between rebuilds to avoid allocating every time.
    private readonly List<Vector3> _verts = new List<Vector3>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<Vector4> _tangents = new List<Vector4>();
    private readonly List<int> _tris = new List<int>();

    private void Start()
    {
        var file = JsonConvert.DeserializeObject<SegmentFile>(segmentsJson.text);
        float radius = sphereRadius + surfaceOffset;

        foreach (var data in file.segments)
        {
            var segment = new Segment { a = data.a, b = data.b };
            foreach (var line in data.lines)
            {
                if (line.Count < 2) continue;
                segment.polylines.Add(ToSpherePoints(line, radius));
            }
            _segments.Add(segment);
        }

        _mesh = new Mesh
        {
            name = "DynamicBorders",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        // Either component may run Start first: draw now if ownership is
        // already loaded, and in any case redraw whenever it changes.
        if (ownership.IsReady) Rebuild();
        ownership.OnOwnersChanged += Rebuild;
    }

    private void OnDestroy()
    {
        if (ownership != null) ownership.OnOwnersChanged -= Rebuild;
    }

    /// <summary>Switch between "every state border" and "only where owners differ".</summary>
    public void SetDrawAllStateBorders(bool drawAll)
    {
        drawAllStateBorders = drawAll;
        if (_mesh != null) Rebuild();
    }

    private bool ShouldDraw(Segment segment)
    {
        if (drawAllStateBorders) return true;
        if (segment.b == 0) return true; // outer boundary: coastline etc.

        int ownerA = ownership.GetOwner(segment.a);
        int ownerB = ownership.GetOwner(segment.b);
        // Unresolved owners (0) are treated as different from everything.
        return ownerA == 0 || ownerB == 0 || ownerA != ownerB;
    }

    private void Rebuild()
    {
        _verts.Clear();
        _uvs.Clear();
        _tangents.Clear();
        _tris.Clear();

        foreach (var segment in _segments)
        {
            if (!ShouldDraw(segment)) continue;

            foreach (var points in segment.polylines)
            {
                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector3 p0 = points[i];
                    Vector3 p1 = points[i + 1];
                    Vector3 direction = p1 - p0;
                    if (direction.sqrMagnitude < 1e-12f) continue; // zero-length: would give the shader a NaN direction
                    direction.Normalize();

                    int baseIndex = _verts.Count;
                    _verts.Add(p0); _uvs.Add(new Vector2(-1, 0)); _tangents.Add(direction);
                    _verts.Add(p0); _uvs.Add(new Vector2(1, 0));  _tangents.Add(direction);
                    _verts.Add(p1); _uvs.Add(new Vector2(-1, 0)); _tangents.Add(direction);
                    _verts.Add(p1); _uvs.Add(new Vector2(1, 0));  _tangents.Add(direction);

                    _tris.Add(baseIndex);     _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 1);
                    _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
                }
            }
        }

        _mesh.Clear();
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.SetVertices(_verts);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTangents(_tangents);
        _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateBounds();
    }

    // tolerance in degrees; 0.03 is about 1.3 px at 16384 wide
    static List<float[]> Simplify(List<float[]> pts, float tol)
    {
        if (pts.Count < 3) return pts;
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        float tol2 = tol * tol;
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            float ax = pts[a][0], ay = pts[a][1], dx = pts[b][0] - ax, dy = pts[b][1] - ay;
            float len2 = dx * dx + dy * dy, maxD = -1f; int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                float px = pts[i][0] - ax, py = pts[i][1] - ay;
                float t = len2 > 0f ? Mathf.Clamp01((px * dx + py * dy) / len2) : 0f;
                float ex = px - t * dx, ey = py - t * dy;
                float d = ex * ex + ey * ey;
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (maxD > tol2) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
        var result = new List<float[]>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
        return result;
    }

    // Subdivides long spans in lat/lon space before projecting, so every
    // chord stays close to the sphere surface (same fix as the original
    // border mesh's 49th-parallel gap). Lines are open polylines; a closed
    // ring simply repeats its first point at the end.
    private Vector3[] ToSpherePoints(List<float[]> line, float radius)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < line.Count - 1; i++)
        {
            float lon0 = line[i][0], lat0 = line[i][1];
            float lon1 = line[i + 1][0], lat1 = line[i + 1][1];

            float distance = Mathf.Max(Mathf.Abs(lat1 - lat0), Mathf.Abs(lon1 - lon0));
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / maxSegmentDegrees));
            for (int s = 0; s < steps; s++)
            {
                float t = (float)s / steps;
                points.Add(LatLonToPoint(Mathf.Lerp(lat0, lat1, t), Mathf.Lerp(lon0, lon1, t), radius));
            }
        }

        float[] last = line[line.Count - 1];
        points.Add(LatLonToPoint(last[1], last[0], radius));
        return points.ToArray();
    }

    private static Vector3 LatLonToPoint(float latDeg, float lonDeg, float radius)
    {
        float latRad = latDeg * Mathf.Deg2Rad;
        float lonRad = lonDeg * Mathf.Deg2Rad;
        float ringRadius = Mathf.Cos(latRad);
        return new Vector3(
            ringRadius * Mathf.Cos(lonRad),
            Mathf.Sin(latRad),
            ringRadius * Mathf.Sin(lonRad)) * radius;
    }
}