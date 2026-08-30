#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fodinae.Editor;

/// <summary>
/// Builds the deterministic character sets that the CJK font bootstrap and the
/// UI-font normalizer pre-warm into font assets. Latin fonts cover ASCII +
/// en/ru; when CJK is requested the zh/zh-hant dictionaries are added.
/// Shared by the editor tools so the sets never drift between the two pipelines
/// (UI Toolkit TextCore fonts and world-space TMPro fonts).
/// </summary>
internal static class CjkGlyphSetBuilder
{
    public const string EnglishLocalizationPath = "Assets/Resources/Localization/en.json";
    public const string RussianLocalizationPath = "Assets/Resources/Localization/ru.json";
    public const string SimplifiedLocalizationPath = "Assets/Resources/Localization/zh.json";
    public const string TraditionalLocalizationPath = "Assets/Resources/Localization/zh-hant.json";

    private const string RussianAlphabet =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";

    public static string BuildCharacterSet(bool includeCjk)
    {
        var characters = new SortedSet<char>();
        AddRange(characters, 0x20, 0x7E);
        AddFileCharacters(characters, EnglishLocalizationPath);
        AddFileCharacters(characters, RussianLocalizationPath);
        if (includeCjk)
        {
            AddFileCharacters(characters, SimplifiedLocalizationPath);
            AddFileCharacters(characters, TraditionalLocalizationPath);
        }

        foreach (char character in RussianAlphabet)
        {
            characters.Add(character);
        }

        var result = new StringBuilder(characters.Count);
        foreach (char character in characters)
        {
            result.Append(character);
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats a string of characters as a readable, comma-separated list of
    /// U+XXXX code points for diagnostics.
    /// </summary>
    public static string FormatCodePoints(string characters)
    {
        if (string.IsNullOrEmpty(characters))
        {
            return "unknown";
        }

        var codePoints = new string[characters.Length];
        for (int i = 0; i < characters.Length; i++)
        {
            codePoints[i] = $"U+{(int)characters[i]:X4}";
        }

        return string.Join(", ", codePoints);
    }

    /// <summary>
    /// Adds every printable character of the JSON localization file at
    /// <paramref name="path"/> to <paramref name="characters"/>.
    /// </summary>
    private static void AddFileCharacters(ISet<char> characters, string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required localization file is missing.", path);
        }

        foreach (char character in File.ReadAllText(path))
        {
            if (!char.IsControl(character))
            {
                characters.Add(character);
            }
        }
    }

    private static void AddRange(ISet<char> characters, int first, int last)
    {
        for (int codePoint = first; codePoint <= last; codePoint++)
        {
            characters.Add((char)codePoint);
        }
    }
}
