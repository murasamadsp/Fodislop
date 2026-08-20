#nullable enable

using System;
using System.Reflection;
using Fodinae.Game.Managers;
using Fodinae.Player.Logic;
using Fodinae.World.Terrain;
using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools
{
    /// <summary>
    /// Temporary probe: measures the offline (DummyConnection) descent timing.
    /// Enters play mode, clicks the menu play button through reflection, then
    /// logs when each loading milestone is reached relative to the click.
    /// Exits play mode automatically. Remove after diagnosis.
    /// </summary>
    public static class LoadTimingProbe
    {
        private const string MenuPath = "Fodinae/Art/Probe Offline Load Timing";

        private static double _clickTime;
        private static float _timeout;
        private static bool _clicked;
        private static bool _running;
        private static float _startedAtRealTime;

        private static double _teleportAt = -1;
        private static double _basketAt = -1;
        private static double _terrainAt = -1;
        private static double _worldAt = -1;
        private static double _loaderHiddenAt = -1;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (_running)
            {
                Debug.Log("[LoadTimingProbe] Already running.");
                return;
            }

            _running = true;
            _clicked = false;
            _timeout = 90f;
            _startedAtRealTime = Time.realtimeSinceStartup;

            _teleportAt = _basketAt = _terrainAt = _worldAt = _loaderHiddenAt = -1;

            Debug.Log("[LoadTimingProbe] ===== entering play mode =====");
            EditorApplication.isPlaying = true;
            EditorApplication.update += OnUpdate;
        }

        private static void OnUpdate()
        {
            if (!_running)
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                // Play mode ended before completion (error or user).
                Finish("play mode ended prematurely");
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _startedAtRealTime;

            // 1. Click the play button once the menu tree is up.
            if (!_clicked)
            {
                if (TryClickPlayButton())
                {
                    _clicked = true;
                    _clickTime = Time.realtimeSinceStartup;
                    Debug.Log($"[LoadTimingProbe] clicked Play at +{(_clickTime - _startedAtRealTime):F2}s");
                }
            }

            if (!_clicked)
            {
                if (elapsed > 15f)
                {
                    Finish("menu never appeared");
                    return;
                }

                return;
            }

            double now = Time.realtimeSinceStartup - _clickTime;

            PlayerMovementController? player = PlayerMovementController.LocalPlayer;
            if (_teleportAt < 0 && player != null && player.HasServerPosition)
            {
                _teleportAt = now;
                Debug.Log($"[LoadTimingProbe] teleport/position at +{now:F3}s");
            }

            // Basket timing is logged from runtime code ([Probe] Basket) because
            // PlayerStatsModel is not a UnityEngine.Object.
            TerrainRenderer? terrain = TerrainRenderer.Instance;
            if (_terrainAt < 0 && terrain != null && terrain.IsReadyForGameplay)
            {
                _terrainAt = now;
                Debug.Log($"[LoadTimingProbe] terrain ready at +{now:F3}s");
            }

            GameManager? gm = UnityEngine.Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (_worldAt < 0 && gm != null && gm.IsWorldLoaded)
            {
                _worldAt = now;
                Debug.Log($"[LoadTimingProbe] world loaded at +{now:F3}s");
            }

            if (_worldAt >= 0 && _loaderHiddenAt < 0)
            {
                // Give the menu one extra frame to hide the fullscreen layer.
                _loaderHiddenAt = now;
            }

            if (_worldAt >= 0)
            {
                Finish("done");
                return;
            }

            if (now > _timeout)
            {
                Finish("timeout 90s");
            }
        }

        private static bool TryClickPlayButton()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<Fodinae.MainMenu>(FindObjectsInactive.Include);
            if (menu == null)
            {
                return false;
            }

            // Give the UI a couple of frames to build before clicking.
            MethodInfo? method = typeof(Fodinae.MainMenu).GetMethod(
                "OnPlayButtonClicked",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                Debug.LogError("[LoadTimingProbe] OnPlayButtonClicked not found.");
                Finish("reflection failed");
                return false;
            }

            method.Invoke(menu, null);
            return true;
        }

        private static void Finish(string reason)
        {
            _running = false;
            EditorApplication.update -= OnUpdate;

            Debug.Log($"[LoadTimingProbe] ===== RESULT: {reason} =====");
            Debug.Log($"[LoadTimingProbe]   teleport: {Format(_teleportAt)}");
            Debug.Log($"[LoadTimingProbe]   basket:   {Format(_basketAt)}");
            Debug.Log($"[LoadTimingProbe]   terrain:  {Format(_terrainAt)}");
            Debug.Log($"[LoadTimingProbe]   world:    {Format(_worldAt)}");

            EditorApplication.isPlaying = false;
        }

        private static string Format(double v) => v < 0 ? "N/A" : $"+{v:F3}s";
    }
}
