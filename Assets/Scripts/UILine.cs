using MinesServer.Data;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements; // The Namespace for UI Toolkit

public class UILine : VisualElement
{
    public LineDirection Direction { get; set; }
    public Color LineColor { get; set; } = Color.black;
    public float Thickness { get; set; } = 1.0f;

    public UILine()
    {
        // Subscribe to the "Generate Visual Content" event
        // This is like "OnPaint" in Windows Forms
        generateVisualContent += OnGenerateVisualContent;
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        // If we have 0 size, don't draw
        if (contentRect.width < 0.1f || contentRect.height < 0.1f) return;

        var painter = context.painter2D;
        painter.strokeColor = LineColor;
        painter.lineWidth = Thickness;

        painter.BeginPath();

        switch (Direction)
        {
            case LineDirection.Horizontal:
                // Draw line through center Y
                float midY = contentRect.height / 2f;
                painter.MoveTo(new Vector2(0, midY));
                painter.LineTo(new Vector2(contentRect.width, midY));
                break;

            case LineDirection.Vertical:
                // Draw line through center X
                float midX = contentRect.width / 2f;
                painter.MoveTo(new Vector2(midX, 0));
                painter.LineTo(new Vector2(midX, contentRect.height));
                break;

            case LineDirection.Diagonal:
                // Top-Left to Bottom-Right
                painter.MoveTo(new Vector2(0, 0));
                painter.LineTo(new Vector2(contentRect.width, contentRect.height));
                break;

            case LineDirection.ReverseDiagonal:
                // Top-Right to Bottom-Left
                painter.MoveTo(new Vector2(contentRect.width, 0));
                painter.LineTo(new Vector2(0, contentRect.height));
                break;
        }

        painter.Stroke();
    }
}
