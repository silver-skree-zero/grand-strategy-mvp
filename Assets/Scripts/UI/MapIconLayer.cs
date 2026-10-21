using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws every MapIcon as a constant-pixel-size billboard from ONE mesh,
/// handles zoom-based level of detail, screen-space decluttering, hover and
/// click picking.
///
/// Setup (Built-in RP):
///   1. Create an empty child of the Earth at local (0,0,0), identity rotation
///      and scale 1 (so it inherits the Earth's scale and rotation). Add this
///      component; MeshFilter/MeshRenderer are added automatically.
///   2. Make a material with Custom/MapIconBillboard and assign the icon atlas.
///   3. Assign the material, map_icons.json and (optionally) the camera.
///   4. Set sphereRadius/surfaceOffset to match DynamicBorderMesh.
///
/// Dynamic icons (moving fleets, armies) use AddIcon / SetPosition / RemoveIcon.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MapIconLayer : MonoBehaviour
{
    [Serializable]
    public class IconTypeStyle
    {
        public string name = "city";
        [Tooltip("Cell in the atlas, row-major from the bottom-left")]
        public int atlasIndex = 0;
        public Color tint = Color.white;
        [Tooltip("Icon size in pixels at scale 1")]
        public float sizePx = 18f;
        [Tooltip("Shown when this type's toggle is on (SetTypeVisible)")]
        public bool visible = true;
    }

    [Header("Data")]
    [SerializeField] private TextAsset iconsJson;
    [SerializeField] private Material material;
    [SerializeField] private Camera cam;
    [SerializeField] private int renderQueue = 3100;       // above night overlay / borders; tune to your stack

    [Header("Placement")]
    [SerializeField] private float sphereRadius = 1f;      // local units, same as DynamicBorderMesh
    [SerializeField] private float surfaceOffset = 0.01f;

    [Header("Atlas")]
    [SerializeField] private int atlasColumns = 4;
    [SerializeField] private int atlasRows = 4;
    [SerializeField] private List<IconTypeStyle> styles = new List<IconTypeStyle>
    {
        new IconTypeStyle { name = "city",       atlasIndex = 0 },
        new IconTypeStyle { name = "capital",    atlasIndex = 1 },
        new IconTypeStyle { name = "datacenter", atlasIndex = 2 },
        new IconTypeStyle { name = "fab",        atlasIndex = 3 },
        new IconTypeStyle { name = "power",      atlasIndex = 4 },
        new IconTypeStyle { name = "port",       atlasIndex = 5 },
        new IconTypeStyle { name = "spaceport",  atlasIndex = 6 },
        new IconTypeStyle { name = "military",   atlasIndex = 7 },
        new IconTypeStyle { name = "network",    atlasIndex = 8 },
        new IconTypeStyle { name = "refinery",   atlasIndex = 9 },
        new IconTypeStyle { name = "lab",        atlasIndex = 10 },
    };

    [Header("Level of detail")]
    [Tooltip("X = camera distance to Earth centre, Y = minimum priority shown at that distance")]
    [SerializeField] private AnimationCurve minPriorityByDistance = new AnimationCurve(
        new Keyframe(12f, 0f), new Keyframe(18f, 45f), new Keyframe(30f, 80f));
    [SerializeField] private float declutterCellPx = 34f;  // at most one icon per cell; 0 disables
    [SerializeField] private float recomputeInterval = 0.1f;
    [Tooltip("Icon pixel scale vs camera distance (X distance, Y scale)")]
    [SerializeField] private AnimationCurve scaleByDistance = new AnimationCurve(
        new Keyframe(12f, 1f), new Keyframe(30f, 0.8f));

    [Header("Interaction")]
    [SerializeField] private float pickRadiusPx = 16f;
    [SerializeField] private float hoverScale = 1.3f;
    [SerializeField] private Color hoverTint = new Color(1f, 0.95f, 0.6f, 1f);
    [SerializeField] private int clickButton = 0;
    [SerializeField] private float clickMaxDragPx = 6f;

    // ---- Public API / events -----------------------------------------------
    public event Action<MapIcon> IconHovered;     // null when the hover ends
    public event Action<MapIcon> IconClicked;
    public static MapIcon Hovered { get; private set; }
    /// <summary>The globe picker should skip state picking while this is true.</summary>
    public static bool IsPointerOverIcon => Hovered != null;
    public IReadOnlyList<MapIcon> Icons => _icons;

    // ---- State -------------------------------------------------------------
    private readonly List<MapIcon> _icons = new List<MapIcon>();
    private readonly Dictionary<string, MapIcon> _byId = new Dictionary<string, MapIcon>();
    private readonly Dictionary<string, int> _styleByName = new Dictionary<string, int>();
    private readonly Dictionary<int, int> _cellBest = new Dictionary<int, int>();
    private readonly List<int> _visible = new List<int>();
    private readonly List<int> _indices = new List<int>();

    private readonly List<Vector3> _verts = new List<Vector3>();
    private readonly List<Vector2> _uv0 = new List<Vector2>();
    private readonly List<Vector2> _uv1 = new List<Vector2>();
    private readonly List<Color> _colors = new List<Color>();

    private Mesh _mesh;
    private bool _geometryDirty = true;
    private bool _visibilityDirty = true;
    private float _nextRecompute;
    private Vector3 _lastCamPos;
    private Quaternion _lastEarthRot;
    private MapIcon _selected;
    private Vector2 _mouseDownPos;
    private bool _mouseDown;

    private static readonly int IconScaleId = Shader.PropertyToID("_IconScale");

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (material != null)
        {
            material = new Material(material) { renderQueue = renderQueue };
            mr.sharedMaterial = material;
        }

        _mesh = new Mesh { name = "MapIcons", indexFormat = IndexFormat.UInt32 };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        for (int i = 0; i < styles.Count; i++) _styleByName[styles[i].name] = i;

        if (iconsJson != null)
        {
            var file = JsonConvert.DeserializeObject<MapIconFile>(iconsJson.text);
            if (file?.icons != null)
                foreach (var icon in file.icons) AddIconInternal(icon);
        }
    }

    // ---- Dynamic icon API --------------------------------------------------
    public MapIcon AddIcon(MapIcon icon)
    {
        AddIconInternal(icon);
        return icon;
    }

    public bool RemoveIcon(string id)
    {
        if (!_byId.TryGetValue(id, out var icon)) return false;
        int i = icon.index;
        int last = _icons.Count - 1;
        _icons[i] = _icons[last];
        _icons[i].index = i;
        _icons.RemoveAt(last);
        _byId.Remove(id);
        if (_selected == icon) _selected = null;
        if (Hovered == icon) SetHovered(null);
        _geometryDirty = _visibilityDirty = true;
        return true;
    }

    public bool TryGet(string id, out MapIcon icon) => _byId.TryGetValue(id, out icon);

    public void SetPosition(string id, double lat, double lon)
    {
        if (!_byId.TryGetValue(id, out var icon)) return;
        icon.lat = lat; icon.lon = lon;
        WriteIconVerts(icon);
        _visibilityDirty = true;
    }

    public void SetTypeVisible(string typeName, bool visible)
    {
        if (!_styleByName.TryGetValue(typeName, out int s)) return;
        styles[s].visible = visible;
        _visibilityDirty = true;
    }

    /// <summary>Selected icons stay visible regardless of LOD / declutter.</summary>
    public void Select(MapIcon icon)
    {
        _selected = icon;
        _visibilityDirty = true;
    }

    private void AddIconInternal(MapIcon icon)
    {
        if (string.IsNullOrEmpty(icon.id) || _byId.ContainsKey(icon.id)) return;
        icon.styleIndex = _styleByName.TryGetValue(icon.type ?? "", out int s) ? s : 0;
        icon.index = _icons.Count;
        _icons.Add(icon);
        _byId[icon.id] = icon;
        _geometryDirty = _visibilityDirty = true;
    }

    // ---- Frame loop --------------------------------------------------------
    private void LateUpdate()
    {
        if (cam == null || material == null) return;

        bool camMoved = (cam.transform.position - _lastCamPos).sqrMagnitude > 1e-6f
                     || Quaternion.Angle(transform.rotation, _lastEarthRot) > 0.01f;

        if (_geometryDirty) RebuildGeometry();

        if (_visibilityDirty || (camMoved && Time.unscaledTime >= _nextRecompute))
        {
            RecomputeVisible();
            _lastCamPos = cam.transform.position;
            _lastEarthRot = transform.rotation;
            _nextRecompute = Time.unscaledTime + recomputeInterval;
            _visibilityDirty = false;
        }

        float dist = (cam.transform.position - transform.position).magnitude;
        material.SetFloat(IconScaleId, scaleByDistance.Evaluate(dist));

        HandlePicking();
    }

    // ---- Geometry ----------------------------------------------------------
    private Vector3 LatLonToLocal(double latDeg, double lonDeg)
    {
        float lat = (float)latDeg * Mathf.Deg2Rad;
        float lon = (float)lonDeg * Mathf.Deg2Rad;
        float ring = Mathf.Cos(lat);
        // Same convention as DynamicBorderMesh.LatLonToPoint.
        return new Vector3(ring * Mathf.Cos(lon), Mathf.Sin(lat), ring * Mathf.Sin(lon))
               * (sphereRadius + surfaceOffset);
    }

    private void RebuildGeometry()
    {
        _geometryDirty = false;
        _verts.Clear(); _uv0.Clear(); _uv1.Clear(); _colors.Clear();
        for (int i = 0; i < _icons.Count; i++)
        {
            var icon = _icons[i];
            Vector3 p = LatLonToLocal(icon.lat, icon.lon);
            var st = styles[icon.styleIndex];
            var (u0, v0, u1, v1) = AtlasRect(st.atlasIndex);
            float h = st.sizePx * 0.5f;
            for (int c = 0; c < 4; c++)
            {
                float cx = (c == 0 || c == 3) ? -1f : 1f;      // 0:BL 1:BR 2:TR 3:TL
                float cy = (c < 2) ? -1f : 1f;
                _verts.Add(p);
                _uv0.Add(new Vector2(cx < 0 ? u0 : u1, cy < 0 ? v0 : v1));
                _uv1.Add(new Vector2(cx * h, cy * h));
                _colors.Add(st.tint);
            }
        }

        _mesh.Clear();
        _mesh.indexFormat = IndexFormat.UInt32;        // Clear() resets this
        _mesh.SetVertices(_verts);
        _mesh.SetUVs(0, _uv0);
        _mesh.SetUVs(1, _uv1);
        _mesh.SetColors(_colors);
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (sphereRadius + 1f) * 2.5f);
        _visibilityDirty = true;
    }

    private void WriteIconVerts(MapIcon icon)
    {
        if (_geometryDirty) return;             // will be rebuilt anyway
        Vector3 p = LatLonToLocal(icon.lat, icon.lon);
        int b = icon.index * 4;
        for (int c = 0; c < 4; c++) _verts[b + c] = p;
        _mesh.SetVertices(_verts);
    }

    private (float, float, float, float) AtlasRect(int index)
    {
        int col = index % atlasColumns;
        int row = index / atlasColumns;
        float w = 1f / atlasColumns, h = 1f / atlasRows;
        // Inset half a texel-ish to avoid bleeding between cells.
        const float pad = 0.001f;
        return (col * w + pad, row * h + pad, (col + 1) * w - pad, (row + 1) * h - pad);
    }

    // ---- Visibility (LOD + declutter) --------------------------------------
    private float Facing(Vector3 worldPos)
    {
        Vector3 n = (worldPos - transform.position).normalized;
        Vector3 toCam = (cam.transform.position - worldPos).normalized;
        return Vector3.Dot(n, toCam);
    }

    private bool Passes(MapIcon icon, float minPriority)
    {
        if (!styles[icon.styleIndex].visible) return false;
        return icon.priority >= minPriority || icon == _selected;
    }

    private void RecomputeVisible()
    {
        _visible.Clear();
        _cellBest.Clear();

        float dist = (cam.transform.position - transform.position).magnitude;
        float minPriority = minPriorityByDistance.Evaluate(dist);

        for (int i = 0; i < _icons.Count; i++)
        {
            var icon = _icons[i];
            if (!Passes(icon, minPriority)) continue;

            Vector3 world = transform.TransformPoint(LatLonToLocal(icon.lat, icon.lon));
            if (Facing(world) < 0.02f) continue;                 // far side of the globe

            if (declutterCellPx > 0f && icon != _selected)
            {
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;
                int cx = Mathf.FloorToInt(sp.x / declutterCellPx);
                int cy = Mathf.FloorToInt(sp.y / declutterCellPx);
                int key = cx * 73856093 ^ cy * 19349663;
                if (_cellBest.TryGetValue(key, out int other))
                {
                    if (_icons[other].priority >= icon.priority) continue;
                    _cellBest[key] = i;
                }
                else _cellBest[key] = i;
            }
            else _visible.Add(i);
        }

        if (declutterCellPx > 0f)
            foreach (var kv in _cellBest) _visible.Add(kv.Value);
        if (_selected != null && !_visible.Contains(_selected.index) && Passes(_selected, 0f))
            _visible.Add(_selected.index);

        _indices.Clear();
        foreach (int i in _visible)
        {
            int b = i * 4;
            _indices.Add(b); _indices.Add(b + 2); _indices.Add(b + 1);
            _indices.Add(b); _indices.Add(b + 3); _indices.Add(b + 2);
        }
        _mesh.SetIndices(_indices, MeshTopology.Triangles, 0, false);
    }

    // ---- Picking -----------------------------------------------------------
    private void HandlePicking()
    {
        //if (MapViewController.IsPointerOverUI) { SetHovered(null); _mouseDown = false; return; }

        Vector2 mouse = Input.mousePosition;
        float best = pickRadiusPx * pickRadiusPx;
        MapIcon hit = null;
        foreach (int i in _visible)
        {
            var icon = _icons[i];
            Vector3 world = transform.TransformPoint(LatLonToLocal(icon.lat, icon.lon));
            if (Facing(world) < 0.1f) continue;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) continue;
            float d = ((Vector2)sp - mouse).sqrMagnitude;
            if (d < best) { best = d; hit = icon; }
        }
        SetHovered(hit);

        if (Input.GetMouseButtonDown(clickButton)) { _mouseDown = true; _mouseDownPos = mouse; }
        if (Input.GetMouseButtonUp(clickButton))
        {
            if (_mouseDown && Hovered != null && (mouse - _mouseDownPos).magnitude <= clickMaxDragPx)
            {
                Select(Hovered);
                IconClicked?.Invoke(Hovered);
            }
            _mouseDown = false;
        }
    }

    private void SetHovered(MapIcon icon)
    {
        if (icon == Hovered) return;
        if (Hovered != null) StyleIcon(Hovered, false);
        Hovered = icon;
        if (icon != null) StyleIcon(icon, true);
        _mesh.SetUVs(1, _uv1);
        _mesh.SetColors(_colors);
        IconHovered?.Invoke(icon);
    }

    private void StyleIcon(MapIcon icon, bool hover)
    {
        if (_geometryDirty || icon.index * 4 + 3 >= _colors.Count) return;
        var st = styles[icon.styleIndex];
        float h = st.sizePx * 0.5f * (hover ? hoverScale : 1f);
        int b = icon.index * 4;
        for (int c = 0; c < 4; c++)
        {
            float cx = (c == 0 || c == 3) ? -1f : 1f;
            float cy = (c < 2) ? -1f : 1f;
            _uv1[b + c] = new Vector2(cx * h, cy * h);
            _colors[b + c] = hover ? hoverTint : st.tint;
        }
    }

    private void OnDestroy()
    {
        if (Hovered != null) Hovered = null;
        if (_mesh != null) Destroy(_mesh);
    }
}
