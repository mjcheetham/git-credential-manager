using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GitCredentialManager;
using Xunit;

namespace Core.Tests;

public class AsyncTextReaderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abcdef")]
    [InlineData("a\0b\r\n")]
    public async Task ReadAsyncDrainsBufferedInputBeforeReturningEndOfStream(string input)
    {
        using var reader = new AsyncTextReader(new StringReader(input));
        var buffer = new char[3];
        var result = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).AsTask().WaitAsync(Timeout)) != 0)
            result.Append(buffer, 0, count);

        Assert.Equal(input, result.ToString());
        Assert.Equal(-1, reader.Read());
        Assert.Equal(-1, reader.Peek());
        Assert.Null(await reader.ReadLineAsync().WaitAsync(Timeout));
    }

    [Fact]
    public void SynchronousReadsAndPeekPreserveCharactersAndLineEndings()
    {
        using var reader = new AsyncTextReader(new StringReader("abcd\r\nsecond\rthird\nlast"));
        Assert.Equal('a', reader.Peek());
        Assert.Equal('a', reader.Peek());
        Assert.Equal('a', reader.Read());

        var buffer = new char[4];
        Assert.Equal(2, reader.ReadBlock(buffer, 1, 2));
        Assert.Equal("bc", new string(buffer, 1, 2));
        Assert.Equal(1, reader.Read(buffer.AsSpan(1, 1)));
        Assert.Equal('d', buffer[1]);
        Assert.Equal("", reader.ReadLine());
        Assert.Equal("second", reader.ReadLine());
        Assert.Equal("third", reader.ReadLine());
        Assert.Equal("last", reader.ReadLine());
        Assert.Null(reader.ReadLine());
        Assert.Equal("", reader.ReadToEnd());
    }

    [Fact]
    public async Task AsyncArrayReadsHonorOffsetsAndReadBlockStopsAtEndOfStream()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        inner.Add("abcdef");
        char[] buffer = "-----".ToCharArray();

        Assert.Equal(2, await reader.ReadBlockAsync(buffer, 1, 2).WaitAsync(Timeout));
        Assert.Equal("-ab--", new string(buffer));
        await inner.WaitForReadAsync(2);
        inner.Complete();
        Assert.Equal(2, await reader.ReadAsync(buffer, 1, 2).WaitAsync(Timeout));
        Assert.Equal("-cd--", new string(buffer));
        Assert.Equal(2, await reader.ReadBlockAsync(buffer.AsMemory(1, 3)).AsTask().WaitAsync(Timeout));
        Assert.Equal("-ef--", new string(buffer));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("\n\r")]
    [InlineData("one\rtwo\r\nthree\n\nlast")]
    public async Task ReadLineAsyncMatchesTextReaderLineEndings(string input)
    {
        using var expected = new StringReader(input);
        using var reader = new AsyncTextReader(new StringReader(input));
        string? line;
        do
        {
            line = expected.ReadLine();
            Assert.Equal(line, await reader.ReadLineAsync().WaitAsync(Timeout));
        }
        while (line is not null);
    }

    [Fact]
    public async Task ReadLineAsyncHandlesCarriageReturnAndLineFeedInSeparateInnerReads()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        inner.Add("first\r");

        Task<string?> firstLine = reader.ReadLineAsync();
        await inner.WaitForReadAsync(2);
        Assert.False(firstLine.IsCompleted);

        inner.Add("\nsecond\rthird\nlast");
        inner.Complete();
        Assert.Equal("first", await firstLine.WaitAsync(Timeout));
        Assert.Equal("second", await reader.ReadLineAsync().WaitAsync(Timeout));
        Assert.Equal("third", await reader.ReadLineAsync().WaitAsync(Timeout));
        Assert.Equal("last", await reader.ReadLineAsync().WaitAsync(Timeout));
        Assert.Null(await reader.ReadLineAsync().WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(ReadOperation.Read)]
    [InlineData(ReadOperation.ReadBlock)]
    [InlineData(ReadOperation.ReadLine)]
    [InlineData(ReadOperation.ReadToEnd)]
    public async Task CancellationLeavesProducerRunningAndBufferedCharactersAvailable(ReadOperation operation)
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        using var cts = new CancellationTokenSource();
        var buffer = new char[8];
        Task pending = operation switch
        {
            ReadOperation.Read => reader.ReadAsync(buffer.AsMemory(), cts.Token).AsTask(),
            ReadOperation.ReadBlock => reader.ReadBlockAsync(buffer.AsMemory(), cts.Token).AsTask(),
            ReadOperation.ReadLine => reader.ReadLineAsync(cts.Token).AsTask(),
            ReadOperation.ReadToEnd => reader.ReadToEndAsync(cts.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await inner.WaitForReadAsync(1);
        cts.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Timeout));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.False(inner.ReadToken.IsCancellationRequested);

        inner.Add("old");
        await inner.WaitForReadAsync(2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(buffer.AsMemory(), cts.Token).AsTask().WaitAsync(Timeout));

        Assert.Equal(1, await reader.ReadAsync(buffer.AsMemory(0, 1)).AsTask().WaitAsync(Timeout));
        Assert.Equal('o', buffer[0]);
        inner.Add("new");
        inner.Complete();
        Assert.Equal("ldnew", await reader.ReadToEndAsync().WaitAsync(Timeout));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellingOneConcurrentReaderDoesNotCancelAnother(bool cancelFirst)
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        using var cts = new CancellationTokenSource();
        var firstBuffer = new char[1];
        var secondBuffer = new char[1];
        Task<int> first = reader.ReadAsync(firstBuffer.AsMemory(), cancelFirst ? cts.Token : CancellationToken.None).AsTask();
        Task<int> second = reader.ReadAsync(secondBuffer.AsMemory(), cancelFirst ? CancellationToken.None : cts.Token).AsTask();

        await inner.WaitForReadAsync(1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => (cancelFirst ? first : second).WaitAsync(Timeout));
        Task<int> remaining = cancelFirst ? second : first;
        Assert.False(remaining.IsCompleted);

        inner.Add("x");
        Assert.Equal(1, await remaining.WaitAsync(Timeout));
        Assert.Equal('x', (cancelFirst ? secondBuffer : firstBuffer)[0]);
        await inner.WaitForReadAsync(2);
        Assert.Equal(2, inner.ReadCount);
    }

    [Fact]
    public async Task CompetingReadRetriesAfterAnotherReaderConsumesAvailableCharacter()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        using var pausedBuffer = new PausingMemoryManager();
        Task<int> first = reader.ReadAsync(pausedBuffer.Memory).AsTask();
        Task<int>? second = null;

        try
        {
            await inner.WaitForReadAsync(1);
            inner.Add("a");
            await pausedBuffer.Accessed.WaitAsync(Timeout);

            var buffer = new char[1];
            second = reader.ReadAsync(buffer.AsMemory()).AsTask();
            Assert.Equal(1, await second.WaitAsync(Timeout));
            Assert.Equal('a', buffer[0]);

            var failure = new IOException("Inner read failed.");
            inner.Complete(failure);
            pausedBuffer.Resume();
            var actual = await Assert.ThrowsAsync<IOException>(() => first.WaitAsync(Timeout));
            Assert.Same(failure, actual);
        }
        finally
        {
            pausedBuffer.Resume();
            inner.Complete();
            try
            {
                await Task.WhenAll(first, second ?? Task.FromResult(0)).WaitAsync(Timeout);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ReadAsyncReturnsAvailableCharactersWithoutWaitingToFillBuffer()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        var buffer = new char[3];
        Task<int> pending = reader.ReadAsync(buffer.AsMemory()).AsTask();
        await inner.WaitForReadAsync(1);

        inner.Add("a");
        Assert.Equal(1, await pending.WaitAsync(Timeout));
        Assert.Equal('a', buffer[0]);
    }

    [Fact]
    public async Task ReadBlockAsyncWaitsForRequestedCharacters()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        var buffer = new char[3];
        Task<int> pending = reader.ReadBlockAsync(buffer.AsMemory()).AsTask();
        await inner.WaitForReadAsync(1);

        inner.Add("a");
        await inner.WaitForReadAsync(2);
        Assert.False(pending.IsCompleted);
        inner.Add("bc");
        Assert.Equal(3, await pending.WaitAsync(Timeout));
        Assert.Equal("abc", new string(buffer));
    }

    [Fact]
    public async Task EmptyReadsReturnImmediatelyWithoutStartingProducer()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);

        Assert.Equal(0, reader.Read(Span<char>.Empty));
        Assert.Equal(0, reader.ReadBlock([], 0, 0));
        Assert.Equal(0, await reader.ReadAsync(Memory<char>.Empty));
        Assert.Equal(0, await reader.ReadBlockAsync(Memory<char>.Empty));
        Assert.Equal(0, inner.ReadCount);
    }

    [Fact]
    public void ArrayReadsValidateArgumentsBeforeStartingProducer()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        Action<char[], int, int>[] reads =
        [
            (buffer, index, count) => reader.Read(buffer, index, count),
            (buffer, index, count) => reader.ReadBlock(buffer, index, count),
            (buffer, index, count) => reader.ReadAsync(buffer, index, count),
            (buffer, index, count) => reader.ReadBlockAsync(buffer, index, count)
        ];

        foreach (var read in reads)
        {
            Assert.Throws<ArgumentNullException>(() => read(null!, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => read(new char[1], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => read(new char[1], 0, -1));
            Assert.Throws<ArgumentException>(() => read(new char[1], 1, 1));
        }

        Assert.Equal(0, inner.ReadCount);
    }

    [Fact]
    public async Task ReadAsyncReturnsEvenWhenInnerReadAsyncBlocksSynchronously()
    {
        using var inner = new ControlledTextReader(blockSynchronously: true);
        using var reader = new AsyncTextReader(inner);
        var buffer = new char[1];
        Task<Task<int>> invocation = Task.Factory.StartNew(
            () => reader.ReadAsync(buffer.AsMemory()).AsTask(),
            CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        try
        {
            Task<int> pending = await invocation.WaitAsync(Timeout);
            await inner.WaitForReadAsync(1);
            Assert.False(pending.IsCompleted);
            inner.Add("x");
            Assert.Equal(1, await pending.WaitAsync(Timeout));
            Assert.Equal('x', buffer[0]);
        }
        finally
        {
            inner.Complete();
        }
    }

    [Fact]
    public async Task InnerFailureIsReportedAfterBufferedCharactersAreDrained()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        var buffer = new char[1];
        Task<int> first = reader.ReadAsync(buffer.AsMemory()).AsTask();
        await inner.WaitForReadAsync(1);
        inner.Add("ab");
        Assert.Equal(1, await first.WaitAsync(Timeout));
        Assert.Equal('a', buffer[0]);
        await inner.WaitForReadAsync(2);

        var failure = new IOException("Inner read failed.");
        inner.Complete(failure);
        Assert.Equal(1, await reader.ReadAsync(buffer.AsMemory()).AsTask().WaitAsync(Timeout));
        Assert.Equal('b', buffer[0]);
        var actual = await Assert.ThrowsAsync<IOException>(
            () => reader.ReadAsync(buffer.AsMemory()).AsTask().WaitAsync(Timeout));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task ClosingInnerReaderCompletesChannelWithoutDiscardingBufferedCharacters()
    {
        using var inner = new ControlledTextReader();
        using var reader = new AsyncTextReader(inner);
        var buffer = new char[1];
        Task<int> first = reader.ReadAsync(buffer.AsMemory()).AsTask();
        await inner.WaitForReadAsync(1);
        inner.Add("abc");
        Assert.Equal(1, await first.WaitAsync(Timeout));
        await inner.WaitForReadAsync(2);

        inner.Dispose();
        Assert.Equal("bc", await reader.ReadToEndAsync().WaitAsync(Timeout));
        Assert.Equal(-1, reader.Read());
    }

    [Fact]
    public async Task DisposalClosesPendingReadsAndDisposesInnerWithoutWaitingForProducer()
    {
        using var inner = new ControlledTextReader(completeOnDispose: false);
        using var reader = new AsyncTextReader(inner);
        Task<int> pending = reader.ReadAsync(new char[1].AsMemory()).AsTask();
        await inner.WaitForReadAsync(1);

        try
        {
            await Task.Run(reader.Dispose).WaitAsync(Timeout);
            Assert.True(inner.IsDisposed);
            Assert.True(inner.ReadToken.IsCancellationRequested);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(Timeout));
            Assert.Throws<ObjectDisposedException>(() => reader.Read());
            Assert.Throws<ObjectDisposedException>(() => reader.Peek());
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => reader.ReadLineAsync().WaitAsync(Timeout));

            inner.Add("late");
            inner.Complete();
            await inner.WaitForReadFinishedAsync(1);
            Assert.Equal(1, inner.ReadCount);
        }
        finally
        {
            inner.Complete();
        }
    }

    public enum ReadOperation
    {
        Read,
        ReadBlock,
        ReadLine,
        ReadToEnd
    }

    private sealed class PausingMemoryManager : MemoryManager<char>
    {
        private readonly char[] _buffer = new char[1];
        private readonly TaskCompletionSource _accessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pauseNextAccess = 1;

        public override Memory<char> Memory => CreateMemory(_buffer.Length);
        public Task Accessed => _accessed.Task;

        public void Resume() => _resume.TrySetResult();

        public override Span<char> GetSpan()
        {
            if (Interlocked.Exchange(ref _pauseNextAccess, 0) == 1)
            {
                _accessed.TrySetResult();
                _resume.Task.GetAwaiter().GetResult();
            }

            return _buffer;
        }

        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Resume();
        }
    }

    private sealed class ControlledTextReader(bool blockSynchronously = false, bool completeOnDispose = true) : TextReader
    {
        private readonly Channel<string> _chunks = Channel.CreateUnbounded<string>();
        private readonly Channel<int> _readCalls = Channel.CreateUnbounded<int>();
        private readonly Channel<int> _finishedCalls = Channel.CreateUnbounded<int>();
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);
        public CancellationToken ReadToken { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Add(string chunk)
        {
            if (!_chunks.Writer.TryWrite(chunk))
                throw new InvalidOperationException("The input is already complete.");
        }

        public void Complete(Exception? error = null) => _chunks.Writer.TryComplete(error);

        public Task WaitForReadAsync(int expected) => WaitForCallAsync(_readCalls.Reader, expected);

        public Task WaitForReadFinishedAsync(int expected) => WaitForCallAsync(_finishedCalls.Reader, expected);

        private static async Task WaitForCallAsync(ChannelReader<int> calls, int expected)
        {
            while (await calls.ReadAsync().AsTask().WaitAsync(Timeout) < expected)
            {
            }
        }

        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            ReadToken = cancellationToken;
            ValueTask<int> read = ReadNextChunkAsync(buffer);
            return blockSynchronously ? new ValueTask<int>(read.AsTask().GetAwaiter().GetResult()) : read;
        }

        private async ValueTask<int> ReadNextChunkAsync(Memory<char> buffer)
        {
            int call = Interlocked.Increment(ref _readCount);
            _readCalls.Writer.TryWrite(call);
            try
            {
                if (!await _chunks.Reader.WaitToReadAsync())
                    return 0;
                if (!_chunks.Reader.TryRead(out string? chunk))
                    throw new InvalidOperationException("Overlapping inner reads.");
                chunk.AsMemory().CopyTo(buffer);
                return chunk.Length;
            }
            finally
            {
                _finishedCalls.Writer.TryWrite(call);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                if (completeOnDispose)
                    _chunks.Writer.TryComplete(new ObjectDisposedException(nameof(ControlledTextReader)));
            }

            base.Dispose(disposing);
        }
    }
}
