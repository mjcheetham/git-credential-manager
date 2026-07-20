using System.Text.Json.Serialization.Metadata;

namespace GitCredentialManager.Output;

/// <summary>
/// Writes the specified data in a selected output format.
/// </summary>
/// <typeparam name="T">The type of the data to write.</typeparam>
public interface IOutputFormatter<in T>
{
    /// <summary>
    /// Write the specified data.
    /// </summary>
    void Write(T data);
}

public interface IOutputDataTransformer<T>
{
    FieldSet GetFieldSet(T data);
    JsonTypeInfo<T> GetJsonTypeInfo();
}
