using UnityEngine;

/// <summary>
/// Shows the clicked icon in the HUD info panel. Replace the body text with
/// real data once organizations / regional stats exist.
/// </summary>
public class MapIconInfoPresenter : MonoBehaviour
{
    [SerializeField] private MapIconLayer layer;
    //[SerializeField] private MapViewController hud;

    private void OnEnable()  { layer.IconClicked += OnClicked; }
    private void OnDisable() { layer.IconClicked -= OnClicked; }

    private void OnClicked(MapIcon icon)
    {
        string body = icon.pop > 0 ? $"Population: {icon.pop:N0}\n" : "";
        if (!string.IsNullOrEmpty(icon.country)) body += $"Country: {icon.country}\n";
        if (!string.IsNullOrEmpty(icon.detail))  body += icon.detail;
        //hud.ShowInfo(icon.name, body.TrimEnd());
    }
}
