#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Fodinae.UI
{
    public class FloatingChatManager : MonoBehaviour
    {
        [Inject]
        private RobotManager _robotManager = null!;

        private Camera? _camera;
        private FloatingChatBubble? _bubblePrefab;
        private readonly List<FloatingChatBubble> _activeBubbles = new();
        private readonly Queue<FloatingChatBubble> _pool = new();
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ChatEventGateway _chatEvents = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        protected void Start()
        {
            _chatEvents.LocalMessageReceived += ShowLocalChat;
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_camera == null)
            {
                _camera = _gameplayCamera?.Camera;
            }

            if (_bubblePrefab != null || _sceneObjects == null)
            {
                return;
            }

            _bubblePrefab = _sceneObjects.Create<FloatingChatBubble>("ChatBubblePrefab", RuntimeOwner.FloatingUI);
        }

        protected void Update()
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (_activeBubbles[i] == null || !_activeBubbles[i].gameObject.activeInHierarchy)
                {
                    ReturnToPool(_activeBubbles[i]);
                    _activeBubbles.RemoveAt(i);
                }
            }
        }

        protected void OnDestroy()
        {
            if (_chatEvents != null)
            {
                _chatEvents.LocalMessageReceived -= ShowLocalChat;
            }

            _activeBubbles.Clear();
            while (_pool.Count > 0)
            {
                var bubble = _pool.Dequeue();
                if (bubble != null)
                {
                    Destroy(bubble.gameObject);
                }
            }
        }

        public void ShowLocalChat(LocalChatMessagePacket packet)
        {
            TryInitialize();
            if (_camera == null)
            {
                _camera = _gameplayCamera?.Camera;
            }

            var robot = _robotManager?.GetOrCreateRobot(packet.BotId);
            if (robot == null)
            {
                return;
            }

            if (!IsInCameraView(robot.transform.position))
            {
                return;
            }

            var bubble = GetFromPool();
            if (bubble == null)
            {
                return;
            }

            bubble.transform.position = robot.transform.position + (Vector3.up * 1.8f);
            bubble.Init(packet.Text);
            _activeBubbles.Add(bubble);
        }

        private FloatingChatBubble? GetFromPool()
        {
            while (_pool.Count > 0)
            {
                var bubble = _pool.Dequeue();
                if (bubble != null)
                {
                    bubble.gameObject.SetActive(true);
                    return bubble;
                }
            }

            if (_bubblePrefab == null)
            {
                return null;
            }

            var go = Instantiate(_bubblePrefab.gameObject, transform);
            var newBubble = go.GetComponent<FloatingChatBubble>();
            return newBubble;
        }

        private void ReturnToPool(FloatingChatBubble? bubble)
        {
            if (bubble == null)
            {
                return;
            }

            bubble.gameObject.SetActive(false);
            _pool.Enqueue(bubble);
        }

        private bool IsInCameraView(Vector3 worldPos)
        {
            if (_camera == null)
            {
                return false;
            }

            Vector3 vp = _camera.WorldToViewportPoint(worldPos);
            return vp.x >= -0.15f && vp.x <= 1.15f && vp.y >= -0.15f && vp.y <= 1.15f;
        }
    }
}
