using System.Collections.Generic;
using System.Linq;
using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using Godot.Collections;

// This script is attached to the cuelist in main UI
// Originator
namespace Cue2.Base.Classes;


public partial class CueList : Control
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	
	public static System.Collections.Generic.Dictionary<int, Cue> CueIndex; // <CueId, Cue>
	
	// Cuie list reordering properties
	private bool _isReordering;
	private ShellBar _mouseOverShellBar;
	private bool _insertAbove;
	private bool _insertBelow;
	private bool _insertMakeChild;
	
	public int ShellBeingDragged = -1;
	public static int ShellDraggedOver = -1;

	
	private PackedScene _shellBarPackedScene = SceneLoader.LoadPackedScene("uid://d207a67e3ebww", out _);

	// Ui
	private VBoxContainer _cueContainer;
	private Button _addCueButton;
	private Button _expandAllButton;

	private Control _reorderCueControl;
	private Label _reorderLocationLabel;
	private VBoxContainer _reorderListContainer;
	private Panel _reorderIndicatorPanel;
	
	public CueList()
	{
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
	}

	private int _childTally = -1;

	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalData.Cuelist = this;
		_globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

		// Ui
		_cueContainer = GetNode<VBoxContainer>("%CueContainer");
		_addCueButton = GetNode<Button>("%AddCueButton");
		_expandAllButton = GetNode<Button>("%ExpandAllButton");
		
		_reorderCueControl = GetNode<Control>("%ReorderCueControl");
		_reorderLocationLabel = GetNode<Label>("%ReorderLocationLabel");
		_reorderListContainer = GetNode<VBoxContainer>("%ReorderListContainer");
		_reorderIndicatorPanel = GetNode<Panel>("%ReorderIndicatorPanel");

		_addCueButton.Icon = GetThemeIcon("PlusCircled", "AtlasIcons");
		_expandAllButton.Icon = GetThemeIcon("Right", "AtlasIcons");

		_globalSignals.CreateCue += CreateCue;
		_addCueButton.Pressed += CreateCue;

	}

	public Cue CreateCue(Dictionary data) // Create a cue from data
	{
		var newCue = new Cue(data);
		AddCue(newCue);
		return newCue;
	}

	public void CreateCue()
	{
		var newCue = new Cue(); // Create a cue with default values
		AddCue(newCue);

	}
	
	private void AddCue(Cue cue)
	{
		CreateNewShell(cue);
		CueIndex.Add(cue.Id, cue);
		// Will make new cues focused
		//FocusCue(cue); //Read select shell when finished

	}
	// This instantiates the shell scene which creates the UI elements to represent the cue in the scene
	private void CreateNewShell(Cue newCue)
	{
		var shellBar = _shellBarPackedScene.Instantiate<ShellBar>();
		if (ShellSelection.SelectedCues.Count == 0) // No selection, add cue at end of cuelist
		{
			_cueContainer.AddChild(shellBar);
		}
		else
		{
			var selectedCue = ShellSelection.SelectedCues.Last();
			if (selectedCue.ParentId == -1) // Cue selected in main cuelist, add after
			{
				var newIndex = selectedCue.ShellBar.GetIndex() + 1;
				_cueContainer.AddChild(shellBar);
				_cueContainer.MoveChild(shellBar, newIndex);
			}
			else // Selected cue has parent, add as child of that parent
			{
				var parent = FetchCueFromId(selectedCue.ParentId);
				var newIndex = selectedCue.ShellBar.GetIndex() + 1;
				parent.ShellBar.ShellChildContainer.AddChild(shellBar);
				parent.ShellBar.ShellChildContainer.MoveChild(shellBar, newIndex);
				newCue.ParentId = selectedCue.ParentId;
				parent.ChildCues.Add(newCue.Id);
			}
		}

		shellBar.MouseEntered += () => OnMouseEntered(shellBar);
		shellBar.SetCue(newCue);
		newCue.ShellBar = shellBar; // Adds shellbar scene to the cue object.
		shellBar.Set("CueId", newCue.Id); // Sets shell_bar property CueId
	}
	
	public void RemoveCue(Cue cue)
	{
		cue.ShellBar.QueueFree();
		CueIndex.Remove(cue.Id);
	}

	public static Cue FetchCueFromId(int id)
	{
		CueIndex.TryGetValue(id, out Cue cue);
		return cue;
	}
	

	private void OnMouseEntered(ShellBar shellbar)
	{
		_mouseOverShellBar = shellbar;
	}
	
	//==========================//
	//--- Cuelist reordering ---//
	//==========================//
	public void StartReorder(ShellBar shellbar)
	{
		if (_isReordering) return;
		if (!shellbar.Selected)
		{
			_globalData.ShellSelection.SelectIndividualShell(FetchCueFromId(shellbar.CueId));
		}

		if (_reorderListContainer.GetChildCount() > 0)
		{
			foreach (var child in _reorderListContainer.GetChildren())
			{
				child.QueueFree();
			}
		}
		
		foreach (var selectedCue in ShellSelection.SelectedCues)
		{
			var label = new Label();
			label.Text = selectedCue.Name;
			label.AddThemeFontSizeOverride("font_size", 9);
			label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 0.5f));
			_reorderListContainer.AddChild(label);
		}

		_reorderCueControl.Visible = true;
		_isReordering = true;
		ShellBeingDragged = shellbar.CueId;
		var cue = FetchCueFromId(shellbar.CueId);
		var shell = cue.ShellBar;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (!_isReordering) return;

		if (@event is InputEventMouseMotion eventMouseMotion)
		{
			_reorderCueControl.GlobalPosition = new Vector2(eventMouseMotion.Position.X, eventMouseMotion.Position.Y);
			var check = MouseOverCheck();
			if (check)
			{
				var shellPosY = _mouseOverShellBar.GetGlobalPosition().Y;
				var shellSizeY = 24; // This is shell size, used to get size.Y, however it got wrecked with shell children.
				var mouseY = eventMouseMotion.GlobalPosition.Y;
				var margin = shellSizeY / 4;
				_insertAbove = mouseY < shellPosY + margin;
				_insertBelow = mouseY > shellPosY + margin * 3;
				_insertMakeChild = FetchCueFromId(_mouseOverShellBar.CueId).ParentId != -1;
				if (_insertBelow == true)
				{
					if (_insertMakeChild) _reorderLocationLabel.Text = $"Reorder below: {FetchCueFromId(_mouseOverShellBar.CueId).Name} and child of: {FetchCueFromId(FetchCueFromId(_mouseOverShellBar.CueId).ParentId).Name}";
					else _reorderLocationLabel.Text = $"Reorder below: {FetchCueFromId(_mouseOverShellBar.CueId).Name}";
					_reorderIndicatorPanel.GlobalPosition = new Vector2(_mouseOverShellBar.GetGlobalPosition().X, _mouseOverShellBar.GetGlobalPosition().Y + _mouseOverShellBar.Size.Y);
					_reorderIndicatorPanel.Size = new Vector2(_mouseOverShellBar.Size.X, 1);
					_reorderIndicatorPanel.Visible = true;
				}
				else if (_insertAbove == true)
				{
					if (_insertMakeChild) _reorderLocationLabel.Text = $"Reorder above: {FetchCueFromId(_mouseOverShellBar.CueId).Name} and child of: {FetchCueFromId(FetchCueFromId(_mouseOverShellBar.CueId).ParentId).Name}";
					else _reorderLocationLabel.Text = $"Reorder above: {FetchCueFromId(_mouseOverShellBar.CueId).Name}";
					_reorderIndicatorPanel.GlobalPosition = _mouseOverShellBar.GetGlobalPosition();
					_reorderIndicatorPanel.Size = new Vector2(_mouseOverShellBar.Size.X, 1);
					_reorderIndicatorPanel.Visible = true;
				}
				else
				{
					_reorderLocationLabel.Text = $"Make child of: {FetchCueFromId(_mouseOverShellBar.CueId).Name}";
					_reorderIndicatorPanel.GlobalPosition = _mouseOverShellBar.GetGlobalPosition();
					_reorderIndicatorPanel.Size = _mouseOverShellBar.Size;
					_reorderIndicatorPanel.Visible = true;
				}
			}
			else
			{
				_reorderLocationLabel.Text = "Cannot reorder here";
				_reorderIndicatorPanel.Visible = false;
			}
		}
		
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
		{
			EndReorder();
		}
	}

	private void EndReorder()
	{
		// Validate location
		if (MouseOverCheck() == false)
		{
			_isReordering = false;
			_reorderCueControl.Visible = false;
			return;
		}

		// There are 3 reorder conditions:
		// 1 = Inserting above/below cue with no parent = CueList CueContainer holds shell
		// 2 = Inserting above/below a cue with a parent = Setting parent to same as mouseovershells parent
		// 3 = Inserting as child of cue = added to end of parents shell children container
		var parent = _cueContainer; // 1
		if (_insertAbove == false && _insertBelow == false)
		{
			parent = _mouseOverShellBar.ShellChildContainer; // 3
		}
		else if (FetchCueFromId(_mouseOverShellBar.CueId).ParentId != -1)
		{
			parent = FetchCueFromId(FetchCueFromId(_mouseOverShellBar.CueId).ParentId).ShellBar.ShellChildContainer; // 2
		}
		var insertIndex = _mouseOverShellBar.GetIndex();
		
		foreach (var selectedCue in ShellSelection.SelectedCues)
		{
			// Set relevant parents / children from old location then new
			selectedCue.ShellBar.Reparent(parent); // 1,2,3
			
			if (_insertAbove || _insertBelow)
			{
				parent.MoveChild(selectedCue.ShellBar, insertIndex); // 1,2
			}
			
			// Setting properties
			if (selectedCue.ParentId != -1)
			{
				var oldParent = FetchCueFromId(selectedCue.ParentId);
				oldParent.ChildCues.Remove(selectedCue.Id);
				if (parent == _cueContainer) selectedCue.ParentId = -1; // 1
				else if (_insertAbove || _insertBelow)
				{
					var newParent = FetchCueFromId(FetchCueFromId(_mouseOverShellBar.CueId).ParentId);
					selectedCue.ParentId = newParent.Id; // 3
					newParent.ChildCues.Add(selectedCue.Id);
				}
				else
				{
					var newParent = FetchCueFromId(_mouseOverShellBar.CueId);
					selectedCue.ParentId = _mouseOverShellBar.CueId; // 3
					newParent.ChildCues.Add(selectedCue.Id);
				}
			}

			selectedCue.ShellBar.RelationshipChanged();
		}
		
		
		// clean
		foreach (var child in _reorderListContainer.GetChildren())
		{
			child.QueueFree();
		}
		_isReordering = false;
		_reorderCueControl.Visible = false;
		_mouseOverShellBar = null;
		_insertAbove = false;
		_insertBelow = false;
		_insertMakeChild = false;
		

	}

	private bool MouseOverCheck()
	{
		if (_mouseOverShellBar == null) return false;
		var isSelected = ShellSelection.SelectedCues.Contains(FetchCueFromId(_mouseOverShellBar.CueId));
		if (isSelected == false) return true;
		return false;
	}
	

	//--- Save and load ---//
	
	public void ResetCuelist()
	{
		// Removes shellbars from ui
		foreach (var cue in CueIndex)
		{
			cue.Value.ShellBar?.QueueFree();
		}
		// Resets 
		CueIndex = new System.Collections.Generic.Dictionary<int, Cue>();
		ShellSelection.SelectedCues = new List<Cue>();
	}
	
	public Dictionary GetData()
	{
		var saveTable = new Dictionary();
		var cues = new Dictionary();
		var cueOrder = GetCueOrder();
		saveTable.Add("CueOrder", cueOrder);
		foreach (var cue in CueIndex.Values)
		{
			var cueData = cue.GetData();
			
			cues.Add(cue.Id, cueData);
		}
		saveTable.Add("Cues", cues);
		return saveTable;
	}
	
	public Godot.Collections.Dictionary<int, int> GetCueOrder()
	{
		var cueOrder = new Godot.Collections.Dictionary<int, int>();
		for (int i = 0; i < _cueContainer.GetChildren().Count; i++)
		{
			var cueId = _cueContainer.GetChild(i).Get("CueId");
			cueOrder.Add(i, (int)cueId);
		}

		return cueOrder;
	}

	public void LoadData(Dictionary cueData)
	{
		GD.Print($"CueList:LoadData - Loading Cues");

		if (cueData.TryGetValue("Cues", out var cues))
		{
			foreach (var cue in (Dictionary)cues)
			{
				var asDict = cue.Value.AsGodotDictionary();
				var cueDict = new Dictionary();
				foreach (var key in asDict.Keys)
				{
					var value = asDict[key];
					string keyStr = key.ToString();
						
					cueDict[keyStr] = value;
				} 
				Cue newCue = CreateCue(cueDict);
				
				// Patches are instantiated in load sequence seperate form cues. Once patchs and cues are created they
				// need to be linked.
				var newCueAudioComponent = newCue.GetAudioComponent();
				if (newCueAudioComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueAudioComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueAudioComponent.Patch = patch;
					}
				}
				
				var newCueVideoComponent = newCue.GetVideoComponent();
				if (newCueVideoComponent != null)
				{
					var patches = _globalData.Settings.GetAudioOutputPatches();
					patches.TryGetValue(newCueVideoComponent.PatchId, out var patch);
					if (patch != null)
					{
						newCueVideoComponent.Patch = patch;
					}
				}

				var cueLightComps = newCue.GetCueLightComponents();
				if (cueLightComps != null)
				{
					foreach (var cueLightComp in cueLightComps)
					{
						var cuelight = _globalData.CueLightManager.GetCueLight(cueLightComp.CueLightId);
						cueLightComp.CueLight = cuelight;
					}
				}
				
				var oscComponents = newCue.GetOscComponents();
				if (oscComponents != null)
				{
					foreach (var oscComp in oscComponents)
					{
						var oscConnection = OscConnections.GetCueOscConnection(oscComp.OscConnectionId);
						oscComp.OscConnection = oscConnection;
					}
				}


			}
		}

		if (cueData.TryGetValue("CueOrder", out var order))
		{
			var cueOrder = new Godot.Collections.Dictionary<int, int>();
			foreach (var cue in (Godot.Collections.Dictionary)order)
			{
				cueOrder.Add((int)cue.Key, (int)cue.Value);
				//GD.Print(cue.Key + " <-order cue -> " + (int)cue.Value);
			}
			StructureCuelist(cueOrder);	
		}
	}
	
	private void StructureCuelist(Godot.Collections.Dictionary<int, int> cueOrder)
	{
		// Key is child order, value is cueId
		foreach (Cue cue in CueIndex.Values)
		{
			// Assign child shellbars to parents
			if (cue.ParentId != -1)
			{
				GD.Print($"CueList:StructureCuelist - REPARENTING {cue.Name}");
				var parentShell = FetchCueFromId(cue.ParentId).ShellBar;
				cue.ShellBar.Reparent(parentShell.GetNode<VBoxContainer>("%ShellChildContainer"));
				//_cueContainer.RemoveChild(cue.ShellBar);
				//parentShell.GetNode<VBoxContainer>("%ShellChildContainer").AddChild(cue.ShellBar);
				//parentShell.AssignChild(cue.ShellBar);
			}
		}
		
		for (int i = 0; i < cueOrder.Count; i++)
		{
			var cue = CueIndex[cueOrder[i]];
			var shell = (ShellBar)cue.ShellBar;
			if (cue.ParentId != -1) continue;
			_cueContainer.CallDeferred("move_child", shell, i);
		}
	}

}

