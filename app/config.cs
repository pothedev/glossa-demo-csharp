using System;

public static class Config
{
  public static string LanguageFrom = "uk-UA";
  public static string LanguageTo = "en-US";
  public static string InputTTSModel = "Google"; // Google ElevenLabs Native
  public static string OutputTTSModel = "Native";
  public static int PushToTalkKey = 0xA4; //Left Alt
  public static bool InputTranslateEnabled = true;
  public static bool OutputTranslateEnabled = false;
  public static bool SubtitlesEnabled = true;

}

// Available (currently used) languages: en-UK(English) uk-UA(Ukrainian) et-EE(Estonian)