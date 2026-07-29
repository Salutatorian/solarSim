using UnityEngine;

namespace SolarSim.Unity.Canvas
{
    /// <summary>
    /// Plays the GrabCAD MC4 male→female seating clip at a wire join point.
    /// Prefab is built via <c>solarSim → Setup MC4 Connection Prefab</c> after FBX import.
    /// </summary>
    public sealed class Mc4ConnectionPresenter : MonoBehaviour
    {
        public const string ResourcesPath = "SolarSim/MC4/MC4_Connection";
        public const string ConnectStateName = "MC4_Connect";

        [SerializeField] private Animator? animator;
        [SerializeField] private AnimationClip? connectClip;
        [SerializeField] private Transform? maleRoot;
        [SerializeField] private Transform? femaleRoot;
        [Tooltip("World scale for orthographic canvas join points.")]
        [SerializeField] private float displayScale = 0.45f;
        [SerializeField] private bool faceCamera = true;

        private bool _played;

        public bool HasPlayed => _played;

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            transform.localScale = Vector3.one * displayScale;
        }

        private void LateUpdate()
        {
            if (!faceCamera || Camera.main == null) return;
            // Keep the mechanical pair readable on the orthographic design canvas.
            var cam = Camera.main.transform;
            transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
        }

        /// <summary>Play the connect clip once (align → insert → click → seated).</summary>
        public void PlayConnect(bool forceRestart = false)
        {
            if (_played && !forceRestart) return;
            _played = true;

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = true;
                animator.Play(ConnectStateName, 0, 0f);
                return;
            }

            if (connectClip != null)
            {
                var anim = GetComponent<Animation>() ?? gameObject.AddComponent<Animation>();
                anim.playAutomatically = false;
                if (anim.GetClip(connectClip.name) == null)
                    anim.AddClip(connectClip, connectClip.name);
                anim.Play(connectClip.name);
            }
        }

        /// <summary>Jump to the seated pose without replaying the approach.</summary>
        public void SnapToSeated()
        {
            _played = true;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.enabled = true;
                animator.Play(ConnectStateName, 0, 1f);
                animator.Update(0f);
                return;
            }

            if (connectClip != null)
            {
                connectClip.SampleAnimation(gameObject, connectClip.length);
            }
        }

        public static Mc4ConnectionPresenter? TryCreate(Transform parent)
        {
            var prefab = Resources.Load<GameObject>(ResourcesPath);
            if (prefab == null) return null;

            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = "MC4_Connection";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            var presenter = instance.GetComponent<Mc4ConnectionPresenter>()
                            ?? instance.AddComponent<Mc4ConnectionPresenter>();
            return presenter;
        }
    }
}
