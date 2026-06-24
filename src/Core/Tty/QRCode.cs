using System;
using System.Collections;
using System.Collections.Generic;
using QRCoder;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GitCredentialManager.Tty;

/// <summary>
/// Renders a QR code to a console.
/// </summary>
public class QRCode : IRenderable
{
    private const char FullBlock = '█';
    private const char TopBlock = '▀';
    private const char BottomBlock = '▄';
    private const char EmptyBlock = ' ';

    private string _content;
    private IRenderable _renderable;

    public QRCode(string content)
    {
        _content = content;
        UpdateRenderables();
    }

    public string Content
    {
        get => _content;
        set { _content = value; UpdateRenderables(); }
    }

    private void UpdateRenderables()
    {
        using var generator = new QRCodeGenerator();
        QRCodeData qrCode = generator.CreateQrCode(Content, QRCodeGenerator.ECCLevel.L);
        List<BitArray> matrix = qrCode.ModuleMatrix;

        var width = matrix[0].Length;
        var height = matrix.Count;

        // To save on space (since terminal characters are taller than they are wide),
        // we will use the top and bottom half-block characters to render two rows of the
        // QR code in a single row of text.
        var lines = new string[(int)Math.Ceiling(height / 2f)];
        for (int y = 0; y < height; y += 2)
        {
            var line = new char[width];
            for (int x = 0; x < width; x++)
            {
                bool top = matrix[y][x];
                bool bottom = (y + 1 < height) && matrix[y + 1][x];

                if (top && bottom)
                {
                    line[x] = FullBlock;
                }
                else if (top)
                {
                    line[x] = TopBlock;
                }
                else if (bottom)
                {
                    line[x] = BottomBlock;
                }
                else
                {
                    line[x] = EmptyBlock;
                }
            }

            lines[y / 2] = new string(line);
        }

        _renderable = new Text(string.Join(Environment.NewLine, lines)) { Overflow = Overflow.Crop };
    }

    public Measurement Measure(RenderOptions options, int maxWidth) =>
        _renderable.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        _renderable.Render(options, maxWidth);
}
