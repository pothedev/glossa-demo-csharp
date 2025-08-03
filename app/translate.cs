using System;
using DotNetEnv;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;


public static class Translator
{
    public static async Task<string> Translate(string text, string languageFrom, string languageTo)
    {
        string DeeplKey = Environment.GetEnvironmentVariable("DEEPL_API_KEY");
        if (string.IsNullOrEmpty(DeeplKey))
            throw new Exception("DEEPL_API_KEY is missing in .env or environment.");

        using var client = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("auth_key", DeeplKey),
            new KeyValuePair<string, string>("text", text),
            new KeyValuePair<string, string>("source_lang", languageFrom),
            new KeyValuePair<string, string>("target_lang", languageTo)
        });

        var response = await client.PostAsync("https://api-free.deepl.com/v2/translate", content);
        var responseString = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"DeepL API error: {response.StatusCode} - {responseString}");

        using var doc = JsonDocument.Parse(responseString);
        return doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();
    }
}
