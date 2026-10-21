using System.Text;
using System.Text.RegularExpressions;

namespace CaissaDesk.Core;

public static class PgnHandler
{
    public static string ExportPgn(GameTree tree)
    {
        var sb = new StringBuilder();

        // 1. Headers
        foreach (var (k, v) in tree.Headers)
        {
            sb.AppendLine($"[{k} \"{v}\"]");
        }
        sb.AppendLine();

        // 2. Movetext
        if (tree.Root.Children.Count > 0)
        {
            FormatNodeChildren(tree.Root, sb, true, forceMoveNumber: false);
        }

        sb.Append(' ');
        sb.Append(tree.Headers.TryGetValue("Result", out var res) ? res : "*");
        sb.AppendLine();

        return sb.ToString();
    }

    private static void FormatNodeChildren(MoveNode parent, StringBuilder sb, bool isMainline, bool forceMoveNumber = false)
    {
        if (parent.IsRoot && !string.IsNullOrWhiteSpace(parent.Comment))
        {
            sb.Append($"{{{parent.Comment.Trim()}}}");
        }

        if (parent.Children.Count == 0) return;

        // First child is the mainline continuation
        var mainChild = parent.Children[0];
        FormatSingleNode(mainChild, sb, forceMoveNumber: forceMoveNumber);

        bool hadInterruption = false;
        // Subsequent children are variations
        for (int i = 1; i < parent.Children.Count; i++)
        {
            sb.Append(" (");
            var varChild = parent.Children[i];
            FormatSingleNode(varChild, sb, forceMoveNumber: true);
            FormatNodeChildren(varChild, sb, false);
            sb.Append(')');
            hadInterruption = true;
        }

        if (!string.IsNullOrWhiteSpace(mainChild.Comment))
        {
            hadInterruption = true;
        }

        // Continue mainline
        FormatNodeChildren(mainChild, sb, isMainline, forceMoveNumber: hadInterruption);
    }

    private static void FormatSingleNode(MoveNode node, StringBuilder sb, bool forceMoveNumber = false)
    {
        sb.Append(' ');
        if (node.IsWhiteMove || forceMoveNumber)
        {
            sb.Append(node.MoveNumber);
            sb.Append(node.IsWhiteMove ? ". " : "... ");
        }

        sb.Append(node.San);

        foreach (var nag in node.Nags)
        {
            sb.Append($" ${nag}");
        }

        if (!string.IsNullOrWhiteSpace(node.Clock) && !string.IsNullOrWhiteSpace(node.Comment))
        {
            sb.Append($" {{[%clk {node.Clock}] {node.Comment.Trim()}}}");
        }
        else if (!string.IsNullOrWhiteSpace(node.Clock))
        {
            sb.Append($" {{[%clk {node.Clock}]}}");
        }
        else if (!string.IsNullOrWhiteSpace(node.Comment))
        {
            sb.Append($" {{{node.Comment.Trim()}}}");
        }
    }

    public static GameTree ImportPgn(string pgnText)
    {
        if (string.IsNullOrWhiteSpace(pgnText))
            return new GameTree();

        var headerRegex = new Regex(@"\[(\w+)\s+""([^""]*)""\]");
        var matches = headerRegex.Matches(pgnText);

        string? startFen = null;
        foreach (Match m in matches)
        {
            if (string.Equals(m.Groups[1].Value, "FEN", StringComparison.OrdinalIgnoreCase))
            {
                startFen = m.Groups[2].Value;
                break;
            }
        }

        var tree = new GameTree(startFen);
        foreach (Match m in matches)
        {
            tree.Headers[m.Groups[1].Value] = m.Groups[2].Value;
        }

        // Remove headers
        string movetext = headerRegex.Replace(pgnText, "").Trim();

        // Tokenize movetext
        var tokens = TokenizeMovetext(movetext);
        ParseTokensIntoTree(tree, tokens);

        tree.GoToStart();
        return tree;
    }

    private static List<string> TokenizeMovetext(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '{')
            {
                int end = text.IndexOf('}', i);
                if (end == -1) end = len - 1;
                tokens.Add(text.Substring(i, end - i + 1));
                i = end + 1;
            }
            else if (c == '(' || c == ')')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                int start = i;
                while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '{')
                {
                    i++;
                }
                tokens.Add(text.Substring(start, i - start));
            }
        }

        return tokens;
    }

    private static void ParseTokensIntoTree(GameTree tree, List<string> tokens)
    {
        var branchStack = new Stack<MoveNode>();

        foreach (var tok in tokens)
        {
            if (tok == "(")
            {
                // Push current parent onto stack to branch
                if (tree.CurrentNode.Parent != null)
                {
                    branchStack.Push(tree.CurrentNode);
                    tree.NavigateTo(tree.CurrentNode.Parent);
                }
            }
            else if (tok == ")")
            {
                // Pop back to continuation of previous branch
                if (branchStack.Count > 0)
                {
                    var returnNode = branchStack.Pop();
                    tree.NavigateTo(returnNode);
                }
            }
            else if (tok.StartsWith("{") && tok.EndsWith("}"))
            {
                string comment = tok.Substring(1, tok.Length - 2).Trim();
                ParseAndAttachComment(tree.CurrentNode, comment);
            }
            else if (tok.StartsWith("$") && int.TryParse(tok.Substring(1), out int nag))
            {
                if (!tree.CurrentNode.IsRoot && !tree.CurrentNode.Nags.Contains(nag))
                {
                    tree.CurrentNode.Nags.Add(nag);
                }
            }
            else if (tok.EndsWith(".") || Regex.IsMatch(tok, @"^\d+\.*$"))
            {
                // Move numbers like 1. or 1... - skip
                continue;
            }
            else if (tok == "1-0" || tok == "0-1" || tok == "1/2-1/2" || tok == "*")
            {
                tree.Headers["Result"] = tok;
            }
            else
            {
                // Must be a move SAN. Strip a glued leading move number first — tournament PGNs
                // often have no space after it ("1.e4" -> "e4", "12...Nf6" -> "Nf6"); without this
                // SanParser fails to parse the token and the move is silently dropped.
                string san = MoveNumberPrefixRegex.Replace(tok, "");
                if (string.IsNullOrWhiteSpace(san))
                    continue;

                var move = SanParser.ParseSan(tree.CurrentNode.Position, san);
                if (!move.IsEmpty)
                {
                    tree.AddMove(move);
                }
            }
        }
    }

    private static readonly Regex ClkRegex = new(@"\[%clk\s+([0-9:.]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EvalRegex = new(@"\[%eval\s+([#+-]?[0-9.]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MachineTagRegex = new(@"\[%[a-zA-Z0-9_]+(?:\s+[^\]]*)?\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveNumberPrefixRegex = new(@"^\d+\.+", RegexOptions.Compiled);

    private static void ParseAndAttachComment(MoveNode node, string rawComment)
    {
        if (string.IsNullOrWhiteSpace(rawComment)) return;

        // Extract clock time if present
        var clkMatch = ClkRegex.Match(rawComment);
        if (clkMatch.Success)
        {
            node.Clock = clkMatch.Groups[1].Value;
            rawComment = ClkRegex.Replace(rawComment, "");
        }

        // Extract engine eval if present
        var evalMatch = EvalRegex.Match(rawComment);
        if (evalMatch.Success)
        {
            string evalVal = evalMatch.Groups[1].Value;
            if (!evalVal.StartsWith("+") && !evalVal.StartsWith("-") && !evalVal.StartsWith("#"))
            {
                evalVal = "+" + evalVal;
            }
            node.Eval = evalVal;
            rawComment = EvalRegex.Replace(rawComment, "");
        }

        // Strip any remaining machine annotations (e.g. [%emt ...], [%csl ...], [%cal ...])
        rawComment = MachineTagRegex.Replace(rawComment, "");

        // Keep real human commentary
        string cleaned = Regex.Replace(rawComment, @"\s+", " ").Trim();
        if (!string.IsNullOrEmpty(cleaned))
        {
            node.Comment = string.IsNullOrEmpty(node.Comment)
                ? cleaned
                : $"{node.Comment} {cleaned}";
        }
    }
}
