using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager
{
    public static class StreamExtensions
    {
        extension (TextReader reader)
        {
            /// <summary>
            /// Read a dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>.
            /// </summary>
            /// <remarks>
            /// Uses the <see cref="StringComparer.Ordinal"/> comparer for dictionary keys.
            /// <para/>
            /// Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).
            /// </remarks>
            /// <returns>Dictionary read from the text reader.</returns>
            public IDictionary<string, string> ReadDictionary() =>
                ReadDictionary(reader, StringComparer.Ordinal);

            /// <summary>
            /// Read a dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>,
            /// with the specified <see cref="StringComparer"/> used to compare dictionary keys.
            /// </summary>
            /// <remarks>Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).</remarks>
            /// <param name="comparer">Comparer to use when comparing dictionary keys.</param>
            /// <returns>Dictionary read from the text reader.</returns>
            public IDictionary<string, string> ReadDictionary(StringComparer comparer)
            {
                var dict = new Dictionary<string, string>(comparer);

                string line;
                while ((line = reader.ReadLine()) != null && !string.IsNullOrWhiteSpace(line))
                {
                    ParseLine(dict, line);
                }

                return dict;
            }

            /// <summary>
            /// Read a multi-dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>.
            /// </summary>
            /// <remarks>
            /// Uses the <see cref="StringComparer.Ordinal"/> comparer for dictionary keys.
            /// <para/>
            /// Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).
            /// </remarks>
            /// <returns>Dictionary read from the text reader.</returns>
            public IDictionary<string, IList<string>> ReadMultiDictionary() =>
                ReadMultiDictionary(reader, StringComparer.Ordinal);

            /// <summary>
            /// Read a multi-dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>,
            /// with the specified <see cref="StringComparer"/> used to compare dictionary keys.
            /// </summary>
            /// <remarks>Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).</remarks>
            /// <param name="comparer">Comparer to use when comparing dictionary keys.</param>
            /// <returns>Dictionary read from the text reader.</returns>
            public IDictionary<string, IList<string>> ReadMultiDictionary(StringComparer comparer)
            {
                var dict = new Dictionary<string, IList<string>>(comparer);

                string line;
                while ((line = reader.ReadLine()) != null && !string.IsNullOrWhiteSpace(line))
                {
                    ParseMultiLine(dict, line);
                }

                return dict;
            }

            /// <summary>
            /// Asynchronously read a dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>.
            /// </summary>
            /// <remarks>
            /// Uses the <see cref="StringComparer.Ordinal"/> comparer for dictionary keys.
            /// <para/>
            /// Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).
            /// </remarks>
            /// <returns>Dictionary read from the text reader.</returns>
            public Task<IDictionary<string, string>> ReadDictionaryAsync(CancellationToken ct = default) =>
                ReadDictionaryAsync(reader, StringComparer.Ordinal, ct);

            /// <summary>
            /// Asynchronously read a multi-dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>.
            /// </summary>
            /// <remarks>
            /// Uses the <see cref="StringComparer.Ordinal"/> comparer for dictionary keys.
            /// <para/>
            /// Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).
            /// </remarks>
            /// <returns>Dictionary read from the text reader.</returns>
            public Task<IDictionary<string, IList<string>>> ReadMultiDictionaryAsync(CancellationToken ct = default) =>
                ReadMultiDictionaryAsync(reader, StringComparer.Ordinal, ct);

            /// <summary>
            /// Asynchronously read a dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>,
            /// with the specified <see cref="StringComparer"/> used to compare dictionary keys.
            /// </summary>
            /// <remarks>Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).</remarks>
            /// <param name="comparer">Comparer to use when comparing dictionary keys.</param>
            /// <param name="ct">Cancellation token to cancel the in-progress read operation.</param>
            /// <returns>Dictionary read from the text reader.</returns>
            public async Task<IDictionary<string, string>> ReadDictionaryAsync(StringComparer comparer, CancellationToken ct = default)
            {
                var dict = new Dictionary<string, string>(comparer);

                string line;
                while ((line = await reader.ReadLineAsync(ct)) != null && !string.IsNullOrWhiteSpace(line))
                {
                    ParseLine(dict, line);
                }

                return dict;
            }

            /// <summary>
            /// Asynchronously read a multi-dictionary in the form `key1=value\nkey2=value\n\n` from the specified <see cref="TextReader"/>,
            /// with the specified <see cref="StringComparer"/> used to compare dictionary keys.
            /// </summary>
            /// <remarks>Also accepts dictionary lines terminated using \r\n (CR LF) as well as \n (LF).</remarks>
            /// <param name="comparer">Comparer to use when comparing dictionary keys.</param>
            /// <param name="ct">Cancellation token to cancel the in-progress read operation.</param>
            /// <returns>Dictionary read from the text reader.</returns>
            public async Task<IDictionary<string, IList<string>>> ReadMultiDictionaryAsync(StringComparer comparer,
                CancellationToken ct = default)
            {
                var dict = new Dictionary<string, IList<string>>(comparer);

                string line;
                while ((line = await reader.ReadLineAsync(ct)) != null && !string.IsNullOrWhiteSpace(line))
                {
                    ParseMultiLine(dict, line);
                }

                return dict;
            }
        }

        extension (TextWriter writer)
        {
            /// <summary>
            /// Write a dictionary in the form `key1=value\nkey2=value\n\n` to the specified <see cref="TextWriter"/>,
            /// where \n is the configured new-line (see <see cref="TextWriter.NewLine"/>).
            /// </summary>
            /// <remarks>The output dictionary new-lines are determined by the <see cref="TextWriter.NewLine"/> property.</remarks>
            /// <param name="dict">Dictionary to write to the text writer.</param>
            public void WriteDictionary(IDictionary<string, string> dict)
            {
                foreach (var kvp in dict)
                {
                    writer.WriteLine($"{kvp.Key}={kvp.Value}");
                }

                // Write terminating line
                writer.WriteLine();
            }

            /// <summary>
            /// Write a dictionary in the form `key1=value\nkey2=value\n\n` to the specified <see cref="TextWriter"/>,
            /// where \n is the configured new-line (see <see cref="TextWriter.NewLine"/>).
            /// </summary>
            /// <remarks>The output dictionary new-lines are determined by the <see cref="TextWriter.NewLine"/> property.</remarks>
            /// <param name="dict">Dictionary to write to the text writer.</param>
            public void WriteDictionary(IDictionary<string, IList<string>> dict)
            {
                foreach (var kvp in dict)
                {
                    IList<string> values = GetNormalizedValueList(kvp.Value);
                    switch (values.Count)
                    {
                        case 0:
                            break;

                        case 1:
                            writer.WriteLine($"{kvp.Key}={values[0]}");
                            break;

                        default:
                            foreach (string value in values)
                            {
                                writer.WriteLine($"{kvp.Key}[]={value}");
                            }

                            break;
                    }
                }

                // Write terminating line
                writer.WriteLine();
            }

            /// <summary>
            /// Asynchronously write a dictionary in the form `key1=value\nkey2=value\n\n` to the specified <see cref="TextWriter"/>,
            /// where \n is the configured new-line (see <see cref="TextWriter.NewLine"/>).
            /// </summary>
            /// <remarks>The output dictionary new-lines are determined by the <see cref="TextWriter.NewLine"/> property.</remarks>
            /// <param name="dict">Dictionary to write to the text writer.</param>
            public async Task WriteDictionaryAsync(IDictionary<string, string> dict)
            {
                foreach (var kvp in dict)
                {
                    await writer.WriteLineAsync($"{kvp.Key}={kvp.Value}");
                }

                // Write terminating line
                await writer.WriteLineAsync();
            }
        }

        private static void ParseLine(IDictionary<string, string> dict, string line)
        {
            int splitIndex = line.IndexOf('=');
            if (splitIndex > 0)
            {
                string key = line.Substring(0, splitIndex);
                string value = line.Substring(splitIndex + 1);

                dict[key] = value;
            }
        }

        private static void ParseMultiLine(IDictionary<string, IList<string>> dict, string line)
        {
            int splitIndex = line.IndexOf('=');
            if (splitIndex > 0)
            {
                string key = line.Substring(0, splitIndex);
                string value = line.Substring(splitIndex + 1);

                bool multi = key.EndsWith("[]");
                if (multi)
                {
                    key = key.Substring(0, key.Length - 2);
                }

                if (!dict.TryGetValue(key, out IList<string> list))
                {
                    list = new List<string>();
                    dict[key] = list;
                }

                // Only allow one value for non-multi/array entries ("key=value")
                // and reset the array of a multi-entry if the value is empty ("key[]=<empty>")
                bool emptyValue = string.IsNullOrEmpty(value);

                if (!multi || emptyValue)
                {
                    list.Clear();
                }

                if (multi && emptyValue)
                {
                    return;
                }

                list.Add(value);
            }
        }

        private static IList<string> GetNormalizedValueList(IEnumerable<string> values)
        {
            var result = new List<string>();

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    result.Clear();
                }
                else
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }
}
