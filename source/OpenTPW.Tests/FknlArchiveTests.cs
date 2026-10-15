using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>PS2 FKNL archives: synthetic layout, bounds and RefPack members; private corpus behind OPENTPW_PS2_DATA.</summary>
[TestClass]
public class FknlArchiveTests
{
	/// <summary>Minimal FKNL writer: one root with files and nested directories, names in a pool, data after it.</summary>
	private sealed class Builder
	{
		private readonly List<byte> records = new();
		private readonly List<byte> names = new();
		private readonly List<byte> payload = new();
		private readonly List<(int At, Func<int> Value)> fixups = new();

		public sealed record Dir( List<(string Name, byte[] Stored, int Size)> Files, List<(string Name, Dir Child)> Dirs );

		public byte[] Build( Dir root )
		{
			var headerSize = 24;
			int Record( Dir dir )
			{
				var at = headerSize + records.Count;
				records.AddRange( new byte[16] );
				var filesAt = headerSize + records.Count;
				foreach ( var (name, stored, size) in dir.Files )
				{
					var entry = records.Count;
					records.AddRange( new byte[16] );
					var nameAt = names.Count;
					names.AddRange( Encoding.Latin1.GetBytes( name ) );
					names.Add( 0 );
					var dataAt = payload.Count;
					payload.AddRange( stored );
					fixups.Add( (entry, () => NamesStart + nameAt) );
					fixups.Add( (entry + 4, () => DataStart + dataAt) );
					Put( entry + 8, stored.Length );
					Put( entry + 12, size );
				}
				var dirsAt = headerSize + records.Count;
				var dirEntries = new List<(int Entry, Dir Child, int NameAt)>();
				foreach ( var (name, child) in dir.Dirs )
				{
					var entry = records.Count;
					records.AddRange( new byte[8] );
					var nameAt = names.Count;
					names.AddRange( Encoding.Latin1.GetBytes( name ) );
					names.Add( 0 );
					dirEntries.Add( (entry, child, nameAt) );
				}
				Put( at - headerSize, filesAt );
				Put( at - headerSize + 4, dirsAt );
				Put( at - headerSize + 8, dir.Files.Count );
				Put( at - headerSize + 12, dir.Dirs.Count );
				foreach ( var (entry, child, nameAt) in dirEntries )
				{
					fixups.Add( (entry, () => NamesStart + nameAt) );
					Put( entry + 4, Record( child ) );
				}
				return at;
			}
			var rootAt = Record( root );
			NamesStart = headerSize + records.Count;
			DataStart = NamesStart + names.Count;
			foreach ( var (at, value) in fixups )
				Put( at, value() );
			var file = new List<byte>();
			file.AddRange( Encoding.ASCII.GetBytes( "FKNL" ) );
			file.AddRange( BitConverter.GetBytes( 0 ) );
			file.AddRange( BitConverter.GetBytes( DataStart ) );
			file.AddRange( BitConverter.GetBytes( 0 ) );
			file.AddRange( BitConverter.GetBytes( NamesStart ) );
			file.AddRange( BitConverter.GetBytes( rootAt ) );
			file.AddRange( records );
			file.AddRange( names );
			file.AddRange( payload );
			return file.ToArray();
		}

		private int NamesStart { get; set; }
		private int DataStart { get; set; }

		private void Put( int at, int value )
		{
			var bytes = BitConverter.GetBytes( value );
			for ( var i = 0; i < 4; i++ )
				records[at + i] = bytes[i];
		}
	}

	/// <summary>RefPack for "abc" repeated to 13 bytes: three literals plus a 10-byte copy at distance 3, then the end code.</summary>
	private static readonly byte[] RefpackAbc = { 0x10, 0xFB, 0x00, 0x00, 0x0D, 0x1F, 0x02, (byte)'a', (byte)'b', (byte)'c', 0xFC };

	private static byte[] Sample()
	{
		var text = Encoding.ASCII.GetBytes( "abcabcabcabca" );
		var root = new Builder.Dir(
			new() { ("readme.txt", Encoding.ASCII.GetBytes( "stored" ), 6) },
			new()
			{
				("Rides", new Builder.Dir( new(), new() { ("dizzyd", new Builder.Dir( new() { ("dizzyd.RSE", RefpackAbc, text.Length) }, new() )) } )),
				("Sky", new Builder.Dir( new() { ("sky.ssh", new byte[] { 1, 2, 3 }, 3) }, new() ))
			} );
		return new Builder().Build( root );
	}

	[TestMethod]
	public void ReadsTheDirectoryTreeAndStoredAndCompressedMembers()
	{
		using var archive = new FknlArchive( new MemoryStream( Sample() ) );
		Assert.AreEqual( 3, archive.FileCount );
		CollectionAssert.AreEquivalent( new[] { "readme.txt", "Rides/dizzyd/dizzyd.RSE", "Sky/sky.ssh" }, archive.EnumerateFiles().ToArray() );
		CollectionAssert.AreEqual( new[] { "Rides", "Sky" }, archive.GetDirectories( "" ) );
		Assert.AreEqual( "stored", Encoding.ASCII.GetString( archive.GetFile( "readme.txt" ).GetData() ) );
		Assert.AreEqual( "abcabcabcabca", Encoding.ASCII.GetString( archive.GetFile( "rides\\DIZZYD/dizzyd.rse" ).GetData() ), "case-insensitive, either separator" );
		Assert.AreEqual( 13, archive.GetFileSize( "Rides/dizzyd/dizzyd.RSE" ) );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, archive.GetFile( "Sky/sky.ssh" ).GetData() );
		Assert.ThrowsException<FileNotFoundException>( () => archive.GetFile( "Rides/missing.RSE" ) );
		Assert.AreEqual( 0, archive.GetFiles( "Nowhere" ).Length );
	}

	[TestMethod]
	public void RejectsForeignAndCorruptArchives()
	{
		Assert.ThrowsException<InvalidDataException>( () => new FknlArchive( new MemoryStream( Encoding.ASCII.GetBytes( "DWFB" + new string( '\0', 40 ) ) ) ) );
		var sample = Sample();
		var truncated = sample.Take( sample.Length - 10 ).ToArray();
		Assert.ThrowsException<InvalidDataException>( () => new FknlArchive( new MemoryStream( truncated ) ), "member data past the end" );
		var badRoot = (byte[])sample.Clone();
		BitConverter.GetBytes( sample.Length + 100 ).CopyTo( badRoot, 20 );
		Assert.ThrowsException<InvalidDataException>( () => new FknlArchive( new MemoryStream( badRoot ) ) );
		var cycle = (byte[])sample.Clone();
		// Point the first subdirectory entry back at the root record.
		var root = BitConverter.ToInt32( sample, 20 );
		var dirsAt = BitConverter.ToInt32( sample, root + 4 );
		BitConverter.GetBytes( root ).CopyTo( cycle, dirsAt + 4 );
		Assert.ThrowsException<InvalidDataException>( () => new FknlArchive( new MemoryStream( cycle ) ) );
	}

	[TestMethod]
	public void SshContainerReadsRecordsNamesAndRecognisesGmPayloads()
	{
		var data = new List<byte>();
		data.AddRange( Encoding.ASCII.GetBytes( "SHPS" ) );
		data.AddRange( BitConverter.GetBytes( 0 ) );
		data.AddRange( BitConverter.GetBytes( 1 ) );
		data.AddRange( Encoding.ASCII.GetBytes( "GIMX" ) );
		data.AddRange( Encoding.ASCII.GetBytes( "jj_b" ) );
		data.AddRange( BitConverter.GetBytes( 24 ) );
		// Image record at 24: type 0x84, next block 16 + 8 bytes later, 64x64, then a 'GM' payload.
		data.AddRange( new byte[] { 0x84, 24, 0, 0, 64, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, 0 } );
		data.AddRange( new byte[] { (byte)'G', (byte)'M', 4, 4, 8, 0, 0x80, 0 } );
		// Name block 0x70, last.
		data.AddRange( new byte[] { 0x70, 0, 0, 0 } );
		data.AddRange( Encoding.ASCII.GetBytes( "jj_base1\0" ) );
		var ssh = new SshFile( data.ToArray() );
		Assert.AreEqual( "GIMX", ssh.Id );
		var image = ssh.Images.Single();
		Assert.AreEqual( ("jj_b", 0x84, 64, 64, "jj_base1", 8, true, 4, 4), (image.Tag, image.Type, image.Width, image.Height, image.Name, image.PayloadSize, image.IsGmCompressed, image.GmBlocksX, image.GmBlocksY) );
		Assert.ThrowsException<InvalidDataException>( () => new SshFile( Encoding.ASCII.GetBytes( "FSH " + new string( '\0', 20 ) ) ) );
	}

	internal static string? Ps2DataDirectory()
	{
		var root = Environment.GetEnvironmentVariable( "OPENTPW_PS2_DATA" );
		if ( string.IsNullOrEmpty( root ) )
			return null;
		var nested = Path.Combine( root, "DATA" );
		return Directory.Exists( nested ) ? nested : root;
	}

	[TestMethod]
	public void OriginalPs2ArchivesParseAndEveryMemberDecompresses()
	{
		var directory = Ps2DataDirectory();
		if ( directory == null )
			Assert.Inconclusive( "Set OPENTPW_PS2_DATA to the PS2 disc's DATA directory (read in place)." );
		// Observed on the PAL disc SLES-50032.
		var expected = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase )
		{
			["DATA.WAD"] = 1737, ["FANTASY.WAD"] = 2184, ["FRONTEND.WAD"] = 385, ["FRSE.WAD"] = 84, ["HALLOW.WAD"] = 2627, ["HRSE.WAD"] = 96,
			["ICONS.WAD"] = 4, ["JRSE.WAD"] = 91, ["JUNGLE.WAD"] = 2545, ["LIPS.WAD"] = 516, ["LOBBY.WAD"] = 1034, ["MENUS.WAD"] = 30,
			["PARTICLE.WAD"] = 217, ["SPACE.WAD"] = 2858, ["SRSE.WAD"] = 88, ["UI.WAD"] = 182
		};
		var placeholders = new List<string>();
		foreach ( var (name, count) in expected )
		{
			using var archive = new FknlArchive( Path.Combine( directory!, name ) );
			Assert.AreEqual( count, archive.FileCount, name );
			foreach ( var path in archive.EnumerateFiles() )
			{
				var file = (FknlArchiveFile)archive.GetFile( path );
				if ( file.IsEmptyPlaceholder )
				{
					placeholders.Add( $"{name}:{path}" );
					continue;
				}
				Assert.AreEqual( file.Size, file.GetData().Length, $"{name}:{path}" );
			}
		}
		CollectionAssert.AreEquivalent( new[] { "DATA.WAD:Text/translations/eur/final.dup", "DATA.WAD:Text/translations/eur/fre.dup", "DATA.WAD:Text/translations/eur/ger.dup" }, placeholders );
		using var jungle = new FknlArchive( Path.Combine( directory!, "JUNGLE.WAD" ) );
		var ssh = new SshFile( jungle.GetFile( "Features/1x1East/textures/jj_base1.ssh" ).GetData() );
		Assert.AreEqual( (64, 64, true, "jj_base1"), (ssh.Images[0].Width, ssh.Images[0].Height, ssh.Images[0].IsGmCompressed, ssh.Images[0].Name) );
		CollectionAssert.IsSubsetOf( new[] { "Rides/dizzyd/dizzyd.mps", "Rides/dizzyd/dizzyd.aps", "terrain/terrain_1.mps", "terrain/terrain_2.mps" }, jungle.EnumerateFiles().ToArray() );
	}
}
