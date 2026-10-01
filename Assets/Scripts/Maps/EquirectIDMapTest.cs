using UnityEngine;

public class EquirectIDMapTest : MonoBehaviour
{
    [SerializeField] private float minLat = 25f, maxLat = 49f, minLon = -125f, maxLon = -66f;
    [SerializeField] private int width = 1024, height = 512;
    [SerializeField] private byte testID = 1; // 0 = no country
    [SerializeField] private bool flipV = false;

    [Header("Texture")]
    [SerializeField] private Color fillColor = new Color(1f, 0f, 0f, 0.6f);

    public Texture2D Generate()
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
        GetComponent<Renderer>().material.mainTexture = tex;
        return tex;
    }
}