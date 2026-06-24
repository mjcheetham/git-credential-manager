using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace GitCredentialManager.Tty;

/// <summary>
/// A panel that displays an OAuth device code and QR code for device authentication.
/// </summary>
public sealed class DeviceCodePanel : Renderable
{
    private const int MaxWidth = 80; // safe column width for terminals
    private const int PanelBorderWidth = 1;
    private const int PanelPadding = 1;
    private const int ColumnSpacing = 2;

    private static readonly Style CodeStyle = new(foreground: Color.Yellow, decoration: Decoration.Bold);
    private static readonly Style LinkStyle = new(foreground: Color.Blue);
    private static readonly Style NoteStyle = new(foreground: Color.Grey, decoration: Decoration.Italic);

    private readonly string _verificationUrl;
    private readonly string _userCode;
    private readonly QRCode _qrCode;

    public DeviceCodePanel(string verificationUrl, string userCode)
    {
        _verificationUrl = verificationUrl;
        _userCode = userCode;
        _qrCode = new QRCode(verificationUrl);
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        int width = Math.Min(MaxWidth, maxWidth);
        return new Measurement(width, width);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var instructions = new Rows(
            new Text("Scan the QR code or, using a browser on another device, please visit:").Centered(),
            Text.NewLine,
            new Text(_verificationUrl, GetStyle(LinkStyle)).Centered(),
            Text.NewLine,
            new Text("Sign-in and enter the following code:").Centered(),
            Text.NewLine,
            new Text(_userCode, GetStyle(CodeStyle)).Centered(),
            Text.NewLine,
            new Text("Press Ctrl+C to cancel.", GetStyle(NoteStyle)).Centered()
        );

        int width = Math.Min(MaxWidth, maxWidth);
        int contentWidth = width - 2 * (PanelPadding + PanelBorderWidth);
        int qrWidth = _qrCode.Measure(options, contentWidth - ColumnSpacing - 1).Max;
        int textWidth = contentWidth - ColumnSpacing - qrWidth;

        int qrHeight = Segment.SplitLines(
            _qrCode.Render(options, qrWidth)
        ).Count;

        int instructionsHeight = Segment.SplitLines(
            ((IRenderable)instructions).Render(options, textWidth)
        ).Count;

        // Grid cells are top-aligned, so give both alignments the measured row height.
        int height = Math.Max(qrHeight, instructionsHeight);
        var left = Align.Left(instructions, VerticalAlignment.Middle).Height(height);

        var grid = new Grid()
            .AddColumn(new GridColumn
            {
                Width = textWidth,
                Padding = new Padding(0, 0, ColumnSpacing, 0),
            })
            .AddColumn(new GridColumn
            {
                Width = qrWidth,
                NoWrap = true,
                Padding = new Padding(0),
            })
            .AddRow(left, Align.Right(_qrCode, VerticalAlignment.Middle).Height(height));

        IRenderable panel = new Panel(grid)
        {
            Width = width,
            Border = options.Ansi ? BoxBorder.Rounded : BoxBorder.Ascii,
            Padding = new Padding(PanelPadding, 0),
        };

        return panel.Render(options, maxWidth);

        Style? GetStyle(Style style) => options.ColorSystem != ColorSystem.NoColors ? style : (Style?)null;
    }
}
