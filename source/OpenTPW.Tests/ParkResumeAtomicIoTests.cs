using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ParkResumeAtomicIoTests
{
	private string directory = null!;
	private ParkResumeSnapshot snapshot = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine( Path.GetTempPath(), "opentpw-atomic-audit-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		snapshot = ParkResumeSaveFileTests.Envelope();
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete( directory, true );

	private static void IoFailure( Action action )
	{
		Exception? failure = null;
		try { action(); }
		catch ( Exception exception ) { failure = exception; }
		Assert.IsTrue( failure is IOException or UnauthorizedAccessException,
			$"Expected an I/O failure, got {failure?.GetType().Name ?? "success"}." );
	}

	[TestMethod]
	public void ValidationFailurePreservesThePriorSaveAndForeignTemporaryFile()
	{
		var save = Path.Combine( directory, "park.otpw" );
		ParkResumeSaveFile.Save( save, snapshot );
		var previous = File.ReadAllBytes( save );
		var foreign = save + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		File.WriteAllText( foreign, "another writer owns this file" );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Save( save, snapshot with { Version = ParkResumeSaveFile.CurrentVersion + 1 } ) );
		CollectionAssert.AreEqual( previous, File.ReadAllBytes( save ) );
		Assert.AreEqual( "another writer owns this file", File.ReadAllText( foreign ) );
		Assert.AreEqual( 2, Directory.GetFiles( directory ).Length );
		Assert.AreEqual( snapshot.DisplayName, ParkResumeSaveFile.Load( save ).DisplayName );
	}

	[TestMethod]
	public void RenameFailureRemovesOnlyItsOwnedTempAndPreservesExistingContents()
	{
		var save = Path.Combine( directory, "previous.otpw" );
		ParkResumeSaveFile.Save( save, snapshot );
		var previous = File.ReadAllBytes( save );
		var target = Path.Combine( directory, "directory.otpw" );
		Directory.CreateDirectory( target );
		File.WriteAllText( Path.Combine( target, "marker" ), "do not replace this directory" );
		var foreign = target + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		File.WriteAllText( foreign, "foreign" );
		IoFailure( () => ParkResumeSaveFile.Save( target, snapshot ) );
		Assert.AreEqual( "do not replace this directory", File.ReadAllText( Path.Combine( target, "marker" ) ) );
		Assert.AreEqual( "foreign", File.ReadAllText( foreign ) );
		CollectionAssert.AreEqual( previous, File.ReadAllBytes( save ) );
		CollectionAssert.AreEquivalent( new[] { save, foreign }, Directory.GetFiles( directory ) );
		Assert.AreEqual( snapshot.DisplayName, ParkResumeSaveFile.Load( save ).DisplayName );
	}

	[TestMethod]
	public void AFileInTheParentPathPreventsCreationWithoutChangingThatFile()
	{
		var parent = Path.Combine( directory, "blocked" );
		var previous = ParkResumeSaveFile.Serialize( snapshot );
		File.WriteAllBytes( parent, previous );
		IoFailure( () => ParkResumeSaveFile.Save( Path.Combine( parent, "park.otpw" ), snapshot ) );
		CollectionAssert.AreEqual( previous, File.ReadAllBytes( parent ) );
		Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
	}

	[TestMethod]
	public void RepeatedOverwritePublishesWholeSnapshotsAndKeepsOtherTemps()
	{
		var save = Path.Combine( directory, "park.otpw" );
		var foreign = save + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		File.WriteAllText( foreign, "foreign" );
		for ( var index = 0; index < 4; index++ )
		{
			var replacement = snapshot with { DisplayName = $"Snapshot {index}" };
			ParkResumeSaveFile.Save( save, replacement );
			CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( replacement ), File.ReadAllBytes( save ) );
			Assert.AreEqual( replacement.DisplayName, ParkResumeSaveFile.Load( save ).DisplayName );
			CollectionAssert.AreEquivalent( new[] { save, foreign }, Directory.GetFiles( directory ) );
		}
		Assert.AreEqual( "foreign", File.ReadAllText( foreign ) );
	}

	[TestMethod]
	public void NonwritableUnixDirectoryRejectsTempCreationAndKeepsPriorSave()
	{
		if ( OperatingSystem.IsWindows() )
		{
			Assert.Inconclusive( "Unix directory permissions do not apply on Windows." );
			return;
		}
		var save = Path.Combine( directory, "park.otpw" );
		ParkResumeSaveFile.Save( save, snapshot );
		var previous = File.ReadAllBytes( save );
		var mode = File.GetUnixFileMode( directory );
		try
		{
			File.SetUnixFileMode( directory, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite) );
			// Elevated identities may bypass Unix write permissions; test the filesystem capability first.
			var probe = Path.Combine( directory, "permission-probe.tmp" );
			var writable = false;
			try { using var stream = new FileStream( probe, FileMode.CreateNew, FileAccess.Write ); writable = true; }
			catch ( UnauthorizedAccessException ) { }
			if ( writable )
			{
				File.Delete( probe );
				Assert.Inconclusive( "The current identity can bypass nonwritable directory permissions." );
			}
			IoFailure( () => ParkResumeSaveFile.Save( save, snapshot with { DisplayName = "Replacement" } ) );
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( save ) );
			Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
		}
		finally { File.SetUnixFileMode( directory, mode ); }
	}
	[TestMethod]
	public void WindowsDeleteSharingFailureKeepsThePriorSaveAndCleansOwnedTemp()
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Assert.Inconclusive( "Windows denies replacement without delete sharing; Unix rename differs." );
			return;
		}
		var save = Path.Combine( directory, "park.otpw" );
		ParkResumeSaveFile.Save( save, snapshot );
		var previous = File.ReadAllBytes( save );
		using ( var held = new FileStream( save, FileMode.Open, FileAccess.Read, FileShare.Read ) )
			IoFailure( () => ParkResumeSaveFile.Save( save, snapshot with { DisplayName = "Replacement" } ) );
		CollectionAssert.AreEqual( previous, File.ReadAllBytes( save ) );
		Assert.AreEqual( snapshot.DisplayName, ParkResumeSaveFile.Load( save ).DisplayName );
		Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
	}

}
