using UnityEngine;

public class CountryPicker : MonoBehaviour
{
    [SerializeField] private Transform earthTransform; // must have a SphereCollider
    [SerializeField] private Camera cam;
    [SerializeField] private bool flipV = false;
    [SerializeField] private float dragThresholdPixels = 6f;

    public event System.Action<int> OnCountryHoverChanged; // -1 = no country
    public event System.Action<int> OnCountryClicked;
    public event System.Action<int> OnCountryMiddleClicked;

    private Texture2D _idMap;
    private Color32[] _idPixels;
    private int _texWidth, _texHeight;
    private int _currentHoverID = -1;
    private Vector3 _mouseDownPos;
    private bool _isPressed;

    public void SetIDMap(Texture2D idMap)
    {
        _idMap = idMap;
        _idPixels = idMap.GetPixels32();
        _texWidth = idMap.width;
        _texHeight = idMap.height;
    }

    private void Update()
    {
        if (_idPixels == null) return;

        int id = SampleCountryIDUnderMouse();

        if (id != _currentHoverID)
        {
            _currentHoverID = id;
            OnCountryHoverChanged?.Invoke(id);
        }

        if (Input.GetMouseButtonDown(0))
        {
            _isPressed = true;
            _mouseDownPos = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0) && _isPressed)
        {
            _isPressed = false;
            if (Vector3.Distance(_mouseDownPos, Input.mousePosition) <= dragThresholdPixels && id >= 0)
                OnCountryClicked?.Invoke(id);
        }
    }

    private int SampleCountryIDUnderMouse()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return -1;
        if (hit.collider.transform != earthTransform) return -1;

        Vector2 uv = SphereProjection.WorldPointToUV(hit.point, earthTransform, flipV);
        int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * _texWidth), 0, _texWidth - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(uv.y * _texHeight), 0, _texHeight - 1);

        //int id = pixel.r + pixel.g * 256;
        int id = (int)(_idPixels[py * _texWidth + px].r) + (int)(_idPixels[py * _texWidth + px].g) * 256;
        //Debug.Log("test: " + id);
        return id == 0 ? -1 : id;
    }
}