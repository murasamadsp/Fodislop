#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        [Inject]
        private IWorldLabels _labels = null!;

        private IWorldLabel? _label;
        private float _elapsed;
        private const float Duration = 5f;
        private const float FloatSpeed = 0.3f;
        private const float FadeStart = 4f;

        public void Init(string text)
        {
            _elapsed = 0f;
            _label ??= _labels.Create(chatBubble: true);
            _label.SetText(text);
            _label.SetPosition(transform.position);
            _label.SetOpacity(1f);
            _label.SetVisible(true);
            gameObject.SetActive(true);
        }

        protected void Update()
        {
            _elapsed += Time.deltaTime;
            transform.Translate(0, FloatSpeed * Time.deltaTime, 0);
            _label?.SetPosition(transform.position);
            if (_elapsed >= FadeStart)
            {
                _label?.SetOpacity(1f - (_elapsed - FadeStart) / (Duration - FadeStart));
            }

            if (_elapsed >= Duration)
            {
                gameObject.SetActive(false);
            }
        }

        protected void OnDisable()
        {
            _elapsed = 0f;
            _label?.SetVisible(false);
        }

        protected void OnDestroy()
        {
            _label?.Dispose();
            _label = null;
        }
    }
}
