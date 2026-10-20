using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The height profile cache of <see cref="TrackPieceKit.GetHeightProfile"/> over synthetic ride archives: a piece whose
/// profile is not listed in the archive is never opened, a malformed one is opened once and its rejection cached, and a
/// valid one is parsed once and served from the cache even when the archive's bytes are replaced behind it.
/// The archive is a local <see cref="IArchive"/> over an in-test listing, so no ride archive of the original data is needed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TrackHeightProfileCacheTests
{
	private const string ArchiveExtension = ".track";
	private const string ArchiveName = "ride" + ArchiveExtension;
	private const string PieceDirectory = "/ride";
	private const int FirstPiece = 1;

	/// <summary>The bytes each archive hands out, and the opens it saw, keyed by the archive's own path so tests never share them.</summary>
	private static readonly Dictionary<string, byte[]> Payloads = new( StringComparer.Ordinal );
	private static readonly Dictionary<string, int> OpenCounts = new( StringComparer.Ordinal );

	private string root = null!;

	[TestInitialize]
	public void Initialize()
	{
		root = Path.Combine( Path.GetTempPath(), $"opentpw-hmpcache-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Payloads.Remove( ArchivePath );
		OpenCounts.Remove( ArchivePath );
		if ( Directory.Exists( root ) )
			Directory.Delete( root, true );
	}

	private string ArchivePath => Path.Combine( root, ArchiveName );

	private int OpenCount => OpenCounts.GetValueOrDefault( ArchivePath );

	/// <summary>A kit over a fresh file system listing <paramref name="members"/>, so neither archive nor cache survives an earlier call.</summary>
	private TrackPieceKit Kit( params string[] members )
	{
		File.WriteAllLines( ArchivePath, members );
		var fileSystem = new BaseFileSystem( root );
		fileSystem.RegisterArchiveHandler<CountingArchive>( ArchiveExtension );
		return TrackPieceKit.Load( fileSystem, PieceDirectory, TrackFamily.Water );
	}

	/// <summary>
	/// The first piece's stem, read through the public <see cref="TrackPieceKit.StemOf"/> over an empty real directory: the
	/// listing has to name the member the way the kit asks for it, and the stem is what the kit derives that name from.
	/// </summary>
	private static string FirstStem()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-hmpprobe-{Guid.NewGuid():N}" );
		try
		{
			var fileSystem = new BaseFileSystem( directory );
			Directory.CreateDirectory( Path.Combine( directory, "ride" ) );
			return TrackPieceKit.Load( fileSystem, "/ride", TrackFamily.Water ).StemOf( FirstPiece )
				?? throw new InvalidOperationException( "The first piece has no stem." );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	/// <summary>
	/// A valid 1 × 1 cell profile of 5 samples per cell: a 48 byte header, a 5 × 5 profile, one per-cell maximum and one
	/// per-cell flag, 75 bytes. The section offsets are the three the reader requires of each other, and the profile byte
	/// sample <c>(2, 1)</c> reads is 7, holding <c>seed + 7</c>.
	/// </summary>
	private static byte[] Hmp( int seed )
	{
		const int header = 48;
		const int profile = 1 * 5 * 1 * 5;
		const int maxima = header + profile;
		const int flags = maxima + 1;
		var data = new byte[flags + 1];
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0 ), HmpFile.ExpectedVersion );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 2 ), HmpFile.ExpectedMagic );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 100 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), 1 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), header );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), maxima );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), flags );
		for ( var index = 0; index < 6; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 24 + index * 4 ), index );
		for ( var index = 0; index < profile; index++ )
			data[header + index] = (byte)(seed + index);
		data[maxima] = (byte)seed;
		return data;
	}

	[TestMethod]
	public void APieceWithoutAListedProfileIsNeverOpened()
	{
		var stem = FirstStem();
		// the archive holds readable bytes, so a zero open count is the kit's doing and not a failing double
		Payloads[ArchivePath] = Hmp( 1 );
		var kit = Kit( "unrelated.txt" );

		Assert.IsNull( kit.GetHeightProfile( FirstPiece ), $"the archive lists no {stem}.hmp" );
		Assert.AreEqual( 0, OpenCount, "the archive is not opened for a profile the listing does not name" );

		Assert.IsNull( kit.GetHeightProfile( FirstPiece ), "the unlisted profile stays absent" );
		Assert.AreEqual( 0, OpenCount, "a repeated lookup still does not open an unlisted member" );
	}

	[TestMethod]
	public void AMalformedProfileIsOpenedOnceAndItsRejectionIsCached()
	{
		var stem = FirstStem();
		var malformed = Hmp( 1 );
		malformed[2] = 0;
		Payloads[ArchivePath] = malformed;
		var kit = Kit( stem + ".hmp" );

		Assert.IsNull( kit.GetHeightProfile( FirstPiece ), "the reader's rejection of the magic is swallowed" );
		Assert.AreEqual( 1, OpenCount, "the archive is opened once" );

		// a good profile behind it: a second call that still gives nothing is caching the rejection, not reading a bad file
		Payloads[ArchivePath] = Hmp( 1 );
		Assert.IsNull( kit.GetHeightProfile( FirstPiece ), "a good profile behind it does not re-parse the cached rejection" );
		Assert.AreEqual( 1, OpenCount, "the rejection is cached, the archive is not opened again" );
	}

	[TestMethod]
	public void AValidProfileIsParsedOnceAndTheCachedInstanceSurvivesReplacedBytes()
	{
		var stem = FirstStem();
		Payloads[ArchivePath] = Hmp( 1 );
		var kit = Kit( stem + ".hmp" );

		var profile = kit.GetHeightProfile( FirstPiece );
		Assert.IsNotNull( profile );
		Assert.AreEqual( (byte)8, profile!.Sample( 2, 1 ), "profile byte 1 * 5 + 2 of a seed-1 profile" );
		Assert.AreEqual( 1, OpenCount );

		// same shape, different bytes: a second read of the archive would give 107 here
		Payloads[ArchivePath] = Hmp( 100 );
		Assert.AreSame( profile, kit.GetHeightProfile( FirstPiece ) );
		Assert.AreEqual( (byte)8, profile.Sample( 2, 1 ), "the replaced bytes are not read back" );
		Assert.AreEqual( 1, OpenCount, "the archive is opened once, whatever its bytes become" );
	}

	/// <summary>
	/// An archive of the test: it lists the lines written to its own file and hands out the bytes the test registered under
	/// its path, counting the opens. <see cref="BaseFileSystem"/> creates the handler through its single-argument constructor.
	/// </summary>
	public sealed class CountingArchive : IArchive
	{
		private readonly string[] files;
		private readonly string archivePath;

		public CountingArchive( string path )
		{
			archivePath = path;
			files = File.ReadAllLines( path );
		}

		public string[] GetFiles( string internalPath ) => files;

		public string[] GetDirectories( string internalPath ) => Array.Empty<string>();

		public Stream OpenFile( string path )
		{
			OpenCounts[archivePath] = OpenCounts.GetValueOrDefault( archivePath ) + 1;
			return new MemoryStream( Payloads[archivePath], false );
		}

		public long GetFileSize( string path ) => Payloads[archivePath].Length;

		public DateTime GetModifiedTime() => DateTime.UnixEpoch;

		public void ReadFromStream( Stream stream ) => throw new NotSupportedException();

		public ArchiveFile GetFile( string internalPath ) => throw new NotSupportedException();

		public byte[] GetData( int offset, int length ) => Payloads[archivePath].Skip( offset ).Take( length ).ToArray();

		public void Dispose() { }
	}
}
