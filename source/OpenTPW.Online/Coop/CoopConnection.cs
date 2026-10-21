using System.Buffers;
using System.Net.WebSockets;

namespace OpenTPW.Online.Coop;

/// <summary>
/// [EXT:online-coop-session] An open co-op socket (docs/ONLINE.md, "Shared parks"), from <see cref="Client.OnlineClient.ConnectCoopAsync"/>: one send at a time,
/// whole messages received into rented buffers, and a close that never waits, since a browser's only thread cannot block on a pending close.
/// </summary>
public sealed class CoopConnection : IDisposable
{
	/// <summary>How long a close waits for the server's answer before it drops the socket.</summary>
	private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds( 2 );
	private readonly ClientWebSocket socket;
	private readonly SemaphoreSlim sendLock = new( 1, 1 );
	private int closing;

	internal CoopConnection( ClientWebSocket socket ) => this.socket = socket;

	public bool IsOpen => socket.State == WebSocketState.Open;

	/// <summary>Why the server closed the socket (<see cref="CoopCloseStatus"/>), once it has; null while it is open or after an abort.</summary>
	public int? CloseStatus => socket.CloseStatus is { } status ? (int)status : null;

	public Task SendAsync( CoopMessage message, CancellationToken cancel = default ) => SendAsync( message.Encode(), cancel );

	/// <summary>Sends one encoded message after any send before it.</summary>
	public async Task SendAsync( ReadOnlyMemory<byte> message, CancellationToken cancel = default )
	{
		await sendLock.WaitAsync( cancel );
		try
		{
			await socket.SendAsync( message, WebSocketMessageType.Binary, true, cancel );
		}
		finally
		{
			sendLock.Release();
		}
	}

	/// <summary>
	/// The next message, or null once the server has closed the socket (<see cref="CloseStatus"/> says why; the close is answered at once). A text message,
	/// one over <see cref="CoopProtocol.MaximumMessageBytes"/> or a malformed one throws <see cref="InvalidDataException"/>. Cancelling a receive drops the
	/// socket.
	/// </summary>
	public async Task<CoopMessage?> ReceiveAsync( CancellationToken cancel = default )
	{
		var buffer = ArrayPool<byte>.Shared.Rent( 4096 );
		try
		{
			var length = 0;
			while ( true )
			{
				if ( length == buffer.Length )
				{
					var larger = ArrayPool<byte>.Shared.Rent( Math.Min( buffer.Length * 2, CoopProtocol.MaximumMessageBytes + 1 ) );
					buffer.AsSpan( 0, length ).CopyTo( larger );
					ArrayPool<byte>.Shared.Return( buffer );
					buffer = larger;
				}
				var result = await socket.ReceiveAsync( buffer.AsMemory( length ), cancel );
				if ( result.MessageType == WebSocketMessageType.Close )
				{
					Close();
					return null;
				}
				if ( result.MessageType != WebSocketMessageType.Binary )
					throw new InvalidDataException( "Co-op messages are binary." );
				length += result.Count;
				if ( length > CoopProtocol.MaximumMessageBytes )
					throw new InvalidDataException( "The co-op message is too large." );
				if ( result.EndOfMessage )
					return CoopMessage.Decode( buffer.AsSpan( 0, length ) );
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( buffer );
		}
	}

	/// <summary>
	/// Closes the socket without waiting: the close goes out behind any send in progress, and the socket is dropped once the server answers or two seconds
	/// pass. A second close, or one after <see cref="Abort"/>, does nothing.
	/// </summary>
	public void Close( WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string reason = "bye" )
	{
		if ( Interlocked.Exchange( ref closing, 1 ) == 0 )
			_ = CloseInBackgroundAsync( status, reason );
	}

	private async Task CloseInBackgroundAsync( WebSocketCloseStatus status, string reason )
	{
		using var timeout = new CancellationTokenSource( CloseTimeout );
		try
		{
			await sendLock.WaitAsync( timeout.Token );
			try
			{
				if ( socket.State is WebSocketState.Open or WebSocketState.CloseReceived )
					await socket.CloseOutputAsync( status, reason, timeout.Token );
			}
			finally
			{
				sendLock.Release();
			}
			// the receive that reads the server's answer completes the close; nothing waits on it longer than the timeout
			while ( socket.State == WebSocketState.CloseSent && !timeout.IsCancellationRequested )
				await Task.Delay( 20, CancellationToken.None );
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException )
		{
		}
		socket.Abort();
		socket.Dispose();
	}

	/// <summary>Drops the socket at once, without a close (the silence watchdog; a test's lost network).</summary>
	public void Abort()
	{
		Interlocked.Exchange( ref closing, 1 );
		try
		{
			socket.Abort();
		}
		catch ( ObjectDisposedException )
		{
		}
		socket.Dispose();
	}

	/// <summary>Closes the socket without waiting (<see cref="Close"/>).</summary>
	public void Dispose() => Close();
}
