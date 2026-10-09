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
	private static byte[] CreateBank( params (string Name, ushort SampleRate, byte SoundType, uint RawSampleField, byte[] Frames)[] entries )
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
	public void DisposeReleasesTheArchive()
	{
		var archive = new SdtArchive( new MemoryStream( CreateBank( ("a.mp2", 22050, 36, 0, new byte[] { 0 }) ) ) );
		archive.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => archive.GetData( 0, 4 ) );
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
