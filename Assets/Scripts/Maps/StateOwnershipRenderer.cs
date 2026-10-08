using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;

public enum MapViewMode { State, National }

public class StateOwnershipRenderer : MonoBehaviour
{
    [SerializeField] private TextAsset stateIdLookupJson;
    [SerializeField] private TextAsset countryIdLookupJson;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private Renderer overlayRenderer;   // replaces the Material field
    private Material _mat;

    private const int PaletteSize = 256;

    [System.Serializable]
    private class StateInfo
    {
        public string name;
        public int? origin_country_id;
        public List<int> neighbors;   // new
    }

    [System.Serializable]
    private class CountryInfo
    {
        public string iso_a3;
        public string name;
    }

    private Dictionary<int, StateInfo> _states;
    private Texture2D _statePalette;   // static: built once, never changes
    private Texture2D _countryPalette; // static: built once, never changes
    private Texture2D _ownerTex;       // DYNAMIC: seeded from origin, mutated by conquest

    private void Start()
    {
        _mat = overlayRenderer.material;   // same instance PoliticalMapController gets
        _states = JsonConvert.DeserializeObject<Dictionary<int, StateInfo>>(stateIdLookupJson.text);
        var countries = JsonConvert.DeserializeObject<Dictionary<int, CountryInfo>>(countryIdLookupJson.text);

        _statePalette = BuildHashPalette(_states.Keys);
        _countryPalette = BuildHashPalette(countries.Keys);
        _ownerTex = BuildInitialOwnerTexture();

        _mat.SetTexture("_StatePaletteTex", _statePalette);
        _mat.SetTexture("_CountryPaletteTex", _countryPalette);
        _mat.SetTexture("_OwnerTex", _ownerTex);

        var stateAdj = new Dictionary<int, HashSet<int>>();
        var countryAdj = new Dictionary<int, HashSet<int>>();
        foreach (var kv in _states)
        {
            int ca = kv.Value.origin_country_id ?? 0;
            if (kv.Value.neighbors == null) continue;
            foreach (int nb in kv.Value.neighbors)
            {
                AddEdge(stateAdj, kv.Key, nb);
                if (ca == 0 || !_states.TryGetValue(nb, out var other)) continue;
                int cb = other.origin_country_id ?? 0;
                if (cb != 0 && cb != ca) { AddEdge(countryAdj, ca, cb); AddEdge(countryAdj, cb, ca); }
            }
        }

        _statePalette   = BuildColoredPalette(_states.Keys, stateAdj);
        _countryPalette = BuildColoredPalette(countries.Keys, countryAdj);
    }

    public void SetViewMode(MapViewMode mode)
    {
        _mat.SetFloat("_ColorizeByCountry", mode == MapViewMode.National ? 1f : 0f);
    }

    /// <summary>Reassigns one state's current owner -- call this on annexation.</summary>
    public void SetStateOwner(int stateId, int newCountryId)
    {
        WriteOwnerTexel(stateId, newCountryId);
        _ownerTex.Apply(false, false);
    }

    /// <summary>
    /// Reassigns several states at once (e.g. a whole country falling),
    /// uploading to the GPU only once instead of once per state.
    /// </summary>
    public void SetStateOwners(IEnumerable<(int stateId, int countryId)> changes)
    {
        foreach (var (stateId, countryId) in changes)
            WriteOwnerTexel(stateId, countryId);
        _ownerTex.Apply(false, false);
    }

    private void WriteOwnerTexel(int stateId, int countryId)
    {
        int x = stateId % PaletteSize;
        int y = stateId / PaletteSize;
        _ownerTex.SetPixel(x, y, PackId(countryId));
    }

    private Texture2D BuildInitialOwnerTexture()
    {
        var tex = new Texture2D(PaletteSize, PaletteSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var colors = new Color32[PaletteSize * PaletteSize];

        foreach (var kvp in _states)
        {
            int stateId = kvp.Key;
            int countryId = kvp.Value.origin_country_id ?? 0;
            int x = stateId % PaletteSize;
            int y = stateId / PaletteSize;
            colors[y * PaletteSize + x] = PackId(countryId);
        }

        tex.SetPixels32(colors);
        tex.Apply(false, false);
        return tex;
    }

    private Texture2D BuildHashPalette(IEnumerable<int> ids)
    {
        var tex = new Texture2D(PaletteSize, PaletteSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var colors = new Color32[PaletteSize * PaletteSize];

        foreach (int id in ids)
        {
            int x = id % PaletteSize;
            int y = id / PaletteSize;
            colors[y * PaletteSize + x] = HashColor(id);
        }

        tex.SetPixels32(colors);
        tex.Apply(false, false);
        return tex;
    }

    private static Color32 PackId(int id) =>
        new Color32((byte)(id & 0xFF), (byte)((id >> 8) & 0xFF), 0, 255);

    private static Color32 HashColor(int id)
    {
        float hue = Mathf.Abs(Mathf.Sin(id * 12.9898f) * 43758.5453f) % 1f;
        return Color.HSVToRGB(hue, 0.45f, 0.9f);
    }

    private const int SwatchCount = 10;

    private static Color32[] BuildSwatches()
    {
        var swatches = new Color32[SwatchCount];
        for (int h = 0; h < SwatchCount; h++)
        {
            bool even = h % 2 == 0;   // hue-adjacent swatches also differ in brightness
            swatches[h] = Color.HSVToRGB(h / (float)SwatchCount,
                                        even ? 0.55f : 0.80f,
                                        even ? 0.95f : 0.75f);
        }
        return swatches;
    }

    private Texture2D BuildColoredPalette(IEnumerable<int> ids, Dictionary<int, HashSet<int>> adjacency)
    {
        var swatches = BuildSwatches();
        var assigned = new Dictionary<int, int>();
        var useCount = new int[SwatchCount];

        // Most-connected regions first (Welsh-Powell), so the hard cases get free swatches
        var ordered = ids.OrderByDescending(id => adjacency.TryGetValue(id, out var n) ? n.Count : 0)
                        .ThenBy(id => id);

        foreach (int id in ordered)
        {
            var neighborUse = new int[SwatchCount];
            if (adjacency.TryGetValue(id, out var neighbors))
                foreach (int nb in neighbors)
                    if (assigned.TryGetValue(nb, out int c)) neighborUse[c]++;

            // Prefer a swatch no neighbor uses; among those, the least used overall
            int best = 0;
            for (int c = 1; c < SwatchCount; c++)
                if (neighborUse[c] * 100000 + useCount[c] < neighborUse[best] * 100000 + useCount[best])
                    best = c;

            assigned[id] = best;
            useCount[best]++;
        }

        var tex = new Texture2D(PaletteSize, PaletteSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var colors = new Color32[PaletteSize * PaletteSize];
        foreach (var kvp in assigned)
            colors[(kvp.Key / PaletteSize) * PaletteSize + (kvp.Key % PaletteSize)] = swatches[kvp.Value];
        tex.SetPixels32(colors);
        tex.Apply(false, false);
        return tex;
    }

    private static void AddEdge(Dictionary<int, HashSet<int>> g, int a, int b)
    {
        if (!g.TryGetValue(a, out var set)) g[a] = set = new HashSet<int>();
        set.Add(b);
    }
}