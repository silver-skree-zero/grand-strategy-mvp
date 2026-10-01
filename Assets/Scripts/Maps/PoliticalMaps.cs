using UnityEngine;

/// <summary>
/// Generates a texture with a filled rectangle at the given lat/lon bounds,
/// using standard equirectangular UV mapping (matches Unity's default sphere UVs).
/// Attach to the overlay shell sphere.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class EquirectRectangleTest : MonoBehaviour
{
    [Header("Bounds (degrees)")]
    [SerializeField] private float minLat = 25f;
    [SerializeField] private float maxLat = 49f;
    [SerializeField] private float minLon = -125f;
    [SerializeField] private float maxLon = -66f;

    [Header("Texture")]
    [SerializeField] private int width = 1024;
    [SerializeField] private int height = 512;
    [SerializeField] private Color fillColor = new Color(1f, 0f, 0f, 0.6f);
    [SerializeField] private bool flipV = false; // flip if the shape appears mirrored top/bottom
    [SerializeField] private byte testID = 1; // 0 = no country

    private void Start()
    {
        Texture2D tex = GenerateTexture();
        GetComponent<Renderer>().material.mainTexture = tex;
    }

    private Texture2D GenerateTexture()
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var clear = new Color(0, 0, 0, 0);
        var pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float v = (float)y / height;
            if (flipV) v = 1f - v;
            float lat = (v - 0.5f) * 180f;

            for (int x = 0; x < width; x++)
            {
                float u = (float)x / width;
                float lon = (u - 0.5f) * 360f;

                bool inside = lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon;
                pixels[y * width + x] = inside ? fillColor : clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}