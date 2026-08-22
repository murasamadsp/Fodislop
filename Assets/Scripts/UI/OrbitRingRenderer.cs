#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    // A real 3D orbit ring (LineRenderer loop) rendered by the menu scenery
    // camera alongside the planet, so it wraps around the sphere with proper
    // depth occlusion (the far side of the loop passes behind the planet)
    // instead of a flat 2D ellipse drawn on top of everything.
    [RequireComponent(typeof(LineRenderer))]
    [ExecuteAlways]
    public class OrbitRingRenderer : MonoBehaviour
    {
        [SerializeField]
        private Transform? _center;
        [SerializeField]
        private float _radius = 4f;
        [SerializeField]
        private Vector3 _orbitPlaneEulerAngles = new(70f, 0f, -22f);
        [SerializeField]
        private int _segments = 128;
        [SerializeField]
        private float _lineWidth = 0.03f;

        private LineRenderer? _line;
        private Vector3 _lastCenterPosition;
        private bool _built;

        private void OnEnable()
        {
            _line = GetComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = true;
            _built = false;
            Rebuild();
        }

        private void LateUpdate()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            if (_center == null || _line == null)
            {
                return;
            }

            // Segment count / width can be reconfigured (e.g. by an editor
            // build script) after OnEnable already ran, so keep them in sync
            // here rather than caching them once.
            if (_line.positionCount != _segments)
            {
                _line.positionCount = _segments;
                _built = false;
            }

            _line.widthMultiplier = _lineWidth;

            if (_built && _center.position == _lastCenterPosition)
            {
                return;
            }

            Quaternion orbitPlane = Quaternion.Euler(_orbitPlaneEulerAngles);
            for (int i = 0; i < _segments; i++)
            {
                float t = (float)i / _segments * Mathf.PI * 2f;
                var localOffset = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * _radius;
                _line.SetPosition(i, _center.position + (orbitPlane * localOffset));
            }

            _lastCenterPosition = _center.position;
            _built = true;
        }
    }
}
