using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.UI.Utilities;

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
    private const float MaxScale = 200.0f;
    private const float MinBarWidth = 4.0f;
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
        
        // Ruler
        _ruler = new Ruler();
        AddChild(_ruler);
        _ruler.Position = new Vector2(0, 0); // Adjust position as needed, assuming top of the control
        _ruler.Size = new Vector2(Size.X, 12); // Fixed height for ruler
        
        
        LoadTimeline();
    }
    
    public override void _Process(double delta)
    {
        if (_ruler != null)
        {
            _ruler.Offset = _scrollContainer.GetHScroll();
            _ruler.Scale = _scale;
            _ruler.Size = new Vector2(_scrollContainer.Size.X, _ruler.Size.Y); // Sync width with scroll container
            _ruler.QueueRedraw();
        }
    }
    
    

    private void OnZoomChanged(double value)
    {
        _scale = (float)value;
        UpdateAllPositionsAndSizes();
        _ruler.QueueRedraw();
    }

    private void LoadTimeline()
    {
        if (!Visible || !_timeLineContainer.Visible) return;
        
        GD.Print("TimelineInspector:LoadTimeline - Loading timeline");
        
        // Clear existing backgrounds
        foreach (var bg in _rowBackgrounds)
        {
            bg.QueueFree();
        }
        _rowBackgrounds.Clear();

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
        
        // Create zebra row backgrounds
        int maxRow = row; // row is incremented to one past the last
        for (int i = 0; i < maxRow; i++)
        {
            var bg = new ColorRect();
            bg.Color = (i % 2 == 0) ? new Color(0.1f, 0.1f, 0.1f, 1.0f) : new Color(0.2f, 0.2f, 0.2f, 1.0f);
            bg.Position = new Vector2(0, i * RowHeight);
            bg.Size = new Vector2(100, RowHeight); // Initial size, will be updated
            bg.ZIndex = -1; // Behind bars
            _timelineArea.AddChild(bg);
            _rowBackgrounds.Add(bg);
        }

        // Create bars
        foreach (var item in items)
        {
            GD.Print($"TimelineInspector:LoadTimeline - Creating bar");
            var bar = new ColorRect();
            bar.Color = GlobalStyles.LowColor5; // Default color, can be customized
            bar.MouseFilter = MouseFilterEnum.Stop;
            bar.GuiInput += (e) => HandleBarInput(e, item.cue, bar);
            bar.MouseDefaultCursorShape = CursorShape.Move;
            _timelineArea.AddChild(bar);

            var label = new Label();
            label.Text = item.cue.Name;
            label.Position = new Vector2(4, 4);
            bar.AddChild(label);
            
            var timeLabel = new Label();
            timeLabel.Text = $"{UiUtilities.FormatTime(ComputeActionStart(item.cue))} ({UiUtilities.FormatTime(item.cue.PreWait)})";
            timeLabel.Position = new Vector2(14, 20);
            bar.AddChild(timeLabel);
            
            // Add vertical lines for start and end
            var startLine = new ColorRect();
            startLine.Color = GlobalStyles.HighColor3;
            startLine.Size = new Vector2(2, RowHeight - 4);
            startLine.Position = new Vector2(0, 0);
            bar.AddChild(startLine);

            var endLine = new ColorRect();
            endLine.Color = GlobalStyles.HighColor3;
            endLine.Size = new Vector2(2, RowHeight - 4);
            endLine.Position = new Vector2(0, 0); // Position will be updated in sizing
            bar.AddChild(endLine);

            // Add flag square at top for dragging (clickable area)
            var flag = new ColorRect();
            flag.Color = GlobalStyles.HighColor3; // Or any visible color
            flag.Size = new Vector2(10, 10);
            flag.Position = new Vector2(0, RowHeight - 14); // Slightly above and left for visibility
            flag.MouseFilter = MouseFilterEnum.Stop;
            flag.MouseDefaultCursorShape = CursorShape.Move;
            flag.GuiInput += (e) => HandleBarInput(e, item.cue, bar); // Delegate input to bar handler
            bar.AddChild(flag);
            

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

            float calculatedWidth;
            
            if (cue.Duration < 0)
            {
                // Handle looping/infinite duration specially, e.g., fixed width
                calculatedWidth = 100 * _scale; // Arbitrary fixed size for infinite
            }
            else
            {
                calculatedWidth = (float)(cue.Duration * _scale);
            }
            float displayWidth = Mathf.Max(calculatedWidth, MinBarWidth);

            bar.Size = new Vector2(displayWidth, RowHeight - 4);

            bar.Position = new Vector2((float)(start * _scale), _cueToRow[cue] * RowHeight);

            // Update end line position
            var endLine = bar.GetChild<ColorRect>(3); // Adjusted index after adding timeLabel
            endLine.Position = new Vector2(calculatedWidth - 2, 0); // Position at actual end, even if display is min

            // Update time label
            var timeLabel = bar.GetChild<Label>(1);
            timeLabel.Text = $"{UiUtilities.FormatTime(start)} ({UiUtilities.FormatTime(cue.PreWait)})";

            
            maxTime = Math.Max(maxTime, start + (cue.Duration < 0 ? 100 : cue.Duration)); // Arbitrary for infinite
        }

        _timelineArea.CustomMinimumSize = new Vector2((float)(maxTime * _scale + 100), _cueToRow.Values.Max() * RowHeight + RowHeight);
        
        // Update background sizes
        float contentWidth = _timelineArea.CustomMinimumSize.X;
        foreach (var bg in _rowBackgrounds)
        {
            bg.Size = new Vector2(contentWidth, RowHeight);
        }
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
                    _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
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

        float calculatedWidth;
        if (cue.Duration < 0)
        {
            calculatedWidth = 100 * _scale;
        }
        else
        {
            calculatedWidth = (float)(cue.Duration * _scale);
        }

        float displayWidth = Mathf.Max(calculatedWidth, MinBarWidth);
        bar.Size = new Vector2(displayWidth, bar.Size.Y);

        // Update end line
        var endLine = bar.GetChild<ColorRect>(3);
        endLine.Position = new Vector2(calculatedWidth - 2, 0);
        
        // Update time label
        var timeLabel = bar.GetChild<Label>(1);
        timeLabel.Text = $"{UiUtilities.FormatTime(start)} ({UiUtilities.FormatTime(cue.PreWait)})";

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
            float calculatedWidth;
            if (cue.Duration < 0)
            {
                calculatedWidth = 100 * _scale;
            }
            else
            {
                calculatedWidth = (float)(cue.Duration * _scale);
            }

            float displayWidth = Mathf.Max(calculatedWidth, MinBarWidth);
            bar.Size = new Vector2(displayWidth, bar.Size.Y);

            // Update end line
            var endLine = bar.GetChild<ColorRect>(3);
            endLine.Position = new Vector2(calculatedWidth - 2, 0);
            
            // Update time label
            var timeLabel = bar.GetChild<Label>(1);
            timeLabel.Text = $"{UiUtilities.FormatTime(ComputeActionStart(cue))} ({UiUtilities.FormatTime(cue.PreWait)})";
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
        
        // Update background sizes
        float contentWidth = _timelineArea.CustomMinimumSize.X;
        foreach (var bg in _rowBackgrounds)
        {
            bg.Size = new Vector2(contentWidth, RowHeight);
        }
        
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
    
    private partial class Ruler : Control
    {
        public float Scale { get; set; }
        public float Offset { get; set; }

        public override void _Draw()
        {
            // Determine tick interval based on scale for readability
            float targetPixelSpacing = 100.0f; // Desired space between ticks in pixels
            float interval = (float)Mathf.Pow(10, Mathf.Round(Math.Log10(targetPixelSpacing / Scale)));
            if (interval * Scale < 50) interval *= 2;
            else if (interval * Scale > 200) interval /= 2;

            // Calculate start and end time in view
            float tStart = Offset / Scale;
            float tEnd = (Offset + Size.X) / Scale;

            // Find first tick
            float firstTick = Mathf.Ceil(tStart / interval) * interval;

            for (float t = firstTick; t <= tEnd; t += interval)
            {
                float x = t * Scale - Offset;
                // Draw tick line
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), Colors.White, 1.0f);
                // Draw label
                var font = ThemeDB.FallbackFont;
                string labelText = $"{t:F1}s"; // Format to one decimal
                DrawString(font, new Vector2(x + 2, Size.Y), labelText, HorizontalAlignment.Left, -1, 10);
            }
        }
    }
    
}
