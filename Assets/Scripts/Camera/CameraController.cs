using UnityEngine;

/// <summary>
/// Orbital camera that rotates around a target (Earth center) via mouse drag,
/// and zooms via scroll wheel. Attach to an empty "CameraRig" GameObject
/// positioned at the Earth's center; this script will position/rotate itself,
/// and the actual Camera should be a child (or this object can hold the Camera directly).
/// </summary>
[DisallowMultipleComponent]
public class OrbitalCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target; // Earth center; defaults to world origin if null

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 300f;   // degrees per second at full drag speed
    [SerializeField] private float minPitch = -80f;        // clamp so we don't flip over the poles
    [SerializeField] private float maxPitch = 80f;
    [SerializeField] private bool invertY = false;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float minDistance = 12f;
    [SerializeField] private float maxDistance = 30f;
    [SerializeField] private float zoomSmoothTime = 0.15f;

    [Header("Rotation Smoothing")]
    [SerializeField] private float rotationSmoothTime = 0.08f;

    [Header("Input")]
    [SerializeField] private int dragButton = 0; // 0 = left mouse, 1 = right mouse, 2 = middle

    private float _yaw;
    private float _pitch;
    private float _currentDistance;
    private float _targetDistance;
    private float _distanceVelocity;

    private float _yawVelocity;
    private float _pitchVelocity;
    private float _smoothedYaw;
    private float _smoothedPitch;

    private Vector3 _lastMousePosition;
    private bool _isDragging;

    private void Awake()
    {
        if (target == null)
        {
            GameObject earth = GameObject.Find("Earth");
            GameObject originHolder = new GameObject("OrbitTarget_Origin");
            originHolder.transform.position = Vector3.zero;
            target = originHolder.transform;
            if(earth != null)
                target = earth.transform;
        }

        // Initialize yaw/pitch/distance from current transform so you can
        // hand-position the rig in the editor as a starting view.
        Vector3 offset = transform.position - target.position;
        _targetDistance = _currentDistance = offset.magnitude;

        Quaternion lookRot = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        Vector3 euler = lookRot.eulerAngles;
        _yaw = _smoothedYaw = euler.y;
        _pitch = _smoothedPitch = NormalizePitch(euler.x);
    }

    private void Update()
    {
        HandleDragInput();
        HandleZoomInput();
        ApplySmoothingAndPosition();
    }

    private void HandleDragInput()
    {
        if (Input.GetMouseButtonDown(dragButton))
        {
            _isDragging = true;
            _lastMousePosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(dragButton))
        {
            _isDragging = false;
        }

        if (!_isDragging) return;

        Vector3 delta = Input.mousePosition - _lastMousePosition;
        _lastMousePosition = Input.mousePosition;

        float invert = invertY ? -1f : 1f;

        _yaw += delta.x * rotationSpeed * Time.deltaTime * 0.1f;
        _pitch -= delta.y * rotationSpeed * Time.deltaTime * 0.1f * invert;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
    }

    private void HandleZoomInput()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            _targetDistance -= scroll * zoomSpeed;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }
    }

    private void ApplySmoothingAndPosition()
    {
        // Smooth rotation and zoom for a less jittery feel
        _smoothedYaw = Mathf.SmoothDampAngle(_smoothedYaw, _yaw, ref _yawVelocity, rotationSmoothTime);
        _smoothedPitch = Mathf.SmoothDampAngle(_smoothedPitch, _pitch, ref _pitchVelocity, rotationSmoothTime);
        _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance, ref _distanceVelocity, zoomSmoothTime);

        Quaternion rotation = Quaternion.Euler(_smoothedPitch, _smoothedYaw, 0f);
        Vector3 position = target.position + rotation * new Vector3(0f, 0f, -_currentDistance);

        transform.SetPositionAndRotation(position, rotation);
    }

    private static float NormalizePitch(float pitch)
    {
        // Convert 0-360 euler X into a -180..180 range for clamping sanity
        if (pitch > 180f) pitch -= 360f;
        return pitch;
    }

    // Optional: draw the target in the editor for reference
    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target.position, 0.2f);
        Gizmos.DrawLine(transform.position, target.position);
    }
}