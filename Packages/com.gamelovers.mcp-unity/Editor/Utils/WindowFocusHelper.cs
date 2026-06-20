using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace McpUnity.Utils
{
    /// <summary>
    /// Platform-specific helper to bring the Unity Editor window to the foreground.
    /// 
    /// When Unity loses focus (minimized/background), its main thread is throttled
    /// and EditorApplication.delayCall stops executing. MCP requests timeout because
    /// they're queued via delayCall but never processed.
    /// 
    /// This helper is called from the WebSocket background thread (OnMessage)
    /// BEFORE the delayCall, ensuring Unity is in the foreground and processing
    /// main thread work by the the delayCall fires.
    /// 
    /// Windows: P/Invoke SetForegroundWindow + AttachThreadInput workaround
    /// macOS:   P/Invoke NSApplicationActivateIgnoringOtherApps
    /// </summary>
    public static class WindowFocusHelper
    {
        private static IntPtr _unityWindowHandle = IntPtr.Zero;
        private static bool _initialized = false;

        /// <summary>
        /// Must be called from the Unity main thread during initialization
        /// (e.g., McpUnityServer constructor or OnEnable).
        /// Caches the Unity Editor window handle for later use.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

#if UNITY_EDITOR_WIN
            _unityWindowHandle = GetActiveWindow();
            McpLogger.LogInfo($"[MCP Unity] WindowFocusHelper initialized (handle: {_unityWindowHandle})");
#elif UNITY_EDITOR_OSX
            McpLogger.LogInfo("[MCP Unity] WindowFocusHelper initialized (macOS)");
#else
            McpLogger.LogWarning("[MCP Unity] WindowFocusHelper: unsupported platform, auto-focus disabled");
#endif
        }

        /// <summary>
        /// Bring the Unity Editor window to the foreground.
        /// Safe to call from background threads:
        ///   Windows - SetForegroundWindow is a Win32 API call, no main thread needed
        ///   macOS   - NSApplicationActivateIgnoringOtherApps is a C function call
        /// </summary>
        public static void BringToForeground()
        {
#if UNITY_EDITOR_WIN
            if (_unityWindowHandle == IntPtr.Zero)
                return;

            // Standard workaround for SetForegroundWindow restrictions:
            // Attach the calling thread to the foreground window's input queue
            IntPtr foregroundHandle = GetForegroundWindow();

            if (foregroundHandle != _unityWindowHandle)
            {
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundHandle, IntPtr.Zero);
                uint unityThreadId = GetWindowThreadProcessId(_unityWindowHandle, IntPtr.Zero);

                // Restore window if minimized
                ShowWindow(_unityWindowHandle, SW_RESTORE);

                // Attach threads to bypass foreground lock
                if (foregroundThreadId != unityThreadId)
                {
                    AttachThreadInput(unityThreadId, foregroundThreadId, true);
                    SetForegroundWindow(_unityWindowHandle);
                    AttachThreadInput(unityThreadId, foregroundThreadId, false);
                }
                else
                {
                    SetForegroundWindow(_unityWindowHandle);
                }
            }
#elif UNITY_EDITOR_OSX
            // NSApplicationActivateIgnoringOtherApps queues activation on the main
            // event loop, so it's safe to call from any thread.
            try
            {
                NSApplicationActivateIgnoringOtherApps(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MCP Unity] WindowFocusHelper: failed to activate macOS app: {ex.Message}");
            }
#endif
        }

        // --- Windows P/Invoke ---
#if UNITY_EDITOR_WIN
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif

        // --- macOS P/Invoke ---
#if UNITY_EDITOR_OSX
        [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
        private static extern void NSApplicationActivateIgnoringOtherApps(bool flag);
#endif
    }
}
