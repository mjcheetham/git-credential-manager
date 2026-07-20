using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GitCredentialManager.Output;

/// <summary>
/// Writes each field as <c>name LF value NUL</c>, matching <c>git config -z</c>.
/// </summary>
/// <remarks>
/// Fields are emitted in their declared order for every result. Null values are empty.
/// </remarks>
internal sealed class NulOutputFormatter<T>(TextWriter output) : IOutputFormatter<T>
{
    public void Write(T data, IOutputDataTransformer<T> transformer)
    {
        FieldSet fieldSet = transformer.GetFieldSet(data);
        if (fieldSet is null)
        {
            throw new InvalidOperationException("The field set transformer returned null.");
        }

        var sb = new StringBuilder();

        foreach (IReadOnlyList<string> row in fieldSet.Rows)
        {
            for (int i = 0; i < row.Count; i++)
            {
                string value = row[i] ?? string.Empty;
                if (value.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException(
                        $"Value for field set '{fieldSet.Fields[i].Id}' cannot contain a NUL character.",
                        nameof(data));
                }

                sb.Append(fieldSet.Fields[i].Id);
                sb.Append('\n');
                sb.Append(value);
                sb.Append('\0');
            }
        }

        output.Write(sb);
    }
}
