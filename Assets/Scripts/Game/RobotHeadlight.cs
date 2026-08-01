#nullable enable

using Fodinae.Player.Logic;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Game
{
    public class RobotHeadlight : MonoBehaviour
    {
        [SerializeField, Min(0.1f)]
        private float _auraRadius = 12f;
        [SerializeField, Min(0f)]
        private float _intensity = 1f;
        [SerializeField, Range(0.5f, 10f), Tooltip("Virtual height above the terrain. Higher values produce shorter shadows.")]
        private float _auraHeight = 2.5f;

        private PlayerMovementController? _player;
        private bool _headlightEnabled = true;

        protected void Awake()
        {
            _player = GetComponent<PlayerMovementController>()
                ?? GetComponentInParent<PlayerMovementController>();
        }

        protected void OnDisable()
        {
            TerrariaLightingEngine.Instance?.DisablePlayerAura();
        }

        protected void OnDestroy()
        {
            TerrariaLightingEngine.Instance?.DisablePlayerAura();
        }

        protected void LateUpdate()
        {
            if (!_headlightEnabled)
            {
                return;
            }

            _player ??= PlayerMovementController.LocalPlayer;
            var lighting = TerrariaLightingEngine.Instance;
            if (_player == null || lighting == null)
            {
                lighting?.DisablePlayerAura();
                return;
            }

            lighting.SetPlayerAura(
                (Vector2)_player.transform.position,
                _auraRadius,
                _intensity,
                _auraHeight);
        }

        public void SetEnabled(bool enabled)
        {
            _headlightEnabled = enabled;
            if (!enabled)
            {
                TerrariaLightingEngine.Instance?.DisablePlayerAura();
            }
        }
    }
}
