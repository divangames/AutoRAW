using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Undo/redo для правок редактора кадра.</summary>
public sealed class EditorAdjustUndoStack
{
    private const int MaxDepth = 48;
    private readonly Stack<ManualShotAdjust> _undo = new();
    private readonly Stack<ManualShotAdjust> _redo = new();
    private ManualShotAdjust _lastSnapshot = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Reset(ManualShotAdjust current)
    {
        _undo.Clear();
        _redo.Clear();
        _lastSnapshot = current.Clone();
    }

    /// <summary>Зафиксировать снимок после паузы правок (в undo уходит предыдущий).</summary>
    public void CommitSnapshot(ManualShotAdjust current)
    {
        if (AdjustEquals(current, _lastSnapshot))
            return;

        _undo.Push(_lastSnapshot.Clone());
        Trim(_undo);
        _redo.Clear();
        _lastSnapshot = current.Clone();
    }

    public ManualShotAdjust? TryUndo(ManualShotAdjust current)
    {
        if (_undo.Count == 0)
            return null;

        _redo.Push(current.Clone());
        var prev = _undo.Pop();
        _lastSnapshot = prev.Clone();
        return prev;
    }

    public ManualShotAdjust? TryRedo(ManualShotAdjust current)
    {
        if (_redo.Count == 0)
            return null;

        _undo.Push(current.Clone());
        var next = _redo.Pop();
        _lastSnapshot = next.Clone();
        return next;
    }

    private static void Trim(Stack<ManualShotAdjust> stack)
    {
        while (stack.Count > MaxDepth)
        {
            var list = stack.Reverse().Skip(stack.Count - MaxDepth).ToList();
            stack.Clear();
            foreach (var item in list.AsEnumerable().Reverse())
                stack.Push(item);
        }
    }

    private static bool AdjustEquals(ManualShotAdjust a, ManualShotAdjust b) =>
        Math.Abs(a.OffsetX - b.OffsetX) < 0.05
        && Math.Abs(a.OffsetY - b.OffsetY) < 0.05
        && Math.Abs(a.ZoomPercent - b.ZoomPercent) < 0.05
        && Math.Abs(a.RotationDeg - b.RotationDeg) < 0.02
        && a.GridOverlay == b.GridOverlay;
}
