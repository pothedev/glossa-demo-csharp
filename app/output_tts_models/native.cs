using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public static class OutputTTS_Native
{
    private const string OutputDeviceName = "Voicemeeter AUX";
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
        // 1. Find audio output device
        using var enumerator = new MMDeviceEnumerator();
        var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                .FirstOrDefault(d => d.FriendlyName.Contains(OutputDeviceName))
                            ?? throw new Exception($"{OutputDeviceName} not found");

        // 2. Initialize and configure synthesizer
        using var synthesizer = new SpeechSynthesizer();
        ConfigureVoice(synthesizer);

        // 3. Generate audio to memory stream
        using var memoryStream = new MemoryStream();
        synthesizer.SetOutputToWaveStream(memoryStream);
        synthesizer.Speak(text); // Synchronous operation
        memoryStream.Position = 0; // Reset for reading

        // 4. Play audio through NAudio
        await PlayAudioAsync(memoryStream, outputDevice);
    }

    private static void ConfigureVoice(SpeechSynthesizer synthesizer)
    {
        var targetCulture = Settings.GetValue<string>("UserLanguage");
        var installedVoice = synthesizer.GetInstalledVoices()
            .FirstOrDefault(v => v.VoiceInfo.Culture.Name.Equals(targetCulture, StringComparison.OrdinalIgnoreCase));

        if (installedVoice != null)
        {
            synthesizer.SelectVoice(installedVoice.VoiceInfo.Name);
            synthesizer.Rate = 0; // Neutral speaking rate (-10 to 10)
            synthesizer.Volume = 100; // Max volume
        }
        else
        {
            Console.WriteLine($"⚠️ {targetCulture} voice not found. Using default voice.");
        }
    }

    private static async Task PlayAudioAsync(MemoryStream waveStream, MMDevice outputDevice)
    {
        using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
        using var reader = new WaveFileReader(waveStream);
        
        var tcs = new TaskCompletionSource<bool>();
        waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);
        
        waveOut.Init(reader);
        waveOut.Play();
        await tcs.Task; // Wait for playback completion
    }
}