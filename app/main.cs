using System;
using System.IO;
using System.Threading.Tasks;
using DotNetEnv;

class Program
{
    static async Task Main()
    {
        Env.Load("../.env");
        int PTTKey = Config.PushToTalkKey;

        Console.WriteLine("=== Voice Processor Menu ===");
        Console.WriteLine($"Push to talk key: 0x{PTTKey:X}, {KeyCodeTranslator.GetKeyName(PTTKey)}");
        Console.WriteLine("1. Run Input Only");
        Console.WriteLine("2. Run Output Only");
        Console.WriteLine("3. Run Both Simultaneously");
        Console.Write("Choose an option (1, 2, or 3): ");

        var key = Console.ReadKey().KeyChar;
        Console.WriteLine("\n");

        switch (key)
        {
            case '1':
                var inputProcessor = new InputProcessor();
                await inputProcessor.Start();
                break;

            case '2':
                var outputProcessor = new OutputProcessor();
                await outputProcessor.StartContinuousListening();
                break;

            case '3':
                var input = new InputProcessor();
                var output = new OutputProcessor();

                Task inputTask = Task.Run(() => input.Start());
                Task outputTask = Task.Run(() => output.StartContinuousListening());

                await Task.WhenAll(inputTask, outputTask);
                break;

            default:
                Console.WriteLine("❌ Invalid option.");
                break;
        }
    }
}
