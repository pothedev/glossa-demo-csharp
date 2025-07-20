using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Speech.V1;
using NAudio.Wave;
using NAudio.CoreAudioApi;

public class OutputProcessor : IDisposable
{
    const int TargetSampleRate = 16000;
    const int SilenceLimitMs = 500;
    const float SilenceThreshold = 0.01f;

    private readonly SpeechClient speechClient;
    private readonly MMDevice audioEndpoint;
    private readonly float originalVolume;
    private readonly SemaphoreSlim ttsSemaphore = new SemaphoreSlim(1, 1);
    private bool isRunning = true;

    public OutputProcessor()
    {
        var enumerator = new MMDeviceEnumerator();
        audioEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        originalVolume = audioEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar;
        
        speechClient = new SpeechClientBuilder
        {
            CredentialsPath = "stt-key.json"
        }.Build();
    }

    public async Task StartContinuousListening()
    {
        Console.WriteLine("🚀 Starting continuous audio monitoring...");

        while (isRunning)
        {
            try
            {
                await ProcessAudioSession();
                Console.WriteLine("\n🔄 Restarting audio listener...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Session error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task ProcessAudioSession()
    {
        using (var capture = new WasapiLoopbackCapture())
        using (var buffer = new MemoryStream())
        {
            var stopSignal = new TaskCompletionSource<bool>();
            int silentChunks = 0;
            int maxSilentChunks = SilenceLimitMs / 100;

            capture.DataAvailable += (s, e) =>
            {
                buffer.Write(e.Buffer, 0, e.BytesRecorded);
                float rms = CalculateRmsFloat(e.Buffer, e.BytesRecorded);
                bool isSilent = rms < SilenceThreshold;

                if (isSilent)
                {
                    silentChunks++;
                    if (silentChunks >= maxSilentChunks)
                    {
                        Console.WriteLine("🛑 Detected extended silence. Processing audio...");
                        capture.StopRecording();
                    }
                }
                else
                {
                    silentChunks = 0;
                }
            };

            capture.RecordingStopped += async (s, e) =>
            {
                try
                {
                    if (buffer.Length == 0) return;

                    byte[] originalAudio = buffer.ToArray();
                    SaveDebugWav(originalAudio, "raw_capture.wav", capture.WaveFormat);
                    
                    byte[] resampledAudio = ConvertToGoogleFormat(originalAudio, capture.WaveFormat);
                    SaveDebugWav(resampledAudio, "for_stt.wav", new WaveFormat(TargetSampleRate, 16, 1));
                    
                    await ProcessAudio(resampledAudio);
                }
                finally
                {
                    stopSignal.SetResult(true);
                }
            };

            Console.WriteLine("👂 Listening for audio...");
            capture.StartRecording();
            await stopSignal.Task;
        }
    }

    private async Task ProcessAudio(byte[] audioBytes)
    {
        try
        {
            var response = await speechClient.RecognizeAsync(new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = TargetSampleRate,
                LanguageCode = Config.LanguageFrom,
                EnableAutomaticPunctuation = true
            }, RecognitionAudio.FromBytes(audioBytes));

            var fullTranscript = string.Join(" ", response.Results
                .Where(r => r.Alternatives.Count > 0)
                .Select(r => r.Alternatives[0].Transcript));

            if (!string.IsNullOrWhiteSpace(fullTranscript))
            {
                Console.WriteLine($"✅ Full Transcription: {fullTranscript}");
                string translated = await Translator.Translate(fullTranscript);
                Console.WriteLine($"🌍 Full Translation: {translated}");

                await ttsSemaphore.WaitAsync();
                try
                {
                    ElevenLabsTTS.Speak(translated);
                }
                finally
                {
                    RestoreSystemVolume();
                    ttsSemaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Processing error: {ex.Message}");
            RestoreSystemVolume();
        }
    }

    private void SaveDebugWav(byte[] audioData, string filename, WaveFormat format)
    {
        try
        {
            var debugDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio_debug");
            Directory.CreateDirectory(debugDir);
            var filePath = Path.Combine(debugDir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{filename}");

            using (var memStream = new MemoryStream(audioData))
            using (var reader = new RawSourceWaveStream(memStream, format))
            {
                WaveFileWriter.CreateWaveFile(filePath, reader);
            }

            Console.WriteLine($"🔊 Saved debug audio: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to save debug audio: {ex.Message}");
        }
    }


    private void RestoreSystemVolume()
    {
        audioEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolume;
    }

    private byte[] ConvertToGoogleFormat(byte[] audio, WaveFormat sourceFormat)
    {
        using (var sourceStream = new MemoryStream(audio))
        using (var sourceReader = new RawSourceWaveStream(sourceStream, sourceFormat))
        {
            var floatBuffer = new byte[sourceReader.Length];
            sourceReader.Read(floatBuffer, 0, floatBuffer.Length);
            
            var pcmBuffer = ConvertFloatTo16BitPcm(floatBuffer);
            
            var pcmStream = new RawSourceWaveStream(
                new MemoryStream(pcmBuffer), 
                new WaveFormat(sourceFormat.SampleRate, 16, sourceFormat.Channels));
            
            var targetFormat = new WaveFormat(TargetSampleRate, 16, 1);
            using (var resampler = new MediaFoundationResampler(pcmStream, targetFormat))
            {
                resampler.ResamplerQuality = 60;
                using (var outputStream = new MemoryStream())
                {
                    WaveFileWriter.WriteWavFileToStream(outputStream, resampler);
                    return outputStream.ToArray();
                }
            }
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
        isRunning = false;
        RestoreSystemVolume();
        audioEndpoint?.Dispose();
        ttsSemaphore?.Dispose();
        // speechClient doesn't need disposal
        GC.SuppressFinalize(this);
    }
}