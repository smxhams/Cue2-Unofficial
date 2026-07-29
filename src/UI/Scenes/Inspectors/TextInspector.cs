using System;
using System.Globalization;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Scenes;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes.Inspectors;

/// <summary>
/// Inspector for the text overlay component: content, target layer, duration, typography, and preview.
/// </summary>
public partial class TextInspector : Control
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private HistoryManager _history;

    private Cue _focusedCue;
    private TextComponent _focusedText;

    private Label _infoLabel;
    private Control _emptyState;
    private Control _contentRoot;
    private Button _addTextButton;
    private Button _deleteTextButton;

    private Control _previewContainer;
    private TextPreviewer _textPreviewer;

    private OptionButton _targetLayerOption;
    private LineEdit _durationLineEdit;
    private LineEdit _fadeInInput;
    private LineEdit _fadeOutInput;
    private TextEdit _contentTextEdit;
    private CheckBox _useBbcodeCheck;
    private OptionButton _fontOption;
    private PopupMenu _fontPopup;
    private OptionButton _hAlignOption;
    private OptionButton _vAlignOption;
    private SpinBox _fontSizeSpin;
    private ColorPickerButton _fontColorPicker;
    private SpinBox _opacitySpin;
    private SpinBox _marginsSpin;
    private CheckBox _autowrapCheck;
    private SpinBox _outlineSizeSpin;
    private ColorPickerButton _outlineColorPicker;
    private CheckBox _backgroundCheck;
    private ColorPickerButton _backgroundColorPicker;

    private bool _isSyncingUi;
    private bool _alignOptionsReady;
    private bool _fontOptionsReady;
    private string[] _systemFontNames = Array.Empty<string>();

    /// <summary>Max width of the system-font dropdown (px).</summary>
    private const int FontPopupMaxWidth = 320;

    /// <summary>Max height of the system-font dropdown (px); scrolls when content is taller.</summary>
    private const int FontPopupMaxHeight = 280;

    /// <summary>Minimum dropdown width so short buttons still look usable.</summary>
    private const int FontPopupMinWidth = 200;

    /// <inheritdoc />
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _history = _globalData?.HistoryManager;

        BindNodes();
        PopulateAlignOptions();
        EnsureSystemFontList();
        WireSignals();

        UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
        _infoLabel.AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);

        if (_deleteTextButton != null)
        {
            _deleteTextButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
            try
            {
                _deleteTextButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
                _deleteTextButton.ExpandIcon = true;
            }
            catch
            {
                // optional icon
            }
        }

        ShowNoSelection();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_globalSignals != null)
        {
            _globalSignals.ShellFocused -= OnShellFocused;
            _globalSignals.SyncShellInspector -= OnSyncFromHistory;
            _globalSignals.DisplaysChanged -= OnDisplaysChanged;
        }

        if (_history != null)
            _history.HistoryRestored -= OnHistoryRestored;

        if (_fontPopup != null && IsInstanceValid(_fontPopup))
            _fontPopup.AboutToPopup -= OnFontPopupAboutToPopup;
    }

    private void BindNodes()
    {
        _infoLabel = GetNode<Label>("%InfoLabel");
        _emptyState = GetNode<Control>("%EmptyState");
        _contentRoot = GetNode<Control>("%ContentRoot");
        _addTextButton = GetNode<Button>("%AddTextButton");
        _deleteTextButton = GetNode<Button>("%DeleteTextButton");
        _previewContainer = GetNodeOrNull<Control>("%PreviewContainer");
        _textPreviewer = GetNodeOrNull<TextPreviewer>("%TextPreviewer");
        _targetLayerOption = GetNode<OptionButton>("%TargetLayerOptionButton");
        _durationLineEdit = GetNode<LineEdit>("%DurationLineEdit");
        _fadeInInput = GetNodeOrNull<LineEdit>("%FadeInInput");
        _fadeOutInput = GetNodeOrNull<LineEdit>("%FadeOutInput");
        _contentTextEdit = GetNode<TextEdit>("%ContentTextEdit");
        _useBbcodeCheck = GetNode<CheckBox>("%UseBbcodeCheck");
        _fontOption = GetNodeOrNull<OptionButton>("%FontOptionButton");
        if (_fontOption != null)
        {
            // Avoid expanding to the longest font name (can be extremely wide).
            _fontOption.FitToLongestItem = false;
            _fontPopup = _fontOption.GetPopup();
        }
        _hAlignOption = GetNode<OptionButton>("%HAlignOption");
        _vAlignOption = GetNode<OptionButton>("%VAlignOption");
        _fontSizeSpin = GetNode<SpinBox>("%FontSizeSpin");
        _fontColorPicker = GetNode<ColorPickerButton>("%FontColorPicker");
        _opacitySpin = GetNode<SpinBox>("%OpacitySpin");
        _marginsSpin = GetNode<SpinBox>("%MarginsSpin");
        _autowrapCheck = GetNode<CheckBox>("%AutowrapCheck");
        _outlineSizeSpin = GetNode<SpinBox>("%OutlineSizeSpin");
        _outlineColorPicker = GetNode<ColorPickerButton>("%OutlineColorPicker");
        _backgroundCheck = GetNode<CheckBox>("%BackgroundCheck");
        _backgroundColorPicker = GetNode<ColorPickerButton>("%BackgroundColorPicker");
    }

    private void WireSignals()
    {
        _globalSignals.ShellFocused += OnShellFocused;
        _globalSignals.SyncShellInspector += OnSyncFromHistory;
        _globalSignals.DisplaysChanged += OnDisplaysChanged;

        if (_history != null)
            _history.HistoryRestored += OnHistoryRestored;

        _addTextButton.Pressed += OnAddTextPressed;
        _deleteTextButton.Pressed += OnDeleteTextPressed;

        _targetLayerOption.ItemSelected += OnTargetLayerSelected;
        _durationLineEdit.TextSubmitted += OnDurationSubmitted;
        _durationLineEdit.FocusExited += () => OnDurationSubmitted(_durationLineEdit.Text);

        if (_fadeInInput != null)
        {
            _fadeInInput.TextSubmitted += text => OnFadeSubmitted(text, isIn: true);
            _fadeInInput.FocusExited += () => OnFadeSubmitted(_fadeInInput.Text, isIn: true);
        }
        if (_fadeOutInput != null)
        {
            _fadeOutInput.TextSubmitted += text => OnFadeSubmitted(text, isIn: false);
            _fadeOutInput.FocusExited += () => OnFadeSubmitted(_fadeOutInput.Text, isIn: false);
        }

        _contentTextEdit.TextChanged += OnContentChanged;
        _contentTextEdit.FocusExited += OnContentFocusExited;

        _useBbcodeCheck.Toggled += OnUseBbcodeToggled;
        if (_fontOption != null)
            _fontOption.ItemSelected += OnFontSelected;
        if (_fontPopup != null)
        {
            // Cap size immediately; AboutToPopup re-applies against the live button rect.
            _fontPopup.MaxSize = new Vector2I(FontPopupMaxWidth, FontPopupMaxHeight);
            _fontPopup.AboutToPopup += OnFontPopupAboutToPopup;
        }
        _hAlignOption.ItemSelected += OnHAlignSelected;
        _vAlignOption.ItemSelected += OnVAlignSelected;

        _fontSizeSpin.ValueChanged += OnFontSizeChanged;
        WireSpinBoxFocusRelease(_fontSizeSpin, EndFontSizeCoalesce);

        _fontColorPicker.ColorChanged += OnFontColorChanged;
        _fontColorPicker.PopupClosed += EndFontColorCoalesce;

        _opacitySpin.ValueChanged += OnOpacityChanged;
        WireSpinBoxFocusRelease(_opacitySpin, EndOpacityCoalesce);

        _marginsSpin.ValueChanged += OnMarginsChanged;
        WireSpinBoxFocusRelease(_marginsSpin, EndMarginsCoalesce);
        _autowrapCheck.Toggled += OnAutowrapToggled;

        _outlineSizeSpin.ValueChanged += OnOutlineSizeChanged;
        WireSpinBoxFocusRelease(_outlineSizeSpin, EndOutlineSizeCoalesce);

        _outlineColorPicker.ColorChanged += OnOutlineColorChanged;
        _outlineColorPicker.PopupClosed += EndOutlineColorCoalesce;

        _backgroundCheck.Toggled += OnBackgroundToggled;
        _backgroundColorPicker.ColorChanged += OnBackgroundColorChanged;
        _backgroundColorPicker.PopupClosed += EndBackgroundColorCoalesce;
    }

    /// <summary>
    /// Releases focus when the user finishes typing in a SpinBox (Enter), and ends
    /// history coalescing when the embedded LineEdit loses focus.
    /// </summary>
    /// <remarks>
    /// Godot routes keyboard focus to the SpinBox's internal LineEdit, not the SpinBox
    /// itself — so SpinBox.FocusExited alone is unreliable for commit/cleanup.
    /// </remarks>
    /// <param name="spin">SpinBox to wire.</param>
    /// <param name="onFocusExited">Optional coalesce/end callback when editing finishes.</param>
    private static void WireSpinBoxFocusRelease(SpinBox spin, Action onFocusExited = null)
    {
        if (spin == null)
            return;

        spin.FocusMode = FocusModeEnum.All;
        var edit = spin.GetLineEdit();
        if (edit == null)
            return;

        edit.FocusMode = FocusModeEnum.All;
        edit.TextSubmitted += _ => ReleaseSpinBoxFocus(spin);
        if (onFocusExited != null)
            edit.FocusExited += () => onFocusExited();
    }

    /// <summary>
    /// Clears focus from a SpinBox and its embedded LineEdit after input is committed.
    /// </summary>
    private static void ReleaseSpinBoxFocus(SpinBox spin)
    {
        if (spin == null || !IsInstanceValid(spin))
            return;

        var edit = spin.GetLineEdit();
        if (edit != null && IsInstanceValid(edit) && edit.HasFocus())
            edit.ReleaseFocus();
        if (spin.HasFocus())
            spin.ReleaseFocus();
    }

    private void PopulateAlignOptions()
    {
        if (_alignOptionsReady)
            return;

        _hAlignOption.Clear();
        _hAlignOption.AddItem("Left", (int)HorizontalAlignment.Left);
        _hAlignOption.AddItem("Center", (int)HorizontalAlignment.Center);
        _hAlignOption.AddItem("Right", (int)HorizontalAlignment.Right);
        _hAlignOption.AddItem("Fill", (int)HorizontalAlignment.Fill);

        _vAlignOption.Clear();
        _vAlignOption.AddItem("Top", (int)VerticalAlignment.Top);
        _vAlignOption.AddItem("Center", (int)VerticalAlignment.Center);
        _vAlignOption.AddItem("Bottom", (int)VerticalAlignment.Bottom);
        // Godot VerticalAlignment has no Fill in all versions; skip if undefined.

        _alignOptionsReady = true;
    }

    /// <summary>
    /// Loads and caches sorted system font family names once per process.
    /// </summary>
    private void EnsureSystemFontList()
    {
        if (_fontOptionsReady && _systemFontNames.Length > 0)
            return;

        try
        {
            var fonts = OS.GetSystemFonts();
            if (fonts == null || fonts.Length == 0)
            {
                _systemFontNames = Array.Empty<string>();
            }
            else
            {
                var list = new System.Collections.Generic.List<string>(fonts.Length);
                foreach (string name in fonts)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    list.Add(name.Trim());
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                // Deduplicate case-insensitively while keeping first display form.
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var unique = new System.Collections.Generic.List<string>(list.Count);
                foreach (string n in list)
                {
                    if (seen.Add(n))
                        unique.Add(n);
                }
                _systemFontNames = unique.ToArray();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TextInspector:EnsureSystemFontList - {ex.Message}");
            _systemFontNames = Array.Empty<string>();
        }

        _fontOptionsReady = true;
    }

    /// <summary>
    /// Rebuilds the font OptionButton for the current component selection.
    /// </summary>
    private void PopulateFontOptions()
    {
        if (_fontOption == null)
            return;

        EnsureSystemFontList();

        _fontOption.SetBlockSignals(true);
        try
        {
            _fontOption.Clear();
            // Index 0 = theme default (empty FontName).
            _fontOption.AddItem("Default");
            _fontOption.SetItemMetadata(0, string.Empty);

            string current = _focusedText?.FontName?.Trim() ?? string.Empty;
            int selected = 0;
            bool matched = string.IsNullOrEmpty(current);

            foreach (string name in _systemFontNames)
            {
                _fontOption.AddItem(name);
                int idx = _fontOption.ItemCount - 1;
                _fontOption.SetItemMetadata(idx, name);
                if (!matched && string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                {
                    selected = idx;
                    matched = true;
                }
            }

            // Keep a saved family even if missing on this machine.
            if (!matched && !string.IsNullOrEmpty(current))
            {
                _fontOption.AddItem($"{current} (missing)");
                int idx = _fontOption.ItemCount - 1;
                _fontOption.SetItemMetadata(idx, current);
                selected = idx;
            }

            _fontOption.Selected = selected;
        }
        finally
        {
            _fontOption.SetBlockSignals(false);
        }

        // Keep popup bounds in sync after rebuild (item count can change max content height).
        if (_fontPopup != null && IsInstanceValid(_fontPopup))
            _fontPopup.MaxSize = new Vector2I(FontPopupMaxWidth, FontPopupMaxHeight);
    }

    /// <summary>
    /// Limits the font dropdown size and anchors it under the Font option button.
    /// </summary>
    private void OnFontPopupAboutToPopup()
    {
        if (_fontOption == null || !IsInstanceValid(_fontOption)
            || _fontPopup == null || !IsInstanceValid(_fontPopup))
            return;

        // Match button width (clamped) so the list doesn't stretch to long family names.
        float buttonW = _fontOption.Size.X;
        int width = Mathf.Clamp(Mathf.RoundToInt(buttonW), FontPopupMinWidth, FontPopupMaxWidth);

        _fontPopup.MaxSize = new Vector2I(width, FontPopupMaxHeight);
        _fontPopup.MinSize = new Vector2I(width, 0);
        // Height is content-driven up to MaxSize; force width so wrap/clip is consistent.
        int contentH = Mathf.Max(1, Mathf.RoundToInt(_fontPopup.GetContentsMinimumSize().Y));
        _fontPopup.Size = new Vector2I(width, Mathf.Min(FontPopupMaxHeight, contentH));

        // Screen-space position of the button's bottom-left (handles scaled/sub-window transforms).
        Transform2D screenXform = _fontOption.GetScreenTransform();
        Vector2 topLeft = screenXform * Vector2.Zero;
        Vector2 bottomLeft = screenXform * new Vector2(0f, _fontOption.Size.Y);
        var pos = new Vector2I(Mathf.RoundToInt(bottomLeft.X), Mathf.RoundToInt(bottomLeft.Y));

        // Keep the popup on the usable screen area; flip above the button if needed.
        int screenIdx = DisplayServer.WindowGetCurrentScreen();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screenIdx);
        int popupH = Mathf.Min(FontPopupMaxHeight, Mathf.Max(1, (int)_fontPopup.Size.Y));
        if (popupH <= 1)
            popupH = FontPopupMaxHeight;

        if (pos.X + width > usable.Position.X + usable.Size.X)
            pos.X = usable.Position.X + usable.Size.X - width;
        if (pos.X < usable.Position.X)
            pos.X = usable.Position.X;

        if (pos.Y + popupH > usable.Position.Y + usable.Size.Y)
        {
            // Open above the button.
            pos.Y = Mathf.RoundToInt(topLeft.Y) - popupH;
            if (pos.Y < usable.Position.Y)
                pos.Y = usable.Position.Y;
        }

        _fontPopup.Position = pos;

        // OptionButton may reposition after this signal; re-apply once the popup is shown.
        CallDeferred(MethodName.ApplyFontPopupPlacement, width, popupH);
    }

    /// <summary>
    /// Re-asserts font popup size/position after OptionButton's own show logic.
    /// </summary>
    private void ApplyFontPopupPlacement(int width, int height)
    {
        if (_fontOption == null || !IsInstanceValid(_fontOption)
            || _fontPopup == null || !IsInstanceValid(_fontPopup)
            || !_fontPopup.Visible)
            return;

        _fontPopup.MaxSize = new Vector2I(width, FontPopupMaxHeight);
        _fontPopup.Size = new Vector2I(width, Mathf.Clamp(height, 1, FontPopupMaxHeight));

        Transform2D screenXform = _fontOption.GetScreenTransform();
        Vector2 topLeft = screenXform * Vector2.Zero;
        Vector2 bottomLeft = screenXform * new Vector2(0f, _fontOption.Size.Y);
        var pos = new Vector2I(Mathf.RoundToInt(bottomLeft.X), Mathf.RoundToInt(bottomLeft.Y));

        int screenIdx = DisplayServer.WindowGetCurrentScreen();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screenIdx);
        int popupH = (int)_fontPopup.Size.Y;

        if (pos.X + width > usable.Position.X + usable.Size.X)
            pos.X = usable.Position.X + usable.Size.X - width;
        if (pos.X < usable.Position.X)
            pos.X = usable.Position.X;

        if (pos.Y + popupH > usable.Position.Y + usable.Size.Y)
        {
            pos.Y = Mathf.RoundToInt(topLeft.Y) - popupH;
            if (pos.Y < usable.Position.Y)
                pos.Y = usable.Position.Y;
        }

        _fontPopup.Position = pos;
    }

    private void OnShellFocused(int cueId)
    {
        if (cueId < 0)
        {
            _focusedCue = null;
            _focusedText = null;
            ShowNoSelection();
            return;
        }

        _focusedCue = CueList.FetchCueFromId(cueId);
        RefreshFromFocusedCue();
    }

    private void OnSyncFromHistory()
    {
        CallDeferred(MethodName.RefreshFromFocusedCue);
    }

    private void OnHistoryRestored(int scope)
    {
        if (scope == (int)HistoryManager.HistoryScope.Cue
            || scope == (int)HistoryManager.HistoryScope.Cuelist)
        {
            CallDeferred(MethodName.RefreshFromFocusedCue);
        }
    }

    private void OnDisplaysChanged()
    {
        if (_focusedText != null)
        {
            PopulateTargetLayerOptions();
            RefreshPreview(fullLayout: true);
        }
    }

    private void RefreshFromFocusedCue()
    {
        if (_focusedCue == null && _globalData != null && _globalData.FocusedCue >= 0)
            _focusedCue = CueList.FetchCueFromId(_globalData.FocusedCue);

        if (_focusedCue == null)
        {
            _focusedText = null;
            ShowNoSelection();
            return;
        }

        _focusedText = _focusedCue.GetTextComponent();
        if (_focusedText == null)
        {
            ShowEmptyState();
            return;
        }

        ShowContent();
        SyncUiFromModel();
        UpdateCcLinkedHint();
    }

    /// <summary>
    /// When the same cue’s video has closed captions linked, hint that live text is CC-driven.
    /// </summary>
    private void UpdateCcLinkedHint()
    {
        if (_contentTextEdit == null)
            return;

        var video = _focusedCue?.GetVideoComponent();
        bool linked = video != null && video.UseSubtitles && !video.IsImage && video.HasTextSubtitles;
        if (linked)
        {
            _contentTextEdit.PlaceholderText =
                "Closed captions from video will show here during playback (static content is still used in the inspector / when CC is off).";
        }
        else
        {
            _contentTextEdit.PlaceholderText = "Enter overlay text…";
        }
    }

    private void ShowNoSelection()
    {
        _infoLabel.Visible = true;
        _infoLabel.Text = "No shell selected";
        _emptyState.Visible = false;
        _contentRoot.Visible = false;
        ClearPreview();
    }

    private void ShowEmptyState()
    {
        _infoLabel.Visible = false;
        _emptyState.Visible = true;
        _contentRoot.Visible = false;
        ClearPreview();
    }

    private void ShowContent()
    {
        _infoLabel.Visible = false;
        _emptyState.Visible = false;
        _contentRoot.Visible = true;
    }

    private void ClearPreview()
    {
        _textPreviewer?.SetComponent(null);
    }

    private void RefreshPreview(bool fullLayout)
    {
        if (_textPreviewer == null || _focusedText == null)
            return;

        // Preview is always visible beside the editor when content is shown.
        if (fullLayout)
        {
            _textPreviewer.SetComponent(_focusedText);
            _textPreviewer.SetAreasDeferred(_focusedText.TargetLayerId);
        }
        else
        {
            _textPreviewer.SetComponent(_focusedText);
            _textPreviewer.RefreshVisuals();
        }
    }

    private void SyncUiFromModel()
    {
        if (_focusedText == null)
            return;

        _isSyncingUi = true;
        try
        {
            PopulateTargetLayerOptions();

            if (_focusedText.Duration <= 0)
            {
                _durationLineEdit.Text = "0 (until stopped)";
                _durationLineEdit.TooltipText = "0 = stay active until stopped. Enter a time to auto-end.";
            }
            else
            {
                _durationLineEdit.Text = UiUtilities.ParseAndFormatTime(
                    _focusedText.Duration.ToString(CultureInfo.InvariantCulture),
                    out _,
                    out string tip);
                _durationLineEdit.TooltipText = tip + " (0 = until stopped)";
            }

            _contentTextEdit.Text = _focusedText.Content ?? string.Empty;
            _useBbcodeCheck.ButtonPressed = _focusedText.UseBbcode;
            PopulateFontOptions();
            SelectOptionById(_hAlignOption, (int)_focusedText.HorizontalAlignment);
            SelectOptionById(_vAlignOption, (int)_focusedText.VerticalAlignment);
            _fontSizeSpin.Value = _focusedText.FontSize;
            _fontColorPicker.Color = _focusedText.FontColor;
            _opacitySpin.Value = Mathf.RoundToInt(Mathf.Clamp(_focusedText.Opacity, 0f, 1f) * 100f);
            _marginsSpin.Value = _focusedText.Margins;
            _autowrapCheck.ButtonPressed = _focusedText.Autowrap;
            _outlineSizeSpin.Value = _focusedText.OutlineSize;
            _outlineColorPicker.Color = _focusedText.OutlineColor;
            _backgroundCheck.ButtonPressed = _focusedText.BackgroundEnabled;
            _backgroundColorPicker.Color = _focusedText.BackgroundColor;

            if (_fadeInInput != null)
                _fadeInInput.Text = UiUtilities.FormatTime(_focusedText.FadeInDuration);
            if (_fadeOutInput != null)
                _fadeOutInput.Text = UiUtilities.FormatTime(_focusedText.FadeOutDuration);
        }
        finally
        {
            _isSyncingUi = false;
        }

        RefreshPreview(fullLayout: true);
    }

    private void PopulateTargetLayerOptions()
    {
        if (_targetLayerOption == null || _focusedText == null)
            return;

        _targetLayerOption.SetBlockSignals(true);
        try
        {
            _targetLayerOption.Clear();
            _targetLayerOption.AddItem("No Output");
            _targetLayerOption.SetItemMetadata(0, -1);

            int targetId = _focusedText.TargetLayerId;
            int selectedIndex = 0;
            bool matched = targetId < 0;

            if (DisplaysManager.Layers != null)
            {
                foreach (var layer in DisplaysManager.Layers)
                {
                    if (layer == null) continue;
                    _targetLayerOption.AddItem(layer.LayerName);
                    int idx = _targetLayerOption.ItemCount - 1;
                    _targetLayerOption.SetItemMetadata(idx, layer.LayerId);
                    if (layer.LayerId == targetId)
                    {
                        selectedIndex = idx;
                        matched = true;
                    }
                }
            }

            if (!matched && targetId >= 0)
            {
                _targetLayerOption.AddItem($"!!! Missing layer {targetId}");
                int idx = _targetLayerOption.ItemCount - 1;
                _targetLayerOption.SetItemMetadata(idx, targetId);
                selectedIndex = idx;
            }

            _targetLayerOption.Selected = selectedIndex;
        }
        finally
        {
            _targetLayerOption.SetBlockSignals(false);
        }
    }

    private static void SelectOptionById(OptionButton button, int id)
    {
        if (button == null) return;
        for (int i = 0; i < button.ItemCount; i++)
        {
            if (button.GetItemId(i) == id)
            {
                button.Selected = i;
                return;
            }
        }
    }

    private bool CanEdit()
    {
        return !_isSyncingUi
               && _focusedCue != null
               && _focusedText != null
               && _history?.IsRestoring != true;
    }

    private void Record(string description, string coalesceKey = null)
    {
        if (_focusedCue == null || _history == null)
            return;
        _history.RecordCueChange(_focusedCue.Id, description, coalesceKey);
    }

    private void EndCoalesce(string key)
    {
        _history?.EndCoalesceSession(key);
    }

    private string CoalesceKey(string field) =>
        _focusedCue != null ? $"cue:{_focusedCue.Id}:text:{field}" : null;

    private void NotifyLiveVisuals()
    {
        if (_focusedText == null)
            return;
        _globalData?.CueCommandExectutor?.RefreshPlayingTextVisuals(_focusedText);
        RefreshPreview(fullLayout: false);
    }

    private void RecalcDuration()
    {
        _focusedText?.RecalculateDuration();
        _focusedCue?.CalculateTotalDuration();
    }

    private void OnAddTextPressed()
    {
        if (_focusedCue == null || _history?.IsRestoring == true)
            return;

        if (_focusedCue.GetTextComponent() != null)
        {
            RefreshFromFocusedCue();
            return;
        }

        Record("Add text component");
        _focusedText = _focusedCue.AddTextComponent();
        RecalcDuration();
        _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        RefreshFromFocusedCue();
    }

    private void OnDeleteTextPressed()
    {
        if (!CanEdit())
            return;

        Record("Remove text component");
        _focusedCue.RemoveICueComponent(_focusedText);
        _focusedText = null;
        RecalcDuration();
        _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        ShowEmptyState();
    }

    private void OnTargetLayerSelected(long index)
    {
        if (!CanEdit() || _targetLayerOption == null)
            return;

        string item = _targetLayerOption.GetItemText((int)index);
        if (item != null && item.StartsWith("!!! Missing", StringComparison.Ordinal))
            return;

        int layerId = (int)_targetLayerOption.GetItemMetadata((int)index);
        if (_focusedText.TargetLayerId == layerId)
            return;

        Record("Edit text target layer");
        _focusedText.TargetLayerId = layerId;
        // Target layer change requires re-GO to re-host on outputs; preview updates immediately.
        RefreshPreview(fullLayout: true);
    }

    private void OnDurationSubmitted(string text)
    {
        if (!CanEdit())
        {
            if (_durationLineEdit != null && _durationLineEdit.HasFocus())
                _durationLineEdit.ReleaseFocus();
            return;
        }

        string raw = (text ?? string.Empty).Trim();
        if (raw.StartsWith("0 (", StringComparison.Ordinal) || raw == "0")
        {
            if (_focusedText.Duration != 0)
            {
                Record("Edit text duration");
                _focusedText.Duration = 0;
                RecalcDuration();
            }

            SyncDurationFieldOnly();
            if (_durationLineEdit != null && _durationLineEdit.HasFocus())
                _durationLineEdit.ReleaseFocus();
            return;
        }

        UiUtilities.ParseAndFormatTime(raw, out double seconds, out bool isValid);
        if (!isValid)
        {
            SyncDurationFieldOnly();
            if (_durationLineEdit != null && _durationLineEdit.HasFocus())
                _durationLineEdit.ReleaseFocus();
            return;
        }

        seconds = Math.Max(0, seconds);
        if (Math.Abs(_focusedText.Duration - seconds) >= 1e-9)
        {
            Record("Edit text duration");
            _focusedText.Duration = seconds;
            RecalcDuration();
        }

        SyncDurationFieldOnly();
        if (_durationLineEdit != null && _durationLineEdit.HasFocus())
            _durationLineEdit.ReleaseFocus();
    }

    /// <summary>
    /// Commits fade-in or fade-out duration from a time LineEdit.
    /// </summary>
    /// <param name="text">User-entered time string.</param>
    /// <param name="isIn">True for fade-in; false for fade-out.</param>
    private void OnFadeSubmitted(string text, bool isIn)
    {
        var field = isIn ? _fadeInInput : _fadeOutInput;
        if (field == null) return;

        if (!CanEdit())
        {
            if (field.HasFocus()) field.ReleaseFocus();
            return;
        }

        var formatted = UiUtilities.ParseAndFormatTime(text, out var seconds, out string labeled);
        if (string.IsNullOrEmpty(formatted))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid text fade time: {text}", 1);
            double current = isIn
                ? _focusedText.FadeInDuration
                : _focusedText.FadeOutDuration;
            field.Text = UiUtilities.FormatTime(current);
            if (field.HasFocus()) field.ReleaseFocus();
            return;
        }

        seconds = Math.Max(0.0, seconds);
        field.Text = formatted;
        field.TooltipText = labeled + (isIn
            ? " (fade-in at play start)"
            : " (fade-out on stop)");

        double existing = isIn
            ? _focusedText.FadeInDuration
            : _focusedText.FadeOutDuration;
        if (Mathf.IsEqualApprox((float)existing, (float)seconds))
        {
            if (field.HasFocus()) field.ReleaseFocus();
            return;
        }

        Record(isIn ? "Edit text fade-in" : "Edit text fade-out");
        if (isIn)
            _focusedText.FadeInDuration = seconds;
        else
            _focusedText.FadeOutDuration = seconds;

        if (field.HasFocus()) field.ReleaseFocus();
    }

    private void SyncDurationFieldOnly()
    {
        if (_focusedText == null || _durationLineEdit == null)
            return;

        _isSyncingUi = true;
        try
        {
            if (_focusedText.Duration <= 0)
            {
                _durationLineEdit.Text = "0 (until stopped)";
                _durationLineEdit.TooltipText = "0 = stay active until stopped. Enter a time to auto-end.";
            }
            else
            {
                _durationLineEdit.Text = UiUtilities.ParseAndFormatTime(
                    _focusedText.Duration.ToString(CultureInfo.InvariantCulture),
                    out _,
                    out string tip);
                _durationLineEdit.TooltipText = tip + " (0 = until stopped)";
            }
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void OnContentChanged()
    {
        if (!CanEdit())
            return;

        string content = _contentTextEdit.Text ?? string.Empty;
        if (content == (_focusedText.Content ?? string.Empty))
            return;

        Record("Edit text content", CoalesceKey("content"));
        _focusedText.Content = content;
        NotifyLiveVisuals();
    }

    private void OnContentFocusExited()
    {
        EndCoalesce(CoalesceKey("content"));
    }

    private void OnUseBbcodeToggled(bool pressed)
    {
        if (!CanEdit())
            return;
        if (_focusedText.UseBbcode == pressed)
            return;

        Record("Edit text BBCode mode");
        _focusedText.UseBbcode = pressed;
        NotifyLiveVisuals();
    }

    private void OnFontSelected(long index)
    {
        if (!CanEdit() || _fontOption == null)
            return;

        string fontName = _fontOption.GetItemMetadata((int)index).AsString() ?? string.Empty;
        string current = _focusedText.FontName ?? string.Empty;
        if (string.Equals(fontName, current, StringComparison.Ordinal))
            return;

        Record("Edit text font");
        _focusedText.FontName = fontName;
        // Clear file override so system family selection is effective.
        if (!string.IsNullOrEmpty(fontName))
            _focusedText.FontPath = string.Empty;
        NotifyLiveVisuals();
    }

    private void OnHAlignSelected(long index)
    {
        if (!CanEdit())
            return;

        int id = _hAlignOption.GetItemId((int)index);
        var align = (HorizontalAlignment)id;
        if (_focusedText.HorizontalAlignment == align)
            return;

        Record("Edit text horizontal alignment");
        _focusedText.HorizontalAlignment = align;
        NotifyLiveVisuals();
    }

    private void OnVAlignSelected(long index)
    {
        if (!CanEdit())
            return;

        int id = _vAlignOption.GetItemId((int)index);
        var align = (VerticalAlignment)id;
        if (_focusedText.VerticalAlignment == align)
            return;

        Record("Edit text vertical alignment");
        _focusedText.VerticalAlignment = align;
        NotifyLiveVisuals();
    }

    private void OnFontSizeChanged(double value)
    {
        if (!CanEdit())
            return;

        int size = Mathf.Max(1, (int)Math.Round(value));
        if (_focusedText.FontSize == size)
            return;

        Record("Edit text font size", CoalesceKey("fontsize"));
        _focusedText.FontSize = size;
        NotifyLiveVisuals();
    }

    private void EndFontSizeCoalesce() => EndCoalesce(CoalesceKey("fontsize"));

    private void OnFontColorChanged(Color color)
    {
        if (!CanEdit())
            return;
        if (_focusedText.FontColor.IsEqualApprox(color))
            return;

        Record("Edit text font colour", CoalesceKey("fontcolor"));
        _focusedText.FontColor = color;
        NotifyLiveVisuals();
    }

    private void EndFontColorCoalesce() => EndCoalesce(CoalesceKey("fontcolor"));

    private void OnOpacityChanged(double value)
    {
        if (!CanEdit())
            return;

        float opacity = Mathf.Clamp((float)value / 100f, 0f, 1f);
        if (Mathf.IsEqualApprox(_focusedText.Opacity, opacity))
            return;

        Record("Edit text opacity", CoalesceKey("opacity"));
        _focusedText.Opacity = opacity;
        NotifyLiveVisuals();
    }

    private void EndOpacityCoalesce() => EndCoalesce(CoalesceKey("opacity"));

    private void OnMarginsChanged(double value)
    {
        if (!CanEdit())
            return;

        int margins = Mathf.Max(0, (int)Math.Round(value));
        if (_focusedText.Margins == margins)
            return;

        Record("Edit text margins", CoalesceKey("margins"));
        _focusedText.Margins = margins;
        NotifyLiveVisuals();
    }

    private void EndMarginsCoalesce() => EndCoalesce(CoalesceKey("margins"));

    private void OnAutowrapToggled(bool pressed)
    {
        if (!CanEdit())
            return;
        if (_focusedText.Autowrap == pressed)
            return;

        Record("Edit text wrap");
        _focusedText.Autowrap = pressed;
        NotifyLiveVisuals();
    }

    private void OnOutlineSizeChanged(double value)
    {
        if (!CanEdit())
            return;

        int size = Mathf.Max(0, (int)Math.Round(value));
        if (_focusedText.OutlineSize == size)
            return;

        Record("Edit text outline size", CoalesceKey("outlinesize"));
        _focusedText.OutlineSize = size;
        NotifyLiveVisuals();
    }

    private void EndOutlineSizeCoalesce() => EndCoalesce(CoalesceKey("outlinesize"));

    private void OnOutlineColorChanged(Color color)
    {
        if (!CanEdit())
            return;
        if (_focusedText.OutlineColor.IsEqualApprox(color))
            return;

        Record("Edit text outline colour", CoalesceKey("outlinecolor"));
        _focusedText.OutlineColor = color;
        NotifyLiveVisuals();
    }

    private void EndOutlineColorCoalesce() => EndCoalesce(CoalesceKey("outlinecolor"));

    private void OnBackgroundToggled(bool pressed)
    {
        if (!CanEdit())
            return;
        if (_focusedText.BackgroundEnabled == pressed)
            return;

        Record("Edit text background");
        _focusedText.BackgroundEnabled = pressed;
        NotifyLiveVisuals();
    }

    private void OnBackgroundColorChanged(Color color)
    {
        if (!CanEdit())
            return;
        if (_focusedText.BackgroundColor.IsEqualApprox(color))
            return;

        Record("Edit text background colour", CoalesceKey("bgcolor"));
        _focusedText.BackgroundColor = color;
        NotifyLiveVisuals();
    }

    private void EndBackgroundColorCoalesce() => EndCoalesce(CoalesceKey("bgcolor"));
}
