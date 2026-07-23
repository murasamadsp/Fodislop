using Fodinae.Scripts.Player.Logic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game
{
    public class RobotHeadlight : MonoBehaviour
    {
        [SerializeField]
        private float _outerAngle = 60f;
        [SerializeField]
        private float _range = 25f;
        [SerializeField]
        private float _intensity = 1.0f;

        private float _angleCos;
        private bool _isEnabled = true;

        protected void Awake()
        {
            _angleCos = Mathf.Cos(_outerAngle * 0.5f * Mathf.Deg2Rad);
        }

        protected void OnEnable()
        {
            _isEnabled = true;
        }

        protected void OnDisable()
        {
            _isEnabled = false;
            Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
        }

        protected void OnDestroy()
        {
            Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
        }

        protected void LateUpdate()
        {
            if (!_isEnabled)
            {
                return;
            }

            Vector2 dir;
            Vector2 pos;

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                dir = player.LastDirection switch
                {
                    Direction.Up => Vector2.up,
                    Direction.Right => Vector2.right,
                    Direction.Down => Vector2.down,
                    Direction.Left => Vector2.left,
                    _ => Vector2.up
                };
                pos = (Vector2)player.transform.position + (dir * 0.5f);
            }
            else
            {
                float angleRad = transform.eulerAngles.z * Mathf.Deg2Rad;
                dir = new Vector2(-Mathf.Sin(angleRad), Mathf.Cos(angleRad));
                pos = (Vector2)transform.position + (dir * 0.5f);
            }

            Shader.SetGlobalVector("_HeadlightPos", pos);
            Shader.SetGlobalVector("_HeadlightDir", dir);
            Shader.SetGlobalFloat("_HeadlightAngleCos", _angleCos);
            Shader.SetGlobalFloat("_HeadlightRange", _range);
            Shader.SetGlobalFloat("_HeadlightIntensity", _intensity);
        }

        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled)
            {
                Shader.SetGlobalFloat("_HeadlightIntensity", 0f);
            }
        }
    }
}
