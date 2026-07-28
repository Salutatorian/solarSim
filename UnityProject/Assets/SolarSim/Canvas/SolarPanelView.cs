using SolarSim.Domain.Equipment;
using UnityEngine;

namespace SolarSim.Unity.Canvas
{
    /// <summary>
    /// Visual proxy for a SolarPanelInstance. Never owns electrical topology.
    /// Domain position is the panel's min-corner in mm; the transform is centered.
    /// </summary>
    public sealed class SolarPanelView : MonoBehaviour
    {
        private MeshRenderer? _body;
        private MeshRenderer? _selection;
        private Transform? _positivePort;
        private Transform? _negativePort;
        private static Material? _bodyMat;
        private static Material? _selectedMat;
        private static Material? _posMat;
        private static Material? _negMat;

        public System.Guid InstanceId { get; private set; }
        public System.Guid DefinitionId { get; private set; }

        public static SolarPanelView Create(Transform parent)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<SolarPanelView>();
            view.BuildVisuals();
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            return view;
        }

        private void BuildVisuals()
        {
            EnsureMaterials();
            _body = CreateQuad("Body", _bodyMat!, 0);
            _selection = CreateQuad("Selection", _selectedMat!, -1);
            _selection.enabled = false;

            _positivePort = CreatePortDot("PV+", _posMat!).transform;
            _negativePort = CreatePortDot("PV-", _negMat!).transform;
            _positivePort.localScale = Vector3.one * 0.1f;
            _negativePort.localScale = Vector3.one * 0.1f;
        }

        private MeshRenderer CreateQuad(string childName, Material material, int sortingOrder)
        {
            var child = GameObject.CreatePrimitive(PrimitiveType.Quad);
            child.name = childName;
            child.transform.SetParent(transform, false);
            Object.Destroy(child.GetComponent<MeshCollider>());
            var mr = child.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = sortingOrder;
            return mr;
        }

        private MeshRenderer CreatePortDot(string childName, Material material)
        {
            var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            child.name = childName;
            child.transform.SetParent(transform, false);
            Object.Destroy(child.GetComponent<Collider>());
            var mr = child.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return mr;
        }

        private static void EnsureMaterials()
        {
            if (_bodyMat != null) return;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit");
            _bodyMat = new Material(shader) { color = new Color(0.18f, 0.28f, 0.42f) };
            _selectedMat = new Material(shader) { color = new Color(0.18f, 0.44f, 0.93f, 0.45f) };
            _posMat = new Material(shader) { color = new Color(0.83f, 0.18f, 0.18f) };
            _negMat = new Material(shader) { color = new Color(0.12f, 0.12f, 0.12f) };
        }

        public void Bind(SolarPanelInstance instance, SolarPanelDefinition definition)
        {
            InstanceId = instance.Id;
            DefinitionId = definition.Id;
            ApplyTransform(instance, definition);
            name = $"Panel_{definition.Manufacturer}_{definition.Model}_{instance.Id.ToString()[..8]}";
        }

        public void ApplyTransform(SolarPanelInstance instance, SolarPanelDefinition definition)
        {
            var size = WorldScale.PanelSizeMeters(definition, instance.RotationDegrees);
            var widthMm = size.x * WorldScale.MmPerMeter;
            var heightMm = size.y * WorldScale.MmPerMeter;
            var centerMmX = instance.PositionXMm + widthMm / 2.0;
            var centerMmY = instance.PositionYMm + heightMm / 2.0;

            transform.position = WorldScale.MmToWorld(centerMmX, centerMmY);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (_body != null)
                _body.transform.localScale = new Vector3(size.x, size.y, 1f);
            if (_selection != null)
                _selection.transform.localScale = new Vector3(size.x + 0.08f, size.y + 0.08f, 1f);

            if (_positivePort != null)
                _positivePort.localPosition = new Vector3(0f, size.y * 0.5f, -0.01f);
            if (_negativePort != null)
                _negativePort.localPosition = new Vector3(0f, -size.y * 0.5f, -0.01f);

            var col = GetComponent<BoxCollider2D>();
            if (col != null)
                col.size = size;
        }

        public void SetSelected(bool selected)
        {
            if (_selection != null)
                _selection.enabled = selected;
            if (_body != null && _bodyMat != null && _selectedMat != null)
                _body.sharedMaterial = selected ? _selectedMat : _bodyMat;
        }

        public void SetPortsVisible(bool visible)
        {
            if (_positivePort != null) _positivePort.gameObject.SetActive(visible);
            if (_negativePort != null) _negativePort.gameObject.SetActive(visible);
        }

        public Vector3 PositiveWorldPosition =>
            _positivePort != null ? _positivePort.position : transform.position + Vector3.up * 0.4f;

        public Vector3 NegativeWorldPosition =>
            _negativePort != null ? _negativePort.position : transform.position + Vector3.down * 0.4f;

        public Vector3 GetPortWorldPosition(bool positive) =>
            positive ? PositiveWorldPosition : NegativeWorldPosition;
    }
}
