using UnityEngine;

public class PoliticalMapController : MonoBehaviour
{
    [SerializeField] private CountryPicker picker;
    [SerializeField] private Renderer overlayRenderer;
    [SerializeField] private Texture2D idMap; // drag country_id_map.png here directly

    private Material _mat;

    private void Start()
    {
        picker.SetIDMap(idMap);
        _mat = overlayRenderer.material;
        _mat.SetTexture("_IDTex", idMap); // redundant if set on the material asset already, but keeps this authoritative at runtime
    }

    private void OnEnable()
    {
        picker.OnCountryHoverChanged += id => _mat.SetFloat("_HighlightID", id);
        picker.OnCountryClicked += id => Debug.Log($"Clicked country ID {id}");
    }
}