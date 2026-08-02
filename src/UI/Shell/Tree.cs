using Godot;
using System;

public partial class Tree : Godot.Tree
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Tree");
		var tree = GetNode<Tree>("CueTree");
		TreeItem root = tree.CreateItem();
		tree.HideRoot = true;
		TreeItem child1 = tree.CreateItem(root);
		TreeItem child2 = tree.CreateItem(root);
		child2.SetText(0, "Subchild1");
		TreeItem subchild1 = tree.CreateItem(child1);
		subchild1.SetText(0, "Subchild1");
	}



	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
