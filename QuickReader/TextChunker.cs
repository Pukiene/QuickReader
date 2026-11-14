using System.Text;
using System.Text.RegularExpressions;

namespace QuickReader;

/// <summary>
/// Tidies text copied from web pages, PDFs etc. and cuts it into short pieces,
/// so reading can start straight away instead of waiting for a whole page to be prepared.
/// </summary>
internal static class TextChunker
{
    private const int FirstPieceLength = 140; // short, so the voice starts quickly
    private const int PieceLength = 380;

    private static readonly Regex Spaces = new(@"[ \t  -   　]+");
    private static readonly Regex Invisible = new(@"[­​-‍⁠﻿]");
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…][""'”’)\]]?)\s+");
    private static readonly Regex Bullet = new(@"^[•◦▪▫‣⁃●○■□►✓✔*\-–—]\s+");

    public static List<string> Split(string text)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();

        foreach (string sentence in Sentences(Normalize(text)))
        {
            foreach (string part in BreakLongSentence(sentence, PieceLength))
            {
                int limit = pieces.Count == 0 ? FirstPieceLength : PieceLength;
                if (current.Length > 0 && current.Length + 1 + part.Length > limit)
                {
                    pieces.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(part);
            }
        }

        if (current.Length > 0)
            pieces.Add(current.ToString());
        return pieces;
    }

    /// <summary>
    /// Joins lines that were only wrapped (common in PDFs), keeps real line breaks
    /// (headings, list items) as pauses, and removes invisible characters.
    /// </summary>
    internal static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Invisible.Replace(text, string.Empty);

        var result = new StringBuilder(text.Length);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = Bullet.Replace(Spaces.Replace(rawLine, " ").Trim(), string.Empty);
            if (line.Length == 0)
            {
                if (result.Length > 0 && result[^1] != '\n')
                {
                    EndWithPause(result);
                    result.Append('\n');
                }
                continue;
            }

            if (result.Length > 0 && result[^1] != '\n')
            {
                char last = result[^1];
                bool continues = char.IsLower(line[0]);
                if (continues && last == '-' && result.Length > 1 && char.IsLetter(result[^2]))
                {
                    result.Length--;          // "exam-" + "ple" -> "example"
                }
                else if (continues)
                {
                    result.Append(' ');       // sentence wrapped onto the next line
                }
                else
                {
                    EndWithPause(result);     // heading or list item: pause after it
                    result.Append('\n');
                }
            }

            result.Append(line);
        }

        return result.ToString().Trim();
    }

    private static void EndWithPause(StringBuilder text)
    {
        if (text.Length > 0 && char.IsLetterOrDigit(text[^1]))
            text.Append('.');
    }

    private static IEnumerable<string> Sentences(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            foreach (string sentence in SentenceEnd.Split(line))
            {
                string s = sentence.Trim();
                if (s.Length > 0)
                    yield return s;
            }
        }
    }

    /// <summary>Very long sentences are cut at a comma or semicolon, otherwise at a space.</summary>
    private static IEnumerable<string> BreakLongSentence(string sentence, int maxLength)
    {
        while (sentence.Length > maxLength)
        {
            int cut = -1;
            foreach (char mark in new[] { ';', ':', ',', ')', '–', '—' })
            {
                int i = sentence.LastIndexOf(mark, maxLength - 1);
                if (i > maxLength / 3)
                {
                    cut = Math.Max(cut, i + 1);
                }
            }
            if (cut < 0)
                cut = sentence.LastIndexOf(' ', maxLength - 1);
            if (cut <= 0)
                cut = maxLength;

            yield return sentence[..cut].Trim();
            sentence = sentence[cut..].TrimStart();
        }

        if (sentence.Length > 0)
            yield return sentence;
    }
}
