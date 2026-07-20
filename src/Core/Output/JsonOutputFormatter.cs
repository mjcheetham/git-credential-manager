using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GitCredentialManager.Output;

internal sealed class JsonOutputFormatter<T>(TextWriter output) : IOutputFormatter<T>
{
    public void Write(T data, IOutputDataTransformer<T> transformer)
    {
        JsonTypeInfo<T> typeInfo = transformer.GetJsonTypeInfo();
        var opts = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
        };

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, opts))
        {
            JsonSerializer.Serialize(writer, data, typeInfo);
            writer.Flush();
        }

        output.Write(EncodingEx.UTF8NoBom.GetString(buffer.WrittenSpan));
        output.Write('\n');
    }
}
