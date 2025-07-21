using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public static class InputTTS
{
    public static async Task Speak(string text)
    {
        // 1. Look for the render endpoint
        var enumerator = new MMDeviceEnumerator();
        var outputDevice = enumerator.EnumerateAudioEndPoints(
                                    DataFlow.Render, DeviceState.Active)
                                .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter VAIO3"));
        if (outputDevice == null)
        {
            var list = string.Join(", ",
                enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                            .Select(d => d.FriendlyName));
            throw new Exception("Voicemeeter Out A2 render device not found.");
        }

        // 2. Check API key
        var apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new Exception("Missing ElevenLabs API key.");
        }
        // 3. Build JSON payload
        var payload = JsonSerializer.Serialize(new
        {
            text,
            model_id = "eleven_multilingual_v2",
            voice_settings = new
            {
                stability = 0.5f,
                similarity_boost = 0.75f,

            }
        });

        // 4. Send request
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        const string url = "https://api.elevenlabs.io/v1/text-to-speech/aMSt68OGf4xUZAnLpTU8/stream";
        var resp = await http.PostAsync(
            url,
            new StringContent(payload, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();

        // 5. Play audio
        using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
        waveOut.Init(new Mp3FileReader(await resp.Content.ReadAsStreamAsync()));
        waveOut.Play();

        while (waveOut.PlaybackState == PlaybackState.Playing)
            await Task.Delay(100);
    }
}