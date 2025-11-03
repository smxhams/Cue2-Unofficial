using Godot;

namespace Cue2.UI.Utilities;

public partial class TestPattern : Control
{
    public Vector2I PatternPosition = new Vector2I(0, 0);
    public Vector2I PatternSize = new Vector2I(0, 0);
    public string PatternName = "";

    public TestPattern()
    {
        // Empty constructor for Godot
    }

    public TestPattern(Vector2I patternSize, Vector2I patternPosition , string name = "")
    {
        PatternSize = patternSize;
        PatternPosition = patternPosition;
        PatternName = name;
    }
        
    public override void _Ready()
    {
        Size = PatternSize;

        Label nameLabel = new Label();
        nameLabel.Text = PatternName;
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.Position = new Vector2(0, PatternSize.Y / 2 - 30);
        nameLabel.Size = new Vector2(PatternSize.X, 20);
        AddChild(nameLabel);

        Label resLabel = new Label();
        resLabel.Text = $"{PatternSize.X}x{PatternSize.Y}";
        resLabel.HorizontalAlignment = HorizontalAlignment.Center;
        resLabel.VerticalAlignment = VerticalAlignment.Center;
        resLabel.Position = new Vector2(0, PatternSize.Y / 2 + 10);
        resLabel.Size = new Vector2(PatternSize.X, 20);
        AddChild(resLabel);
    }
}