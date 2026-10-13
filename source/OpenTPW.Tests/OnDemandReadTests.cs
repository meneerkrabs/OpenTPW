using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Archives and sound banks read from their files on demand give the same bytes as the same files held in memory: every WAD member
/// and SDT entry of the original game folder, and damaged banks.
/// </summary>
[TestClass]
public class OnDemandReadTests
{
	private static string? Game()
	{
		var game = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		return string.IsNullOrEmpty( game ) || !Directory.Exists( game ) ? null : game;
	}

	private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive };

	private static IEnumerable<WadArchiveFile> Members( ArchiveDirectory directory ) =>
		directory.Children.OfType<WadArchiveFile>().Concat( directory.Children.OfType<ArchiveDirectory>().SelectMany( Members ) );

	[TestMethod]
	public void ArchivesFromAPathReadTheSameMembersAsArchivesInMemory()
	{
		var game = Game();
		if ( game == null )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game to compare the archives." );
			return;
		}
		var members = 0;
		foreach ( var path in Directory.EnumerateFiles( game, "*.wad", Recursive ) )
		{
			using var fromPath = new WadArchive( path );
			using var stream = File.OpenRead( path );
			using var inMemory = new WadArchive( stream );
			var a = Members( fromPath.Root ).ToList();
			var b = Members( inMemory.Root ).ToList();
			CollectionAssert.AreEqual( b.Select( file => file.Name ).ToList(), a.Select( file => file.Name ).ToList(), path );
			for ( var index = 0; index < a.Count; index++ )
			{
				CollectionAssert.AreEqual( inMemory.GetData( b[index].OffsetInArchive, b[index].SizeInArchive ), fromPath.GetData( a[index].OffsetInArchive, a[index].SizeInArchive ), $"{path}: {a[index].Name} raw" );
				CollectionAssert.AreEqual( b[index].GetData(), a[index].GetData(), $"{path}: {a[index].Name}" );
				members++;
			}
		}
		Assert.IsTrue( members > 1000, $"only {members} members compared" );
	}

	/// <summary>A one-member WAD (the layout of PortableFileSystemTests' synthetic archive).</summary>
	private static void WriteWad( string path, string contents )
	{
		var filename = System.Text.Encoding.ASCII.GetBytes( "file.txt\0" );
		var bytes = System.Text.Encoding.ASCII.GetBytes( contents );
		using var writer = new BinaryWriter( File.Create( path ) );
		writer.Write( System.Text.Encoding.ASCII.GetBytes( "DWFB" ) );
		writer.Write( 1 );
		writer.Write( new byte[64] );
		writer.Write( 1 );
		writer.Write( 88 );
		writer.Write( 40 );
		writer.Write( 0 );
		writer.Write( 0 );
		writer.Write( 128 );
		writer.Write( filename.Length );
		writer.Write( 128 + filename.Length );
		writer.Write( bytes.Length );
		writer.Write( 0 );
		writer.Write( bytes.Length );
		writer.Write( new byte[12] );
		writer.Write( filename );
		writer.Write( bytes );
	}

	[TestMethod]
	public void AnArchiveFromAPathLeavesItsFileFreeAndRefusesBadReads()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-wad-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "a.wad" );
			WriteWad( path, "first contents" );
			var archive = new WadArchive( path );
			var member = Members( archive.Root ).Single();
			Assert.AreEqual( "first contents", System.Text.Encoding.ASCII.GetString( member.GetData() ) );

			// No handle stays open between reads: the file can be replaced (a bonus import does that, also on Windows).
			File.Move( path, path + ".old" );
			File.Move( path + ".old", path );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => archive.GetData( -1, 4 ) );

			// A file of another length is not the archive whose directory was read: a clear error, not other bytes.
			WriteWad( path, "replaced with longer contents" );
			Assert.ThrowsException<IOException>( () => archive.GetData( member.OffsetInArchive, member.SizeInArchive ) );

			archive.Dispose();
			Assert.ThrowsException<ObjectDisposedException>( () => archive.GetData( 0, 4 ) );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}

	[TestMethod]
	public void BanksFromAFileReadTheSameEntriesAsBanksInMemory()
	{
		var game = Game();
		if ( game == null )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game to compare the sound banks." );
			return;
		}
		var entries = 0;
		foreach ( var path in Directory.EnumerateFiles( game, "*.sdt", Recursive ) )
		{
			var inMemory = new SoundBank( File.ReadAllBytes( path ) );
			var fromFile = new SoundBank( () => File.OpenRead( path ), new FileInfo( path ).Length );
			Assert.AreEqual( inMemory.Count, fromFile.Count, path );
			for ( uint number = 0; number <= inMemory.Count + 1; number++ )
			{
				CollectionAssert.AreEqual( inMemory.GetMpeg( number ), fromFile.GetMpeg( number ), $"{path}: entry {number}" );
				Assert.AreEqual( inMemory.GetName( number ), fromFile.GetName( number ), $"{path}: name {number}" );
				entries++;
			}
		}
		Assert.IsTrue( entries > 100, $"only {entries} entries compared" );
	}

	[TestMethod]
	public void DamagedBanksBehaveTheSameFromAFile()
	{
		static byte[] Bank( int declared, params int[] offsets )
		{
			var bytes = new byte[200];
			BinaryPrimitives.WriteInt32LittleEndian( bytes, declared );
			for ( var index = 0; index < offsets.Length; index++ )
				BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 4 + index * 4 ), offsets[index] );
			// An entry at 40: header 40 bytes, 16-byte name, size 30 (fits); one at 120: size 100 (runs past the end).
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 40 ), 40 );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 44 ), 30 );
			"FIRST"u8.CopyTo( bytes.AsSpan( 48 ) );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 120 ), 40 );
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 124 ), 100 );
			return bytes;
		}
		foreach ( var bytes in new[] { Bank( 3, 40, 120, 5 ), Bank( 1000, 40 ), Bank( -4 ), new byte[2], Array.Empty<byte>(), Bank( 2, 40, 190 ) } )
		{
			var inMemory = new SoundBank( bytes );
			var fromFile = new SoundBank( () => new MemoryStream( bytes ), bytes.Length );
			Assert.AreEqual( inMemory.Count, fromFile.Count );
			for ( uint number = 0; number <= 4; number++ )
			{
				CollectionAssert.AreEqual( inMemory.GetMpeg( number ), fromFile.GetMpeg( number ), $"entry {number}" );
				Assert.AreEqual( inMemory.GetName( number ), fromFile.GetName( number ), $"name {number}" );
			}
		}

		// A bank whose file was replaced by one of another length reports that instead of reading other bytes.
		var current = Bank( 3, 40, 120, 5 );
		var replaced = new SoundBank( () => new MemoryStream( current ), current.Length );
		current = new byte[300];
		Assert.ThrowsException<IOException>( () => replaced.GetMpeg( 1 ) );
	}
}
