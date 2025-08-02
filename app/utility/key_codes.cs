using System;
using System.Collections.Generic;

public static class KeyCodeTranslator
{
    private static readonly Dictionary<int, string> KeyCodeToName = new Dictionary<int, string>
    {
        // Modifier Keys
        {0xA0, "Left Shift"},
        {0xA1, "Right Shift"},
        {0xA2, "Left Control"},
        {0xA3, "Right Control"},
        {0xA4, "Left Alt"},
        {0xA5, "Right Alt"},

        // Alphabet Keys
        {0x41, "A"}, {0x42, "B"}, {0x43, "C"}, {0x44, "D"}, {0x45, "E"},
        {0x46, "F"}, {0x47, "G"}, {0x48, "H"}, {0x49, "I"}, {0x4A, "J"},
        {0x4B, "K"}, {0x4C, "L"}, {0x4D, "M"}, {0x4E, "N"}, {0x4F, "O"},
        {0x50, "P"}, {0x51, "Q"}, {0x52, "R"}, {0x53, "S"}, {0x54, "T"},
        {0x55, "U"}, {0x56, "V"}, {0x57, "W"}, {0x58, "X"}, {0x59, "Y"},
        {0x5A, "Z"},

        // Number Keys
        {0x30, "0"}, {0x31, "1"}, {0x32, "2"}, {0x33, "3"}, {0x34, "4"},
        {0x35, "5"}, {0x36, "6"}, {0x37, "7"}, {0x38, "8"}, {0x39, "9"},

        // Function Keys
        {0x70, "F1"}, {0x71, "F2"}, {0x72, "F3"}, {0x73, "F4"},
        {0x74, "F5"}, {0x75, "F6"}, {0x76, "F7"}, {0x77, "F8"},
        {0x78, "F9"}, {0x79, "F10"}, {0x7A, "F11"}, {0x7B, "F12"},
        {0x7C, "F13"}, {0x7D, "F14"}, {0x7E, "F15"}, {0x7F, "F16"},
        {0x80, "F17"}, {0x81, "F18"}, {0x82, "F19"}, {0x83, "F20"},
        {0x84, "F21"}, {0x85, "F22"}, {0x86, "F23"}, {0x87, "F24"},

        // Arrow and Navigation
        {0x25, "Left Arrow"}, {0x26, "Up Arrow"}, {0x27, "Right Arrow"}, {0x28, "Down Arrow"},
        {0x21, "Page Up"}, {0x22, "Page Down"}, {0x23, "End"}, {0x24, "Home"},

        // Special Characters and Keys
        {0x1B, "Escape"}, {0x20, "Space"}, {0x0D, "Enter"}, {0x09, "Tab"},
        {0x2D, "Insert"}, {0x2E, "Delete"}, {0x08, "Backspace"},

        // Numpad Keys
        {0x60, "NumPad 0"}, {0x61, "NumPad 1"}, {0x62, "NumPad 2"}, {0x63, "NumPad 3"},
        {0x64, "NumPad 4"}, {0x65, "NumPad 5"}, {0x66, "NumPad 6"}, {0x67, "NumPad 7"},
        {0x68, "NumPad 8"}, {0x69, "NumPad 9"}, {0x6A, "Multiply"}, {0x6B, "Add"},
        {0x6C, "Separator"}, {0x6D, "Subtract"}, {0x6E, "Decimal"}, {0x6F, "Divide"},

        // Symbols and Others
        {0xBA, ";"}, {0xBB, "="}, {0xBC, ","}, {0xBD, "-"}, {0xBE, "."},
        {0xBF, "/"}, {0xC0, "`"}, {0xDB, "["}, {0xDC, "\\"}, {0xDD, "]"}, {0xDE, "'"},

        // Others
        {0x5B, "Left Windows"}, {0x5C, "Right Windows"}, {0x5D, "Applications"},
        {0x90, "Num Lock"}, {0x91, "Scroll Lock"}, {0x14, "Caps Lock"},
    };

    public static string GetKeyName(int vkCode)
    {
        if (KeyCodeToName.TryGetValue(vkCode, out var name))
        {
            return name;
        }
        return $"Unknown Key (0x{vkCode:X})";
    }
}
