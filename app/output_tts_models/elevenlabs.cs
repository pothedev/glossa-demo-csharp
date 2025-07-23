using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using DotNetEnv;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

public static class OutputTTS_ElevenLabs
{

    // Voicemeeter API imports
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_Login();
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_SetParameterFloat(string param, float value);
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_GetParameterFloat(string param, ref float value);
    
    private const int VAIO_STRIP = 3;  
    private const float MUTED_VOLUME = -10.0f;
    private static readonly SemaphoreSlim _voicemeeterLock = new SemaphoreSlim(1, 1);
    private static bool _voicemeeterInitialized = false;

    private static readonly ConcurrentQueue<string> _speechQueue = new();
    private static readonly SemaphoreSlim _playbackLock = new(1, 1);
    private static bool _isPlaying;

    public static Task Speak(string text)
    {
        _speechQueue.Enqueue(text);
        _ = ProcessQueueAsync(); // Fire-and-forget
        return Task.CompletedTask;
    }

    private static async Task ProcessQueueAsync()
    {
        if (_isPlaying) return;

        await _playbackLock.WaitAsync();
        try
        {
            _isPlaying = true;
            while (_speechQueue.TryDequeue(out var text))
            {
                await PlayTextAsync(text);
            }
        }
        finally
        {
            _isPlaying = false;
            _playbackLock.Release();
        }
    }

    private static async Task PlayTextAsync(string text)
    {
        WasapiOut waveOut = null;
        float originalVaioVolume = 0f;

        try
        {
            await InitializeVoicemeeter();
            VBVMR_GetParameterFloat($"Strip[{VAIO_STRIP}].Gain", ref originalVaioVolume);
            SetParameterSafe($"Strip[{VAIO_STRIP}].Gain", MUTED_VOLUME);

            var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter AUX")) 
                ?? throw new Exception("AUX device not found");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("xi-api-key", 
                Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") 
                ?? throw new Exception("Missing API key"));

            var response = await httpClient.PostAsync(
                "https://api.elevenlabs.io/v1/text-to-speech/aMSt68OGf4xUZAnLpTU8/stream",
                new StringContent(
                    JsonSerializer.Serialize(new {
                        text = text,
                        model_id = "eleven_multilingual_v2",
                        voice_settings = new {
                            stability = 0.5,
                            similarity_boost = 0.75
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            response.EnsureSuccessStatusCode();

            waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
            var tcs = new TaskCompletionSource<bool>();
            waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);
            
            waveOut.Init(new Mp3FileReader(await response.Content.ReadAsStreamAsync()));
            waveOut.Play();
            await tcs.Task; // Properly await playback completion
        }
        finally
        {
            waveOut?.Dispose();
            SetParameterSafe($"Strip[{VAIO_STRIP}].Gain", originalVaioVolume);
        }
    }

    private static async Task InitializeVoicemeeter()
    {
        await _voicemeeterLock.WaitAsync();
        try
        {
            if (!_voicemeeterInitialized)
            {
                int retry = 0;
                while (VBVMR_Login() != 0 && retry++ < 3)
                {
                    await Task.Delay(500);
                    if (retry == 3) 
                        throw new Exception("Voicemeeter login failed after 3 attempts. Ensure:\n" +
                                          "1. Voicemeeter is running\n" +
                                          "2. Both apps are running as administrator");
                }
                _voicemeeterInitialized = true;
            }
        }
        finally
        {
            _voicemeeterLock.Release();
        }
    }

    private static void SetParameterSafe(string param, float value)
    {
        int result = VBVMR_SetParameterFloat(param, value);
        if (result != 0)
            throw new Exception($"Failed to set {param} (Error: {result})");
    }
}