using System;
using System.Collections.Generic;
using Spectre.Console;

namespace GitCredentialManager.Output;

internal sealed class TableOutputFormatter<T>(IAnsiConsole console) : IOutputFormatter<T>
{
    public void Write(T data, IOutputDataTransformer<T> transformer)
    {
        FieldSet fieldSet = transformer.GetFieldSet(data);
        if (fieldSet is null)
        {
            throw new InvalidOperationException("The field set transformer returned null.");
        }

        var table = new Table
        {
            Border = TableBorder.Rounded,
        };

        foreach (Field field in fieldSet.Fields)
        {
            table.AddColumn(new TableColumn(Markup.Escape(field.DisplayName)));
        }

        foreach (IReadOnlyList<string> row in fieldSet.Rows)
        {
            var values = new string[row.Count];
            for (int i = 0; i < row.Count; i++)
            {
                values[i] = Markup.Escape(row[i] ?? string.Empty);
            }

            table.AddRow(values);
        }

        console.Write(table);
    }
}
