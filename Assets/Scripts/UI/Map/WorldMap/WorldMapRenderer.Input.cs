#nullable enable

using System;
using UnityEngine.UIElements;

namespace Kern.UI
{
    internal sealed class WorldMapInputDispatcher
    {
        private readonly MapInteractionController _interaction;
        private readonly WorldMapPanel _panel;
        private readonly MapTextureController _textureController;
        private readonly Action _clampViewCenter;
        private readonly Action _requestRender;
        private readonly MapPinchGesture _pinch = new();

        public WorldMapInputDispatcher(
            MapInteractionController interaction,
            WorldMapPanel panel,
            MapTextureController textureController,
            Action clampViewCenter,
            Action requestRender)
        {
            _interaction = interaction;
            _panel = panel;
            _textureController = textureController;
            _clampViewCenter = clampViewCenter;
            _requestRender = requestRender;
        }

        public void HandleWheel(
            WheelEvent evt,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            ref float viewCenterX,
            ref float viewCenterY)
        {
            bool renderRequested = false;
            _interaction.HandleMouseScroll(
                _panel.Overlay,
                _panel.Image,
                evt.delta.y,
                evt.mousePosition,
                _textureController.TexWidth,
                _textureController.TexHeight,
                maxCellsPerPixel,
                ref cellsPerPixel,
                ref viewCenterX,
                ref viewCenterY,
                ref renderRequested,
                _clampViewCenter);

            if (renderRequested)
            {
                _requestRender();
            }
        }

        public void HandlePointerDown(PointerDownEvent evt)
        {
            if (_panel.Image != null && _pinch.Down(evt, _panel.Image))
            {
                _interaction.CancelDragForPinch();
                evt.StopPropagation();
                return;
            }

            _interaction.HandlePointerDown(evt, _panel.Image);
        }

        public void HandlePointerMove(
            PointerMoveEvent evt,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            float dragSpeed,
            ref float viewCenterX,
            ref float viewCenterY,
            ref bool followPlayer)
        {
            bool renderRequested = false;
            if (_pinch.Move(evt, out float wheelDelta, out UnityEngine.Vector2 center) && _pinch.IsPinching)
            {
                _interaction.HandleMouseScroll(
                    _panel.Overlay,
                    _panel.Image,
                    wheelDelta,
                    center,
                    _textureController.TexWidth,
                    _textureController.TexHeight,
                    maxCellsPerPixel,
                    ref cellsPerPixel,
                    ref viewCenterX,
                    ref viewCenterY,
                    ref renderRequested,
                    _clampViewCenter);
                followPlayer = false;
                if (renderRequested)
                {
                    _requestRender();
                }

                evt.StopPropagation();
                return;
            }

            _interaction.HandlePointerMove(
                evt,
                _panel.Image,
                _textureController.TexWidth,
                _textureController.TexHeight,
                cellsPerPixel,
                dragSpeed,
                ref viewCenterX,
                ref viewCenterY,
                ref followPlayer,
                ref renderRequested,
                _clampViewCenter);

            if (renderRequested)
            {
                _requestRender();
            }
        }

        public void HandlePointerUp(PointerUpEvent evt)
        {
            _pinch.Up(evt, _panel.Image);
            _interaction.HandlePointerUp(evt, _panel.Image);
        }
    }
}
