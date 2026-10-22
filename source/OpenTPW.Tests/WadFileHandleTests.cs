using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// An archive opened from a path keeps its file open between member reads instead of opening it for each read (issue #754), at most
/// <see cref="WadArchive.MaxOpenHandles"/> files at a time, closed by <see cref="WadArchive.CloseFileHandles"/>; the bytes read are the same.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WadFileHandleTests
{
	private string directory = null!;
	private int previousMax;

	[TestInitialize]
	public void Setup()
	{
		directory = Directory.CreateTempSubdirectory( "opentpw-wad-handles-" ).FullName;
		previousMax = WadArchive.MaxOpenHandles;
		WadArchive.CloseFileHandles();
	}

	[TestCleanup]
	public void Cleanup()
	{
		WadArchive.MaxOpenHandles = previousMax;
		WadArchive.CloseFileHandles();
		LoadProfile.Enable( false );
		Directory.Delete( directory, recursive: true );
	}

	private string Wad( string name, string contents )
	{
		var path = Path.Combine( directory, name );
		OnDemandReadTests.WriteWad( path, contents );
		return path;
	}

	private static string Read( WadArchive archive )
	{
		var member = archive.Root.Children.OfType<WadArchiveFile>().Single();
		return Encoding.ASCII.GetString( archive.GetData( member.OffsetInArchive, member.SizeInArchive ) );
	}

	private static long FileOpens() => LoadProfile.Counters().GetValueOrDefault( "file opens" );

	[TestMethod]
	public void TheFileTheDirectoryWasReadFromServesEveryMemberRead()
	{
		var path = Wad( "a.wad", "kept contents" );
		LoadProfile.Enable( true );
		using var archive = new WadArchive( path );
		for ( var read = 0; read < 10; read++ )
			Assert.AreEqual( "kept contents", Read( archive ) );
		Assert.AreEqual( 1L, FileOpens(), "one open for the directory and ten reads" );
		Assert.AreEqual( 10L, LoadProfile.Counters().GetValueOrDefault( "wad member reads" ) );
		Assert.AreEqual( 1, WadArchive.OpenHandleCount );

		archive.Dispose();
		Assert.AreEqual( 0, WadArchive.OpenHandleCount, "a disposed archive closes its file" );
	}

	[TestMethod]
	public void ClosedFilesAreOpenedAgainOnTheNextRead()
	{
		var path = Wad( "a.wad", "reopened" );
		LoadProfile.Enable( true );
		using var archive = new WadArchive( path );
		WadArchive.CloseFileHandles();
		Assert.AreEqual( 0, WadArchive.OpenHandleCount );
		// closed, the file can be replaced by another of the same length, also on Windows; a read opens the file now at the path
		OnDemandReadTests.WriteWad( path, "Reopened" );
		Assert.AreEqual( "Reopened", Read( archive ) );
		Assert.AreEqual( "Reopened", Read( archive ) );
		Assert.AreEqual( 2L, FileOpens(), "the directory's open and one after the close" );
	}

	[TestMethod]
	public void AtMostTheBoundStaysOpenTheLeastRecentlyReadClosingFirst()
	{
		WadArchive.MaxOpenHandles = 2;
		var archives = Enumerable.Range( 0, 5 ).Select( index => new WadArchive( Wad( $"{index}.wad", $"archive {index}" ) ) ).ToList();
		try
		{
			Assert.AreEqual( 2, WadArchive.OpenHandleCount );
			LoadProfile.Enable( true );
			// 3 and 4 are open: reading them opens nothing
			Assert.AreEqual( "archive 3", Read( archives[3] ) );
			Assert.AreEqual( "archive 4", Read( archives[4] ) );
			Assert.AreEqual( 0L, FileOpens() );
			// 0 opens again and closes 3, the least recently read
			Assert.AreEqual( "archive 0", Read( archives[0] ) );
			Assert.AreEqual( 1L, FileOpens() );
			Assert.AreEqual( "archive 4", Read( archives[4] ) );
			Assert.AreEqual( 1L, FileOpens() );
			Assert.AreEqual( "archive 3", Read( archives[3] ) );
			Assert.AreEqual( 2L, FileOpens() );
			Assert.AreEqual( 2, WadArchive.OpenHandleCount );

			WadArchive.MaxOpenHandles = 0;
			Assert.AreEqual( "archive 1", Read( archives[1] ) );
			Assert.AreEqual( 0, WadArchive.OpenHandleCount, "with no kept files every read opens and closes the file, as before" );
		}
		finally
		{
			foreach ( var archive in archives )
				archive.Dispose();
		}
	}

	[TestMethod]
	public void TheKeptFileLetsOtherReadersInAndCanBeMovedOrDeleted()
	{
		var path = Wad( "a.wad", "shared" );
		using var archive = new WadArchive( path );
		Assert.AreEqual( 1, WadArchive.OpenHandleCount );
		// another reader, as File.OpenRead opens (sharing reading only): the kept file only reads, so it is let in
		using ( var reader = File.OpenRead( path ) )
			Assert.IsTrue( reader.Length > 0 );
		// moved away and deleted while open (the kept file shares deleting): the archive still reads the file its directory came from
		File.Move( path, path + ".moved" );
		File.Delete( path + ".moved" );
		Assert.IsFalse( File.Exists( path + ".moved" ) );
		Assert.AreEqual( "shared", Read( archive ) );
		WadArchive.CloseFileHandles();
		Assert.ThrowsException<FileNotFoundException>( () => Read( archive ), "closed, the next read opens the path, where no file is now" );
	}

	[TestMethod]
	public void AFailedReadLeavesNoFileOpen()
	{
		WadArchive.MaxOpenHandles = 0;
		using var archive = new WadArchive( Wad( "a.wad", "failed read" ) );
		Assert.AreEqual( 0, WadArchive.OpenHandleCount );
		// more bytes than an array can hold: the read opens the file, then fails to allocate
		Assert.ThrowsException<OutOfMemoryException>( () => archive.GetData( 0, int.MaxValue ) );
		Assert.AreEqual( 0, WadArchive.OpenHandleCount );
		WadArchive.MaxOpenHandles = 4;
		Assert.ThrowsException<OutOfMemoryException>( () => archive.GetData( 0, int.MaxValue ) );
		Assert.AreEqual( 0, WadArchive.OpenHandleCount, "also when files are kept" );
		Assert.AreEqual( "failed read", Read( archive ) );
		Assert.AreEqual( 1, WadArchive.OpenHandleCount );
	}

	[TestMethod]
	public void ClosingWaitsForAReadInProgressAndClosesItsFile()
	{
		using var archive = new WadArchive( Wad( "a.wad", "in progress" ) );
		Assert.AreEqual( 1, WadArchive.OpenHandleCount );
		// a read in progress holds the archive's read lock
		var readLock = typeof( WadArchive ).GetField( "readLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( archive )!;
		using var held = new System.Threading.ManualResetEventSlim();
		using var release = new System.Threading.ManualResetEventSlim();
		var reader = Task.Run( () =>
		{
			lock ( readLock )
			{
				held.Set();
				release.Wait();
			}
		} );
		held.Wait();
		var closing = Task.Run( WadArchive.CloseFileHandles );
		Assert.IsFalse( closing.Wait( 200 ), "the close waits for the read" );
		release.Set();
		Assert.IsTrue( closing.Wait( 10000 ) );
		reader.Wait();
		Assert.AreEqual( 0, WadArchive.OpenHandleCount, "every file open when the close began is closed when it returns" );
	}

	[TestMethod]
	public void ReadsFromSeveralThreadsGetTheirOwnBytesWhileFilesAreClosedUnderThem()
	{
		WadArchive.MaxOpenHandles = 2;
		var archives = Enumerable.Range( 0, 6 ).Select( index => new WadArchive( Wad( $"{index}.wad", $"contents of archive {index}" ) ) ).ToList();
		try
		{
			var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
			Parallel.For( 0, 6000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, step =>
			{
				var index = step % archives.Count;
				if ( step % 97 == 0 )
					WadArchive.CloseFileHandles();
				var text = Read( archives[index] );
				if ( text != $"contents of archive {index}" )
					failures.Add( $"{index}: {text}" );
			} );
			CollectionAssert.AreEqual( Array.Empty<string>(), failures.ToArray() );
			Assert.IsTrue( WadArchive.OpenHandleCount <= 2 );
		}
		finally
		{
			foreach ( var archive in archives )
				archive.Dispose();
		}
		Assert.AreEqual( 0, WadArchive.OpenHandleCount );
	}
}
