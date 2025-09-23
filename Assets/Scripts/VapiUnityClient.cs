using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Unity client for Vapi.ai speech-to-speech communication
/// Provides push-to-talk toggle functionality for real-time voice conversations
/// </summary>
public class VapiUnityClient : MonoBehaviour
{
    [Header("Vapi Settings")]
    [Tooltip("Your Vapi.ai API key from dashboard")]
    public string apiKey = "";

    [Tooltip("The assistant ID you want to talk to")]
    public string assistantId = "";

    [Header("Audio Settings")]
    [Tooltip("AudioSource component for playing received audio")]
    public AudioSource outputSource;

    [Tooltip("Microphone gain multiplier (higher = louder mic input)")]
    [Range(1f, 10f)]
    public float micGain = 5f;

    [Tooltip("Output audio volume (0 = silent, 1 = full volume)")]
    [Range(0f, 1f)]
    public float outputVolume = 0.8f;

    [Header("Push-to-Talk Settings")]
    [Tooltip("Key to press for toggle connection on/off")]
    public KeyCode talkKey = KeyCode.Space;

    [Tooltip("Enable toggle mode (true) or always-on mode (false)")]
    public bool pushToTalkMode = true;

    // ===== PRIVATE VARIABLES =====

    /// <summary>WebSocket URL received from Vapi after creating a call</summary>
    private string websocketUrl;

    /// <summary>WebSocket client for real-time communication</summary>
    private ClientWebSocket ws;

    /// <summary>Cancellation token source for graceful WebSocket shutdown</summary>
    private CancellationTokenSource cts;

    /// <summary>AudioClip from microphone recording</summary>
    private AudioClip micClip;

    /// <summary>Last processed sample position to avoid re-processing</summary>
    private int lastSample = 0;

    /// <summary>Current talking/connection state</summary>
    private bool isTalking = false;

    /// <summary>Flag to track if we've ever been connected (for debugging)</summary>
    private bool wasConnected = false;

    /// <summary>Thread-safe queue for incoming audio samples from Vapi</summary>
    private Queue<float> audioQueue = new Queue<float>();

    /// <summary>Lock object for thread-safe audio queue access</summary>
    private readonly object audioLock = new object();

    /// <summary>Unused variable - keeping for potential future use</summary>
    private int writePosition = 0;

    // ===== AUDIO CONSTANTS =====

    /// <summary>Sample rate for both microphone and playback (Vapi uses 16kHz)</summary>
    private const int SAMPLE_RATE = 16000;

    /// <summary>Audio buffer size - 2 seconds worth of samples</summary>
    private const int BUFFER_SIZE = SAMPLE_RATE * 2;

    // ===== JSON SERIALIZABLE CLASSES FOR VAPI API =====

    /// <summary>Transport configuration for Vapi call creation</summary>
    [System.Serializable]
    public class Transport
    {
        public string provider;
    }

    /// <summary>Request body structure for creating a new Vapi call</summary>
    [System.Serializable]
    public class CallRequest
    {
        public string assistantId;
        public Transport transport;
    }

    /// <summary>Response structure from Vapi after creating a call</summary>
    [System.Serializable]
    public class CallResponse
    {
        public string id;
        public string assistantId;
        public string type;
        public TransportResponse transport;
    }

    /// <summary>Transport information in Vapi call response containing WebSocket URL</summary>
    [System.Serializable]
    public class TransportResponse
    {
        public string provider;
        public string websocketCallUrl;
    }

    // ===== UNITY LIFECYCLE METHODS =====

    /// <summary>
    /// Initialize the client on start
    /// In toggle mode, waits for user input. In always-on mode, connects immediately
    /// </summary>
    private IEnumerator Start()
    {
        // Check if we should auto-connect or wait for user input
        if (!pushToTalkMode)
        {
            // Always-on mode: connect immediately
            yield return StartCoroutine(CreateCall());
        }
        else
        {
            // Toggle mode: wait for user to press the talk key
            Debug.Log("🔄 Toggle mode - Press " + talkKey + " to connect/disconnect");
        }
    }

    /// <summary>
    /// Handle input and microphone data transmission every frame
    /// </summary>
    private void Update()
    {
        // Handle toggle key input (only on key press, not hold)
        if (pushToTalkMode && Input.GetKeyDown(talkKey))
        {
            if (!isTalking)
            {
                // Not connected - start connection
                StartTalking();
            }
            else
            {
                // Connected - disconnect
                StopTalking();
            }
        }

        // Send microphone data if we're in talking mode and connection is ready
        if (isTalking && ShouldSendMicData())
        {
            SendMicrophoneData();
        }
    }

    // ===== CONNECTION MANAGEMENT METHODS =====

    /// <summary>
    /// Start talking session - creates new call and connects to Vapi
    /// </summary>
    private void StartTalking()
    {
        isTalking = true;
        Debug.Log("🎤 Connecting to Vapi...");

        // Always create a new call for each session (Vapi requires unique call IDs)
        StartCoroutine(CreateCall());
    }

    /// <summary>
    /// Stop talking session - disconnects WebSocket and cleans up resources
    /// </summary>
    private void StopTalking()
    {
        isTalking = false;
        Debug.Log("🔇 Disconnecting from Vapi...");

        // Close WebSocket connection and clean up
        DisconnectWebSocket();

        // Clear WebSocket URL to force new call creation next time
        websocketUrl = null;
    }

    /// <summary>
    /// Check if we should send microphone data
    /// All conditions must be true: mic exists, WebSocket connected, in talking mode
    /// </summary>
    private bool ShouldSendMicData()
    {
        return micClip != null &&
               ws != null &&
               ws.State == WebSocketState.Open &&
               isTalking;
    }

    // ===== VAPI API COMMUNICATION =====

    /// <summary>
    /// Create a new call with Vapi API and get WebSocket URL for real-time communication
    /// </summary>
    private IEnumerator CreateCall()
    {
        string url = "https://api.vapi.ai/call";

        // Prepare request data for Vapi API
        CallRequest requestData = new CallRequest
        {
            assistantId = assistantId,
            transport = new Transport { provider = "vapi.websocket" }
        };

        string body = JsonUtility.ToJson(requestData);
        Debug.Log("📤 Request Body: " + body);

        // Create HTTP POST request to Vapi
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            // Set up request body and headers
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();

            string cleanApiKey = apiKey.Trim();

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + cleanApiKey);

            // Send request and wait for response
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("✅ CreateCall Response: " + req.downloadHandler.text);

                // Parse response to get WebSocket URL
                CallResponse resp = JsonUtility.FromJson<CallResponse>(req.downloadHandler.text);
                websocketUrl = resp.transport.websocketCallUrl;

                Debug.Log("🔗 WebSocket URL: " + websocketUrl);

                // Start WebSocket connection (async)
                _ = ConnectWebSocket();
            }
            else
            {
                // Handle API call failure
                Debug.LogError("❌ Error: " + req.responseCode + " " + req.error);
                Debug.LogError("Response Body: " + req.downloadHandler.text);

                // Reset talking state on failure
                if (pushToTalkMode)
                {
                    isTalking = false;
                    Debug.Log("🔄 Connection failed, ready for retry");
                }
            }
        }
    }

    // ===== WEBSOCKET MANAGEMENT =====

    /// <summary>
    /// Connect to Vapi WebSocket for real-time audio streaming
    /// Handles both sending (microphone) and receiving (assistant voice) audio
    /// </summary>
    private async Task ConnectWebSocket()
    {
        try
        {
            Debug.Log($"🔌 Connecting to: {websocketUrl}");

            // Create new WebSocket client
            ws = new ClientWebSocket();
            cts = new CancellationTokenSource();

            // Connect to Vapi WebSocket
            await ws.ConnectAsync(new Uri(websocketUrl), cts.Token);
            Debug.Log("✅ WebSocket connected!");

            wasConnected = true;

            // Set up audio systems
            SetupAudioPlayback();
            StartMicrophone();

            // Start background task to receive audio data from Vapi
            _ = Task.Run(async () =>
            {
                var buffer = new byte[8192]; // 8KB buffer for incoming data
                try
                {
                    // Keep receiving data while WebSocket is open
                    while (ws.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // Server closed the connection
                            Debug.LogWarning("⚠️ WebSocket closed by server");
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            // Received audio data from assistant
                            ProcessIncomingAudio(buffer, result.Count);
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            // Received text message (usually status/control messages)
                            string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            Debug.Log("📥 WS Message: " + msg);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when we cancel the operation
                    Debug.Log("🔌 WebSocket operation cancelled");
                }
                catch (Exception ex)
                {
                    // Unexpected error
                    if (!cts.Token.IsCancellationRequested)
                    {
                        Debug.LogError("❌ WebSocket receive error: " + ex.Message);
                        Debug.LogError("❌ Stack trace: " + ex.StackTrace);
                    }
                }
                finally
                {
                    Debug.Log("🔌 WebSocket receive loop ended");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogError("❌ WebSocket connection error: " + ex.Message);
            Debug.LogError("❌ Stack trace: " + ex.StackTrace);

            // Reset state on connection failure
            if (pushToTalkMode)
            {
                isTalking = false;
            }
        }
    }

    /// <summary>
    /// Disconnect WebSocket and clean up all resources
    /// Called when user stops talking or on application quit
    /// </summary>
    private void DisconnectWebSocket()
    {
        try
        {
            Debug.Log("🔌 Starting disconnect process...");

            // Cancel any ongoing WebSocket operations
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }

            // Close and dispose WebSocket connection
            if (ws != null)
            {
                if (ws.State == WebSocketState.Open)
                {
                    Debug.Log("🔌 Closing WebSocket connection...");
                    _ = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnected", CancellationToken.None);
                }
                ws.Dispose();
                ws = null;
            }

            // Stop microphone recording
            if (Microphone.IsRecording(null))
            {
                Microphone.End(null);
                Debug.Log("🎤 Microphone stopped");
            }
            micClip = null;
            lastSample = 0;

            // Clear audio queue
            lock (audioLock)
            {
                audioQueue.Clear();
            }

            // Stop audio playback and clean up AudioClip
            if (outputSource != null)
            {
                if (outputSource.isPlaying)
                {
                    outputSource.Stop();
                    Debug.Log("🔊 Audio playback stopped");
                }

                // Destroy old AudioClip to prevent memory leaks
                if (outputSource.clip != null)
                {
                    DestroyImmediate(outputSource.clip);
                    outputSource.clip = null;
                    Debug.Log("🔊 AudioClip destroyed");
                }
            }

            Debug.Log("✅ Disconnect process completed");
        }
        catch (Exception ex)
        {
            Debug.LogError("❌ Disconnect error: " + ex.Message);
        }
    }

    // ===== AUDIO PROCESSING METHODS =====

    /// <summary>
    /// Set up audio playback system for receiving assistant voice
    /// Creates a streaming AudioClip that continuously plays incoming audio
    /// </summary>
    private void SetupAudioPlayback()
    {
        // Stop any currently playing audio
        if (outputSource.isPlaying)
        {
            outputSource.Stop();
        }

        // Clean up old AudioClip to prevent memory leaks
        if (outputSource.clip != null)
        {
            DestroyImmediate(outputSource.clip);
            outputSource.clip = null;
        }

        // Create new streaming AudioClip with unique name
        // The OnAudioRead callback will be called continuously to fill audio data
        outputSource.clip = AudioClip.Create(
            "VapiPlayback_" + System.DateTime.Now.Ticks, // Unique name to avoid conflicts
            BUFFER_SIZE,    // Number of samples in the clip
            1,              // Mono audio
            SAMPLE_RATE,    // 16kHz sample rate (matches Vapi)
            true,           // Enable streaming mode
            OnAudioRead     // Callback function to provide audio data
        );

        // Configure AudioSource settings
        outputSource.loop = true;              // Loop the audio clip
        outputSource.volume = outputVolume;    // Set volume
        outputSource.Play();                   // Start playing

        Debug.Log("🔊 Audio playback setup complete");
    }

    /// <summary>
    /// Process incoming audio data from Vapi assistant
    /// Converts PCM16 audio to float samples and adds to playback queue
    /// </summary>
    /// <param name="buffer">Raw audio data from WebSocket</param>
    /// <param name="count">Number of bytes in the buffer</param>
    private void ProcessIncomingAudio(byte[] buffer, int count)
    {
        // Each audio sample is 2 bytes (16-bit PCM)
        int sampleCount = count / 2;

        // Thread-safe addition to audio queue
        lock (audioLock)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                // Convert 16-bit PCM to float (-1.0 to 1.0 range)
                short pcm = BitConverter.ToInt16(buffer, i * 2);
                float sample = pcm / 32768f;
                audioQueue.Enqueue(sample);
            }
        }
    }

    /// <summary>
    /// Audio callback function called by Unity's audio system
    /// Fills the provided audio buffer with samples from our queue
    /// This runs on the audio thread, so it must be fast and thread-safe
    /// </summary>
    /// <param name="data">Audio buffer to fill with sample data</param>
    private void OnAudioRead(float[] data)
    {
        // Thread-safe access to audio queue
        lock (audioLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (audioQueue.Count > 0)
                {
                    // Get next sample from queue and apply volume
                    data[i] = audioQueue.Dequeue() * outputVolume;
                }
                else
                {
                    // No audio data available, output silence
                    data[i] = 0f;
                }
            }
        }
    }

    // ===== MICROPHONE METHODS =====

    /// <summary>
    /// Start microphone recording for sending voice to Vapi
    /// Only starts if not already recording
    /// </summary>
    private void StartMicrophone()
    {
        if (!Microphone.IsRecording(null))
        {
            // Start recording: device=null (default), loop=true, length=10s, frequency=16kHz
            micClip = Microphone.Start(null, true, 10, SAMPLE_RATE);
            lastSample = 0;
            Debug.Log("🎤 Microphone started at " + SAMPLE_RATE + "Hz");
        }
    }

    /// <summary>
    /// Capture and send microphone data to Vapi
    /// Called every frame when in talking mode
    /// Only sends new audio data since last frame
    /// </summary>
    private void SendMicrophoneData()
    {
        // Get current recording position
        int pos = Microphone.GetPosition(null);

        // Calculate how many new samples we have
        int diff = pos - lastSample;
        if (diff < 0) diff += micClip.samples; // Handle wraparound

        if (diff > 0)
        {
            // Get the new audio samples
            float[] samples = new float[diff];
            micClip.GetData(samples, lastSample);

            // Convert float samples to 16-bit PCM for WebSocket transmission
            byte[] buffer = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                // Apply microphone gain and clamp to prevent distortion
                short pcm = (short)Mathf.Clamp(samples[i] * micGain * 32767f, -32768, 32767);
                BitConverter.GetBytes(pcm).CopyTo(buffer, i * 2);
            }

            // Send audio data to Vapi via WebSocket
            _ = ws.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Binary,
                true, // End of message
                CancellationToken.None
            );
        }

        // Update last processed sample position
        lastSample = pos;
    }

    // ===== DEBUG AND UI METHODS =====

    /// <summary>
    /// Display debug information on screen during play mode
    /// Shows connection status, audio queue size, and instructions
    /// </summary>
    private void OnGUI()
    {
        if (Application.isPlaying)
        {
            // Show audio queue status (thread-safe)
            lock (audioLock)
            {
                GUI.Label(new Rect(10, 10, 300, 20), $"Audio Queue: {audioQueue.Count} samples");
            }

            // Show WebSocket connection status
            GUI.Label(new Rect(10, 30, 300, 20), $"WebSocket: {(ws?.State.ToString() ?? "None")}");

            // Show talking state
            GUI.Label(new Rect(10, 50, 300, 20), $"Talking: {isTalking}");

            // Show push-to-talk instructions
            if (pushToTalkMode)
            {
                string status = isTalking ? "🔴 CONNECTED - Press " + talkKey + " to disconnect"
                                         : "⚪ DISCONNECTED - Press " + talkKey + " to connect";
                GUI.Label(new Rect(10, 70, 400, 20), status);
            }
        }
    }

    // ===== UNITY CLEANUP METHODS =====

    /// <summary>
    /// Clean up resources when application quits
    /// </summary>
    private void OnApplicationQuit()
    {
        DisconnectWebSocket();
    }

    /// <summary>
    /// Clean up resources when this GameObject is destroyed
    /// </summary>
    private void OnDestroy()
    {
        DisconnectWebSocket();
    }
}