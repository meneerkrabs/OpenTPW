using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SdtEntryTests
{
	/// <summary>Writes a bank whose entries use the 40-byte layout proven over the installed corpus.</summary>
	internal static byte[] CreateBank( params (string Name, ushort SampleRate, byte SoundType, uint RawSampleField, byte[] Frames)[] entries )
	{
		var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( entries.Length );
		var offset = 4 + entries.Length * 4;
		foreach ( var entry in entries )
		{
			writer.Write( offset );
			offset += 40 + entry.Frames.Length;
		}
		foreach ( var entry in entries )
		{
			writer.Write( 40 );
			writer.Write( entry.Frames.Length );
			var name = new byte[16];
			Encoding.ASCII.GetBytes( entry.Name ).CopyTo( name, 0 );
			writer.Write( name );
			writer.Write( entry.SampleRate );
			writer.Write( (ushort)(entry.SoundType << 8 | 16) );
			writer.Write( 0 );
			writer.Write( entry.RawSampleField );
			writer.Write( 0 );
			writer.Write( entry.Frames );
		}
		return output.ToArray();
	}

	private static string Named( ArchiveFile file ) => ((MP2File)file).Name;

	[TestMethod]
	public void FortyByteHeaderFieldsAreReadAtTheirProvenOffsets()
	{
		// Frame bytes are not decoded here; 44100 is written to exercise the UInt16 sample-rate path.
		var frames = new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 };
		var bank = CreateBank( ("sp_test.mp2", 44100, 37, 0x12345, frames) );
		using var archive = new SdtArchive( new MemoryStream( bank ) );

		Assert.AreEqual( 0, archive.SkippedEntries.Count );
		var file = archive.soundFiles.Single();
		Assert.AreEqual( "sp_test.mp2", file.Name );
		Assert.AreEqual( 40, file.Header );
		Assert.AreEqual( 44100, file.SampleRate );
		Assert.AreEqual( 16, file.BitsPerSample );
		Assert.AreEqual( MP2File.SoundTypes.MP2_STEREO, file.SoundType );
		Assert.AreEqual( 0x12345, file.RawSampleField );
		CollectionAssert.AreEqual( frames, file.SoundData );
		CollectionAssert.AreEqual( frames, file.FrameData );
		Assert.AreEqual( 40 + frames.Length, file.Data.Length );
	}

	[TestMethod]
	public void TruncatedNamesAreKeptAsStoredAndFoundByExactName()
	{
		var bank = CreateBank( ("woodgearsmax2B.", 22050, 36, 0, new byte[] { 1 }), ("abc.mp", 22050, 36, 0, new byte[] { 2 }) );
		using var archive = new SdtArchive( new MemoryStream( bank ) );

		CollectionAssert.AreEqual( new[] { "woodgearsmax2B.", "abc.mp" }, archive.GetFiles( "" ) );
		Assert.AreEqual( "woodgearsmax2B.", Named( archive.GetFile( "woodgearsmax2B." ) ) );
		Assert.ThrowsException<FileNotFoundException>( () => archive.GetFile( "woodgearsmax2B" ) );
		Assert.ThrowsException<FileNotFoundException>( () => archive.GetFile( "abc" ) );
	}

	[TestMethod]
	public void ExactLookupDoesNotSelectAPrefixCollision()
	{
		// "abc.mp2" comes first, so a StartsWith lookup for "abc.mp" would return the wrong entry.
		var bank = CreateBank( ("abc.mp2", 22050, 36, 0, new byte[] { 1 }), ("abc.mp", 22050, 36, 0, new byte[] { 2 }) );
		using var archive = new SdtArchive( new MemoryStream( bank ) );

		Assert.AreEqual( "abc.mp", Named( archive.GetFile( "abc.mp" ) ) );
		Assert.AreEqual( "abc.mp2", Named( archive.GetFile( "ABC.MP2" ) ) );
		CollectionAssert.AreEqual( new byte[] { 2 }, archive.GetFile( "abc.mp" ).GetData()[40..] );
	}

	[TestMethod]
	public void SoundFileSharesTheBufferWithoutCopying()
	{
		var bytes = new byte[] { 1, 2, 3 };
		var sound = new SoundFile( bytes );
		Assert.AreSame( bytes, sound.buffer );
		sound.Dispose();
	}

	[TestMethod]
	[DoNotParallelize]
	public void ConstructorsReadTheSameSyntheticSdtEntry()
	{
		var frames = new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 };
		var bank = CreateBank( ("beep.mp2", 44100, 37, 0x12345, frames) );
		byte[] entry;
		using ( var archive = new SdtArchive( new MemoryStream( bank ) ) )
			entry = archive.GetFile( "beep.mp2" ).GetData();

		var fromBytes = new SoundFile( entry );

		var fragmented = new FragmentedSeekableStream( entry, 3 );
		var fromFragmented = new SoundFile( fragmented );
		AssertSameEntry( fromBytes, fromFragmented );
		Assert.ThrowsException<ObjectDisposedException>( () => fragmented.Position = 0 );

		var nonSeekable = new NonSeekableStream( entry );
		var fromNonSeekable = new SoundFile( nonSeekable );
		AssertSameEntry( fromBytes, fromNonSeekable );
		Assert.IsTrue( nonSeekable.IsDisposed );

		var positioned = new MemoryStream( entry );
		positioned.Position = 5;
		var fromPositioned = new SoundFile( positioned );
		CollectionAssert.AreEqual( entry[5..], fromPositioned.buffer );
		Assert.ThrowsException<ObjectDisposedException>( () => positioned.Position = 0 );

		var root = Path.Combine( Path.GetTempPath(), $"opentpw-sound-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
		var previousFileSystem = FileSystem;
		try
		{
			File.WriteAllBytes( Path.Combine( root, "bank.sdt" ), bank );
			FileSystem = new BaseFileSystem( root );
			FileSystem.RegisterArchiveHandler<SdtArchive>( ".sdt" );

			var fromPath = new SoundFile( "bank/beep.mp2" );
			AssertSameEntry( fromBytes, fromPath );
			fromPath.Dispose();
		}
		finally
		{
			FileSystem = previousFileSystem;
			Directory.Delete( root, true );
		}

		fromBytes.Dispose();
		fromFragmented.Dispose();
		fromNonSeekable.Dispose();
		fromPositioned.Dispose();
	}

	private static void AssertSameEntry( SoundFile expected, SoundFile actual )
	{
		CollectionAssert.AreEqual( expected.buffer, actual.buffer );

		using var expectedStream = new MemoryStream();
		using var actualStream = new MemoryStream();
		var expectedFile = expected.GetFile( expectedStream );
		var actualFile = actual.GetFile( actualStream );

		Assert.AreEqual( expectedFile.Header, actualFile.Header );
		Assert.AreEqual( expectedFile.Name, actualFile.Name );
		Assert.AreEqual( expectedFile.SampleRate, actualFile.SampleRate );
		Assert.AreEqual( expectedFile.BitsPerSample, actualFile.BitsPerSample );
		Assert.AreEqual( expectedFile.SoundType, actualFile.SoundType );
		Assert.AreEqual( expectedFile.RawSampleField, actualFile.RawSampleField );
		CollectionAssert.AreEqual( expectedFile.SoundData, actualFile.SoundData );
		CollectionAssert.AreEqual( expectedFile.FrameData, actualFile.FrameData );
		CollectionAssert.AreEqual( expectedFile.Data, actualFile.Data );
	}

	private static void AssertSameBank( SdtArchive expected, SdtArchive actual )
	{
		Assert.AreEqual( expected.soundFiles.Count, actual.soundFiles.Count );
		CollectionAssert.AreEqual( expected.SkippedEntries, actual.SkippedEntries );
		for ( var index = 0; index < expected.soundFiles.Count; index++ )
		{
			var expectedFile = expected.soundFiles[index];
			var actualFile = actual.soundFiles[index];
			Assert.AreEqual( expectedFile.Header, actualFile.Header );
			Assert.AreEqual( expectedFile.Name, actualFile.Name );
			Assert.AreEqual( expectedFile.SampleRate, actualFile.SampleRate );
			Assert.AreEqual( expectedFile.Channels, actualFile.Channels );
			Assert.AreEqual( expectedFile.BitsPerSample, actualFile.BitsPerSample );
			Assert.AreEqual( expectedFile.SoundType, actualFile.SoundType );
			Assert.AreEqual( expectedFile.RawSampleField, actualFile.RawSampleField );
			CollectionAssert.AreEqual( expectedFile.SoundData, actualFile.SoundData );
			CollectionAssert.AreEqual( expectedFile.FrameData, actualFile.FrameData );
			CollectionAssert.AreEqual( expectedFile.Data, actualFile.Data );
		}
	}

	private sealed class FragmentedSeekableStream : Stream
	{
		private readonly MemoryStream inner;
		private readonly int maximumRead;

		public FragmentedSeekableStream( byte[] bytes, int maximumRead )
		{
			inner = new MemoryStream( bytes, false );
			this.maximumRead = maximumRead;
		}

		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => inner.Length;
		public override long Position
		{
			get => inner.Position;
			set => inner.Position = value;
		}

		public override int Read( byte[] buffer, int offset, int count ) =>
			inner.Read( buffer, offset, Math.Min( count, maximumRead ) );

		public override int Read( Span<byte> buffer ) =>
			inner.Read( buffer[..Math.Min( buffer.Length, maximumRead )] );

		public override long Seek( long offset, SeekOrigin origin ) => inner.Seek( offset, origin );
		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			if ( disposing )
				inner.Dispose();
			base.Dispose( disposing );
		}
	}

	private sealed class NonSeekableStream : Stream
	{
		private readonly MemoryStream inner;

		public bool IsDisposed { get; private set; }

		public NonSeekableStream( byte[] bytes )
		{
			inner = new MemoryStream( bytes, false );
		}

		public override bool CanRead => !IsDisposed;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => inner.Read( buffer );
		public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			IsDisposed = true;
			if ( disposing )
				inner.Dispose();
			base.Dispose( disposing );
		}
	}

	private sealed class TruncatedSeekableStream : Stream
	{
		private readonly MemoryStream inner;
		private readonly long reportedLength;

		public bool IsDisposed { get; private set; }

		public TruncatedSeekableStream( byte[] bytes, long reportedLength )
		{
			inner = new MemoryStream( bytes, false );
			this.reportedLength = reportedLength;
		}

		public override bool CanRead => !IsDisposed;
		public override bool CanSeek => !IsDisposed;
		public override bool CanWrite => false;
		public override long Length => reportedLength;
		public override long Position
		{
			get => inner.Position;
			set => inner.Position = value;
		}

		public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => inner.Read( buffer );
		public override long Seek( long offset, SeekOrigin origin ) => inner.Seek( offset, origin );
		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			IsDisposed = true;
			if ( disposing )
				inner.Dispose();
			base.Dispose( disposing );
		}
	}

	[TestMethod]
	public void DisposeReleasesTheArchive()
	{
		var archive = new SdtArchive( new MemoryStream( CreateBank( ("a.mp2", 22050, 36, 0, new byte[] { 0 }) ) ) );
		archive.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => archive.GetData( 0, 4 ) );
	}

	[TestMethod]
	public void NullBankStreamKeepsItsArgumentExceptionContract()
	{
		Assert.ThrowsException<ArgumentNullException>( () => new SdtArchive( (Stream)null! ) );
	}

	[TestMethod]
	public void FragmentedSeekableBankEqualsTheCompleteBank()
	{
		var bank = CreateBank(
			("first.mp2", 44100, 37, 0x12345, new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 }),
			("second.mp", 22050, 36, 7, new byte[] { 1 }));

		using var expected = new SdtArchive( new MemoryStream( bank ) );
		using var actual = new SdtArchive( new FragmentedSeekableStream( bank, 3 ) );
		Assert.AreEqual( 2, expected.soundFiles.Count );

		AssertSameBank( expected, actual );
	}

	[TestMethod]
	public void BankAtCurrentPositionIsReadFromThatPositionAndDisposesTheSource()
	{
		var bank = CreateBank( ("beep.mp2", 44100, 37, 0x12345, new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 }) );
		var prefixed = new byte[bank.Length + 5];
		prefixed.AsSpan( 0, 5 ).Fill( 0xAA );
		bank.CopyTo( prefixed, 5 );

		var source = new MemoryStream( prefixed ) { Position = 5 };
		using var expected = new SdtArchive( new MemoryStream( bank ) );
		using var actual = new SdtArchive( source );

		AssertSameBank( expected, actual );
		Assert.ThrowsException<ObjectDisposedException>( () => source.Position = 0 );
	}

	[TestMethod]
	public void PrematureEndOfStreamThrowsAndDisposesTheSource()
	{
		var bank = CreateBank( ("beep.mp2", 44100, 37, 0x12345, new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 }) );
		var source = new TruncatedSeekableStream( bank[..^1], bank.Length );

		Assert.ThrowsException<EndOfStreamException>( () => new SdtArchive( source ) );
		Assert.IsTrue( source.IsDisposed );
	}

	[TestMethod]
	public void PathAndStreamConstructorsProduceTheSameBank()
	{
		var bank = CreateBank(
			("first.mp2", 44100, 37, 0x12345, new byte[] { 0xFF, 0xF5, 0x62, 0xC0, 1, 2, 3, 0 }),
			("second.mp", 22050, 36, 7, new byte[] { 1 }));
		var root = Directory.CreateTempSubdirectory( "opentpw-sdt-bank-" ).FullName;
		try
		{
			var path = Path.Combine( root, "bank.sdt" );
			File.WriteAllBytes( path, bank );
			using var fromPath = new SdtArchive( path );
			using var fromStream = new SdtArchive( new MemoryStream( bank ) );
			Assert.AreEqual( 2, fromStream.soundFiles.Count );
			AssertSameBank( fromStream, fromPath );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	/// <summary>Locates the installed game data through OPENTPW_GAME_PATH; inconclusive when unset.</summary>
	private static string InstalledGamePath()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to an installed copy of Theme Park World." );
		return gamePath!;
	}

	/// <summary>Reads an MPEG audio frame header (ISO/IEC 11172-3 / 13818-3) at <paramref name="offset"/>.</summary>
	private static (int Version, int Layer, int Mode, int SampleRate) ReadFrameHeader( byte[] data, int offset )
	{
		var word = (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
		Assert.AreEqual( 0xFFE00000u, word & 0xFFE00000u, "MPEG sync word" );
		var version = (int)(word >> 19) & 3;
		var layer = (int)(word >> 17) & 3;
		var rateIndex = (int)(word >> 10) & 3;
		var mode = (int)(word >> 6) & 3;
		Assert.AreNotEqual( 1, version, "reserved MPEG version" );
		Assert.AreNotEqual( 3, rateIndex, "reserved sample rate index" );
		var rates = version == 3 ? new[] { 44100, 48000, 32000 } : version == 2 ? new[] { 22050, 24000, 16000 } : new[] { 11025, 12000, 8000 };
		return (version, layer, mode, rates[rateIndex]);
	}

	[TestMethod]
	public void InstalledBanksMatchTheProvenEntryHeader()
	{
		var root = InstalledGamePath();
		var banks = Directory.EnumerateFiles( root, "*.sdt", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive } ).OrderBy( path => path, StringComparer.Ordinal ).ToArray();
		Assert.IsTrue( banks.Length > 0, "No SDT banks found under OPENTPW_GAME_PATH." );

		// Truncated 16-byte names collide in these banks; any other duplicate is a regression.
		var expectedDuplicates = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
		{
			["Data/global/sound/UIHD.sdt"] = "tp_balloon_pop_ x2",
			["Data/levels/jungle/Sound/AmbientHD.sdt"] = "tp strange deep x2; tp strangely de x2",
		};
		var actualDuplicates = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var entries = 0;

		foreach ( var bank in banks )
		{
			using var archive = new SdtArchive( bank );
			var relative = Path.GetRelativePath( root, bank ).Replace( '\\', '/' );
			Assert.AreEqual( 0, archive.SkippedEntries.Count, $"{relative}: {string.Join( "; ", archive.SkippedEntries )}" );

			var duplicates = archive.soundFiles.GroupBy( file => file.Name.ToLowerInvariant() ).Where( group => group.Count() > 1 )
				.OrderBy( group => group.Key, StringComparer.Ordinal ).Select( group => $"{group.Key} x{group.Count()}" ).ToArray();
			if ( duplicates.Length > 0 )
				actualDuplicates[relative] = string.Join( "; ", duplicates );

			foreach ( var file in archive.soundFiles )
			{
				entries++;
				var where = $"{relative} '{file.Name}'";
				Assert.AreEqual( 40, file.Header, where );
				Assert.AreEqual( 16, file.BitsPerSample, where );
				Assert.AreEqual( 0, (int)file.Data[^1], $"{where}: trailing byte is not zero" );
				Assert.AreEqual( file.Header + file.SoundData.Length, file.Data.Length, where );

				var frame = ReadFrameHeader( file.Data, file.Header );
				Assert.AreEqual( frame.SampleRate, file.SampleRate, $"{where}: header rate versus frame rate" );
				var expectedType = frame.Mode == 3 ? MP2File.SoundTypes.MP2_MONO : MP2File.SoundTypes.MP2_STEREO;
				Assert.AreEqual( expectedType, file.SoundType, $"{where}: sound type versus channel mode {frame.Mode}" );
			}
		}

		Assert.IsTrue( entries > 0 );
		Assert.AreEqual( string.Join( " | ", expectedDuplicates.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key}: {pair.Value}" ) ),
			string.Join( " | ", actualDuplicates.OrderBy( pair => pair.Key ).Select( pair => $"{pair.Key}: {pair.Value}" ) ) );
	}
}
