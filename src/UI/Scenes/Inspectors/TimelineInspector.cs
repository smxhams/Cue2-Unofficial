using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cue2.UI.Scenes.Inspectors;

public partial class TimelineInspector : Control
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    
    private Cue _focusedCue;

    private Label _infoLabel;
    
    private MarginContainer _timeLineContainer;

    // Timeline UI elements
    private ScrollContainer _scrollContainer;
    private VBoxContainer _timelineItemsContainer;
    private Control _timelineArea;
    private HSlider _zoomSlider;
    private Ruler _ruler;
    private float _scale = 10.0f; // Pixels per second
    private const float RowHeight = 40.0f;
    private const float MinScale = 1.0f;
    private const float MaxScale = 50.0f;
    private Dictionary<Cue, ColorRect> _cueToBar = new Dictionary<Cue, ColorRect>();
    private Dictionary<Cue, int> _cueToRow = new Dictionary<Cue, int>();
    private List<ColorRect> _rowBackgrounds = new List<ColorRect>();

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        _globalSignals.ShellFocused += ShellSelected;
        
        _infoLabel = GetNode<Label>("%InfoLabel");
        _timeLineContainer = GetNode<MarginContainer>("%TimelineContainer");
        
        VisibilityChanged += LoadTimeline;


        // Zoom slider
        _zoomSlider = GetNode<HSlider>("%ZoomSlider");
        _zoomSlider.MinValue = MinScale;
        _zoomSlider.MaxValue = MaxScale;
        _zoomSlider.Value = _scale;
        _zoomSlider.ValueChanged += OnZoomChanged;

        // Scroll container
        _scrollContainer = GetNode<ScrollContainer>("%TimelineScrollContainer");
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowAlways;
        _scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways;

        // Timeline area
        _timelineArea = GetNode<Control>("%TimelineArea");
        _timelineArea.MouseFilter = MouseFilterEnum.Pass;

        LoadTimeline();
    }

    private void OnZoomChanged(double value)
    {
        _scale = (float)value;
        UpdateAllPositionsAndSizes();
    }

    private void LoadTimeline()
    {
        if (!Visible || !_timeLineContainer.Visible) return;
        
        GD.Print("TimelineInspector:LoadTimeline - Loading timeline");

        // Clear existing bars
        foreach (var bar in _cueToBar.Values)
        {
            bar.QueueFree();
        }
        _cueToBar.Clear();
        _cueToRow.Clear();

        if (_focusedCue == null) return;

        // Collect all cues in the hierarchy
        var items = new List<TimelineItem>();
        int row = 0;
        CollectCues(_focusedCue, items, ref row);

        // Create bars
        foreach (var item in items)
        {
            GD.Print($"TimelineInspector:LoadTimeline - Creating bar");
            var bar = new ColorRect();
            bar.Color = Colors.Blue; // Default color, can be customized
            bar.MouseFilter = MouseFilterEnum.Stop;
            bar.GuiInput += (e) => HandleBarInput(e, item.cue, bar);
            _timelineArea.AddChild(bar);

            var label = new Label();
            label.Text = item.cue.Name;
            label.Position = new Vector2(4, 4);
            bar.AddChild(label);

            _cueToBar[item.cue] = bar;
            _cueToRow[item.cue] = item.row;
        }

        UpdateAllPositionsAndSizes();
    }

    private void CollectCues(Cue cue, List<TimelineItem> items, ref int row, int depth = 0)
    {
        items.Add(new TimelineItem { cue = cue, row = row++ });

        foreach (var childId in cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child != null)
            {
                CollectCues(child, items, ref row, depth + 1);
            }
        }
    }

    private double ComputeActionStart(Cue cue)
    {
        if (cue.ParentId == -1)
        {
            return cue.PreWait;
        }
        else
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            if (parent == null)
            {
                GD.PrintErr($"TimelineInspector:ComputeActionStart - Parent not found for cue {cue.Id}");
                return 0;
            }
            return ComputeActionStart(parent) + cue.PreWait;
        }
    }

    private double ComputeParentActionStart(Cue cue)
    {
        if (cue.ParentId == -1)
        {
            return 0;
        }
        else
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            return ComputeActionStart(parent);
        }
    }

    private void UpdateAllPositionsAndSizes()
    {
        double maxTime = 0;

        foreach (var kvp in _cueToBar)
        {
            var cue = kvp.Key;
            var bar = kvp.Value;
            var start = ComputeActionStart(cue);

            if (cue.Duration < 0)
            {
                // Handle looping/infinite duration specially, e.g., fixed width
                bar.CustomMinimumSize = new Vector2(100 * _scale, RowHeight - 4); // Arbitrary fixed size for infinite
            }
            else
            {
                bar.CustomMinimumSize = new Vector2((float)(cue.Duration * _scale), RowHeight - 4);
            }


            bar.Position = new Vector2((float)(start * _scale), _cueToRow[cue] * RowHeight);

            maxTime = Math.Max(maxTime, start + (cue.Duration < 0 ? 100 : cue.Duration)); // Arbitrary for infinite
        }

        //_timelineArea.CustomMinimumSize = new Vector2((float)(maxTime * _scale + 100), _cueToRow.Values.Max() * RowHeight + RowHeight);
    }

    private bool _dragging;
    private Vector2 _initialBarPos;
    private Vector2 _initialMousePos;
    private Cue _draggedCue;

    private void HandleBarInput(InputEvent @event, Cue cue, ColorRect bar)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _dragging = true;
                    _initialBarPos = bar.Position;
                    _initialMousePos = GetViewport().GetMousePosition();
                    _draggedCue = cue;
                }
                else
                {
                    _dragging = false;
                    _draggedCue = null;
                    // Finalize, perhaps emit signal to update shell or other UI
                    _globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
                }
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _dragging && _draggedCue == cue)
        {
            var currentMousePos = GetViewport().GetMousePosition();
            var delta = currentMousePos - _initialMousePos;

            var newX = _initialBarPos.X + delta.X;
            newX = Mathf.Max(0, newX); // Prevent negative position

            bar.Position = new Vector2(newX, bar.Position.Y);

            var newStart = newX / _scale;
            var parentStart = ComputeParentActionStart(cue);
            var newPreWait = newStart - parentStart;

            if (newPreWait < 0)
            {
                // Reset position if invalid
                bar.Position = _initialBarPos;
                return;
            }

            cue.PreWait = newPreWait;

            // Recalculate durations up the hierarchy
            RecalcDurationsUp(cue);

            // Update subtree positions and sizes
            UpdateSubtreePositions(cue);

            // Update ancestor sizes
            if (cue.ParentId != -1)
            {
                var parent = CueList.FetchCueFromId(cue.ParentId);
                UpdateAncestorSizes(parent);
            }

            // Update timeline size
            UpdateTimelineSize();
        }
    }

    private void RecalcDurationsUp(Cue cue)
    {
        cue.CalculateTotalDuration();
        if (cue.ParentId != -1)
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            if (parent != null)
            {
                RecalcDurationsUp(parent);
            }
        }
    }

    private void UpdateSubtreePositions(Cue cue)
    {
        if (!_cueToBar.TryGetValue(cue, out var bar)) return;

        var start = ComputeActionStart(cue);
        bar.Position = new Vector2((float)(start * _scale), bar.Position.Y);

        if (cue.Duration < 0)
        {
            bar.Size = new Vector2(100 * _scale, bar.Size.Y); // Arbitrary for infinite
        }
        else
        {
            bar.Size = new Vector2((float)(cue.Duration * _scale), bar.Size.Y);
        }

        foreach (var childId in cue.ChildCues)
        {
            var child = CueList.FetchCueFromId(childId);
            if (child != null)
            {
                UpdateSubtreePositions(child);
            }
        }
    }

    private void UpdateAncestorSizes(Cue cue)
    {
        if (cue == null) return;

        if (_cueToBar.TryGetValue(cue, out var bar))
        {
            if (cue.Duration < 0)
            {
                bar.Size = new Vector2(100 * _scale, bar.Size.Y);
            }
            else
            {
                bar.Size = new Vector2((float)(cue.Duration * _scale), bar.Size.Y);
            }
        }

        if (cue.ParentId != -1)
        {
            var parent = CueList.FetchCueFromId(cue.ParentId);
            UpdateAncestorSizes(parent);
        }
    }

    private void UpdateTimelineSize()
    {
        double maxTime = 0;
        foreach (var kvp in _cueToBar)
        {
            var cue = kvp.Key;
            var start = ComputeActionStart(cue);
            var dur = cue.Duration < 0 ? 100 : cue.Duration;
            maxTime = Math.Max(maxTime, start + dur);
        }
        _timelineArea.CustomMinimumSize = new Vector2((float)(maxTime * _scale + 100), _timelineArea.CustomMinimumSize.Y);
    }

    private void ShellSelected(int cueId)
    {
        _focusedCue = CueList.FetchCueFromId(cueId);

        if (_focusedCue == null)
        {
            GD.Print($"TimelineInspector:ShellSelected - No cue selected");
            _infoLabel.Visible = true;
            _timeLineContainer.Visible = false;
            return;
        }
        
        _infoLabel.Visible = false;
        _timeLineContainer.Visible = true;
        
        LoadTimeline();
    }

    private struct TimelineItem
    {
        public Cue cue;
        public int row;
    }
}
