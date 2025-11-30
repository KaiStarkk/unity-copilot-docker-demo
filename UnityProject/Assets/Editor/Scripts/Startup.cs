using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Editor
{
    /// <summary>
    /// Unity Editor startup script for CI/CD environments.
    /// This script keeps Unity alive after initialization, enabling
    /// external tools (like MCP servers) to interact with the Editor.
    ///
    /// Usage:
    ///   unity-editor -batchmode -nographics -executeMethod Editor.Startup.Init
    /// </summary>
    [InitializeOnLoad]
    public static class Startup
    {
        private static TcpListener _server;
        private static Thread _serverThread;
        private static volatile bool _running;
        private static readonly int Port = 8090;

        static Startup()
        {
            // Static constructor runs when Unity loads the assembly
            Debug.Log("[Startup] Static constructor called - Unity Editor is loading");
        }

        /// <summary>
        /// Entry point for -executeMethod command line argument.
        /// This method starts a simple TCP server and blocks to keep Unity alive.
        /// </summary>
        public static void Init()
        {
            Debug.Log("===========================================");
            Debug.Log("[Startup] Init() called via -executeMethod");
            Debug.Log($"[Startup] Unity Version: {Application.unityVersion}");
            Debug.Log($"[Startup] Project Path: {Application.dataPath}");
            Debug.Log($"[Startup] Platform: {Application.platform}");
            Debug.Log("===========================================");

            // Start a simple health check server
            StartHealthServer();

            // Signal that initialization is complete
            Debug.Log("[Startup] Unity-MCP-Ready");
            Debug.Log($"[Startup] Health server listening on port {Port}");

            // Keep Unity alive by entering a blocking loop
            Debug.Log("[Startup] Entering keep-alive loop...");
            KeepAlive();
        }

        /// <summary>
        /// Starts a simple TCP server for health checks and basic communication.
        /// External tools can connect to verify Unity is running.
        /// </summary>
        private static void StartHealthServer()
        {
            _running = true;
            _serverThread = new Thread(RunServer)
            {
                IsBackground = true,
                Name = "Unity-HealthServer"
            };
            _serverThread.Start();
        }

        private static void RunServer()
        {
            try
            {
                _server = new TcpListener(IPAddress.Any, Port);
                _server.Start();
                Debug.Log($"[Startup] TCP server started on port {Port}");

                while (_running)
                {
                    if (_server.Pending())
                    {
                        var client = _server.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Startup] Server error: {ex.Message}");
            }
        }

        private static void HandleClient(object state)
        {
            using (var client = (TcpClient)state)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream))
            using (var writer = new StreamWriter(stream) { AutoFlush = true })
            {
                try
                {
                    var request = reader.ReadLine();
                    Debug.Log($"[Startup] Received request: {request}");

                    // Simple health check response
                    var response = $"{{\"status\":\"ok\",\"unity_version\":\"{Application.unityVersion}\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}";
                    writer.WriteLine(response);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Startup] Client handler error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Keeps Unity alive indefinitely.
        /// Checks for a shutdown signal file to allow graceful termination.
        /// </summary>
        private static void KeepAlive()
        {
            var shutdownSignalPath = "/tmp/unity-shutdown-signal";

            while (true)
            {
                // Check for shutdown signal
                if (File.Exists(shutdownSignalPath))
                {
                    Debug.Log("[Startup] Shutdown signal detected. Exiting...");
                    _running = false;
                    _server?.Stop();

                    try { File.Delete(shutdownSignalPath); } catch { }

                    EditorApplication.Exit(0);
                    break;
                }

                // Sleep to avoid busy-waiting
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Call this to gracefully shut down Unity from another script or tool.
        /// </summary>
        public static void Shutdown()
        {
            Debug.Log("[Startup] Shutdown() called");
            File.WriteAllText("/tmp/unity-shutdown-signal", "shutdown");
        }
    }
}
