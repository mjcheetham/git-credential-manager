using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GitCredentialManager;

/// <summary>
/// Represents a text reader that supports cancellation of asynchronous read operations.
/// </summary>
/// <remarks>
/// <para>
/// Characters are read from the underlying reader on a background task and stored
/// in an unbounded buffer. Reading continues when no read operations are pending.
/// </para>
/// <para>
/// Canceling a read operation does not cancel reads from the underlying reader.
/// Characters that have not been consumed remain available to subsequent read operations.
/// </para>
/// <para>
/// When the underlying reader reaches the end of its input or is closed, buffered
/// characters remain available until consumed. Other read exceptions are propagated
/// after buffered characters have been consumed.
/// </para>
/// <para>Read operations are not synchronized.</para>
/// </remarks>
public class AsyncTextReader : TextReader
{
    private readonly TextReader _inner;
    private readonly Channel<char> _channel = Channel.CreateUnbounded<char>();
    private readonly Lock _sync = new();
    private readonly CancellationTokenSource _cts = new();

    private Task? _readTask;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncTextReader"/> class
    /// that reads from the specified text reader.
    /// </summary>
    /// <param name="inner">The text reader from which to read characters.</param>
    /// <exception cref="inner">
    /// <paramref name="inner"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>Disposing this instance also disposes <paramref name="inner"/>.</remarks>
    public AsyncTextReader(TextReader inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    private void EnsureStarted()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_readTask is null)
            {
                CancellationToken ct = _cts.Token;
                // Even ReadAsync can block synchronously, as Console.In does,
                // so we run the read loop on a background thread to avoid blocking the caller.
                _readTask = Task.Run(() => ReadLoop(_inner, _channel.Writer, ct), ct);
            }
        }
    }

    private static async Task ReadLoop(TextReader reader, ChannelWriter<char> writer, CancellationToken ct)
    {
        Exception? error = null;
        try
        {
            var buffer = new char[1024];
            while (!ct.IsCancellationRequested)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (count == 0)
                    break;

                for (int i = 0; i < count; i++)
                {
                    if (!writer.TryWrite(buffer[i]))
                        return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            writer.TryComplete(error);
        }
    }

    /// <inheritdoc />
    public override int Peek()
    {
        EnsureStarted();
        while (WaitForDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult())
        {
            if (_channel.Reader.TryPeek(out char value))
                return value;
        }

        return -1;
    }

    /// <inheritdoc />
    public override int Read()
    {
        Span<char> buffer = stackalloc char[1];
        return Read(buffer) == 0 ? -1 : buffer[0];
    }

    /// <inheritdoc />
    public override int Read(char[] buffer, int index, int count)
    {
        ValidateBufferArguments(buffer, index, count);
        return Read(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int Read(Span<char> buffer) => ReadBuffer(buffer, readBlock: false);

    /// <inheritdoc />
    public override int ReadBlock(char[] buffer, int index, int count)
    {
        ValidateBufferArguments(buffer, index, count);
        return ReadBlock(buffer.AsSpan(index, count));
    }

    /// <inheritdoc />
    public override int ReadBlock(Span<char> buffer) => ReadBuffer(buffer, readBlock: true);

    private int ReadBuffer(Span<char> buffer, bool readBlock)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        EnsureStarted();
        int total = 0;
        while (total < buffer.Length && WaitForDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult())
        {
            total += ReadAvailable(buffer[total..]);
            if (!readBlock && total > 0)
                break;
        }

        return total;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        ValidateBufferArguments(buffer, index, count);
        return ReadAsync(buffer.AsMemory(index, count)).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken ct = default) =>
        ReadBufferAsync(buffer, readBlock: false, ct);

    /// <inheritdoc />
    public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
    {
        ValidateBufferArguments(buffer, index, count);
        return ReadBlockAsync(buffer.AsMemory(index, count)).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken ct = default) =>
        ReadBufferAsync(buffer, readBlock: true, ct);

    private static void ValidateBufferArguments(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
            throw new ArgumentException("The buffer is too small for the requested range.");
    }

    private async ValueTask<int> ReadBufferAsync(Memory<char> buffer, bool readBlock, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return 0;

        EnsureStarted();
        int total = 0;
        do
        {
            int count = await ReadCoreAsync(buffer[total..], ct).ConfigureAwait(false);
            if (count == 0)
                break;
            total += count;
        }
        while (readBlock && total < buffer.Length);

        return total;
    }

    private async ValueTask<int> ReadCoreAsync(Memory<char> buffer, CancellationToken ct)
    {
        while (await WaitForDataAsync(ct).ConfigureAwait(false))
        {
            int count = ReadAvailable(buffer.Span);
            if (count > 0)
                return count;
        }

        return 0;
    }

    private int ReadAvailable(Span<char> buffer)
    {
        int count = 0;
        while (count < buffer.Length && _channel.Reader.TryRead(out char value))
            buffer[count++] = value;
        return count;
    }

    private async ValueTask<bool> WaitForDataAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        bool available = await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return available;
    }

    /// <inheritdoc />
    public override string? ReadLine() => ReadLineAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();

    /// <inheritdoc />
    public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureStarted();
        var result = new StringBuilder();
        var buffer = new char[1];
        while (await ReadCoreAsync(buffer, ct).ConfigureAwait(false) != 0)
        {
            char value = buffer[0];
            if (value is '\r' or '\n')
            {
                if (value == '\r'
                    && await WaitForDataAsync(ct).ConfigureAwait(false)
                    && _channel.Reader.TryPeek(out char next) && next == '\n')
                {
                    _channel.Reader.TryRead(out _);
                }

                return result.ToString();
            }

            result.Append(value);
        }

        return result.Length == 0 ? null : result.ToString();
    }

    /// <inheritdoc />
    public override string ReadToEnd() => ReadToEndAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override Task<string> ReadToEndAsync() => ReadToEndAsync(CancellationToken.None);

    /// <inheritdoc />
    public override async Task<string> ReadToEndAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureStarted();
        var result = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await ReadCoreAsync(buffer, ct).ConfigureAwait(false)) != 0)
            result.Append(buffer, 0, count);
        return result.ToString();
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <paramref name="disposing"/> is <see langword="true"/>, pending read operations
    /// fail with an <see cref="ObjectDisposedException"/> and the underlying reader is disposed.
    /// This method requests cancellation of underlying read operations but does not wait
    /// for the background task to complete. Disposing the underlying reader can block.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _channel.Writer.TryComplete(new ObjectDisposedException(nameof(AsyncTextReader)));
            }

            try
            {
                _cts.Cancel();
            }
            finally
            {
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    _cts.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
