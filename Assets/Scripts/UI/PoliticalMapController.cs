using UnityEngine;

public class PoliticalMapController : MonoBehaviour
{
    [SerializeField] private CountryPicker picker;
    [SerializeField] private Renderer overlayRenderer;
    [SerializeField] private EquirectIDMapTest idMapGenerator;

    private Material _mat;

    private void Start()
    {
        Texture2D idMap = idMapGenerator.Generate();
        picker.SetIDMap(idMap);
        _mat = overlayRenderer.material;
        _mat.SetTexture("_IDTex", idMap);
    }

    private void OnEnable()
    {
        picker.OnCountryHoverChanged += id => _mat.SetFloat("_HighlightID", id);
        picker.OnCountryClicked += id => Debug.Log($"Clicked country ID {id}"); // swap for popup later
    }
}