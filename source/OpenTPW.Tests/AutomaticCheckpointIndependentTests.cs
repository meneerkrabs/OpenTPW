using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class AutomaticCheckpointIndependentTests
{
	private string root = null!;
	private BaseFileSystem files = null!;
	private GameEdition previous;
	[TestInitialize]
	public void Initialize() { previous = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); root = Directory.CreateTempSubdirectory( "opentpw-auto-independent-" ).FullName; files = new( root ); }
	[TestCleanup]
	public void Cleanup() { GameEditions.Select( previous ); Directory.Delete( root, true ); }
	private string Profile => files.GetAbsolutePath( PlayerTicketFile.FileName );
	private string Auto => AutomaticThemeContinuation.Slot( files, "jungle" );
	private static ParkResumeSnapshot Snapshot() => ParkResumeSaveFileTests.Envelope() with
	{
		Gate = new ParkGate.ParkGateSnapshot( 0 ),
		Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() )
	};

	[TestMethod]
	public void OpaqueProfileBytesAreExactEvenWhenAutomaticSelectionIsSemantic()
	{
		var snapshot = Snapshot(); ParkResumeSaveFile.Save( Auto, snapshot );
		var entry = AutomaticThemeContinuation.Probe( files, "jungle" )!;
		File.AppendAllText( Auto, "\n \t" );
		Assert.AreEqual( snapshot.Clock, AutomaticThemeContinuation.Read( files, entry ).Clock );
		File.WriteAllText( Profile, "unknown opaque history" );
		var session = new AutomaticCheckpointSession( files, "jungle", () => files, () => true );
		File.AppendAllText( Profile, " " ); var captures = 0;
		Assert.ThrowsException<InvalidDataException>( () => session.TryCheckpoint( () => session.BoundProfileDigest, () => { captures++; return snapshot; }, () => Assert.Fail() ) );
		Assert.AreEqual( 0, captures ); Assert.IsFalse( session.CheckpointCommitted );
		Assert.AreEqual( "unknown opaque history ", File.ReadAllText( Profile ) );
	}

	[TestMethod]
	public void ExactProfileSizeBoundaryAcceptsOpaqueBytesAndWrongReceiptNeverCaptures()
	{
		var bytes = new byte[PlayerTicketFile.MaximumFileSize]; bytes[^1] = 1; File.WriteAllBytes( Profile, bytes );
		var session = new AutomaticCheckpointSession( files, "jungle", () => files, () => true );
		Assert.AreEqual( Convert.ToHexString( SHA256.HashData( bytes ) ), session.BoundProfileDigest );
		var captures = 0;
		Assert.ThrowsException<InvalidDataException>( () => session.TryCheckpoint( () => new string( '0', 64 ), () => { captures++; return Snapshot(); }, () => Assert.Fail() ) );
		Assert.AreEqual( 0, captures ); Assert.IsFalse( File.Exists( Auto ) );
		Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest!.ToLowerInvariant(), Snapshot, () => { } ) );
		CollectionAssert.AreEqual( bytes, File.ReadAllBytes( Profile ) );
	}

	[TestMethod]
	public void RootReplacementAfterLegitimateFlushKeepsProgressWithoutWritingOrRetiring()
	{
		var original = Snapshot(); ParkResumeSaveFile.Save( Auto, original ); var before = File.ReadAllBytes( Auto );
		var current = files; var session = new AutomaticCheckpointSession( files, "jungle", () => current, () => true );
		var progress = new PlayerTicketLedger().Capture() with { ExtraKeys = 1 }; var expected = PlayerTicketFile.Serialize( progress );
		var captures = 0; var retired = 0;
		Assert.IsFalse( session.TryCheckpoint( () =>
		{
			PlayerTicketFile.Save( Profile, progress ); current = new BaseFileSystem( Path.Combine( root, "fresh-root" ) );
			return Convert.ToHexString( SHA256.HashData( expected ) );
		}, () => { captures++; return original; }, () => retired++ ) );
		Assert.AreEqual( 0, captures ); Assert.AreEqual( 0, retired ); Assert.IsFalse( session.CheckpointCommitted );
		CollectionAssert.AreEqual( expected, File.ReadAllBytes( Profile ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Auto ) );
	}
}
