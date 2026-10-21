namespace CaissaDesk.Core;

public class GameTree
{
    public MoveNode Root { get; private set; }
    public MoveNode CurrentNode { get; private set; }
    public Dictionary<string, string> Headers { get; } = new();

    public event Action? PositionChanged;

    public bool CanGoBack => CurrentNode.Parent != null;
    public bool CanGoForward => CurrentNode.Children.Count > 0;

    public GameTree(string? startFen = null)
    {
        var initialPos = FenParser.Parse(startFen ?? BoardPosition.StartFen);
        Root = new MoveNode(Move.Empty, "", initialPos);
        CurrentNode = Root;

        Headers["Event"] = "Casual Analysis";
        Headers["Site"] = "Caissalytics";
        Headers["Date"] = DateTime.UtcNow.ToString("yyyy.MM.dd");
        Headers["White"] = "White";
        Headers["Black"] = "Black";
        Headers["Result"] = "*";

        if (!string.IsNullOrWhiteSpace(startFen) && startFen != BoardPosition.StartFen)
        {
            Headers["SetUp"] = "1";
            Headers["FEN"] = startFen;
        }
    }

    public MoveNode AddMove(Move move)
    {
        var pos = CurrentNode.Position;
        string san = SanParser.ToSan(pos, move);
        var nextPos = MoveGenerator.ApplyMove(pos, move);

        // Check if move already exists as a child
        var existing = CurrentNode.Children.FirstOrDefault(c => c.Move.From == move.From && c.Move.To == move.To && c.Move.Promotion == move.Promotion);
        if (existing != null)
        {
            CurrentNode = existing;
            PositionChanged?.Invoke();
            return existing;
        }

        var newNode = new MoveNode(move, san, nextPos, CurrentNode);
        CurrentNode.Children.Add(newNode);
        CurrentNode = newNode;

        if (MoveGenerator.IsCheckmate(nextPos))
        {
            if (!Headers.ContainsKey("Result") || Headers["Result"] == "*")
            {
                Headers["Result"] = nextPos.ActiveColor == PieceColor.Black ? "1-0" : "0-1";
                Headers["Termination"] = "Checkmate";
            }
        }
        else if (MoveGenerator.IsStalemate(nextPos))
        {
            if (!Headers.ContainsKey("Result") || Headers["Result"] == "*")
            {
                Headers["Result"] = "1/2-1/2";
                Headers["Termination"] = "Stalemate";
            }
        }

        PositionChanged?.Invoke();
        return newNode;
    }

    public MoveNode? AddMoveSan(string san)
    {
        var move = SanParser.ParseSan(CurrentNode.Position, san);
        if (move.IsEmpty) return null;
        return AddMove(move);
    }

    public bool GoBack()
    {
        if (CurrentNode.Parent != null)
        {
            CurrentNode = CurrentNode.Parent;
            PositionChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool GoForward(int childIndex = 0)
    {
        if (childIndex >= 0 && childIndex < CurrentNode.Children.Count)
        {
            CurrentNode = CurrentNode.Children[childIndex];
            PositionChanged?.Invoke();
            return true;
        }
        return false;
    }

    public bool GoToNextVariation()
    {
        // 1. If current node's parent has multiple branches, switch to the next sibling variation
        if (CurrentNode.Parent != null && CurrentNode.Parent.Children.Count > 1)
        {
            int currentIdx = CurrentNode.Parent.Children.IndexOf(CurrentNode);
            if (currentIdx >= 0 && currentIdx < CurrentNode.Parent.Children.Count - 1)
            {
                CurrentNode = CurrentNode.Parent.Children[currentIdx + 1];
                PositionChanged?.Invoke();
                return true;
            }
            // If already at last sibling, wrap to first
            if (currentIdx == CurrentNode.Parent.Children.Count - 1)
            {
                CurrentNode = CurrentNode.Parent.Children[0];
                PositionChanged?.Invoke();
                return true;
            }
        }

        // 2. Or if current node has variations forward (more than 1 child), step into variation index 1
        if (CurrentNode.Children.Count > 1)
        {
            CurrentNode = CurrentNode.Children[1];
            PositionChanged?.Invoke();
            return true;
        }

        return false;
    }

    public bool GoToPreviousVariation()
    {
        // 1. If current node's parent has multiple branches, switch to the previous sibling variation
        if (CurrentNode.Parent != null && CurrentNode.Parent.Children.Count > 1)
        {
            int currentIdx = CurrentNode.Parent.Children.IndexOf(CurrentNode);
            if (currentIdx > 0)
            {
                CurrentNode = CurrentNode.Parent.Children[currentIdx - 1];
                PositionChanged?.Invoke();
                return true;
            }
            // If at first sibling, wrap to last
            if (currentIdx == 0)
            {
                CurrentNode = CurrentNode.Parent.Children[^1];
                PositionChanged?.Invoke();
                return true;
            }
        }

        return false;
    }

    public void GoToStart()
    {
        CurrentNode = Root;
        PositionChanged?.Invoke();
    }

    public void GoToEnd()
    {
        while (CurrentNode.Children.Count > 0)
        {
            CurrentNode = CurrentNode.Children[0];
        }
        PositionChanged?.Invoke();
    }

    public void NavigateTo(MoveNode target)
    {
        CurrentNode = target;
        PositionChanged?.Invoke();
    }

    public void PromoteVariation(MoveNode node)
    {
        var curr = node;
        bool changed = false;
        while (curr.Parent != null)
        {
            int idx = curr.Parent.Children.IndexOf(curr);
            if (idx > 0)
            {
                curr.Parent.Children.RemoveAt(idx);
                curr.Parent.Children.Insert(0, curr);
                changed = true;
            }
            curr = curr.Parent;
        }
        if (changed)
        {
            PositionChanged?.Invoke();
        }
    }

    public MoveNode? FindDeepestLeaf()
    {
        return FindDeepestLeaf(Root);
    }

    private static MoveNode? FindDeepestLeaf(MoveNode node)
    {
        if (node.Children.Count == 0) return node.IsRoot ? null : node;
        MoveNode? best = null;
        int bestDepth = -1;
        foreach (var child in node.Children)
        {
            var leaf = FindDeepestLeaf(child);
            if (leaf != null)
            {
                int depth = GetDepth(leaf);
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = leaf;
                }
            }
        }
        return best;
    }

    public int GetMainlineDepth()
    {
        int depth = 0;
        var curr = Root;
        while (curr.Children.Count > 0)
        {
            depth++;
            curr = curr.Children[0];
        }
        return depth;
    }

    public static int GetDepth(MoveNode node)
    {
        int depth = 0;
        var curr = node;
        while (curr.Parent != null)
        {
            depth++;
            curr = curr.Parent;
        }
        return depth;
    }

    public void DeleteSubtree(MoveNode node)
    {
        if (node.Parent == null) return;
        var parent = node.Parent;
        parent.Children.Remove(node);
        if (CurrentNode == node || IsDescendant(node, CurrentNode))
        {
            CurrentNode = parent;
        }
        PositionChanged?.Invoke();
    }

    private static bool IsDescendant(MoveNode ancestor, MoveNode node)
    {
        var curr = node;
        while (curr.Parent != null)
        {
            if (curr.Parent == ancestor) return true;
            curr = curr.Parent;
        }
        return false;
    }

    public List<int> GetCurrentNodePath()
    {
        var path = new List<int>();
        var curr = CurrentNode;
        while (curr.Parent != null)
        {
            int idx = curr.Parent.Children.IndexOf(curr);
            if (idx == -1) break;
            path.Add(idx);
            curr = curr.Parent;
        }
        path.Reverse();
        return path;
    }

    public void NavigatePath(IReadOnlyList<int> path)
    {
        var curr = Root;
        foreach (int idx in path)
        {
            if (idx >= 0 && idx < curr.Children.Count)
            {
                curr = curr.Children[idx];
            }
            else
            {
                break;
            }
        }
        NavigateTo(curr);
    }
}
