using System.Collections;
using System.Collections.Generic;
using QRCoder;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GitCredentialManager;

public class QRCode : IRenderable
{
    private string _content;
    private Color _foregroundColor;
    private Color _backgroundColor;
    private Canvas _canvas;

    public QRCode(string content) : this(content, Color.White, Color.Black) { }

    public QRCode(string content, Color foregroundColor, Color backgroundColor)
    {
        _content = content;
        _foregroundColor = foregroundColor;
        _backgroundColor = backgroundColor;
        UpdateCanvas();
    }

    public string Content
    {
        get => _content;
        set { _content = value; UpdateCanvas(); }
    }

    public Color ForegroundColor
    {
        get => _foregroundColor;
        set { _foregroundColor = value; UpdateCanvas(); }
    }

    public Color BackgroundColor
    {
        get => _backgroundColor;
        set { _backgroundColor = value; UpdateCanvas(); }
    }

    private void UpdateCanvas()
    {
        using var generator = new QRCodeGenerator();
        QRCodeData qrCode = generator.CreateQrCode(Content, QRCodeGenerator.ECCLevel.L);
        List<BitArray> matrix = qrCode.ModuleMatrix;

        var canvas = new Canvas(matrix[0].Count, matrix.Count);
        for (int y = 0; y < matrix.Count; y++)
        {
            for (int x = 0; x < matrix[y].Count; x++)
            {
                canvas.SetPixel(x, y, matrix[y][x] ? ForegroundColor : BackgroundColor);
            }
        }

        _canvas = canvas;
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        ((IRenderable)_canvas).Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        ((IRenderable)_canvas).Render(options, maxWidth);
}

public static class AnsiConsoleExtensions
{
    extension(IAnsiConsole console)
    {
        public void WriteQrCode(string content)
        {
            console.Write(new QRCode(content));
        }
    }
}
