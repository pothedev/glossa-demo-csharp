using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.TextToSpeech.V1;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public static class OutputTTS_Google
{
    private static TextToSpeechClient _ttsClient;
    private const string OutputDeviceName = "Voicemeeter VAIO3";
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
        if (_isPlaying) return; // Already processing

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
        // Initialize Google TTS client (reusable)
        _ttsClient ??= await TextToSpeechClient.CreateAsync();

        // Find audio output device
        using var enumerator = new MMDeviceEnumerator();
        var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                .FirstOrDefault(d => d.FriendlyName.Contains(OutputDeviceName))
                            ?? throw new Exception($"{OutputDeviceName} not found");

        // Configure voice
        var voice = new VoiceSelectionParams
        {
            LanguageCode = Config.LanguageTo,
            Name = $"{Config.LanguageTo}-Wavenet-A",
            SsmlGender = SsmlVoiceGender.Male
        };

        var audioConfig = new AudioConfig
        {
            AudioEncoding = AudioEncoding.Mp3,
            SpeakingRate = 1.0,
            Pitch = 0.0,
            VolumeGainDb = 0.0
        };

        // Generate speech
        var response = await _ttsClient.SynthesizeSpeechAsync(new SynthesizeSpeechRequest
        {
            Input = new SynthesisInput { Text = text },
            Voice = voice,
            AudioConfig = audioConfig
        });

        // Play audio
        await PlayAudioAsync(response.AudioContent.ToByteArray(), outputDevice);
    }

    private static async Task PlayAudioAsync(byte[] mp3Data, MMDevice outputDevice)
    {
        using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
        using var mp3Stream = new MemoryStream(mp3Data);
        using var reader = new Mp3FileReader(mp3Stream);
        
        var tcs = new TaskCompletionSource<bool>();
        waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);
        
        waveOut.Init(reader);
        waveOut.Play();
        await tcs.Task; // Wait for playback to complete
    }
}