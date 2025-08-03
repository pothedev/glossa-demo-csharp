using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using Google.Protobuf;
using System.Collections.Generic;
using System.Text.Json;

public class OutputProcessor : IDisposable
{
    const int TargetSampleRate = 16000;
    const int SilenceLimitMs = 800;
    const float SilenceThreshold = 0.01f;
    const int StreamingChunkSize = 4096;  // Optimal for gRPC streaming

    private readonly SpeechClient _speechClient;
    private readonly MMDevice _vaioDevice;
    private bool _isRunning = true;
    private readonly WaveFormat _targetFormat = new WaveFormat(TargetSampleRate, 16, 1);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public OutputProcessor()
    {
        var enumerator = new MMDeviceEnumerator();
        _vaioDevice = GetAudioDevice("Voicemeeter Input") ?? 
                      throw new Exception("Voicemeeter device not found");

        _speechClient = new SpeechClientBuilder
        {
            CredentialsPath = "../google-key.json"
        }.Build();
    }

    public async Task StartContinuousListening()
    {
        Console.WriteLine("🚀 Starting continuous audio monitoring...");

        while (_isRunning && !_cts.IsCancellationRequested)
        {
            try
            {
                await ProcessAudioSession();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Session error: {ex.Message}");
                await Task.Delay(1000, _cts.Token);
            }
        }
    }

    private async Task ProcessAudioSession()
    {
        using var capture = new WasapiLoopbackCapture(_vaioDevice);
        using var buffer = new MemoryStream();
        var stopSignal = new TaskCompletionSource<bool>();
        int silentChunks = 0;
        int maxSilentChunks = SilenceLimitMs / 100;
        bool hasSpeech = false;

        capture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded == 0) return;
            
            buffer.Write(e.Buffer, 0, e.BytesRecorded);
            float rms = CalculateRmsFloat(e.Buffer, e.BytesRecorded);
            bool isSilent = rms < SilenceThreshold;

            if (isSilent)
            {
                silentChunks++;
                if (hasSpeech && silentChunks >= maxSilentChunks)
                {
                    Console.WriteLine("🛑 Detected extended silence. Processing audio...");
                    capture.StopRecording();
                }
            }
            else
            {
                hasSpeech = true;
                silentChunks = 0;
            }
        };

        capture.RecordingStopped += async (s, e) =>
        {
            try
            {
                if (buffer.Length == 0 || !hasSpeech)
                {
                    Console.WriteLine("🔇 No speech detected - skipping processing");
                    return;
                }

                byte[] resampledAudio = ConvertToGoogleFormat(buffer.GetBuffer(), capture.WaveFormat);
                SaveDebugWav(resampledAudio, "stt_input.wav", _targetFormat);

                await ProcessAudioWithStreaming(resampledAudio);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Processing error: {ex.Message}");
            }
            finally
            {
                stopSignal.TrySetResult(true);
            }
        };

        Console.WriteLine("👂 Listening for audio...");
        capture.StartRecording();
        await stopSignal.Task;
    }

   private MMDevice GetAudioDevice(string nameContains)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

        if (device != null)
        {
            Console.WriteLine($"Found audio device: {device.FriendlyName}");
        }
        else
        {
            Console.WriteLine($"No audio device found containing: \"{nameContains}\"");
        }

        return device;
    }


    private async Task ProcessAudioWithStreaming(byte[] audioBytes)
    {
        try
        {
            // Skip processing if both features disabled
            if (!Settings.GetValue<bool>("OutputTranslateEnabled") && !Settings.GetValue<bool>("SubtitlesEnabled"))
            {
                return;
            }

            // Initialize streaming call
            var streamingCall = _speechClient.StreamingRecognize();
            string languageCode = Settings.GetValue<string>("TargetLanguage");

            // Configure speech recognition
            await streamingCall.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = new StreamingRecognitionConfig
                {
                    Config = new RecognitionConfig
                    {
                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                        SampleRateHertz = TargetSampleRate,
                        LanguageCode = languageCode,
                        EnableAutomaticPunctuation = true,
                        Model = "latest_long"
                    },
                    InterimResults = false,
                    SingleUtterance = false
                }
            });

            // Stream audio in chunks
            int offset = 0;
            while (offset < audioBytes.Length && !_cts.IsCancellationRequested)
            {
                int chunkSize = Math.Min(StreamingChunkSize, audioBytes.Length - offset);
                await streamingCall.WriteAsync(new StreamingRecognizeRequest
                {
                    AudioContent = ByteString.CopyFrom(audioBytes, offset, chunkSize)
                });
                offset += chunkSize;
            }

            // Complete the stream
            await streamingCall.WriteCompleteAsync();

            // Process responses
            var transcripts = new List<string>();
            var responseStream = streamingCall.GetResponseStream();

            await foreach (var response in responseStream.WithCancellation(_cts.Token))
            {
                foreach (var result in response.Results)
                {
                    if (result.Alternatives.Count > 0)
                    {
                        transcripts.Add(result.Alternatives[0].Transcript);
                        Console.WriteLine($"📊 Confidence: {result.Alternatives[0].Confidence:P}");
                    }
                }
            }

            // Output results
            if (transcripts.Count > 0)
            {
                string fullTranscript = string.Join(" ", transcripts);
                Console.WriteLine($"✅ Final Transcription: {fullTranscript}");

                if (Settings.GetValue<bool>("OutputTranslateEnabled"))
                {
                    string translated = await Translator.Translate(
                        fullTranscript,
                        Settings.GetValue<string>("TargetLanguage").Substring(0, 2),
                        Settings.GetValue<string>("UserLanguage").Substring(0, 2)
                    );
                    Console.WriteLine($"🌍 Translation: {translated}");

                    switch (Settings.GetValue<string>("OutputTTSModel"))
                    {
                        case "Google": _ = OutputTTS_Google.Speak(translated); break;
                        case "ElevenLabs": _ = OutputTTS_ElevenLabs.Speak(translated); break;
                        default: _ = OutputTTS_Native.Speak(translated); break;
                    }
                }
                else if (Settings.GetValue<bool>("SubtitlesEnabled"))
                {
                    string translated = await Translator.Translate(
                        fullTranscript,
                        Settings.GetValue<string>("TargetLanguage").Substring(0, 2),
                        Settings.GetValue<string>("UserLanguage").Substring(0, 2)
                    );
                    Console.WriteLine($"🌍 Translation: {translated}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Processing error: {ex.Message}");
        }
    }

    private byte[] ConvertToGoogleFormat(byte[] audio, WaveFormat sourceFormat)
    {
        try
        {
            // 1. First convert the float audio to 16-bit PCM
            byte[] pcmBuffer = ConvertFloatTo16BitPcm(audio);
            
            // 2. Then resample using MediaFoundation
            using (var pcmStream = new RawSourceWaveStream(new MemoryStream(pcmBuffer), 
                new WaveFormat(sourceFormat.SampleRate, 16, sourceFormat.Channels)))
            using (var resampler = new MediaFoundationResampler(pcmStream, new WaveFormat(TargetSampleRate, 16, 1)))
            {
                resampler.ResamplerQuality = 60;
                using (var outputStream = new MemoryStream())
                {
                    WaveFileWriter.WriteWavFileToStream(outputStream, resampler);
                    return outputStream.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Audio conversion error: {ex.Message}");
            throw;
        }
    }

    private void SaveDebugWav(byte[] audioData, string filename, WaveFormat format)
    {
        try
        {
            var debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio_debug");
            Directory.CreateDirectory(debugDir);
            var filePath = Path.Combine(debugDir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{filename}");

            using var memStream = new MemoryStream(audioData);
            using var reader = new RawSourceWaveStream(memStream, format);
            WaveFileWriter.CreateWaveFile(filePath, reader);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Debug audio save failed: {ex.Message}");
        }
    }
    private byte[] ConvertFloatTo16BitPcm(byte[] floatBuffer)
    {
        int sampleCount = floatBuffer.Length / 4;
        var pcmBuffer = new byte[sampleCount * 2];
        
        for (int i = 0, pcmIndex = 0; i < floatBuffer.Length; i += 4, pcmIndex += 2)
        {
            float sample = BitConverter.ToSingle(floatBuffer, i);
            short pcmSample = (short)(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
            pcmBuffer[pcmIndex] = (byte)(pcmSample & 0xFF);
            pcmBuffer[pcmIndex + 1] = (byte)((pcmSample >> 8) & 0xFF);
        }

        return pcmBuffer;
    }

    private float CalculateRmsFloat(byte[] buffer, int bytesRecorded)
    {
        int floatCount = bytesRecorded / 4;
        if (floatCount == 0) return 0;

        double sumSquares = 0;
        for (int i = 0; i < bytesRecorded; i += 4)
        {
            float sample = BitConverter.ToSingle(buffer, i);
            sumSquares += sample * sample;
        }

        return (float)Math.Sqrt(sumSquares / floatCount);
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        _vaioDevice?.Dispose();
        GC.SuppressFinalize(this);
    }
}