using UnityEngine;

namespace SolarSim.Unity.Canvas
{
    /// <summary>Simple dual-tone series wire between two world points.</summary>
    public sealed class WireView : MonoBehaviour
    {
        private LineRenderer? _leadA;
        private LineRenderer? _leadB;
        private SpriteRenderer? _plug;
        private Transform? _plugAnchor;
        private Mc4ConnectionPresenter? _mc4;

        public System.Guid ConnectionId { get; private set; }

        public static WireView Create(Transform parent, System.Guid connectionId)
        {
            var go = new GameObject($"Wire_{connectionId.ToString()[..8]}");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<WireView>();
            view.ConnectionId = connectionId;
            view.Build();
            return view;
        }

        private void Build()
        {
            _leadA = CreateLead("LeadA");
            _leadB = CreateLead("LeadB");
            var plugGo = new GameObject("Plug");
            plugGo.transform.SetParent(transform, false);
            _plugAnchor = plugGo.transform;
            _plug = plugGo.AddComponent<SpriteRenderer>();
            _plug.sprite = WhiteSprite;
            _plug.color = new Color(0.07f, 0.07f, 0.07f);
            _plug.sortingOrder = 5;
            plugGo.transform.localScale = new Vector3(0.07f, 0.07f, 1f);

            // Prefer the GrabCAD MC4 prefab when the Art pipeline has been set up.
            _mc4 = Mc4ConnectionPresenter.TryCreate(_plugAnchor);
            if (_mc4 != null)
            {
                _plug.enabled = false;
                _mc4.PlayConnect();
            }
        }

        private LineRenderer CreateLead(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var lr = child.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.025f;
            lr.endWidth = 0.025f;
            lr.numCapVertices = 4;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.sortingOrder = 4;
            return lr;
        }

        private static Sprite? _whiteSprite;
        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite != null) return _whiteSprite;
                var tex = Texture2D.whiteTexture;
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                return _whiteSprite;
            }
        }

        public void SetEndpoints(Vector3 from, Vector3 to, Color fromColor, Color toColor, bool selected)
        {
            var mid = new Vector3((from.x + to.x) * 0.5f, Mathf.Min(from.y, to.y) - 0.25f, 0f);
            if (_leadA != null)
            {
                _leadA.startColor = _leadA.endColor = fromColor;
                _leadA.startWidth = _leadA.endWidth = selected ? 0.04f : 0.025f;
                _leadA.SetPosition(0, from);
                _leadA.SetPosition(1, mid);
            }
            if (_leadB != null)
            {
                _leadB.startColor = _leadB.endColor = toColor;
                _leadB.startWidth = _leadB.endWidth = selected ? 0.04f : 0.025f;
                _leadB.SetPosition(0, to);
                _leadB.SetPosition(1, mid);
            }
            if (_plugAnchor != null)
                _plugAnchor.position = mid;
        }
    }
}
