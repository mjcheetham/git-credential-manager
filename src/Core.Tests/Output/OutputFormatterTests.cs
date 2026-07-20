using System;
using System.IO;
using System.Text.Json.Serialization;
using GitCredentialManager.Output;
using GitCredentialManager.Tty;
using Xunit;

namespace GitCredentialManager.Tests.Output;

public class OutputFormatterTests
{
    [Fact]
    public void Table()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        var console = AnsiConsoleFactory.CreateForWriter(writer, isRedirected: true);
        var formatter = new TableOutputFormatter<TestRecord[]>(console);

        formatter.Write([
                new TestRecord("alice", "global", "first"),
                new TestRecord("bob", "local", "second")
            ],
            TestRecord);

        writer.Flush();
        byte[] outputBytes = ms.ToArray();
        var output = EncodingEx.UTF8NoBom.GetString(outputBytes);

        Assert.Contains("NAME", output);
        Assert.Contains("SCOPE", output);
        Assert.Contains("DETAIL", output);
        Assert.Contains("alice", output);
        Assert.Contains("global", output);
        Assert.Contains("first", output);
        Assert.Contains("bob", output);
        Assert.Contains("local", output);
        Assert.Contains("second", output);
    }

    [Fact]
    public void Nul()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        var formatter = new NulOutputFormatter<TestRecord[]>(writer);

        formatter.Write([
            new TestRecord("alice", "global", "first"),
            new TestRecord("bob", "local", "second")
        ]);

        writer.Flush();
        byte[] outputBytes = ms.ToArray();
        var output = EncodingEx.UTF8NoBom.GetString(outputBytes);
    }

    [Fact]
    public void Table_NoFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FieldSet());
    }

    [Fact]
    public void Table_DuplicateFieldIds_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FieldSet(
            new Field("id"), new Field("id")
        ));
    }

    [Fact]
    public void Table_RowWithWrongValueCount_Throws()
    {
        var table = new FieldSet(
            new Field("name"),
            new Field("scope")
        );

        Assert.Throws<ArgumentException>(() => table.AddRow("alice"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\rname")]
    [InlineData("bad\nname")]
    public void Field_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new Field(name));
    }

    [Fact]
    public void Field_NameContainingNul_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Field("bad\0id"));
    }
}

internal sealed record TestRecord(string Name, string Scope, string Detail)
{
    public static FieldSet CreateFieldSet(TestRecord[] records)
    {
        var fields = new[]
        {
            new Field("name", "NAME"),
            new Field("scope", "SCOPE"),
            new Field("detail", "DETAIL"),
        };

        var fieldSet = new FieldSet(fields);

        foreach (var record in records)
        {
            fieldSet.AddRow(record.Name, record.Scope, record.Detail);
        }

        return fieldSet;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TestRecord))]
internal partial class OutputFormatterTestJsonContext : JsonSerializerContext;
