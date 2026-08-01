#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing
{

    [DisallowMultipleComponent]
    public class MotionBlurTag : MonoBehaviour
    {
        private const float MaxTrackedFrameDisplacement = 2f;

        private static readonly List<MotionBlurTag> s_ActiveTags = new();
        public static IReadOnlyList<MotionBlurTag> ActiveTags => s_ActiveTags;

        public Vector3 PreviousFrameWorldPosition { get; private set; }
        public Vector2 Velocity { get; private set; }

        private void OnEnable()
        {
            PreviousFrameWorldPosition = transform.position;
            Velocity = Vector2.zero;
            if (!s_ActiveTags.Contains(this))
            {
                s_ActiveTags.Add(this);
            }
        }

        private void OnDisable()
        {
            s_ActiveTags.Remove(this);
        }

        private void OnDestroy()
        {
            s_ActiveTags.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_ActiveTags.Clear();
        }

        private void LateUpdate()
        {
            Vector3 currentPos = transform.position;
            Vector2 displacement = currentPos - PreviousFrameWorldPosition;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Velocity = displacement.sqrMagnitude <= MaxTrackedFrameDisplacement * MaxTrackedFrameDisplacement
                ? displacement / dt
                : Vector2.zero;
            PreviousFrameWorldPosition = currentPos;
        }
    }
}
