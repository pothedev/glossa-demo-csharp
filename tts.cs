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

public static class ElevenLabsTTS
{
    // Voicemeeter API imports
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_Login();
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_SetParameterFloat(string param, float value);
    [DllImport("VoicemeeterRemote64.dll")]
    private static extern int VBVMR_GetParameterFloat(string param, ref float value);
    
    private const int VAIO_STRIP = 3;  // VAIO3 strip
    private const float MUTED_VOLUME = -10.0f;
    private static readonly SemaphoreSlim _voicemeeterLock = new SemaphoreSlim(1, 1);
    private static bool _voicemeeterInitialized = false;

    public static async Task Speak(string text)
    {
        WasapiOut waveOut = null;
        float originalVaioVolume = 0f;

        try
        {
            // 1. Initialize Voicemeeter (thread-safe with retries)
            await InitializeVoicemeeter();

            // 2. Store and modify VAIO volume only
            VBVMR_GetParameterFloat($"Strip[{VAIO_STRIP}].Gain", ref originalVaioVolume);
            SetParameterSafe($"Strip[{VAIO_STRIP}].Gain", MUTED_VOLUME);

            // 3. Find AUX output device (flexible name matching)
            var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter AUX")) 
                ?? throw new Exception("AUX device not found. Available devices: " + 
                    string.Join(", ", enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Select(d => d.FriendlyName)));

            // 4. Play TTS through AUX
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("xi-api-key", 
                Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") 
                ?? throw new Exception("Missing ElevenLabs API key in .env"));

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
            waveOut.Init(new Mp3FileReader(await response.Content.ReadAsStreamAsync()));
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            waveOut?.Dispose();
            // Restore VAIO volume (ignore errors during cleanup)
            try { SetParameterSafe($"Strip[{VAIO_STRIP}].Gain", originalVaioVolume); }
            catch { /* Suppress errors during cleanup */ }
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