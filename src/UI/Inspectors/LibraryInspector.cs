// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Inspectors;

/// <summary>
/// Inspector tab for the user-scoped cue library: browse folders, save/load cues,
/// organize entries, and package optional media.
/// </summary>
public partial class LibraryInspector : Control
{
    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private CueLibraryManager _library;

    // Toolbar
    private Button _saveButton;
    private Button _loadButton;
    private Button _newFolderButton;
    private Button _renameButton;
    private Button _deleteButton;
    private Button _refreshButton;
    private Button _openFolderButton;
    private LineEdit _searchEdit;
    private Label _pathLabel;

    // Panes
    private HSplitContainer _mainSplit;
    /// <summary>
    /// Must be <see cref="Godot.Tree"/> — a project demo type also named <c>Tree</c>
    /// lives in the global namespace and would make typed GetNode lookups fail.
    /// </summary>
    private Godot.Tree _folderTree;
    private ItemList _entryList;
    private Label _detailLabel;
    private Label _emptyLabel;

    private const int DefaultSplitOffset = 200;

    // Save dialog
    private AcceptDialog _saveDialog;
    private LineEdit _saveNameEdit;
    private CheckBox _saveIncludeChildren;
    private CheckBox _saveIncludeMedia;
    private Label _saveFolderLabel;

    // Load options (inline toggles)
    private OptionButton _insertModeOption;
    private CheckBox _copyMediaCheck;

    // Name dialog (folder / rename)
    private AcceptDialog _nameDialog;
    private LineEdit _nameDialogEdit;
    private Action<string> _nameDialogCallback;

    // Confirm delete
    private ConfirmationDialog _confirmDialog;
    private Action _confirmCallback;

    private string _selectedFolder = string.Empty;
    private string _selectedEntryRelative = string.Empty;
    private bool _isRefreshing;
    private bool _treeSignalsWired;
    private bool _toolbarSignalsWired;
    private bool _entryListSignalsWired;
    private int _readyRetryCount;
    private readonly List<LibraryEntryInfo> _visibleEntries = new();

    private const int MaxReadyRetries = 20;

    /// <inheritdoc />
    public override void _Ready()
    {
        // Do not assume GlobalData children or unique-name resolution are ready this frame.
        // Resolve + refresh on a deferred pass (with retries).
        BuildDialogs();
        VisibilityChanged += OnVisibilityChanged;
        CallDeferred(nameof(TryInitializeAndRefresh));
        CallDeferred(nameof(ApplyLocalization));
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        VisibilityChanged -= OnVisibilityChanged;
        if (_globalSignals != null)
        {
            _globalSignals.ShellFocused -= OnShellFocused;
            _globalSignals.LocaleChanged -= OnLocaleChanged;
        }
        UnwireToolbarSignals();
        UnwireTreeSignals();
    }

    /// <summary>
    /// Applies catalog translations to toolbar labels, placeholders, and tooltips.
    /// </summary>
    private void ApplyLocalization()
    {
        if (!GodotObject.IsInstanceValid(this))
            return;
        UiLocalizer.LocalizeTree(this);
        if (_globalSignals != null)
        {
            _globalSignals.LocaleChanged -= OnLocaleChanged;
            _globalSignals.LocaleChanged += OnLocaleChanged;
        }
    }

    /// <summary>
    /// Re-localizes library inspector chrome when the UI language changes.
    /// </summary>
    /// <param name="localeCode">New locale code.</param>
    private void OnLocaleChanged(string localeCode)
    {
        if (!GodotObject.IsInstanceValid(this))
            return;
        UiLocalizer.LocalizeTree(this);
    }

    /// <summary>
    /// Resolves GlobalData, library manager, and scene nodes; retries if anything is still missing.
    /// </summary>
    private void TryInitializeAndRefresh()
    {
        if (!EnsureDependencies())
        {
            _readyRetryCount++;
            if (_readyRetryCount <= MaxReadyRetries)
            {
                CallDeferred(nameof(TryInitializeAndRefresh));
                return;
            }

            GD.PrintErr(
                "LibraryInspector:TryInitializeAndRefresh - Gave up waiting for dependencies. " +
                $"library={_library != null}, tree={_folderTree != null}, globalData={_globalData != null}");
            SetDetail("Cue library failed to initialize (see log).");
            return;
        }

        _readyRetryCount = 0;
        RefreshAll();
        EnsureSplitLayout();
    }

    /// <summary>
    /// Lazily resolves managers and UI nodes. Safe to call repeatedly.
    /// </summary>
    /// <returns>True when library manager and folder tree are available.</returns>
    private bool EnsureDependencies()
    {
        if (_globalData == null || !GodotObject.IsInstanceValid(_globalData))
            _globalData = GetNodeOrNull<GlobalData>("/root/GlobalData");

        if (_globalSignals == null || !GodotObject.IsInstanceValid(_globalSignals))
        {
            _globalSignals = GetNodeOrNull<GlobalSignals>("/root/GlobalSignals");
            if (_globalSignals != null)
            {
                // Avoid double-subscribe if retried
                _globalSignals.ShellFocused -= OnShellFocused;
                _globalSignals.ShellFocused += OnShellFocused;
            }
        }

        if (_library == null || !GodotObject.IsInstanceValid(_library))
            _library = ResolveLibraryManager();

        CacheNodes();
        WireToolbarSignals();
        WireEntryListSignals();
        WireTreeSignals();

        return _library != null && _folderTree != null
               && GodotObject.IsInstanceValid(_library)
               && GodotObject.IsInstanceValid(_folderTree);
    }

    private CueLibraryManager ResolveLibraryManager()
    {
        if (_globalData == null)
            return null;

        if (_globalData.CueLibraryManager != null && GodotObject.IsInstanceValid(_globalData.CueLibraryManager))
            return _globalData.CueLibraryManager;

        var byName = _globalData.GetNodeOrNull<CueLibraryManager>(nameof(CueLibraryManager));
        if (byName != null)
            return byName;

        foreach (Node child in _globalData.GetChildren())
        {
            if (child is CueLibraryManager manager)
                return manager;
        }

        return null;
    }

    private void CacheNodes()
    {
        _saveButton ??= GetNodeOrNull<Button>("%SaveButton");
        _loadButton ??= GetNodeOrNull<Button>("%LoadButton");
        _newFolderButton ??= GetNodeOrNull<Button>("%NewFolderButton");
        _renameButton ??= GetNodeOrNull<Button>("%RenameButton");
        _deleteButton ??= GetNodeOrNull<Button>("%DeleteButton");
        _refreshButton ??= GetNodeOrNull<Button>("%RefreshButton");
        _openFolderButton ??= GetNodeOrNull<Button>("%OpenFolderButton");
        _searchEdit ??= GetNodeOrNull<LineEdit>("%SearchEdit");
        _pathLabel ??= GetNodeOrNull<Label>("%PathLabel");
        _mainSplit ??= GetNodeOrNull<HSplitContainer>("%MainSplit")
                       ?? GetNodeOrNull<HSplitContainer>("RootMargin/VBox/MainSplit");
        _entryList ??= GetNodeOrNull<ItemList>("%EntryList")
                       ?? GetNodeOrNull<ItemList>("RootMargin/VBox/MainSplit/RightPane/EntryList");
        _detailLabel ??= GetNodeOrNull<Label>("%DetailLabel");
        _emptyLabel ??= GetNodeOrNull<Label>("%EmptyLabel");
        _insertModeOption ??= GetNodeOrNull<OptionButton>("%InsertModeOption");
        _copyMediaCheck ??= GetNodeOrNull<CheckBox>("%CopyMediaCheck");

        if (_folderTree == null || !GodotObject.IsInstanceValid(_folderTree))
        {
            // Explicit Godot.Tree — bare "Tree" binds to the global demo class in src/UI/Shell/Tree.cs.
            _folderTree = GetNodeOrNull<Godot.Tree>("%FolderTree")
                          ?? GetNodeOrNull<Godot.Tree>("RootMargin/VBox/MainSplit/LeftPanel/FolderTree")
                          ?? GetNodeOrNull<Godot.Tree>("RootMargin/VBox/MainSplit/FolderTree")
                          ?? FindChild("FolderTree", recursive: true, owned: false) as Godot.Tree;
        }

        if (_folderTree != null && GodotObject.IsInstanceValid(_folderTree))
        {
            // Tree must have columns before CreateItem or items will not appear.
            _folderTree.Columns = 1;
            _folderTree.HideRoot = true;
            _folderTree.SelectMode = Godot.Tree.SelectModeEnum.Single;
            _folderTree.ColumnTitlesVisible = false;
            _folderTree.SetColumnExpand(0, true);
            _folderTree.SetColumnClipContent(0, true);
        }

        if (_insertModeOption != null && _insertModeOption.ItemCount == 0)
        {
            _insertModeOption.AddItem("Below selection", (int)LibraryInsertMode.BelowSelection);
            _insertModeOption.AddItem("End of list", (int)LibraryInsertMode.End);
            _insertModeOption.AddItem("As child of selection", (int)LibraryInsertMode.AsChild);
            _insertModeOption.Selected = 0;
        }

        if (_copyMediaCheck != null)
            _copyMediaCheck.ButtonPressed = true;
    }

    private void WireToolbarSignals()
    {
        if (_toolbarSignalsWired)
            return;

        // Only mark wired once we have at least the primary actions.
        if (_saveButton == null && _refreshButton == null)
            return;

        if (_saveButton != null) _saveButton.Pressed += OnSavePressed;
        if (_loadButton != null) _loadButton.Pressed += OnLoadPressed;
        if (_newFolderButton != null) _newFolderButton.Pressed += OnNewFolderPressed;
        if (_renameButton != null) _renameButton.Pressed += OnRenamePressed;
        if (_deleteButton != null) _deleteButton.Pressed += OnDeletePressed;
        if (_refreshButton != null) _refreshButton.Pressed += OnRefreshPressed;
        if (_openFolderButton != null) _openFolderButton.Pressed += OnOpenFolderPressed;
        if (_searchEdit != null) _searchEdit.TextChanged += OnSearchChanged;

        _toolbarSignalsWired = true;
        WireEntryListSignals();
    }

    private void WireEntryListSignals()
    {
        if (_entryListSignalsWired || _entryList == null || !GodotObject.IsInstanceValid(_entryList))
            return;

        _entryList.ItemSelected += OnEntrySelected;
        _entryList.ItemActivated += OnEntryActivated;
        _entryListSignalsWired = true;
    }

    private void UnwireToolbarSignals()
    {
        if (!_toolbarSignalsWired)
            return;

        if (_saveButton != null) _saveButton.Pressed -= OnSavePressed;
        if (_loadButton != null) _loadButton.Pressed -= OnLoadPressed;
        if (_newFolderButton != null) _newFolderButton.Pressed -= OnNewFolderPressed;
        if (_renameButton != null) _renameButton.Pressed -= OnRenamePressed;
        if (_deleteButton != null) _deleteButton.Pressed -= OnDeletePressed;
        if (_refreshButton != null) _refreshButton.Pressed -= OnRefreshPressed;
        if (_openFolderButton != null) _openFolderButton.Pressed -= OnOpenFolderPressed;
        if (_searchEdit != null) _searchEdit.TextChanged -= OnSearchChanged;

        _toolbarSignalsWired = false;

        if (_entryListSignalsWired && _entryList != null && GodotObject.IsInstanceValid(_entryList))
        {
            _entryList.ItemSelected -= OnEntrySelected;
            _entryList.ItemActivated -= OnEntryActivated;
        }
        _entryListSignalsWired = false;
    }

    private void WireTreeSignals()
    {
        if (_treeSignalsWired || _folderTree == null || !GodotObject.IsInstanceValid(_folderTree))
            return;

        _folderTree.ItemSelected += OnFolderSelected;
        _folderTree.ItemMouseSelected += OnFolderMouseSelected;
        _treeSignalsWired = true;
    }

    private void UnwireTreeSignals()
    {
        if (!_treeSignalsWired || _folderTree == null)
            return;

        if (GodotObject.IsInstanceValid(_folderTree))
        {
            _folderTree.ItemSelected -= OnFolderSelected;
            _folderTree.ItemMouseSelected -= OnFolderMouseSelected;
        }

        _treeSignalsWired = false;
    }

    private void OnRefreshPressed() => RefreshAll();

    private void BuildDialogs()
    {
        // Save options dialog
        _saveDialog = new AcceptDialog
        {
            Title = "Save to Library",
            OkButtonText = "Save",
            DialogHideOnOk = false
        };
        var saveVBox = new VBoxContainer();
        saveVBox.AddThemeConstantOverride("separation", 8);

        _saveFolderLabel = new Label { Text = "Folder: /" };
        saveVBox.AddChild(_saveFolderLabel);

        saveVBox.AddChild(new Label { Text = "Name" });
        _saveNameEdit = new LineEdit { PlaceholderText = "Cue name" };
        saveVBox.AddChild(_saveNameEdit);

        _saveIncludeChildren = new CheckBox
        {
            Text = "Include nested children",
            ButtonPressed = true
        };
        saveVBox.AddChild(_saveIncludeChildren);

        _saveIncludeMedia = new CheckBox
        {
            Text = "Copy media into library",
            ButtonPressed = true
        };
        saveVBox.AddChild(_saveIncludeMedia);

        _saveDialog.AddChild(saveVBox);
        _saveDialog.Confirmed += OnSaveDialogConfirmed;
        AddChild(_saveDialog);

        // Generic name dialog
        _nameDialog = new AcceptDialog
        {
            Title = "Name",
            OkButtonText = "OK",
            DialogHideOnOk = false
        };
        _nameDialogEdit = new LineEdit();
        _nameDialog.AddChild(_nameDialogEdit);
        _nameDialog.Confirmed += OnNameDialogConfirmed;
        AddChild(_nameDialog);

        // Confirm
        _confirmDialog = new ConfirmationDialog
        {
            Title = "Confirm",
            OkButtonText = "Delete",
            CancelButtonText = "Cancel"
        };
        _confirmDialog.Confirmed += OnConfirmDialogConfirmed;
        AddChild(_confirmDialog);
    }

    private void OnVisibilityChanged()
    {
        if (!Visible)
            return;

        // Tab starts hidden; resolve deps + rebuild after layout has a real width.
        CallDeferred(nameof(TryInitializeAndRefresh));
    }

    /// <summary>
    /// Ensures the left folder pane has a usable width after the tab becomes visible.
    /// </summary>
    private void EnsureSplitLayout()
    {
        if (_mainSplit == null || !GodotObject.IsInstanceValid(_mainSplit))
            _mainSplit = GetNodeOrNull<HSplitContainer>("%MainSplit")
                         ?? GetNodeOrNull<HSplitContainer>("RootMargin/VBox/MainSplit");

        if (_mainSplit == null)
            return;

        // Godot 4.6 SplitContainer uses SplitOffset; a zero/negative value collapses the tree pane.
        if (_mainSplit.SplitOffset < 120)
            _mainSplit.SplitOffset = DefaultSplitOffset;
    }

    private void OnShellFocused(int _)
    {
        UpdateActionButtons();
    }

    // ── Refresh ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the folder tree and entry list from disk.
    /// </summary>
    public void RefreshAll()
    {
        if (!EnsureDependencies())
        {
            // Still warming up — retry via the init path rather than erroring once.
            if (_readyRetryCount < MaxReadyRetries)
                CallDeferred(nameof(TryInitializeAndRefresh));
            else
                SetDetail("Cue library is not available.");
            return;
        }

        _isRefreshing = true;
        try
        {
            _library.EnsureLibraryInitialized();
            if (_pathLabel != null)
                _pathLabel.Text = _library.LibraryRootPath;

            RebuildFolderTree();
            RefreshEntryList();
            UpdateActionButtons();
            EnsureSplitLayout();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void RebuildFolderTree()
    {
        if (!EnsureDependencies())
        {
            GD.PrintErr(
                "LibraryInspector:RebuildFolderTree - Still not ready. " +
                $"tree={_folderTree != null}, library={_library != null}");
            return;
        }

        string previous = _selectedFolder;

        // Columns must be set before CreateItem (CreateItem returns null when Columns == 0).
        _folderTree.Columns = 1;
        _folderTree.HideRoot = true;
        _folderTree.Clear();

        var root = _folderTree.CreateItem();
        if (root == null)
        {
            GD.PrintErr("LibraryInspector:RebuildFolderTree - CreateItem returned null (check Columns).");
            return;
        }

        root.SetText(0, "Library");
        root.SetMetadata(0, string.Empty);
        root.SetSelectable(0, true);
        root.Collapsed = false;

        // Explicit "Library root" row so users can select the root with HideRoot = true.
        var rootRow = _folderTree.CreateItem(root);
        rootRow.SetText(0, "(Library root)");
        rootRow.SetMetadata(0, string.Empty);
        rootRow.SetTooltipText(0, "Top-level library folder");

        var folders = _library.ListAllFolders();
        GD.Print($"LibraryInspector:RebuildFolderTree - {folders.Count} folder(s) under {_library.LibraryRootPath}");

        foreach (var folder in folders)
        {
            if (folder == null || string.IsNullOrEmpty(folder.RelativePath))
                continue;
            EnsureTreePath(root, folder.RelativePath);
        }

        ExpandAllTreeItems(root);

        // Reselect previous folder if possible; otherwise select library root.
        SelectFolderInTree(previous);
        if (_folderTree.GetSelected() == null)
        {
            rootRow.Select(0);
            _selectedFolder = string.Empty;
        }

        _folderTree.QueueRedraw();
    }

    private TreeItem EnsureTreePath(TreeItem root, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || root == null)
            return root;

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        TreeItem current = root;
        string built = string.Empty;
        foreach (var part in parts)
        {
            built = string.IsNullOrEmpty(built) ? part : built + "/" + part;
            TreeItem child = FindChildByRelative(current, built);
            if (child == null)
            {
                child = _folderTree.CreateItem(current);
                if (child == null)
                {
                    GD.PrintErr($"LibraryInspector:EnsureTreePath - Failed to create item for '{built}'.");
                    return current;
                }
                child.SetText(0, part);
                child.SetMetadata(0, built);
                child.SetTooltipText(0, built);
                child.Collapsed = false;
            }
            current = child;
        }
        return current;
    }

    private static void ExpandAllTreeItems(TreeItem item)
    {
        if (item == null) return;
        item.Collapsed = false;
        var child = item.GetFirstChild();
        while (child != null)
        {
            ExpandAllTreeItems(child);
            child = child.GetNext();
        }
    }

    private static TreeItem FindChildByRelative(TreeItem parent, string relative)
    {
        var child = parent.GetFirstChild();
        while (child != null)
        {
            if (string.Equals(child.GetMetadata(0).AsString(), relative, StringComparison.OrdinalIgnoreCase))
                return child;
            child = child.GetNext();
        }
        return null;
    }

    private void SelectFolderInTree(string relative)
    {
        if (_folderTree == null) return;
        var root = _folderTree.GetRoot();
        if (root == null) return;

        // Prefer a visible child row (HideRoot means selecting the root is not useful).
        var item = FindVisibleItemByRelative(root, relative ?? string.Empty);
        if (item == null && string.IsNullOrEmpty(relative))
        {
            // Fall back to first child ("(Library root)")
            item = root.GetFirstChild();
        }

        if (item != null)
        {
            item.Select(0);
            var p = item.GetParent();
            while (p != null)
            {
                p.Collapsed = false;
                p = p.GetParent();
            }
            _selectedFolder = item.GetMetadata(0).AsString() ?? string.Empty;
            _folderTree.ScrollToItem(item);
        }
    }

    /// <summary>
    /// Finds a tree item by relative path metadata, skipping the hidden root when metadata is empty
    /// so the visible "(Library root)" row is preferred.
    /// </summary>
    private static TreeItem FindVisibleItemByRelative(TreeItem root, string relative)
    {
        if (root == null) return null;

        // Search children first so empty metadata matches the visible root row, not the hidden root.
        var child = root.GetFirstChild();
        while (child != null)
        {
            var found = FindItemRecursive(child, relative);
            if (found != null)
                return found;
            child = child.GetNext();
        }

        // Only match the hidden root when we truly need it (should be rare with HideRoot).
        if (string.Equals(root.GetMetadata(0).AsString(), relative, StringComparison.OrdinalIgnoreCase))
            return root;

        return null;
    }

    private static TreeItem FindItemRecursive(TreeItem item, string relative)
    {
        if (item == null) return null;
        if (string.Equals(item.GetMetadata(0).AsString(), relative, StringComparison.OrdinalIgnoreCase))
            return item;
        var child = item.GetFirstChild();
        while (child != null)
        {
            var found = FindItemRecursive(child, relative);
            if (found != null) return found;
            child = child.GetNext();
        }
        return null;
    }

    private void RefreshEntryList()
    {
        if (_entryList == null || _library == null) return;

        _entryList.Clear();
        _visibleEntries.Clear();
        _selectedEntryRelative = string.Empty;

        string filter = _searchEdit?.Text?.Trim() ?? string.Empty;
        var entries = _library.ListEntries(_selectedFolder);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(filter) &&
                entry.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            _visibleEntries.Add(entry);
            string suffix = string.Empty;
            if (entry.CueCount > 1) suffix += $"  · {entry.CueCount} cues";
            if (entry.HasMedia) suffix += "  · media";
            _entryList.AddItem(entry.DisplayName + suffix);
        }

        // Also show subfolders as navigable items at top of list for convenience
        // (folders stay primary in the tree)

        if (_emptyLabel != null)
        {
            bool empty = _visibleEntries.Count == 0;
            _emptyLabel.Visible = empty;
            _emptyLabel.Text = empty
                ? (string.IsNullOrEmpty(_selectedFolder)
                    ? "Library is empty. Select a cue and click Save to Library."
                    : "No cues in this folder.")
                : string.Empty;
        }

        SetDetail(string.IsNullOrEmpty(_selectedFolder)
            ? "Select a library cue to see details."
            : $"Folder: /{_selectedFolder}");
    }

    private void UpdateActionButtons()
    {
        bool hasEntry = !string.IsNullOrEmpty(_selectedEntryRelative);
        bool hasFolder = !string.IsNullOrEmpty(_selectedFolder);
        bool hasCue = _globalData != null && _globalData.FocusedCue >= 0;

        if (_loadButton != null) _loadButton.Disabled = !hasEntry;
        if (_saveButton != null) _saveButton.Disabled = !hasCue;
        if (_renameButton != null) _renameButton.Disabled = !hasEntry && !hasFolder;
        if (_deleteButton != null) _deleteButton.Disabled = !hasEntry && !hasFolder;
    }

    private void SetDetail(string text)
    {
        if (_detailLabel != null)
            _detailLabel.Text = text ?? string.Empty;
    }

    // ── Selection handlers ─────────────────────────────────────────────────

    private void OnFolderSelected()
    {
        if (_isRefreshing || _folderTree == null) return;
        var item = _folderTree.GetSelected();
        if (item == null) return;
        _selectedFolder = item.GetMetadata(0).AsString() ?? string.Empty;
        RefreshEntryList();
        UpdateActionButtons();
    }

    private void OnFolderMouseSelected(Vector2 _pos, long _mouseButtonIndex)
    {
        OnFolderSelected();
    }

    private void OnEntrySelected(long index)
    {
        int i = (int)index;
        if (i < 0 || i >= _visibleEntries.Count)
        {
            _selectedEntryRelative = string.Empty;
            UpdateActionButtons();
            return;
        }

        var entry = _visibleEntries[i];
        _selectedEntryRelative = entry.RelativePath;
        ShowEntryDetail(entry);
        UpdateActionButtons();
    }

    private void OnEntryActivated(long index)
    {
        OnEntrySelected(index);
        OnLoadPressed();
    }

    private void ShowEntryDetail(LibraryEntryInfo entry)
    {
        if (entry == null)
        {
            SetDetail(string.Empty);
            return;
        }

        string saved = string.IsNullOrEmpty(entry.SavedAt) ? "—" : entry.SavedAt;
        SetDetail(
            $"{entry.DisplayName}\n" +
            $"Saved: {saved}\n" +
            $"Cues: {entry.CueCount}" +
            (entry.IncludeChildren ? " (includes children)" : "") + "\n" +
            $"Media: {(entry.HasMedia ? (entry.LibraryRelativeMedia ? "packaged" : "sidecar present") : "none")}\n" +
            $"Path: {entry.RelativePath}");
    }

    private void OnSearchChanged(string _)
    {
        RefreshEntryList();
    }

    // ── Actions ────────────────────────────────────────────────────────────

    private void OnSavePressed()
    {
        if (_library == null || _globalData == null) return;

        int cueId = _globalData.FocusedCue;
        var cue = CueList.FetchCueFromId(cueId);
        if (cue == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Library: select a cue before saving.", (int)LogType.Warning);
            return;
        }

        _saveNameEdit.Text = string.IsNullOrWhiteSpace(cue.Name) ? $"Cue {cue.Id}" : cue.Name;
        _saveIncludeChildren.ButtonPressed = cue.ChildCues != null && cue.ChildCues.Count > 0;
        _saveIncludeChildren.Disabled = cue.ChildCues == null || cue.ChildCues.Count == 0;
        _saveIncludeMedia.ButtonPressed = true;
        _saveFolderLabel.Text = string.IsNullOrEmpty(_selectedFolder)
            ? "Folder: / (library root)"
            : $"Folder: /{_selectedFolder}";

        _saveDialog.PopupCentered(new Vector2I(420, 260));
        _saveNameEdit.GrabFocus();
        _saveNameEdit.SelectAll();
    }

    private void OnSaveDialogConfirmed()
    {
        if (_library == null || _globalData == null) return;

        var cue = CueList.FetchCueFromId(_globalData.FocusedCue);
        if (cue == null)
        {
            _saveDialog.Hide();
            return;
        }

        string name = _saveNameEdit.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Library: enter a name for the entry.", (int)LogType.Warning);
            return;
        }

        bool exists = _library.EntryExists(_selectedFolder, name);
        if (exists)
        {
            // Ask overwrite via confirm then re-save
            _confirmDialog.DialogText = $"'{name}' already exists. Overwrite?";
            _confirmCallback = () => DoSave(cue, name, overwrite: true);
            _confirmDialog.OkButtonText = "Overwrite";
            _confirmDialog.PopupCentered();
            return;
        }

        DoSave(cue, name, overwrite: false);
        _saveDialog.Hide();
    }

    private void DoSave(Cue cue, string name, bool overwrite)
    {
        var options = new LibrarySaveOptions
        {
            RelativeFolder = _selectedFolder,
            DisplayName = name,
            IncludeChildren = _saveIncludeChildren.ButtonPressed,
            IncludeMedia = _saveIncludeMedia.ButtonPressed,
            Overwrite = overwrite
        };

        var result = _library.SaveCue(cue, options);
        if (!result.Success)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Library: save failed — {result.Message}", (int)LogType.Error);
            return;
        }

        _saveDialog.Hide();
        RefreshEntryList();
        // Select the new entry
        for (int i = 0; i < _visibleEntries.Count; i++)
        {
            if (string.Equals(_visibleEntries[i].RelativePath, result.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                _entryList.Select(i);
                OnEntrySelected(i);
                break;
            }
        }
    }

    private void OnLoadPressed()
    {
        if (_library == null || string.IsNullOrEmpty(_selectedEntryRelative))
            return;

        var options = new LibraryLoadOptions
        {
            InsertMode = _insertModeOption != null
                ? (LibraryInsertMode)_insertModeOption.GetSelectedId()
                : LibraryInsertMode.BelowSelection,
            CopyMediaIntoShow = _copyMediaCheck == null || _copyMediaCheck.ButtonPressed
        };

        // If insert mode uses GetSelectedId and items were added with ids, OK.
        // Fallback if GetSelectedId returns -1:
        if (_insertModeOption != null && _insertModeOption.GetSelectedId() < 0)
            options.InsertMode = (LibraryInsertMode)_insertModeOption.Selected;

        var result = _library.LoadEntry(_selectedEntryRelative, options);
        if (!result.Success)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Library: load failed — {result.Message}", (int)LogType.Error);
        }
    }

    private void OnNewFolderPressed()
    {
        if (_library == null) return;
        _nameDialog.Title = "New Folder";
        _nameDialogEdit.Text = "New Folder";
        _nameDialogCallback = name =>
        {
            var result = _library.CreateFolder(_selectedFolder, name);
            if (!result.Success)
            {
                _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                    $"Library: {result.Message}", (int)LogType.Error);
                return;
            }
            _nameDialog.Hide();
            string created = result.RelativePath;
            RebuildFolderTree();
            SelectFolderInTree(created);
            RefreshEntryList();
            UpdateActionButtons();
        };
        _nameDialog.PopupCentered(new Vector2I(360, 120));
        _nameDialogEdit.GrabFocus();
        _nameDialogEdit.SelectAll();
    }

    private void OnRenamePressed()
    {
        if (_library == null) return;

        // Prefer entry rename when an entry is selected
        if (!string.IsNullOrEmpty(_selectedEntryRelative))
        {
            var entry = _visibleEntries.FirstOrDefault(e =>
                string.Equals(e.RelativePath, _selectedEntryRelative, StringComparison.OrdinalIgnoreCase));
            string current = entry?.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(_selectedEntryRelative);
            _nameDialog.Title = "Rename Cue";
            _nameDialogEdit.Text = current;
            _nameDialogCallback = name =>
            {
                var result = _library.RenameEntry(_selectedEntryRelative, name);
                if (!result.Success)
                {
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Library: {result.Message}", (int)LogType.Error);
                    return;
                }
                _nameDialog.Hide();
                _selectedEntryRelative = result.RelativePath;
                RefreshEntryList();
                // Reselect
                for (int i = 0; i < _visibleEntries.Count; i++)
                {
                    if (string.Equals(_visibleEntries[i].RelativePath, result.RelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _entryList.Select(i);
                        OnEntrySelected(i);
                        break;
                    }
                }
            };
            _nameDialog.PopupCentered(new Vector2I(360, 120));
            _nameDialogEdit.GrabFocus();
            _nameDialogEdit.SelectAll();
            return;
        }

        if (!string.IsNullOrEmpty(_selectedFolder))
        {
            string current = System.IO.Path.GetFileName(_selectedFolder.Replace('/', System.IO.Path.DirectorySeparatorChar));
            _nameDialog.Title = "Rename Folder";
            _nameDialogEdit.Text = current;
            _nameDialogCallback = name =>
            {
                var result = _library.RenameFolder(_selectedFolder, name);
                if (!result.Success)
                {
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Library: {result.Message}", (int)LogType.Error);
                    return;
                }
                _nameDialog.Hide();
                _selectedFolder = result.RelativePath;
                RebuildFolderTree();
                SelectFolderInTree(_selectedFolder);
                RefreshEntryList();
            };
            _nameDialog.PopupCentered(new Vector2I(360, 120));
            _nameDialogEdit.GrabFocus();
            _nameDialogEdit.SelectAll();
        }
    }

    private void OnDeletePressed()
    {
        if (_library == null) return;

        if (!string.IsNullOrEmpty(_selectedEntryRelative))
        {
            var entry = _visibleEntries.FirstOrDefault(e =>
                string.Equals(e.RelativePath, _selectedEntryRelative, StringComparison.OrdinalIgnoreCase));
            string label = entry?.DisplayName ?? _selectedEntryRelative;
            _confirmDialog.DialogText = $"Delete library cue '{label}'?\nThis cannot be undone.";
            _confirmDialog.OkButtonText = "Delete";
            string path = _selectedEntryRelative;
            _confirmCallback = () =>
            {
                var result = _library.DeleteEntry(path);
                if (!result.Success)
                {
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Library: {result.Message}", (int)LogType.Error);
                    return;
                }
                _selectedEntryRelative = string.Empty;
                RefreshEntryList();
                UpdateActionButtons();
            };
            _confirmDialog.PopupCentered();
            return;
        }

        if (!string.IsNullOrEmpty(_selectedFolder))
        {
            _confirmDialog.DialogText =
                $"Delete folder '/{_selectedFolder}' and all of its contents?\nThis cannot be undone.";
            _confirmDialog.OkButtonText = "Delete";
            string folder = _selectedFolder;
            _confirmCallback = () =>
            {
                var result = _library.DeleteFolder(folder);
                if (!result.Success)
                {
                    _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                        $"Library: {result.Message}", (int)LogType.Error);
                    return;
                }
                _selectedFolder = string.Empty;
                RebuildFolderTree();
                SelectFolderInTree(string.Empty);
                RefreshEntryList();
                UpdateActionButtons();
            };
            _confirmDialog.PopupCentered();
        }
    }

    private void OnOpenFolderPressed()
    {
        if (_library == null) return;
        try
        {
            string path = string.IsNullOrEmpty(_selectedFolder)
                ? _library.LibraryRootPath
                : LibraryPaths.ToAbsolute(_selectedFolder);
            OS.ShellShowInFileManager(path);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"LibraryInspector:OnOpenFolderPressed - {ex.Message}");
        }
    }

    private void OnNameDialogConfirmed()
    {
        string name = _nameDialogEdit?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Library: name cannot be empty.", (int)LogType.Warning);
            return;
        }
        _nameDialogCallback?.Invoke(name);
    }

    private void OnConfirmDialogConfirmed()
    {
        _confirmCallback?.Invoke();
        _confirmCallback = null;
        // Reset overwrite button text after use
        if (_confirmDialog != null)
            _confirmDialog.OkButtonText = "Delete";
    }
}
