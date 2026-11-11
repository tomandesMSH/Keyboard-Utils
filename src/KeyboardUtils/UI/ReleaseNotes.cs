using System.Text.RegularExpressions;

namespace KeyboardUtils;

enum NoteBlockKind { Heading, Bullet, Paragraph }

/// <summary>A run of text, bold or not.</summary>
sealed record NoteSpan(string Text, bool Bold);

/// <summary>One line of release notes: a heading, a bullet or a paragraph.</summary>
sealed record NoteBlock(NoteBlockKind Kind, IReadOnlyList<NoteSpan> Spans)
{
    public string Text => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// Release notes (GitHub's Markdown) as plain blocks for "What's new". Only for reading: headings, bullets and bold
/// stay; links keep their text; code, italics and other marks are dropped. Nothing in them is ever opened or run.
/// </summary>
static partial class ReleaseNotes
{
    public static List<NoteBlock> ToBlocks(string? markdown)
    {
        var blocks = new List<NoteBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
            return blocks;
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count > 0)
                blocks.Add(new NoteBlock(NoteBlockKind.Paragraph, Inline(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        foreach (string raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }
            if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                FlushParagraph();
                blocks.Add(new NoteBlock(NoteBlockKind.Heading, [new NoteSpan(Plain(heading.Groups[1].Value), true)]));
            }
            else if (BulletPattern().Match(line) is { Success: true } bullet)
            {
                FlushParagraph();
                blocks.Add(new NoteBlock(NoteBlockKind.Bullet, Inline(bullet.Groups[1].Value)));
            }
            else
                paragraph.Add(line);
        }
        FlushParagraph();
        return blocks;
    }

    /// <summary>A line's text with **bold** runs kept apart; every other mark removed.</summary>
    static List<NoteSpan> Inline(string text)
    {
        var spans = new List<NoteSpan>();
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            string plain = Plain(parts[i]);
            if (plain.Length > 0)
                spans.Add(new NoteSpan(plain, Bold: i % 2 == 1 && i < parts.Length - 1));
        }
        return spans;
    }

    /// <summary>Links → their text, `code` / *italics* / _italics_ / ~~strike~~ → the bare text.</summary>
    static string Plain(string text)
    {
        text = LinkPattern().Replace(text, "$1");
        text = text.Replace("`", "").Replace("~~", "").Replace("**", "");
        text = ItalicPattern().Replace(text, "$1$2");
        return text;
    }

    [GeneratedRegex(@"^#{1,6}\s+(.*?)\s*#*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)([^*]+?)\*(?![\w*])|(?<!\w)_(?!\s)([^_]+?)_(?!\w)")]
    private static partial Regex ItalicPattern();
}
