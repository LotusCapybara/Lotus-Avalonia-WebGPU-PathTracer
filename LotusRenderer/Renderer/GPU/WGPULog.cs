using System;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.WebGPU;

// a rough implementation of log parsing from web-gpu rust backend
// so I can work in a more human way
public static class WGPULog
{
    public static void LogUncaptured(ErrorType type, string msg)
    {
        // Separate the Header from the Tree
        int treeIndex = msg.IndexOf("┌─", StringComparison.Ordinal);
        string headerPart = treeIndex >= 0 ? msg.Substring(0, treeIndex) : msg;
        string treePart = treeIndex >= 0 ? msg.Substring(treeIndex) : "";

        // Clean the Header (Remove excessive whitespace)
        string cleanHeader = Regex.Replace(headerPart, @"\s{2,}", " ");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkRed;
        Console.Write($" [WebGPU {type}] ");
        Console.ResetColor();
        Console.WriteLine();

        // Parse Header Logic (Your existing logic)
        var parts = cleanHeader.Split("Caused by:");

        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: {parts[0].Trim()}");
        }

        if (parts.Length > 1)
        {
            string details = parts[1];

            var locationMatch = Regex.Match(details, @"In\s(\w+)");
            if (locationMatch.Success)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Location: {locationMatch.Groups[1].Value}");
                details = details.Replace(locationMatch.Value, "").Trim();
            }

            var noteMatch = Regex.Match(details, @"note:\s(label\s?=\s?`[^`]+`)");
            if (noteMatch.Success)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  {noteMatch.Groups[1].Value.Replace("`", "'")}");
                details = details.Replace(noteMatch.Value, "").Replace("note:", "").Trim();
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"  Details: {details}");
            }
        }

        // print re formatted tree
        if (!string.IsNullOrEmpty(treePart))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(ReformatShaderTree(treePart));
        }

        Console.ResetColor();
        Console.WriteLine("----------------------------------------");
        
        throw new Exception($"WebGPU Validation Error: {parts[0].Trim()}");
    }

    private static string ReformatShaderTree(string rawTree)
    {
        if (string.IsNullOrWhiteSpace(rawTree)) return "";
        
        rawTree = rawTree
            .Replace("┌", "+")
            .Replace("│", "|")
            .Replace("─", "-")
            .Replace("└", "+")
            .Replace("┘", "+");

        var lines = rawTree.Split('\n');
        var sb = new StringBuilder();

        // Find the indentation of the second line (apparently it starts always with pipe or code but
        // I'll add more cases if there are
        // We use the second line because the first line (┌─) position is relative to the previous text.
        int minIndent = int.MaxValue;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            int currentIndent = 0;
            foreach (char c in lines[i])
            {
                if (c != ' ') break;
                currentIndent++;
            }
            
            if (currentIndent < minIndent) minIndent = currentIndent;
        }

        if (minIndent == int.MaxValue) minIndent = 0;

        // Rebuild the string, removing 'minIndent' from the start of every line
        // Special case for line 0 (┌─), I give it a little identation
        
        sb.AppendLine("  " + lines[0].TrimStart()); // Force the first line to the left

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length > minIndent)
            {
                // Remove the wasted space
                sb.AppendLine("  " + line.Substring(minIndent)); 
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }
}