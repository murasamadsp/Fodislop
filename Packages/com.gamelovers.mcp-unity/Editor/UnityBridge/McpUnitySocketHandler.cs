using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using McpUnity.Tools;
using McpUnity.Resources;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using System.Collections.Specialized;
using McpUnity.Utils;
using System.Threading;

namespace McpUnity.Unity
{
    /// <summary>
    /// WebSocket handler for MCP Unity communications
    /// </summary>
    public class McpUnitySocketHandler : WebSocketBehavior
    {
        private readonly McpUnityServer _server;
        private readonly int _connectionGeneration;

        // Thread-safe queue for dispatching WebSocket messages onto Unity's main thread.
        // EditorApplication.delayCall is NOT used because it stops executing when Unity
        // loses focus (minimized/background). EditorApplication.update still fires at
        // ~4 fps even in the background, ensuring queued actions are processed.
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private static int _processingSubscribed = 0;

        /// <summary>
        /// Creates a WebSocket handler for the active server generation.
        /// </summary>
        public McpUnitySocketHandler(McpUnityServer server, int connectionGeneration)
        {
            _server = server;
            _connectionGeneration = connectionGeneration;
        }

        /// <summary>
        /// Create a standardized error response
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="errorType">Type of error</param>
        /// <returns>A JObject containing the error information</returns>
        public static JObject CreateErrorResponse(string message, string errorType)
        {
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };
        }
        
        /// <summary>
        /// Handle incoming messages from WebSocket clients.
        /// WebSocketSharp invokes this on a background thread; we marshal the entire
        /// message-handling body onto Unity's main thread via a ConcurrentQueue drained
        /// by EditorApplication.update, instead of EditorApplication.delayCall.
        ///
        /// Why delayCall was replaced: delayCall stops executing when Unity loses focus
        /// (minimized/background). EditorApplication.update still fires at ~4 fps even
        /// in the background, so queued actions are always processed promptly.
        ///
        /// Why this matters (original concern): accessing EditorStyles or scheduling
        /// EditorCoroutines from a background thread can NRE inside
        /// PropertyEditor+Styles..cctor, which under CLR rules permanently bricks that
        /// type for the rest of the AppDomain and turns the Inspector black until
        /// Unity is restarted.
        /// </summary>
        protected override void OnMessage(MessageEventArgs e)
        {
            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Auto-focus: bring Unity to foreground if it's unfocused.
            // This fires before the queue dispatch so Unity's main thread is running
            // at full speed by the time the action is processed.
            // The P/Invoke runs on the WebSocket background thread (safe on Win/macOS).
            if (McpUnitySettings.Instance.AutoBringToForeground)
            {
                WindowFocusHelper.BringToForeground();
            }

            string data = e.Data;
            EnsureProcessingSubscribed();
            _mainThreadQueue.Enqueue(() => HandleMessageAsync(data));
        }

        /// <summary>
        /// Subscribe to EditorApplication.update (once, thread-safe) to begin draining
        /// the main-thread action queue.
        /// </summary>
        private static void EnsureProcessingSubscribed()
        {
            if (Interlocked.Exchange(ref _processingSubscribed, 1) == 0)
            {
                EditorApplication.update += ProcessMainThreadQueue;
            }
        }

        /// <summary>
        /// Drain the main-thread action queue. Runs on Unity's main thread via
        /// EditorApplication.update. Empty dequeues are virtually free, so this
        /// handler stays subscribed for the lifetime of the AppDomain.
        /// </summary>
        private static void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    McpLogger.LogError($"[MCP Unity] Error in queued main thread action: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle WebSocket connection open.
        /// Supports multiple concurrent MCP clients (e.g. multiple Claude Code instances).
        /// Cleans up only inactive (dead) sessions to prevent file descriptor accumulation
        /// while keeping other active clients connected.
        /// websocket-sharp uses Mono's IOSelector/select(), which can crash when FD
        /// values exceed ~1024, so stale session cleanup is important.
        /// See: https://github.com/CoderGamester/mcp-unity/issues/110
        /// </summary>
        protected override void OnOpen()
        {
            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Clean up inactive (dead) sessions to prevent file descriptor accumulation.
            // Only removes sessions that are no longer connected — active clients are preserved.
            // Note: Do NOT use ActiveIDs here — it pings every client and blocks.
            var inactiveIds = Sessions.InactiveIDs.ToList();
            if (inactiveIds.Count > 0)
            {
                foreach (var oldId in inactiveIds)
                {
                    // Also remove from our tracking dictionary
                    _server.Clients.TryRemove(oldId, out _);
                    try
                    {
                        Sessions.CloseSession(oldId, CloseStatusCode.Normal, "Stale session cleanup");
                    }
                    catch (Exception ex)
                    {
                        McpLogger.LogWarning($"Error closing stale session {oldId}: {ex.Message}");
                    }
                }
                McpLogger.LogInfo($"Cleaned up {inactiveIds.Count} inactive session(s)");
            }

            // Extract client name from the X-Client-Name header (if available)
            string clientName = "";
            NameValueCollection headers = Context.Headers;
            if (headers != null && headers.Contains("X-Client-Name"))
            {
                clientName = headers["X-Client-Name"];
            }

            if (!_server.ShouldTrackClient(_connectionGeneration))
            {
                CloseUntrackedConnection();
                return;
            }

            // Add the client to the server's tracking dictionary
            _server.Clients[ID] = clientName;

            McpLogger.LogInfo($"WebSocket client connected (ID: {ID}, Name: {(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)}, Total clients: {_server.Clients.Count})");
        }

        /// <summary>
        /// Handle WebSocket connection close
        /// </summary>
        protected override void OnClose(CloseEventArgs e)
        {
            _server.Clients.TryGetValue(ID, out string clientName);

            // Remove the client from the server
            _server.Clients.TryRemove(ID, out _);

            McpLogger.LogInfo($"WebSocket client '{clientName}' disconnected: {e.Reason} (Remaining clients: {_server.Clients.Count})");
        }

        /// <summary>
        /// Handle WebSocket errors
        /// </summary>
        protected override void OnError(ErrorEventArgs e)
        {
            McpLogger.LogError($"WebSocket error: {e.Message}");
        }

        /// <summary>
        /// Process a WebSocket message on the Unity main thread.
        /// Safe to call EditorCoroutineUtility, Selection, and other Editor APIs from here.
        /// </summary>
        private async void HandleMessageAsync(string data)
        {
            try
            {
                if (!_server.ShouldTrackClient(_connectionGeneration))
                {
                    CloseUntrackedConnection();
                    return;
                }

                McpLogger.LogInfo($"WebSocket message received: {data}");
                JObject requestJson;
                try
                {
                    requestJson = JObject.Parse(data);
                }
                catch (JsonReaderException jre)
                {
                    McpLogger.LogError($"Invalid JSON received: {jre.Message}. Data: {data}");
                    // Attempt to send a parse error response. No requestId is available yet.
                    Send(CreateResponse(null, CreateErrorResponse($"Invalid JSON format: {jre.Message}", "invalid_json")).ToString(Formatting.None));
                    return;
                }

                var method = requestJson["method"]?.ToString();
                var parameters = requestJson["params"] as JObject ?? new JObject();
                var requestId = requestJson["id"]?.ToString();
                // We need to dispatch to Unity's main thread and wait for completion
                var tcs = new TaskCompletionSource<JObject>();

                if (string.IsNullOrEmpty(method))
                {
                    tcs.SetResult(CreateErrorResponse("Missing method in request", "invalid_request"));
                }
                else if (_server.TryGetTool(method, out var tool))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
                }
                else if (_server.TryGetResource(method, out var resource))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResourceCoroutine(resource, parameters, tcs));
                }
                else
                {
                    tcs.SetResult(CreateErrorResponse($"Unknown method: {method}", "unknown_method"));
                }

                JObject responseJson = await tcs.Task;
                JObject jsonRpcResponse = CreateResponse(requestId, responseJson);
                string responseStr = jsonRpcResponse.ToString(Formatting.None);

                McpLogger.LogInfo($"WebSocket message response for request ID '{requestId}': {responseStr}");

                // Send the response back to the client
                Send(responseStr);
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error processing message: {ex.Message}");

                Send(CreateErrorResponse($"Internal server error: {ex.Message}", "internal_error").ToString(Formatting.None));
            }
        }

        private void CloseUntrackedConnection()
        {
            try
            {
                WebSocket webSocket = Context?.WebSocket;
                if (webSocket?.ReadyState == WebSocketState.Open)
                {
                    webSocket.Close(CloseStatusCode.Away, "Server is restarting");
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogWarning($"Error closing untracked WebSocket connection: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Execute a tool with the provided parameters
        /// </summary>
        private IEnumerator ExecuteTool(McpToolBase tool, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (tool.IsAsync)
                {
                    tool.ExecuteAsync(parameters, tcs);
                }
                else
                {
                    var result = tool.Execute(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error executing tool {tool.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to execute tool {tool.Name}: {ex.Message}",
                    "tool_execution_error"
                ));
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Fetch a resource with the provided parameters
        /// </summary>
        private IEnumerator FetchResourceCoroutine(McpResourceBase resource, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (resource.IsAsync)
                {
                    resource.FetchAsync(parameters, tcs);
                }
                else
                {
                    var result = resource.Fetch(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error fetching resource {resource.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to fetch resource {resource.Name}: {ex.Message}",
                    "resource_fetch_error"
                ));
            }
            yield return null;
        }
        
        /// <summary>
        /// Create a JSON-RPC 2.0 response
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <param name="result">Result object</param>
        /// <returns>JSON-RPC 2.0 response</returns>
        private JObject CreateResponse(string requestId, JObject result)
        {
            // Format as JSON-RPC 2.0 response
            JObject jsonRpcResponse = new JObject
            {
                ["id"] = requestId
            };
            
            // Add result or error
            if (result.TryGetValue("error", out var errorObj))
            {
                jsonRpcResponse["error"] = errorObj;
            }
            else
            {
                jsonRpcResponse["result"] = result;
            }
            
            return jsonRpcResponse;
        }
    }
}
