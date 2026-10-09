using UnityEngine;

public class PoliticalMapController : MonoBehaviour
{
    [SerializeField] private CountryPicker picker;
    [SerializeField] private Renderer overlayRenderer;
    [SerializeField] private Texture2D idMap; // drag country_id_map.png here directly
    [SerializeField] private StateOwnershipRenderer stateOwnershipRenderer;
    [SerializeField] float periodSeconds = 3f;
    [SerializeField, Range(0.01f, 0.5f)] float softness = 0.25f;
    [SerializeField, Range(0f, 1f)] float threshold = 0.5f;  // 0.5 -> occupier color for 1/3 of the cycle


    private Material _mat;

    private void Start()
    {
        picker.SetIDMap(idMap);
        _mat = overlayRenderer.material;
        _mat.SetTexture("_IDTex", idMap); // redundant if set on the material asset already, but keeps this authoritative at runtime
    }

    private void Update()
    {
        float s = Mathf.Sin(Time.time * (2f * Mathf.PI / periodSeconds));
        float t = Mathf.Clamp01((s - (threshold - softness)) / (2f * softness));
        Shader.SetGlobalFloat("_Pulse", t * t * (3f - 2f * t));   // smoothstep
    }

    private void OnEnable()
    {
        picker.OnCountryHoverChanged += id => _mat.SetFloat("_HighlightID", id);
        picker.OnCountryClicked += id => onMiddleClick(id);
        picker.OnCountryMiddleClicked += id => onMiddleClick(id);
    }

    private void onClick(int id)
    {
        Debug.Log($"Clicked country ID {id}");
        stateOwnershipRenderer.SetStateOwner(id, 155);
        Debug.Log($"Clicked state now owned by country ID {stateOwnershipRenderer.GetOwner(id)}");
    }

    private void onMiddleClick(int id)
    {
        Debug.Log($"Clicked country ID {id}");
        stateOwnershipRenderer.SetStateController(id, 155);
        Debug.Log($"Clicked state now controlled by country ID {stateOwnershipRenderer.GetController(id)}");
    }
}