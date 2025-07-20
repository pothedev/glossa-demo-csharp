using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;


class Program
{
    static async Task Main()
    {
        Console.WriteLine($"DLL exists: {File.Exists("VoicemeeterRemote64.dll")}");
        Console.WriteLine($"Current directory: {Environment.CurrentDirectory}");

        // var enumerator = new MMDeviceEnumerator();
        // var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        // foreach (var dev in devices)
        // {
        //     Console.WriteLine($"{dev.FriendlyName}"); // List all outputs
        // }

        Console.WriteLine("=== Voice Processor Menu ===");
        Console.WriteLine("1. Test Input");
        Console.WriteLine("2. Test Output");
        Console.Write("Choose an option (1 or 2): ");

        var key = Console.ReadKey().KeyChar;
        Console.WriteLine("\n");

        switch (key)
        {
            case '1':
                var inputProcessor = new InputProcessor();
                await inputProcessor.Start();
                break;

            case '2':
              // In your main program:
              var processor = new OutputProcessor();
              await processor.StartContinuousListening(); // Runs forever
              break;

            default:
                Console.WriteLine("❌ Invalid option.");
                break;
        }
    }
}
