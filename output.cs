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
            CredentialsPath = "stt-key.json"
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
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ProcessAudioWithStreaming(byte[] audioBytes)
    {
        try
        {
            var streamingCall = _speechClient.StreamingRecognize();
            
            // Send configuration first
            await streamingCall.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = new StreamingRecognitionConfig
                {
                    Config = new RecognitionConfig
                    {
                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                        SampleRateHertz = TargetSampleRate,
                        LanguageCode = Config.LanguageFrom,
                        EnableAutomaticPunctuation = true
                    },
                    InterimResults = false
                }
            });

            // Stream audio in chunks
            int offset = 0;
            while (offset < audioBytes.Length && !_cts.IsCancellationRequested)
            {
                int chunkSize = Math.Min(StreamingChunkSize, audioBytes.Length - offset);
                var request = new StreamingRecognizeRequest
                {
                    AudioContent = ByteString.CopyFrom(audioBytes, offset, chunkSize)
                };
                await streamingCall.WriteAsync(request);
                offset += chunkSize;
            }
            
            await streamingCall.WriteCompleteAsync();
            var responseStream = streamingCall.GetResponseStream();

            // Process results
            var transcripts = new List<string>();
            await foreach (var response in responseStream.WithCancellation(_cts.Token))
            {
                foreach (var result in response.Results)
                {
                    if (result.Alternatives.Count > 0)
                    {
                        transcripts.Add(result.Alternatives[0].Transcript);
                    }
                }
            }

            string fullTranscript = string.Join(" ", transcripts);
            if (!string.IsNullOrWhiteSpace(fullTranscript))
            {
                Console.WriteLine($"✅ Full Transcription: {fullTranscript}");
                string translated = await Translator.Translate(fullTranscript);
                Console.WriteLine($"🌍 Full Translation: {translated}");

                _ = OutputTTS.Speak(translated);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ gRPC Streaming error: {ex.Message}");
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