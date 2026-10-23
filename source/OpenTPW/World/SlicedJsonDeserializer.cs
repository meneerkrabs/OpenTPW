using System.Text.Json;
using System.Threading.Tasks.Sources;

namespace OpenTPW;

/// <summary>
/// Deserializes a JSON value with the serializer's own asynchronous reader, fed a slice of the bytes a step, so that a large save's parse spreads over several frames (the
/// browser has no second thread to hand it to; the counterpart of <see cref="SlicedJsonSerializer"/>). The serializer reads from a stream whose reads complete only when a step
/// feeds them: each <see cref="Step"/> hands the waiting reads up to a slice of bytes in all (<see cref="SlicedJsonDeserializer.SliceBytes"/> unless a test asks for another).
/// A read completes inline, in the step that feeds it, with the thread's synchronization context set aside meanwhile, so the serializer parses what it was handed, on the
/// step's thread, before it asks for more and the step returns; a step after which the parse has neither ended nor asked for a read throws. The result and every failure are the serializer's own: the same converters, options and checks as a parse of the whole span,
/// but for a leading UTF-8 byte order mark, which a stream's reader skips and a span's refuses (<see cref="ParkResumeSaveFile.DeserializeSteps"/> leaves such bytes to the
/// whole parse). Disposing the parse part way fails its waiting read, so the serializer gives its buffers back.
/// </summary>
internal sealed class SlicedJsonDeserializer<T> : IDisposable
{
	private readonly FedStream stream;
	private readonly Task<T?> parse;
	private readonly int slice;

	internal SlicedJsonDeserializer( ReadOnlyMemory<byte> bytes, JsonSerializerOptions options, int slice = SlicedJsonDeserializer.SliceBytes )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( slice, 1 );
		this.slice = slice;
		stream = new FedStream( bytes );
		// the parse runs until its first read waits for a step
		parse = JsonSerializer.DeserializeAsync<T>( stream, options ).AsTask();
	}

	/// <summary>True once the parse has ended, with its value or its failure (<see cref="Result"/>).</summary>
	internal bool Done => parse.IsCompleted;

	/// <summary>Steps run so far.</summary>
	internal int Steps { get; private set; }

	/// <summary>Feeds the next slice and lets the serializer parse it; true once the parse has ended.</summary>
	internal bool Step()
	{
		if ( !parse.IsCompleted )
		{
			Steps++;
			stream.Feed( slice );
			// the serializer parses only in the steps that feed it: a parse that neither ended nor waits for its next read went on elsewhere
			if ( !parse.IsCompleted && !stream.Waiting )
				throw new InvalidOperationException( "The sliced parse went on outside its step." );
		}
		return parse.IsCompleted;
	}

	/// <summary>The parsed value, or the parse's failure thrown as the serializer threw it; only once <see cref="Done"/>.</summary>
	internal T? Result => parse.IsCompleted ? parse.GetAwaiter().GetResult() : throw new InvalidOperationException( "The parse has not ended." );

	public void Dispose()
	{
		if ( !parse.IsCompleted )
			stream.Abandon();
		// a parse given up part way ends in the failure Abandon gave it: nobody waits for that
		_ = parse.Exception;
	}

	/// <summary>
	/// A read-only stream over the bytes whose reads wait for <see cref="Feed"/>. Its reads are its own value task source, which runs the reader's continuation inline when
	/// it completes a read (<see cref="ManualResetValueTaskSourceCore{TResult}"/>, never asynchronously and never through a scheduling context).
	/// </summary>
	private sealed class FedStream( ReadOnlyMemory<byte> bytes ) : Stream, IValueTaskSource<int>
	{
		private int position;
		private ManualResetValueTaskSourceCore<int> read;
		private Memory<byte> waitingBuffer;

		/// <summary>True while a read waits for <see cref="Feed"/>.</summary>
		internal bool Waiting { get; private set; }

		/// <summary>Hands waiting reads up to <paramref name="budget"/> bytes; a read after the last byte reads the end.</summary>
		internal void Feed( int budget )
		{
			while ( Waiting && budget > 0 )
			{
				var count = Math.Min( Math.Min( waitingBuffer.Length, budget ), bytes.Length - position );
				bytes.Slice( position, count ).CopyTo( waitingBuffer );
				position += count;
				budget -= count;
				Waiting = false;
				waitingBuffer = default;
				// the serializer parses what it got here, and has asked for its next read or ended when this returns
				Inline( count, null );
				if ( count == 0 )
					return;
			}
		}

		/// <summary>
		/// Completes the waiting read with <paramref name="count"/> bytes, or with <paramref name="failure"/>, while the thread has no synchronization context: the reader's
		/// continuation and those of the serializer's own asynchronous steps after it then run inline, here (a thread with a context of its own has the runtime queue a
		/// task's continuations elsewhere, even those that asked for no context).
		/// </summary>
		private void Inline( int count, Exception? failure )
		{
			var context = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext( null );
			try
			{
				if ( failure == null )
					read.SetResult( count );
				else
					read.SetException( failure );
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext( context );
			}
		}

		/// <summary>Fails the waiting read: the serializer ends with that failure and gives its buffers back.</summary>
		internal void Abandon()
		{
			if ( !Waiting )
				return;
			Waiting = false;
			waitingBuffer = default;
			Inline( 0, new OperationCanceledException( "The sliced parse was given up." ) );
		}

		public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
		{
			if ( Waiting )
				throw new InvalidOperationException( "A read is already waiting." );
			if ( buffer.Length == 0 )
				return ValueTask.FromResult( 0 );
			read.Reset();
			Waiting = true;
			waitingBuffer = buffer;
			return new ValueTask<int>( this, read.Version );
		}

		int IValueTaskSource<int>.GetResult( short token ) => read.GetResult( token );

		ValueTaskSourceStatus IValueTaskSource<int>.GetStatus( short token ) => read.GetStatus( token );

		// inline in the feeding step, whatever context the reader asked to resume on: the step is the parse's only place
		void IValueTaskSource<int>.OnCompleted( Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags ) =>
			read.OnCompleted( continuation, state, token, flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext );

		public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException( "The sliced parse reads asynchronously only." );
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => position; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}
}

/// <summary>The slice <see cref="SlicedJsonDeserializer{T}"/> hands the serializer a step.</summary>
internal static class SlicedJsonDeserializer
{
	/// <summary>The bytes a step hands the serializer at most: about 2 ms of a save's parse in the page compiled ahead of time.</summary>
	internal const int SliceBytes = 32 * 1024;
}

