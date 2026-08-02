#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.World;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class MissionArrowUI : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _arrow;
        private Camera? _camera;
        private ushort? _targetX;
        private ushort? _targetY;
        [Inject]
        private IPlayerStats _playerStats = null!;

        protected void Start()
        {
            _camera = Camera.main;

            _arrow = new VisualElement();
            _arrow.name = "MissionArrow";
            _arrow.AddToClassList("mission-arrow");

            // Видимость — рантайм-состояние
            _arrow.style.display = DisplayStyle.None;
            _doc.rootVisualElement.Add(_arrow);

            var stats = _playerStats as PlayerStatsModel;
            if (stats != null)
            {
                stats.OnMissionArrowChanged += OnArrowChanged;
                if (stats.MissionArrowX.HasValue && stats.MissionArrowY.HasValue)
                {
                    Debug.Log($"[MissionArrowUI] Initial arrow target: ({stats.MissionArrowX}, {stats.MissionArrowY})");
                    _targetX = stats.MissionArrowX;
                    _targetY = stats.MissionArrowY;
                    _arrow.style.display = DisplayStyle.Flex;
                }
            }
        }

        protected void OnDestroy()
        {
            var existing = _playerStats as PlayerStatsModel;
            if (existing != null)
            {
                existing.OnMissionArrowChanged -= OnArrowChanged;
            }
        }

        private void OnArrowChanged()
        {
            Debug.Log("[MissionArrowUI] OnArrowChanged fired");
            var stats = ServiceLocator.Resolve<IPlayerStats>() as PlayerStatsModel;
            if (stats == null || !stats.MissionArrowX.HasValue || !stats.MissionArrowY.HasValue)
            {
                Debug.Log("[MissionArrowUI] Arrow cleared (null target)");
                _targetX = null;
                _targetY = null;
                if (_arrow != null)
                {
                    _arrow.style.display = DisplayStyle.None;
                }

                return;
            }

            _targetX = stats.MissionArrowX;
            _targetY = stats.MissionArrowY;
            Debug.Log($"[MissionArrowUI] Arrow target set: ({_targetX}, {_targetY}), showing element");
            if (_arrow != null)
            {
                _arrow.style.display = DisplayStyle.Flex;
            }
        }

        protected void LateUpdate()
        {
            if (!_targetX.HasValue || !_targetY.HasValue || _camera == null)
            {
                return;
            }

            var worldPos = CoordinateUtils.ServerToUnityPos(_targetX.Value, _targetY.Value);
            var screenPos = _camera.WorldToScreenPoint(worldPos);

            if (_doc == null || _doc.rootVisualElement == null || _doc.rootVisualElement.panel == null || _arrow == null)
            {
                return;
            }

            if (screenPos.z < 0f)
            {
                if (_arrow.style.display != DisplayStyle.None)
                {
                    _arrow!.style.display = DisplayStyle.None;
                }

                return;
            }

            if (_arrow.style.display != DisplayStyle.Flex)
            {
                _arrow.style.display = DisplayStyle.Flex;
            }

            var panelPos = RuntimePanelUtils.ScreenToPanel(
                _doc.rootVisualElement.panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));

            float halfW = _doc.rootVisualElement.resolvedStyle.width / 2f;
            float halfH = _doc.rootVisualElement.resolvedStyle.height / 2f;

            float posX = panelPos.x - (_arrow.resolvedStyle.width / 2f);
            float posY = panelPos.y - (_arrow.resolvedStyle.height / 2f);

            float maxX = _doc.rootVisualElement.resolvedStyle.width - _arrow.resolvedStyle.width;
            float maxY = _doc.rootVisualElement.resolvedStyle.height - _arrow.resolvedStyle.height;

            bool offScreen = posX < 0 || posX > maxX || posY < 0 || posY > maxY;

            if (offScreen)
            {
                var dir = new Vector2(panelPos.x - halfW, panelPos.y - halfH);
                if (dir.magnitude < 0.001f)
                {
                    dir = Vector2.up;
                }

                dir.Normalize();

                const float margin = 40f;
                float clampedX = Mathf.Clamp(panelPos.x, margin, _doc.rootVisualElement.resolvedStyle.width - margin) - (_arrow.resolvedStyle.width / 2f);
                float clampedY = Mathf.Clamp(panelPos.y, margin, _doc.rootVisualElement.resolvedStyle.height - margin) - (_arrow.resolvedStyle.height / 2f);

                _arrow.style.left = clampedX;
                _arrow.style.top = clampedY;

                float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                _arrow.style.rotate = new Rotate(Angle.Degrees(targetAngle - 45f));
            }
            else
            {
                _arrow.style.left = posX;
                _arrow.style.top = posY;
                _arrow.style.rotate = new Rotate(Angle.Degrees(45f));
            }
        }
    }
}
