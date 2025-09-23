# VapiUnityClient

A Unity component that lets you connect and disconnect from a Vapi.ai assistant in real-time. It streams your microphone audio over WebSockets and plays back the assistant’s voice through an `AudioSource`. The connection can be toggled on or off using a key of your choice.

---

## Features

* Connect/disconnect to a Vapi.ai assistant with a key press
* Real-time microphone capture at 16 kHz mono
* WebSocket streaming to Vapi.ai
* Smooth playback using a streaming `AudioClip` and an `AudioSource`
* Simple inspector configuration for API Key, Assistant ID, hotkey, volumes, and modes

---

## 1) Install the Script

1. Create the folders (if they do not exist):
   `Assets/Scripts/`
2. Place `VapiUnityClient.cs` inside `Assets/Scripts/`.

---

## 2) Add the Component to a GameObject

1. In your Scene, select the GameObject you want to host the AI agent.
2. In the Inspector, click **Add Component** and search for **Vapi Unity Client**.
3. Add an **Audio Source** component to the object where you want the assistant’s voice to play.
4. Drag & drop the **Audio Source** into the `Output Source` field of the `VapiUnityClient` component.

---

## 3) Create and Configure an Assistant on Vapi.ai

1. Go to **[https://vapi.ai/](https://vapi.ai/)** and create an account.
2. From the dashboard, create a new **Assistant** using the **Blank Template**.
3. Configure the assistant:

   * **STT** (Speech-to-Text) service
   * **LLM** (Language Model) service
   * **TTS** (Text-to-Speech) service and voice
4. Save the assistant and copy its **Assistant ID**.

---

## 4) Create an API Key

1. In the Vapi dashboard, open **API Keys**.
2. Generate a new **API Key**.
3. Copy the key.

---

## 5) Configure the Unity Component (Inspector)

* **API Key**: Paste your API key from the dashboard.
* **Assistant Id**: Paste your Assistant ID.
* **Output Source**: Assign the `AudioSource` where playback should occur.
* **Mic Gain**: Microphone input gain multiplier. Default is 5.
* **Output Volume**: Playback volume (0–1).
* **Talk Key**: Select a key from the dropdown to connect/disconnect from the WebSocket.

---

## 6) Usage at Runtime

* Press **Play** in Unity.
* Press your chosen **Talk Key** to connect to the assistant.
* Press the same key again to disconnect completely.
* While connected, your microphone is streamed and assistant audio is played back.
* Disconnecting closes the WebSocket and stops audio streaming.

---

## 7) Permissions

* **Desktop**: No special setup required.
  
---

## 8) Troubleshooting

**No audio playback**

* Check that your assistant has TTS enabled and that `Output Source` is assigned.

**Microphone not sending**

* Make sure your OS microphone is enabled. On mobile, ensure permissions are granted.

**WebSocket not connecting**

* Verify your API Key and Assistant ID are correct.
* Check Console logs for response errors.

---

## Quick Setup Checklist

1. Place script in `Assets/Scripts/`.
2. Add `VapiUnityClient` component to a GameObject.
3. Add an `AudioSource` and assign it.
4. Create Assistant on Vapi.ai (Blank Template, configure STT/LLM/TTS).
5. Copy **Assistant ID** and **API Key**.
6. Paste both into Inspector fields.
7. Select a **Talk Key** to control connection.
8. Press Play, press key to connect/disconnect.
