using Godot;
using System;
using System.Collections.Generic;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;

namespace Cue2.Base.Classes;

/// <summary>
/// Encapsulates the complex state machine, mouse tracking, drop target calculation,
/// and commit/cancel logic for reordering cues (including nesting support).
/// This keeps CueList focused on higher-level concerns.
/// </summary>
internal sealed class CueReorder
{
    private readonly CueList _owner;

    public bool IsActive { get; private set; }
    public ShellBar MouseOverShellBar { get; private set; }
    public bool InsertAbove { get; private set; }
    public bool InsertBelow { get; private set; }
    public bool InsertMakeChild { get; private set; }
    public bool DropAtEndAsTopLevel { get; private set; }
    public int DraggedCueId { get; private set; } = -1;

    public CueReorder(CueList owner, Control reorderCueControl, Label reorderLocationLabel, VBoxContainer reorderListContainer, Panel reorderIndicatorPanel, VBoxContainer cueContainer)
    {
        _owner = owner;
        _reorderCueControl = reorderCueControl;
        _reorderLocationLabel = reorderLocationLabel;
        _reorderListContainer = reorderListContainer;
        _reorderIndicatorPanel = reorderIndicatorPanel;
        _cueContainer = cueContainer;
    }

    private readonly Control _reorderCueControl;
    private readonly Label _reorderLocationLabel;
    private readonly VBoxContainer _reorderListContainer;
    private readonly Panel _reorderIndicatorPanel;
    private readonly VBoxContainer _cueContainer;

    public void Start(ShellBar shellbar)
    {
        if (IsActive) return;

        if (!shellbar.Selected)
        {
            _owner.SelectIndividualForReorder(shellbar.CueId);
        }

        if (_reorderListContainer.GetChildCount() > 0)
        {
            foreach (var child in _reorderListContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        PrepareReorderPreviewLabels();

        _reorderCueControl.Visible = true;
        IsActive = true;
        DraggedCueId = shellbar.CueId;
        ResetDropFlags();
        MouseOverShellBar = shellbar;

        // Initial hover is the dragged item itself
    }

    public void ProcessInput(InputEvent @event)
    {
        if (!IsActive) return;

        if (@event is InputEventMouseMotion eventMouseMotion)
        {
            var reorderControl = _reorderCueControl;
            reorderControl.GlobalPosition = new Vector2(eventMouseMotion.Position.X, eventMouseMotion.Position.Y);
            UpdateDropTarget(eventMouseMotion.GlobalPosition.Y);
        }

        // Left release = commit
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            Commit();
        }

        // Cancel support (ESC or right-click)
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
        {
            Cancel();
        }
        if (@event is InputEventMouseButton rmb && rmb.ButtonIndex == MouseButton.Right && rmb.Pressed)
        {
            Cancel();
        }
    }

    private void UpdateDropTarget(float mouseY)
    {
        bool check = IsValidDropTarget();
        bool handledAsEnd = false;

        // Detect blank space below the *entire* list (after the visually last cue).
        // This should always result in a top-level append, even if the last cue is nested.
        var lastShell = _owner.GetLastVisibleShellBar();
        if (lastShell != null)
        {
            float lastBottom = lastShell.GetGlobalPosition().Y + lastShell.Size.Y;
            if (mouseY > lastBottom + 8)  // tolerance for "blank area below everything"
            {
                InsertAbove = false;
                InsertBelow = true;
                InsertMakeChild = false;
                DropAtEndAsTopLevel = true;

                _reorderLocationLabel.Text = "Reorder at end (top level)";
                var indicator = _reorderIndicatorPanel;
                indicator.GlobalPosition = new Vector2(_cueContainer.GetGlobalPosition().X, lastBottom);
                indicator.Size = new Vector2(_cueContainer.Size.X, 2);
                indicator.Visible = true;

                handledAsEnd = true;
                check = true;
            }
        }

        if (!handledAsEnd)
        {
            DropAtEndAsTopLevel = false;

            if (check && MouseOverShellBar != null)
            {
                var targetCueId = MouseOverShellBar.CueId;
                var shellPosY = MouseOverShellBar.GetGlobalPosition().Y;
                var shellSizeY = 26; // ShellHeight
                var margin = shellSizeY / 4;

                InsertAbove = mouseY < shellPosY + margin;
                InsertBelow = mouseY > shellPosY + margin * 3;
                var targetCue = CueList.FetchCueFromId(targetCueId);
                InsertMakeChild = targetCue != null && targetCue.ParentId != -1;

                string targetName = targetCue?.Name ?? "?";
                string parentName = "";
                if (InsertMakeChild && targetCue != null)
                {
                    var p = CueList.FetchCueFromId(targetCue.ParentId);
                    parentName = p?.Name ?? "?";
                }

                var label = _reorderLocationLabel;
                var indicator = _reorderIndicatorPanel;

                if (InsertBelow)
                {
                    label.Text = InsertMakeChild
                        ? $"Reorder below: {targetName} and child of: {parentName}"
                        : $"Reorder below: {targetName}";
                    indicator.GlobalPosition = new Vector2(MouseOverShellBar.GetGlobalPosition().X, MouseOverShellBar.GetGlobalPosition().Y + MouseOverShellBar.Size.Y);
                    indicator.Size = new Vector2(MouseOverShellBar.Size.X, 1);
                    indicator.Visible = true;
                }
                else if (InsertAbove)
                {
                    label.Text = InsertMakeChild
                        ? $"Reorder above: {targetName} and child of: {parentName}"
                        : $"Reorder above: {targetName}";
                    indicator.GlobalPosition = MouseOverShellBar.GetGlobalPosition();
                    indicator.Size = new Vector2(MouseOverShellBar.Size.X, 1);
                    indicator.Visible = true;
                }
                else
                {
                    label.Text = $"Make child of: {targetName}";
                    indicator.GlobalPosition = MouseOverShellBar.GetGlobalPosition();
                    indicator.Size = MouseOverShellBar.Size;
                    indicator.Visible = true;
                }
            }
            else
            {
                _reorderLocationLabel.Text = "Cannot reorder here";
                _reorderIndicatorPanel.Visible = false;
            }
        }
    }

    private bool IsValidDropTarget()
    {
        if (MouseOverShellBar == null) return false;
        var targetCue = CueList.FetchCueFromId(MouseOverShellBar.CueId);
        if (targetCue == null) return false;
        return !ShellSelection.SelectedCues.Contains(targetCue);
    }

    public void Commit()
    {
        if (!IsActive) return;

        // Validate location. Blank end-of-list is always allowed.
        bool isEndDrop = DropAtEndAsTopLevel;
        if (!isEndDrop && !IsValidDropTarget())
        {
            Cancel();
            return;
        }

        var targetCue = isEndDrop ? null : CueList.FetchCueFromId(MouseOverShellBar?.CueId ?? -1);
        if (!isEndDrop && targetCue == null)
        {
            Cancel();
            return;
        }

        // Check for cycles for any of the moved items (skip for end-of-list top level drop)
        if (!isEndDrop)
        {
            foreach (var sc in ShellSelection.SelectedCues)
            {
                int prospective = (!InsertAbove && !InsertBelow) ? targetCue.Id : targetCue.ParentId;
                if (WouldCreateCycle(sc, prospective))
                {
                    _owner.EmitLog($"CueList:EndReorder - Cycle would be created; aborting reorder for {sc.Name}", (int)LogType.Warning);
                    Cancel();
                    return;
                }
            }
        }

        // Snapshot the shells we will move, trying to preserve their relative visual order.
        // Fall back to SelectedCues order if we cannot obtain a full ordered list.
        var toMove = new List<ShellBar>();
        try
        {
            foreach (var c in ShellSelection.SelectedCues)
            {
                if (c?.ShellBar != null) toMove.Add(c.ShellBar);
            }
        }
        catch
        {
            foreach (var c in ShellSelection.SelectedCues)
                if (c?.ShellBar != null) toMove.Add(c.ShellBar);
        }

        if (toMove.Count == 0)
        {
            Cancel();
            return;
        }

        // Snapshot child counts before any structural changes
        var childCountBefore = new Dictionary<Cue, int>();
        foreach (var c in CueList.CueIndex.Values)
        {
            childCountBefore[c] = c.ChildCues.Count;
        }

        // Track parents that will lose or gain children
        var affectedParents = new HashSet<Cue>();
        foreach (var mc in ShellSelection.SelectedCues)
        {
            if (mc != null && mc.ParentId != -1)
            {
                var op = CueList.FetchCueFromId(mc.ParentId);
                if (op != null) affectedParents.Add(op);
            }
        }

        // Compute final target
        var (targetContainer, rawInsertIndex, newParentId, isMakeChild) = DetermineReorderTarget();

        // Detach all to-move shells
        foreach (var sb in toMove)
        {
            var cue = CueList.FetchCueFromId(sb.CueId);
            if (cue == null) continue;

            if (cue.ParentId != -1)
            {
                var oldP = CueList.FetchCueFromId(cue.ParentId);
                oldP?.ChildCues.Remove(cue.Id);
            }
            sb.GetParent()?.RemoveChild(sb);
            cue.ParentId = -1;
        }

        // Re-compute insert index
        int insertIndex = Math.Clamp(rawInsertIndex, 0, Math.Max(0, targetContainer.GetChildCount()));

        if (DropAtEndAsTopLevel)
        {
            insertIndex = targetContainer.GetChildCount();
        }

        // Insert the moved items
        foreach (var sb in toMove)
        {
            targetContainer.AddChild(sb);
            if (!DropAtEndAsTopLevel && !isMakeChild && (InsertAbove || InsertBelow))
            {
                targetContainer.MoveChild(sb, insertIndex);
                insertIndex++;
            }
        }

        // Sync data model
        SyncChildListsFromContainers();

        // Apply ParentId
        foreach (var sb in toMove)
        {
            var cue = CueList.FetchCueFromId(sb.CueId);
            if (cue == null) continue;

            bool foundParent = false;
            foreach (var other in CueList.CueIndex.Values)
            {
                if (other.ChildCues.Contains(cue.Id))
                {
                    cue.ParentId = other.Id;
                    foundParent = true;
                    break;
                }
            }
            if (!foundParent)
                cue.ParentId = -1;

            sb.RelationshipChanged();
        }

        // Add any new parents
        foreach (var sb in toMove)
        {
            var mc = CueList.FetchCueFromId(sb.CueId);
            if (mc != null && mc.ParentId != -1)
            {
                var np = CueList.FetchCueFromId(mc.ParentId);
                if (np != null) affectedParents.Add(np);
            }
        }

        // Refresh UI on affected parents
        foreach (var parent in affectedParents)
        {
            if (parent.ShellBar != null)
            {
                int before = childCountBefore.TryGetValue(parent, out var b) ? b : 0;
                if (parent.ChildCues.Count > 0 && before == 0)
                {
                    parent.Expanded = true;
                }
                parent.ShellBar.RelationshipChanged();
            }
        }

        Cleanup(keepChanges: true);
    }

    public void Cancel()
    {
        Cleanup(keepChanges: false);
    }

    private void Cleanup(bool keepChanges)
    {
        foreach (var child in _reorderListContainer.GetChildren())
        {
            child.QueueFree();
        }

        IsActive = false;
        _reorderCueControl.Visible = false;
        MouseOverShellBar = null;
        ResetDropFlags();
        DraggedCueId = -1;

        if (!keepChanges)
        {
            // Shells were already detached in Commit if we got that far; 
            // for pure cancel, they should still be in original positions.
        }
    }

    public void ResetDropFlags()
    {
        InsertAbove = false;
        InsertBelow = false;
        InsertMakeChild = false;
        DropAtEndAsTopLevel = false;
    }

    public void SetMouseOver(ShellBar shellbar)
    {
        MouseOverShellBar = shellbar;
    }

    private void PrepareReorderPreviewLabels()
    {
        foreach (var selectedCue in ShellSelection.SelectedCues)
        {
            var label = new Label();
            label.Text = selectedCue.Name;
            label.AddThemeFontSizeOverride("font_size", 9);
            label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 0.5f));
            _reorderListContainer.AddChild(label);
        }
    }

    private (VBoxContainer targetContainer, int insertIndex, int newParentId, bool isMakeChild) DetermineReorderTarget()
    {
        if (DropAtEndAsTopLevel)
        {
            return (_cueContainer, _cueContainer.GetChildCount(), -1, false);
        }

        var targetShell = MouseOverShellBar;
        if (targetShell == null)
            return (_cueContainer, 0, -1, false);

        VBoxContainer container = _cueContainer;
        int newPid = -1;
        bool makeChild = false;

        if (!InsertAbove && !InsertBelow)
        {
            container = targetShell.ShellChildContainer ?? _cueContainer;
            newPid = targetShell.CueId;
            makeChild = true;
        }
        else if (CueList.FetchCueFromId(targetShell.CueId)?.ParentId != -1)
        {
            var targetParent = CueList.FetchCueFromId(CueList.FetchCueFromId(targetShell.CueId).ParentId);
            container = targetParent?.ShellBar?.ShellChildContainer ?? _cueContainer;
            newPid = targetParent?.Id ?? -1;
        }

        int idx = targetShell.GetIndex();
        return (container, idx, newPid, makeChild);
    }

    private bool WouldCreateCycle(Cue movingCue, int prospectiveParentId)
    {
        if (movingCue == null || prospectiveParentId == -1) return false;
        if (movingCue.Id == prospectiveParentId) return true;

        var current = CueList.FetchCueFromId(prospectiveParentId);
        while (current != null)
        {
            if (current.Id == movingCue.Id) return true;
            if (current.ParentId == -1) break;
            current = CueList.FetchCueFromId(current.ParentId);
        }
        return false;
    }

    private void SyncChildListsFromContainers()
    {
        foreach (var cueEntry in CueList.CueIndex)
        {
            var cue = cueEntry.Value;
            if (cue.ChildCues.Count == 0 && cue.ShellBar?.ShellChildContainer?.GetChildCount() == 0)
                continue;

            var container = cue.ShellBar?.ShellChildContainer;
            if (container == null) continue;

            var ordered = new List<int>();
            foreach (var child in container.GetChildren())
            {
                if (child is ShellBar sb)
                {
                    int id = sb.CueId;
                    if (id >= 0) ordered.Add(id);
                }
            }
            cue.ChildCues = ordered;
        }
    }
}
