using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.TextToSpeech.V1;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public static class InputTTS_Google
{
    private const string CredentialsPath = "../google-key.json"; // Relative to executable
    
    public static async Task Speak(string text)
    {

        // 1. Find audio output device
        using var enumerator = new MMDeviceEnumerator();
        var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter VAIO3"))
                            ?? throw new Exception("Voicemeeter VAIO3 render device not found.");

        // 2. Initialize Google TTS client
        if (!File.Exists(CredentialsPath))
        {
            throw new FileNotFoundException(
                $"Google credentials not found at: {Path.GetFullPath(CredentialsPath)}\n" +
                "Please ensure:\n" +
                "1. google-key.json exists in the project's root folder\n" +
                "2. The file is copied to output directory (set 'Copy to Output Directory' = 'Copy if newer')"
            );
        }

        var client = new TextToSpeechClientBuilder
        {
            CredentialsPath = CredentialsPath
        }.Build();


        // 3. Configure voice parameters
        var voice = new VoiceSelectionParams
        {
            LanguageCode = Settings.GetValue<string>("LanguageTo"),
            Name = $"{Settings.GetValue<string>("LanguageTo")}-Wavenet-A", // uk-UA-Wavenet-A en-US-Wavenet-D
            SsmlGender = SsmlVoiceGender.Male
        };

        var audioConfig = new AudioConfig
        {
            AudioEncoding = AudioEncoding.Mp3,
            SpeakingRate = 1.0, // Normal speed
            Pitch = 0.0,        // Neutral pitch
            VolumeGainDb = 0.0  // Default volume
        };

        // 4. Generate speech via gRPC
        var response = await client.SynthesizeSpeechAsync(new SynthesizeSpeechRequest
        {
            Input = new SynthesisInput { Text = text },
            Voice = voice,
            AudioConfig = audioConfig
        });

        // 5. Play audio using NAudio
        using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
        using var mp3Stream = new MemoryStream(response.AudioContent.ToByteArray());
        using var reader = new Mp3FileReader(mp3Stream);

        waveOut.Init(reader);
        waveOut.Play();

        // Wait until playback completes
        while (waveOut.PlaybackState == PlaybackState.Playing)
            await Task.Delay(100);
    }
}