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
        Position = PatternPosition;

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

        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawLine(new Vector2(0, 0), new Vector2(size.X, 0), Colors.Red, 4);
        DrawLine(new Vector2(size.X, 0), new Vector2(size.X, size.Y), Colors.Red, 4);
        DrawLine(new Vector2(size.X, size.Y), new Vector2(0, size.Y), Colors.Red, 4);
        DrawLine(new Vector2(0, size.Y), new Vector2(0, 0), Colors.Red, 4);

        float minDim = Mathf.Min(size.X, size.Y);
        float radius = 0.2f * minDim;
        Vector2 center = size / 2;

        // Draw grey 10px grid lines
        Color dimGray = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        for (int i = 1; ; i++)
        {
            float x = center.X + i * 10;
            if (x >= size.X) break;
            DrawVerticalLine(x, dimGray, 1, center, radius, size);
            x = center.X - i * 10;
            if (x < 0) continue;
            DrawVerticalLine(x, dimGray, 1, center, radius, size);
        }
        for (int i = 1; ; i++)
        {
            float y = center.Y + i * 10;
            if (y >= size.Y) break;
            DrawHorizontalLine(y, dimGray, 1, center, radius, size);
            y = center.Y - i * 10;
            if (y < 0) continue;
            DrawHorizontalLine(y, dimGray, 1, center, radius, size);
        }

        // Draw white 100px grid lines
        for (int i = 1; ; i++)
        {
            float x = center.X + i * 100;
            if (x >= size.X) break;
            DrawVerticalLine(x, Colors.White, 1, center, radius, size);
            x = center.X - i * 100;
            if (x < 0) continue;
            DrawVerticalLine(x, Colors.White, 1, center, radius, size);
        }
        for (int i = 1; ; i++)
        {
            float y = center.Y + i * 100;
            if (y >= size.Y) break;
            DrawHorizontalLine(y, Colors.White, 1, center, radius, size);
            y = center.Y - i * 100;
            if (y < 0) continue;
            DrawHorizontalLine(y, Colors.White, 1, center, radius, size);
        }

        // Draw blue center lines
        DrawVerticalLine(center.X, Colors.Blue, 4, center, radius, size);
        DrawHorizontalLine(center.Y, Colors.Blue, 4, center, radius, size);

        // Draw diagonal lines from corners to opposite
        DrawClippedLine(new Vector2(0, 0), new Vector2(size.X, size.Y), Colors.Blue, 4, center, radius);
        DrawClippedLine(new Vector2(size.X, 0), new Vector2(0, size.Y), Colors.Blue, 4, center, radius);

        DrawArc(center, radius, 0, Mathf.Tau, 64, Colors.Blue, 2, true);

        float largeRadius = minDim / 2;
        DrawArc(center, largeRadius, 0, Mathf.Tau, 64, Colors.Red, 1, true);
    }

    private void DrawVerticalLine(float x, Color color, float width, Vector2 center, float radius, Vector2 size)
    {
        float dx = x - center.X;
        if (Mathf.Abs(dx) > radius)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), color, width);
        }
        else
        {
            float dy = Mathf.Sqrt(radius * radius - dx * dx);
            float y1 = center.Y - dy;
            float y2 = center.Y + dy;
            if (y1 > 0) DrawLine(new Vector2(x, 0), new Vector2(x, y1), color, width);
            if (y2 < size.Y) DrawLine(new Vector2(x, y2), new Vector2(x, size.Y), color, width);
        }
    }

    private void DrawHorizontalLine(float y, Color color, float width, Vector2 center, float radius, Vector2 size)
    {
        float dy = y - center.Y;
        if (Mathf.Abs(dy) > radius)
        {
            DrawLine(new Vector2(0, y), new Vector2(size.X, y), color, width);
        }
        else
        {
            float dx = Mathf.Sqrt(radius * radius - dy * dy);
            float x1 = center.X - dx;
            float x2 = center.X + dx;
            if (x1 > 0) DrawLine(new Vector2(0, y), new Vector2(x1, y), color, width);
            if (x2 < size.X) DrawLine(new Vector2(x2, y), new Vector2(size.X, y), color, width);
        }
    }

    private void DrawClippedLine(Vector2 A, Vector2 B, Color color, float width, Vector2 center, float radius)
    {
        Vector2 D = B - A;
        Vector2 C = A - center;
        float a = D.Dot(D);
        float b = 2 * C.Dot(D);
        float cc = C.Dot(C) - radius * radius;
        float discriminant = b * b - 4 * a * cc;
        if (discriminant < 0)
        {
            // no intersection, draw full
            DrawLine(A, B, color, width, true);
        }
        else
        {
            float sqrtD = Mathf.Sqrt(discriminant);
            float s1 = (-b - sqrtD) / (2 * a);
            float s2 = (-b + sqrtD) / (2 * a);
            // sort s1 < s2
            if (s1 > s2) (s1, s2) = (s2, s1);
            // draw 0 to s1, s2 to 1
            if (s1 > 0) DrawLine(A, A + s1 * D, color, width);
            if (s2 < 1) DrawLine(A + s2 * D, B, color, width);
        }
    }
}