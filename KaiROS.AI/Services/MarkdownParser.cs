using System.Text.RegularExpressions;

namespace KaiROS.AI.Services;

public class MarkdownParser
{
    // Pattern to match code blocks: ```language\ncode\n```
    private static readonly Regex CodeBlockPattern = new(
        @"```(\w*)\n?([\s\S]*?)```",
        RegexOptions.Compiled);

    // Pattern to match inline code: `code`
    private static readonly Regex InlineCodePattern = new(
        @"`([^`]+)`",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses markdown content and returns a list of content segments
    /// </summary>
    public static List<MarkdownSegment> Parse(string content)
    {
        var segments = new List<MarkdownSegment>();
        var lastIndex = 0;

        // Find all code blocks
        var matches = CodeBlockPattern.Matches(content);

        foreach (Match match in matches)
        {
            // Add text before this code block
            if (match.Index > lastIndex)
            {
                var textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrWhiteSpace(textBefore))
                {
                    segments.Add(new MarkdownSegment
                    {
                        Type = SegmentType.Text,
                        Content = textBefore.Trim()
                    });
                }
            }

            // Add the code block
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value.Trim();

            segments.Add(new MarkdownSegment
            {
                Type = SegmentType.CodeBlock,
                Content = code,
                Language = string.IsNullOrEmpty(language) ? "code" : language
            });

            lastIndex = match.Index + match.Length;
        }

        // Check for unclosed code block at the end (for streaming)
        if (lastIndex < content.Length)
        {
            var remaining = content.Substring(lastIndex);
            // If the remaining text starts with ``` but matches failed (unclosed), treat as code block
            var unclosedMatch = Regex.Match(remaining, @"^```(\w*)\n?([\s\S]*)");
            if (unclosedMatch.Success)
            {
                var language = unclosedMatch.Groups[1].Value;
                var code = unclosedMatch.Groups[2].Value; // No trim at end to preserve typing

                segments.Add(new MarkdownSegment
                {
                    Type = SegmentType.CodeBlock,
                    Content = code,
                    Language = string.IsNullOrEmpty(language) ? "code" : language
                });
            }
            else if (!string.IsNullOrWhiteSpace(remaining))
            {
                segments.Add(new MarkdownSegment
                {
                    Type = SegmentType.Text,
                    Content = remaining.Trim()
                });
            }
        }
        else if (segments.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            // Fallback for single unclosed block at start
            var unclosedMatch = Regex.Match(content, @"^```(\w*)\n?([\s\S]*)");
            if (unclosedMatch.Success)
            {
                segments.Add(new MarkdownSegment
                {
                    Type = SegmentType.CodeBlock,
                    Content = unclosedMatch.Groups[2].Value,
                    Language = unclosedMatch.Groups[1].Value
                });
            }
            else
            {
                segments.Add(new MarkdownSegment
                {
                    Type = SegmentType.Text,
                    Content = content
                });
            }
        }

        return segments;
    }

    /// <summary>
    /// Detects if content contains code blocks
    /// </summary>
    public static bool HasCodeBlocks(string content)
    {
        return CodeBlockPattern.IsMatch(content);
    }

    /// <summary>
    /// Attempts to detect programming language from code content
    /// </summary>
    public static string DetectLanguage(string code)
    {
        // Simple heuristics
        if (code.Contains("using System") || code.Contains("namespace ") || code.Contains("public class"))
            return "csharp";
        if (code.Contains("import React") || code.Contains("const ") || code.Contains("function ") || code.Contains("=>"))
            return "javascript";
        if (code.Contains("def ") || code.Contains("import ") && code.Contains(":"))
            return "python";
        if (code.Contains("SELECT ") || code.Contains("FROM ") || code.Contains("WHERE "))
            return "sql";
        if (code.Contains("<html") || code.Contains("<div") || code.Contains("</"))
            return "html";
        if (code.Contains("{") && code.Contains(":") && code.Contains(";") && !code.Contains("function"))
            return "css";
        if (code.Contains("#!/bin/bash") || code.Contains("echo ") || code.Contains("$"))
            return "bash";

        return "code";
    }
}

public class MarkdownSegment
{
    public SegmentType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = "code";
}

public enum SegmentType
{
    Text,
    CodeBlock,
    InlineCode
}
