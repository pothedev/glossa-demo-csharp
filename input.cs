using System;
using System.IO;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using NAudio.Wave;
using System.Speech.Recognition;

public class InputProcessor
{
    const int SampleRate = 16000;
    const int SilenceLimitMs = 500;
    const float SilenceThreshold = 0.02f;

    private SpeechClient speechClient;
    private bool isListening = true;

    public async Task Start()
    {
        foreach (var recognizer in SpeechRecognitionEngine.InstalledRecognizers())
        {
            Console.WriteLine($"ID: {recognizer.Id}, Culture: {recognizer.Culture}");
        }

        speechClient = new SpeechClientBuilder
        {
            CredentialsPath = "stt-key.json"
        }.Build();

        Console.WriteLine("🎤 Speak freely. The program will listen continuously.\n");

        while (isListening)
        {
            await ListenAndProcessOnce();
        }
    }

    private async Task ListenAndProcessOnce()
    {
        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 1),
            BufferMilliseconds = 100
        };

        var buffer = new MemoryStream();
        int silentChunks = 0;
        int maxSilentChunks = SilenceLimitMs / waveIn.BufferMilliseconds;

        var stopSignal = new TaskCompletionSource();

        waveIn.DataAvailable += (s, a) =>
        {
            buffer.Write(a.Buffer, 0, a.BytesRecorded);

            if (IsSilent(a.Buffer, a.BytesRecorded))
            {
                silentChunks++;
                if (silentChunks >= maxSilentChunks)
                {
                    waveIn.StopRecording();
                }
            }
            else
            {
                silentChunks = 0;
            }
        };

        waveIn.RecordingStopped += (s, a) => stopSignal.SetResult();

        waveIn.StartRecording();
        await stopSignal.Task;

        byte[] audioData = buffer.ToArray();
        _ = Task.Run(() => TranscribeAndSpeak(audioData));
    }

    private async Task TranscribeAndSpeak(byte[] audioBytes)
    {
        try
        {
            var response = speechClient.Recognize(new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = SampleRate,
                LanguageCode = Config.LanguageFrom
            }, RecognitionAudio.FromBytes(audioBytes));

            foreach (var result in response.Results)
            {
                string transcript = result.Alternatives[0].Transcript;
                Console.WriteLine("✅ Final: " + transcript);

                string translated = await Translator.Translate(transcript); // Optional translation
                Console.WriteLine("🌍 Translated: " + translated);
                ElevenLabsTTS.Speak(translated);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Transcription failed: " + ex.Message);
        }
    }

    private bool IsSilent(byte[] buffer, int bytesRecorded, float threshold = SilenceThreshold)
    {
        int bytesPerSample = 2;
        int samples = bytesRecorded / bytesPerSample;
        if (samples == 0) return true;

        double sumSquares = 0;
        for (int i = 0; i < bytesRecorded; i += bytesPerSample)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            float amplitude = sample / 32768f;
            sumSquares += amplitude * amplitude;
        }

        float rms = (float)Math.Sqrt(sumSquares / samples);
        return rms < threshold;
    }
}
