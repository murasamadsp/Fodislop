#nullable enable

using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Облако локального сообщения над роботом.
    /// </summary>
    /// <remarks>
    /// Поведение снято с эталона (`МиныЧячина/Assets/Scripts/LocalChatMessages.cs`)
    /// и повторено по числам, а не по общему смыслу:
    ///
    /// — живёт три секунды и исчезает разом, без затухания;
    /// — не всплывает вверх, а ДОГОНЯЕТ робота с запаздыванием: каждый кадр
    ///   позиция считается как 0.3 от цели плюс 0.7 от текущей. Отсюда мягкое
    ///   отставание облака от рывков движения — оно и читается как облако, а не
    ///   как жёстко приклеенная табличка;
    /// — цель смещена на полклетки влево, тоже как в эталоне.
    ///
    /// Секунды считаются по <c>unscaledTime</c>: облако живёт своё время
    /// независимо от замедления игры.
    /// </remarks>
    public class FloatingChatBubble : MonoBehaviour
    {
        private const float Lifetime = 3f;
        private const float FollowWeight = 0.3f;
        private const float TargetOffsetX = -0.5f;

        [Inject]
        private IWorldLabels _labels = null!;

        private IWorldLabel? _label;
        private Transform? _target;
        private float _expiresAt;

        /// <summary>Кому принадлежит облако. Одно на робота.</summary>
        public int OwnerId { get; private set; }

        public void Init(int ownerId, string text, Transform target)
        {
            OwnerId = ownerId;
            _target = target;
            _expiresAt = Time.unscaledTime + Lifetime;

            _label ??= _labels.Create(chatBubble: true);
            _label.SetText(text);
            _label.SetOpacity(1f);

            // Первый кадр ставится точно в цель, без сглаживания: иначе облако
            // прилетало бы к роботу из точки, где висело прошлое сообщение.
            transform.position = ResolveTargetPosition();
            _label.SetPosition(transform.position);
            _label.SetVisible(true);
            gameObject.SetActive(true);
        }

        public void Expire() => gameObject.SetActive(false);

        protected void Update()
        {
            Vector3 target = ResolveTargetPosition();
            transform.position =
                (FollowWeight * target) + ((1f - FollowWeight) * transform.position);
            _label?.SetPosition(transform.position);

            if (Time.unscaledTime > _expiresAt)
            {
                gameObject.SetActive(false);
            }
        }

        private Vector3 ResolveTargetPosition()
        {
            // Робот мог исчезнуть, пока облако живёт: тогда оно доживает свои
            // секунды там, где остановилось, а не прыгает в начало координат.
            Vector3 position = _target != null ? _target.position : transform.position;
            position.x += _target != null ? TargetOffsetX : 0f;
            return position;
        }

        protected void OnDisable()
        {
            _target = null;
            _label?.SetVisible(false);
        }

        protected void OnDestroy()
        {
            _label?.Dispose();
            _label = null;
        }
    }
}
