using System;
using System.Collections.Generic;

namespace GitCredentialManager.Output;

public delegate FieldSet FieldSetTransformer<in T>(T result);

/// <summary>
/// A flat projection of data used by table and NUL output.
/// </summary>
public sealed class FieldSet
{
    private readonly List<IReadOnlyList<string>> _rows = new();

    /// <summary>
    /// Create a field set with one or more ordered columns.
    /// </summary>
    public FieldSet(params Field[] fields)
    {
        EnsureArgument.NotNull(fields, nameof(fields));

        if (fields.Length == 0)
        {
            throw new ArgumentException("At least one field is required.", nameof(fields));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Field field in fields)
        {
            if (field is null)
            {
                throw new ArgumentException("Fields cannot contain null entries.", nameof(fields));
            }

            if (!ids.Add(field.Id))
            {
                throw new ArgumentException($"Duplicate field ID '{field.Id}'.", nameof(fields));
            }
        }

        Fields = Array.AsReadOnly((Field[])fields.Clone());
        Rows = _rows.AsReadOnly();
    }

    /// <summary>
    /// The ordered fields in this field set.
    /// </summary>
    public IReadOnlyList<Field> Fields { get; }

    /// <summary>
    /// The rows in this field set.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>
    /// Add a row whose values correspond to <see cref="Fields"/>.
    /// </summary>
    public void AddRow(params string[] values)
    {
        EnsureArgument.NotNull(values, nameof(values));

        if (values.Length != Fields.Count)
        {
            throw new ArgumentException(
                $"Expected {Fields.Count} values but received {values.Length}.",
                nameof(values));
        }

        _rows.Add(Array.AsReadOnly((string[])values.Clone()));
    }
}

/// <summary>
/// Describes one field in a field set.
/// </summary>
public sealed class Field
{
    /// <summary>
    /// Create a field whose heading is the same as its ID.
    /// </summary>
    public Field(string id)
        : this(id, id)
    {
    }

    /// <summary>
    /// Create a field with an ID and display name.
    /// </summary>
    public Field(string id, string displayName)
    {
        EnsureArgument.NotNullOrWhiteSpace(id, nameof(id));
        EnsureArgument.NotNullOrWhiteSpace(displayName, nameof(displayName));

        if (id.IndexOf('\0') >= 0 || id.IndexOf('\r') >= 0 || id.IndexOf('\n') >= 0)
        {
            throw new ArgumentException("Field IDs cannot contain NUL, CR, or LF characters.", nameof(id));
        }

        Id = id;
        DisplayName = displayName;
    }

    /// <summary>
    /// The machine-readable name.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The human-readable heading.
    /// </summary>
    public string DisplayName { get; }
}
