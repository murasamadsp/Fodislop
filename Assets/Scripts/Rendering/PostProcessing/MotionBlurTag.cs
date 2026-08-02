#nullable enable

using System.Collections.Generic;
using Fodinae.Game;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing
{
    [DisallowMultipleComponent]
    public class MotionBlurTag : MonoBehaviour
    {
        private const float MaxTrackedFrameDisplacement = 2f;

        private static readonly List<MotionBlurTag> ActiveTagsCollection = new();
        public static IReadOnlyList<MotionBlurTag> ActiveTags => ActiveTagsCollection;

        public Vector3 PreviousFrameWorldPosition { get; private set; }
        public Vector2 Velocity { get; private set; }

        public Robot? CachedRobot { get; private set; }
        public SpriteRenderer? CachedSpriteRenderer { get; private set; }

        private void Awake()
        {
            CachedRobot = GetComponent<Robot>();
            CachedSpriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            PreviousFrameWorldPosition = transform.position;
            Velocity = Vector2.zero;
            if (!ActiveTagsCollection.Contains(this))
            {
                ActiveTagsCollection.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveTagsCollection.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveTagsCollection.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ActiveTagsCollection.Clear();
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
