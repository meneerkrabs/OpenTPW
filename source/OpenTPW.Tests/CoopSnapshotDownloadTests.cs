using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Client;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-snapshot-join] The client's ranged download of a join snapshot (<see cref="OnlineClient.DownloadCoopSnapshotAsync"/>, docs/COOP.md "Joining a running session") against a
/// scripted server: a line that stalls or breaks resumes from the bytes it has with a range, a server that ignores the range sends it all again, a line that broke after its last byte needs no
/// other attempt, and the bytes are checked against their length and digest.
/// </summary>
[TestClass]
public class CoopSnapshotDownloadTests
{
	private const string Session = "0123456789abcdef";
	private static readonly TimeSpan Stall = TimeSpan.FromMilliseconds( 200 );

	/// <summary>A response body that sends <paramref name="bytes"/>, then stalls until its read is cancelled or breaks (a line that stopped moving, or dropped).</summary>
	private sealed class LineStream( byte[] bytes, bool breaks ) : Stream
	{
		private int position;
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => position; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read( byte[] buffer, int offset, int count ) => ReadAsync( buffer.AsMemory( offset, count ) ).AsTask().GetAwaiter().GetResult();

		public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
		{
			if ( position < bytes.Length )
			{
				var count = Math.Min( buffer.Length, bytes.Length - position );
				bytes.AsMemory( position, count ).CopyTo( buffer );
				position += count;
				return count;
			}
			if ( breaks )
				throw new IOException( "The connection was reset." );
			await Task.Delay( Timeout.Infinite, cancellationToken );
			return 0;
		}

		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}

	/// <summary>The scripted server: each request gets the next answer, which sees the range it asked for.</summary>
	private sealed class Server( params Func<RangeItemHeaderValue?, HttpResponseMessage>[] answers ) : HttpMessageHandler
	{
		public List<RangeItemHeaderValue?> Ranges { get; } = new();

		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
		{
			var range = request.Headers.Range?.Ranges.Single();
			Ranges.Add( range );
			Assert.IsTrue( Ranges.Count <= answers.Length, "no more requests than the script holds" );
			return Task.FromResult( answers[Ranges.Count - 1]( range ) );
		}
	}

	private static HttpResponseMessage Body( HttpStatusCode status, byte[] bytes, bool stallsAfter = false, bool breaksAfter = false ) => new( status )
	{
		Content = stallsAfter || breaksAfter ? new StreamContent( new LineStream( bytes, breaksAfter ) ) : new ByteArrayContent( bytes )
	};

	private static async Task<byte[]> Download( Server server, byte[] snapshot, string? sha256 = null )
	{
		using var http = new HttpClient( server );
		using var client = new OnlineClient( new Uri( "http://localhost:5000/" ), http );
		return await client.DownloadCoopSnapshotAsync( Session, sha256 ?? Convert.ToHexStringLower( SHA256.HashData( snapshot ) ), snapshot.Length, Stall );
	}

	private static byte[] Snapshot() => RandomNumberGenerator.GetBytes( 200 * 1024 );

	[TestMethod]
	public async Task AStalledLineResumesFromTheBytesItHasWithARange()
	{
		var snapshot = Snapshot();
		var half = snapshot.Length / 2;
		var server = new Server(
			_ => Body( HttpStatusCode.OK, snapshot[..half], stallsAfter: true ),
			range => Body( HttpStatusCode.PartialContent, snapshot[(int)range!.From!.Value..] ) );
		CollectionAssert.AreEqual( snapshot, await Download( server, snapshot ) );
		Assert.AreEqual( 2, server.Ranges.Count );
		Assert.IsNull( server.Ranges[0] );
		Assert.AreEqual( (half, (long?)null), (server.Ranges[1]!.From, server.Ranges[1]!.To), "the rest, from the first byte it lacks" );
	}

	[TestMethod]
	public async Task ABrokenLineResumesAndAServerThatIgnoresTheRangeSendsItAllAgain()
	{
		var snapshot = Snapshot();
		var server = new Server(
			_ => Body( HttpStatusCode.OK, snapshot[..1000], breaksAfter: true ),
			_ => Body( HttpStatusCode.OK, snapshot ) );
		CollectionAssert.AreEqual( snapshot, await Download( server, snapshot ) );
		Assert.AreEqual( 1000L, server.Ranges[1]!.From );
	}

	[TestMethod]
	public async Task ALineThatBreaksOrStallsAfterItsLastByteNeedsNoOtherAttempt()
	{
		foreach ( var breaks in new[] { true, false } )
		{
			var snapshot = Snapshot();
			// a range from the end would be refused (416): the digest decides
			var server = new Server( _ => Body( HttpStatusCode.OK, snapshot, stallsAfter: !breaks, breaksAfter: breaks ) );
			CollectionAssert.AreEqual( snapshot, await Download( server, snapshot ) );
			Assert.AreEqual( 1, server.Ranges.Count );
		}
	}

	[TestMethod]
	public void TheLinkTellsTheJoinWhatEachTransferFailureMeans()
	{
		OnlineException Refused( HttpStatusCode status, TimeSpan? retryAfter = null ) => new( status, $"{(int)status}: refused", retryAfter: retryAfter );
		var busy = CoopLink.DownloadFailure( Refused( HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds( 30 ) ) );
		Assert.IsTrue( busy.Busy && !busy.Permanent && !busy.Missing, "too many downloads: wait and download again" );
		Assert.AreEqual( TimeSpan.FromSeconds( 30 ), busy.RetryAfter );
		Assert.IsTrue( CoopLink.DownloadFailure( Refused( HttpStatusCode.ServiceUnavailable ) ).Busy, "a server error" );
		Assert.IsTrue( CoopLink.DownloadFailure( Refused( HttpStatusCode.NotFound ) ).Missing, "a snapshot rotated out" );
		var forbidden = CoopLink.DownloadFailure( Refused( HttpStatusCode.Forbidden ) );
		Assert.IsTrue( forbidden.Permanent && !forbidden.Busy, "no longer the session's player" );
		foreach ( var plain in new Exception[] { Refused( HttpStatusCode.BadRequest ), new IOException( "The line broke." ), new InvalidDataException( "The bytes do not match." ) } )
		{
			var failed = CoopLink.DownloadFailure( plain );
			Assert.IsTrue( !failed.Succeeded && !failed.Busy && !failed.Permanent && !failed.Missing, $"a failed download: {plain.Message}" );
		}
		var large = CoopLink.UploadFailure( Refused( HttpStatusCode.RequestEntityTooLarge ) );
		Assert.IsTrue( large.TooLarge && large.Permanent );
		Assert.IsTrue( CoopLink.UploadFailure( Refused( HttpStatusCode.Forbidden ) ).Permanent && CoopLink.UploadFailure( Refused( HttpStatusCode.NotFound ) ).Permanent,
			"not the host's, or the session gone" );
		var later = CoopLink.UploadFailure( Refused( HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes( 5 ) ) );
		Assert.IsTrue( !later.Permanent && !later.TooLarge );
		Assert.AreEqual( TimeSpan.FromMinutes( 5 ), later.RetryAfter );
		Assert.IsFalse( CoopLink.UploadFailure( new HttpRequestException( "No route to the server." ) ).Permanent );
	}

	[TestMethod]
	public async Task TheBytesAreCheckedAndAMissingSnapshotOrALineThatKeepsStallingFails()
	{
		var snapshot = Snapshot();
		await Assert.ThrowsExceptionAsync<InvalidDataException>( () => Download( new Server( _ => Body( HttpStatusCode.OK, snapshot.Append( (byte)0 ).ToArray() ) ), snapshot ),
			"longer than its host said" );
		await Assert.ThrowsExceptionAsync<InvalidDataException>( () => Download( new Server( _ => Body( HttpStatusCode.OK, snapshot ) ), snapshot, new string( 'a', 64 ) ),
			"bytes that do not match the digest" );
		var missing = new Server( _ => new HttpResponseMessage( HttpStatusCode.NotFound ) );
		Assert.AreEqual( HttpStatusCode.NotFound, (await Assert.ThrowsExceptionAsync<OnlineException>( () => Download( missing, snapshot ) )).Status );
		Assert.AreEqual( 1, missing.Ranges.Count, "a snapshot the server no longer keeps is not asked for again" );
		var stalling = new Server( Enumerable.Repeat<Func<RangeItemHeaderValue?, HttpResponseMessage>>( _ => Body( HttpStatusCode.OK, snapshot[..10], stallsAfter: true ), 3 ).ToArray() );
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => Download( stalling, snapshot ) );
		Assert.AreEqual( 3, stalling.Ranges.Count, "three attempts in all" );
	}
}
