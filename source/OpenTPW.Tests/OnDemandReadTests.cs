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
	internal static void WriteWad( string path, string contents )
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

			// The kept file shares deleting: it can be moved while the archive is open, also on Windows. Wherever archives may change (a bonus
			// import), the kept files are closed first; then the file can be replaced, and the next read opens it again.
			File.Move( path, path + ".old" );
			File.Move( path + ".old", path );
			WadArchive.CloseFileHandles();
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

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChangedReopenedLengthIsRefusedBeforeAllocatingTheRequestedPayload( bool grow )
	{
		const int requested = 16 * 1024 * 1024;
		var directory = Directory.CreateTempSubdirectory( "opentpw-wad-preflight-" ).FullName;
		WadArchiveFile? member = null;
		try
		{
			var path = Path.Combine( directory, "a.wad" );
			WriteWad( path, "original member" );
			var original = File.ReadAllBytes( path );
			using var archive = new WadArchive( path );
			member = Members( archive.Root ).Single();
			var cached = member.GetData();
			using ( var changed = new FileStream( path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete ) )
				changed.SetLength( grow ? original.Length + 1 : original.Length - 1 );
			// Warm the existing refusal/exception path without a large payload.
			Assert.ThrowsException<IOException>( () => archive.GetData( 0, 1 ) );
			var allocated = GC.GetAllocatedBytesForCurrentThread();
			Assert.ThrowsException<IOException>( () => archive.GetData( 0, requested ) );
			allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
			Console.WriteLine( $"Changed-source refusal: {allocated} bytes allocated for a {requested}-byte request (grow={grow})." );
			Assert.IsTrue( allocated < requested / 2, $"Changed-source refusal allocated {allocated} bytes for a {requested}-byte request." );
			File.WriteAllBytes( path, original );
			CollectionAssert.AreEqual( original.Take( 32 ).ToArray(), archive.GetData( 0, 32 ), "A refused read leaves the handle closed and can recover after the original bytes are restored." );
			archive.Dispose();
			Assert.AreSame( cached, member.GetData(), "Already cached member data remains available after archive disposal." );
		}
		finally { member?.Free(); Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void UnchangedPathSourceStillAllowsLargeZeroPaddedDirectReads()
	{
		const int requested = 16 * 1024 * 1024;
		var directory = Directory.CreateTempSubdirectory( "opentpw-wad-padding-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "a.wad" );
			WriteWad( path, "small member" );
			var original = File.ReadAllBytes( path );
			using var archive = new WadArchive( path );
			var data = archive.GetData( 0, requested );
			Assert.AreEqual( requested, data.Length, "No arbitrary request-size cap is introduced." );
			CollectionAssert.AreEqual( original, data.Take( original.Length ).ToArray() );
			Assert.AreEqual( -1, data.AsSpan( original.Length ).IndexOfAnyExcept( (byte)0 ), "An unchanged short source retains zero padding." );
		}
		finally { Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void ZeroLengthAndBeyondOriginalEndStillDoNotReopenThePath()
	{
		var directory = Directory.CreateTempSubdirectory( "opentpw-wad-shortcuts-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "a.wad" );
			WriteWad( path, "small member" );
			var size = new FileInfo( path ).Length;
			using var archive = new WadArchive( path );
			File.Delete( path );
			Assert.AreEqual( 0, archive.GetData( 0, 0 ).Length );
			CollectionAssert.AreEqual( new byte[8], archive.GetData( checked((int)size), 8 ) );
			CollectionAssert.AreEqual( new byte[8], archive.GetData( int.MaxValue, 8 ), "Wide beyond-end offsets keep their existing padding behavior." );
			// the kept file (issue #754) still reads the deleted file; once it is closed, a request that needs original bytes opens the path again
			WadArchive.CloseFileHandles();
			Assert.ThrowsException<FileNotFoundException>( () => archive.GetData( 0, 1 ), "A request that needs original bytes still reopens the source." );
		}
		finally { Directory.Delete( directory, recursive: true ); }
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
