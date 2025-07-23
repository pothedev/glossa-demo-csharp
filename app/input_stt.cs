using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Google.Cloud.Speech.V1;
using NAudio.Wave;
using Google.Protobuf;

public class InputProcessor
{
    const int SampleRate = 16000;
    private readonly SpeechClient _speechClient;
    private readonly BlockingCollection<byte[]> _audioBufferQueue = new BlockingCollection<byte[]>(100);
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task _processingTask;

    public InputProcessor()
    {
        _speechClient = new SpeechClientBuilder
        {
            CredentialsPath = "../google-key.json"
        }.Build();
    }

    public async Task Start()
    {
        Console.WriteLine("🎤 Speak freely. Continuous listening active.\n");
        _processingTask = Task.Run(ProcessAudioBuffers);
        await StartContinuousRecognitionAsync();
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        _audioBufferQueue.CompleteAdding();
        await _processingTask;
    }

    private async Task StartContinuousRecognitionAsync()
    {
        using (var waveIn = new WaveInEvent())
        {
            waveIn.WaveFormat = new WaveFormat(SampleRate, 1);
            waveIn.BufferMilliseconds = 100;
            waveIn.DataAvailable += (s, e) => 
            {
                var buffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
                _audioBufferQueue.Add(buffer);
            };

            waveIn.StartRecording();
            Console.WriteLine("Microphone recording started...");
            await Task.Delay(-1, _cts.Token); // Block until canceled
            waveIn.StopRecording();
        }
    }

    private async Task ProcessAudioBuffers()
    {
        while (!_audioBufferQueue.IsCompleted)
        {
            try
            {
                // Create the streaming call
                var streamingCall = _speechClient.StreamingRecognize();

                // Start the writer task
                var writeTask = Task.Run(async () =>
                {
                    try
                    {
                        // First write the config
                        await streamingCall.WriteAsync(new StreamingRecognizeRequest
                        {
                            StreamingConfig = new StreamingRecognitionConfig
                            {
                                Config = new RecognitionConfig
                                {
                                    Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                                    SampleRateHertz = SampleRate,
                                    LanguageCode = Config.LanguageFrom,
                                    //EnableAutomaticPunctuation = true
                                    Model = "latest_long"
                                },
                                InterimResults = false,
                            }
                        });

                        // Then write the audio content
                        foreach (var buffer in _audioBufferQueue.GetConsumingEnumerable(_cts.Token))
                        {
                            await streamingCall.WriteAsync(new StreamingRecognizeRequest
                            {
                                AudioContent = ByteString.CopyFrom(buffer)
                            });
                        }
                    }
                    finally
                    {
                        await streamingCall.WriteCompleteAsync();
                    }
                });

                // Read responses
                await foreach (var response in streamingCall.GetResponseStream())
                {
                    foreach (var result in response.Results)
                    {
                        if (result.IsFinal)
                        {
                            await ProcessFinalResult(result);
                        }
                    }
                }

                await writeTask;
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Processing error: {ex.Message}");
                await Task.Delay(1000, _cts.Token);
            }
        }
    }

    private async Task ProcessFinalResult(StreamingRecognitionResult result)
    {
        try
        {
            string transcript = result.Alternatives[0].Transcript;
            Console.WriteLine($"✅ Final: {transcript}");

            string translated = await Translator.Translate(transcript);
            Console.WriteLine($"🌍 Translated: {translated}");

            switch (Config.InputTTSModel)
            {
                case "Google":
                    await InputTTS_Google.Speak(translated);
                    break;
                case "ElevenLabs":
                    await InputTTS_ElevenLabs.Speak(translated);
                    break;
                case "Native":
                    await InputTTS_Native.Speak(translated);
                    break;
                default:
                    await InputTTS_Native.Speak(translated);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Processing error: {ex.Message}");
        }
    }
}