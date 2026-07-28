using UnityEngine;

namespace SolarSim.Unity.Canvas
{
    /// <summary>
    /// Orthographic design camera. Pan/zoom only — never scales real panel mm sizes.
    /// World unit = 1 meter. Panel dimensions are converted from mm at spawn time.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class DesignCameraController : MonoBehaviour
    {
        [SerializeField] private float minOrthoSize = 1f;
        [SerializeField] private float maxOrthoSize = 80f;
        [SerializeField] private float zoomSpeed = 0.12f;

        private Camera _camera = null!;
        private bool _panning;
        private Vector3 _panOrigin;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(1f, 1f, 1f, 1f);
            if (_camera.orthographicSize < 4f)
                _camera.orthographicSize = 8f;
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();
        }

        private void HandleZoom()
        {
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            var mouseWorldBefore = _camera.ScreenToWorldPoint(Input.mousePosition);
            var factor = 1f - scroll * zoomSpeed;
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize * factor, minOrthoSize, maxOrthoSize);
            var mouseWorldAfter = _camera.ScreenToWorldPoint(Input.mousePosition);
            var delta = mouseWorldBefore - mouseWorldAfter;
            transform.position += new Vector3(delta.x, delta.y, 0f);
        }

        private void HandlePan()
        {
            var spacePan = Input.GetKey(KeyCode.Space) && Input.GetMouseButton(0);
            var middlePan = Input.GetMouseButton(2);

            if ((spacePan || middlePan) && !_panning)
            {
                _panning = true;
                _panOrigin = _camera.ScreenToWorldPoint(Input.mousePosition);
            }
            else if (_panning && (spacePan || middlePan))
            {
                var current = _camera.ScreenToWorldPoint(Input.mousePosition);
                var delta = _panOrigin - current;
                transform.position += new Vector3(delta.x, delta.y, 0f);
            }
            else
            {
                _panning = false;
            }
        }
    }
}
