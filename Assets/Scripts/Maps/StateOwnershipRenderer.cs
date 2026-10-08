using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public enum MapViewMode { State, National }

public class StateOwnershipRenderer : MonoBehaviour
{
    [SerializeField] private TextAsset stateIdLookupJson;
    [SerializeField] private TextAsset countryIdLookupJson;
    [SerializeField] private Material overlayMaterial;

    private const int PaletteSize = 256;

    [System.Serializable]
    private class StateInfo
    {
        public string name;
        public int? origin_country_id;
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
        _states = JsonConvert.DeserializeObject<Dictionary<int, StateInfo>>(stateIdLookupJson.text);
        var countries = JsonConvert.DeserializeObject<Dictionary<int, CountryInfo>>(countryIdLookupJson.text);

        _statePalette = BuildHashPalette(_states.Keys);
        _countryPalette = BuildHashPalette(countries.Keys);
        _ownerTex = BuildInitialOwnerTexture();

        overlayMaterial.SetTexture("_StatePaletteTex", _statePalette);
        overlayMaterial.SetTexture("_CountryPaletteTex", _countryPalette);
        overlayMaterial.SetTexture("_OwnerTex", _ownerTex);
    }

    public void SetViewMode(MapViewMode mode)
    {
        overlayMaterial.SetFloat("_ColorizeByCountry", mode == MapViewMode.National ? 1f : 0f);
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
}