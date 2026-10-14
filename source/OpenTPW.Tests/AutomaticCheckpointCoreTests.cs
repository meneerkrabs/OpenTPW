using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class AutomaticCheckpointCoreTests
{
	private string root = null!;
	private BaseFileSystem files = null!;
	private GameEdition priorEdition;
	[TestInitialize]
	public void Initialize()
	{
		priorEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Directory.CreateTempSubdirectory( "opentpw-auto-core-" ).FullName; files = new BaseFileSystem( root );
	}
	[TestCleanup]
	public void Cleanup() { GameEditions.Select( priorEdition ); Directory.Delete( root, true ); }
	private static ParkResumeSnapshot Snapshot() => ParkResumeSaveFileTests.Envelope() with
	{
		Gate = new ParkGate.ParkGateSnapshot( 0 ),
		Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() )
	};
	private AutomaticCheckpointSession Session( Func<bool>? current = null, Func<BaseFileSystem>? currentFiles = null ) =>
		new( files, "jungle", currentFiles ?? (() => files), current ?? (() => true) );
	private string Auto => AutomaticThemeContinuation.Slot( files, "jungle" );
	private string Manual => files.GetAbsolutePath( "opentpw-jungle-world.json" );

	[TestMethod]
	public void DedicatedAutomaticSlotReaderRoundTripsWithoutChangingManualBytes()
	{
		var snapshot = Snapshot(); ParkResumeSaveFile.Save( Manual, snapshot ); var manual = File.ReadAllBytes( Manual );
		Assert.IsNull( AutomaticThemeContinuation.Probe( files, "jungle" ) );
		ParkResumeSaveFile.Save( Auto, snapshot );
		var entry = AutomaticThemeContinuation.Probe( files, "JUNGLE" ); Assert.IsNotNull( entry );
		Assert.AreEqual( Auto, entry.Path ); Assert.AreNotEqual( Manual, entry.Path );
		var loaded = AutomaticThemeContinuation.Read( files, entry );
		CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( snapshot ), ParkResumeSaveFile.Serialize( loaded ) );
		Assert.AreEqual( snapshot.Clock, loaded.Clock ); Assert.AreEqual( snapshot.Guests.Guests.Length, loaded.Guests.Guests.Length );
		CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) ); Assert.IsNotNull( ThemeContinuation.Probe( files, "jungle" ) );
	}
	[TestMethod]
	public void CheckpointWritesAfterFlushAndFreshCaptureBeforeRetiringExactlyOnce()
	{
		var snapshot = Snapshot(); ParkResumeSaveFile.Save( Manual, snapshot ); var manual = File.ReadAllBytes( Manual );
		var order = new List<string>(); var live = new object(); var original = live; var session = Session( () => ReferenceEquals( live, original ) );
		Assert.IsTrue( session.TryCheckpoint( () => { order.Add( "flush" ); return session.BoundProfileDigest; }, () => { order.Add( "capture" ); return snapshot; }, () =>
		{
			Assert.IsTrue( File.Exists( Auto ) ); Assert.IsTrue( session.CheckpointCommitted ); Assert.AreSame( original, live );
			order.Add( "retire" ); live = new object();
		} ) );
		CollectionAssert.AreEqual( new[] { "flush", "capture", "retire" }, order ); Assert.IsTrue( session.Completed );
		Assert.IsFalse( session.TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) );
		CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) );
	}
	[DataTestMethod]
	[DataRow( "missing" )]
	[DataRow( "malformed" )]
	[DataRow( "otherTheme" )]
	[DataRow( "inspection" )]
	[DataRow( "missingStaff" )]
	[DataRow( "TPI" )]
	public void UnavailableAutomaticDataNeverBecomesAContinuation( string reason )
	{
		var saved = Snapshot();
		if ( reason == "malformed" ) File.WriteAllText( Auto, "{" );
		if ( reason == "inspection" ) ParkResumeSaveFile.Save( Auto, saved with { StartKind = ParkStartKind.OriginalSaveReference } );
		if ( reason == "missingStaff" ) ParkResumeSaveFile.Save( Auto, saved with { Staff = null } );
		if ( reason == "otherTheme" )
		{
			var node = JsonNode.Parse( ParkResumeSaveFile.Serialize( saved ) )!; node["Theme"] = "fantasy"; node["Economy"]!["Theme"] = "fantasy";
			File.WriteAllText( Auto, node.ToJsonString() );
		}
		if ( reason == "TPI" ) GameEditions.Select( GameEdition.ThemeParkInc );
		Assert.IsNull( AutomaticThemeContinuation.Probe( files, "jungle" ) );
		Assert.IsFalse( File.Exists( Manual ) );
	}
	[TestMethod]
	public void ForgedPathNamespaceUnknownThemeAndChangedDataRefuseSelectedRead()
	{
		var saved = Snapshot(); ParkResumeSaveFile.Save( Auto, saved ); var entry = AutomaticThemeContinuation.Probe( files, "jungle" )!;
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Slot( files, "../jungle" ) );
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Read( files, entry with { Path = Manual } ) );
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Read( new BaseFileSystem( root ), entry ) );
		ParkResumeSaveFile.Save( Auto, saved with { DisplayName = "different" } );
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Read( files, entry ) );
	}
	[TestMethod]
	public void ChangedWorldWithIdenticalMenuMetadataRefusesOldSelection()
	{
		var saved = Snapshot(); ParkResumeSaveFile.Save( Auto, saved ); var entry = AutomaticThemeContinuation.Probe( files, "jungle" )!;
		ParkResumeSaveFile.Save( Auto, saved with { Clock = saved.Clock with { PendingSeconds = 0.005 } } );
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Read( files, entry ) );
	}
	[TestMethod]
	public void InstantActionRetainsItsSavedModeWithoutCreatingAProgressFile()
	{
		var saved = Snapshot(); var node = JsonNode.Parse( ParkResumeSaveFile.Serialize( saved ) )!;
		node["StartKind"] = (int)ParkStartKind.InstantAction; node["Economy"]!["Mode"] = (int)ParkGameMode.InstantAction;
		var instant = ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) );
		var session = Session(); Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest, () => instant, () => { } ) );
		var entry = AutomaticThemeContinuation.Probe( files, "jungle" )!; Assert.AreEqual( ParkGameMode.InstantAction, entry.Mode );
		Assert.AreEqual( ParkStartKind.InstantAction, AutomaticThemeContinuation.Read( files, entry ).StartKind );
		Assert.IsFalse( File.Exists( files.GetAbsolutePath( PlayerTicketFile.FileName ) ) );
	}
	[TestMethod]
	public void OpaqueMalformedProfileIsComparedWithoutBeingParsedOrReplaced()
	{
		var profile = files.GetAbsolutePath( PlayerTicketFile.FileName ); var bytes = Encoding.UTF8.GetBytes( "opaque invalid history" ); File.WriteAllBytes( profile, bytes );
		var session = Session(); Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest, Snapshot, () => { } ) );
		CollectionAssert.AreEqual( bytes, File.ReadAllBytes( profile ) );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ProfileChangeBeforeOrDuringCaptureRetainsPriorSlots( bool during )
	{
		var saved = Snapshot(); ParkResumeSaveFile.Save( Auto, saved ); ParkResumeSaveFile.Save( Manual, saved );
		var prior = File.ReadAllBytes( Auto ); var manual = File.ReadAllBytes( Manual ); var profile = files.GetAbsolutePath( PlayerTicketFile.FileName );
		File.WriteAllText( profile, "original" ); var session = Session(); var captured = 0; var retired = 0;
		if ( !during ) File.WriteAllText( profile, "external" );
		Assert.ThrowsException<InvalidDataException>( () => session.TryCheckpoint( () => session.BoundProfileDigest, () => { captured++; if ( during ) File.WriteAllText( profile, "external" ); return saved; }, () => retired++ ) );
		Assert.AreEqual( during ? 1 : 0, captured ); Assert.AreEqual( 0, retired ); Assert.IsFalse( session.CheckpointCommitted );
		CollectionAssert.AreEqual( prior, File.ReadAllBytes( Auto ) ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) );
	}
	[TestMethod]
	public void ExternalReplacementDuringNoOpFlushCannotBecomeAnOwnedReceipt()
	{
		var profile = files.GetAbsolutePath( PlayerTicketFile.FileName ); File.WriteAllText( profile, "owned" ); var session = Session();
		Assert.ThrowsException<InvalidDataException>( () => session.TryCheckpoint( () => { File.WriteAllText( profile, "external" ); return session.BoundProfileDigest; }, () => throw new Exception( "Unexpected capture" ), () => Assert.Fail() ) );
		Assert.IsFalse( File.Exists( Auto ) ); Assert.IsFalse( session.CheckpointCommitted ); Assert.AreEqual( "external", File.ReadAllText( profile ) );
	}
	[TestMethod]
	public void SelectionFingerprintDescribesDecodedOwnStateNotWhitespace()
	{
		ParkResumeSaveFile.Save( Auto, Snapshot() ); var entry = AutomaticThemeContinuation.Probe( files, "jungle" )!;
		File.WriteAllText( Auto, " \n" + File.ReadAllText( Auto ) + "\n " ); Assert.IsNotNull( AutomaticThemeContinuation.Read( files, entry ) );
	}
	[TestMethod]
	public void OversizedProfileBindingRefusesBeforeAllocationOrCapture()
	{
		var profile = files.GetAbsolutePath( PlayerTicketFile.FileName ); File.WriteAllText( profile, "warm" ); Session();
		using ( var stream = File.Create( profile ) ) stream.SetLength( 1024 * 1024 );
		var allocated = GC.GetAllocatedBytesForCurrentThread(); Assert.ThrowsException<InvalidDataException>( () => Session() );
		Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - allocated < 64 * 1024, "Reject oversized binding before allocating its payload." ); Assert.IsFalse( File.Exists( Auto ) );
		Assert.IsFalse( Session( () => false ).TryCheckpoint( () => throw new Exception( "Unexpected stale flush" ), Snapshot, () => Assert.Fail() ),
			"A retired owner must not open even an oversized profile binding." );
	}
	[TestMethod]
	public void FlushFailureAndCancelledOrStaleOwnersNeverCaptureOrRetire()
	{
		var session = Session(); Assert.ThrowsException<IOException>( () => session.TryCheckpoint( () => throw new IOException( "flush" ), () => throw new Exception( "capture" ), () => Assert.Fail() ) );
		session.Cancel(); Assert.IsFalse( session.TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) );
		Assert.IsFalse( Session( () => false ).TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) );
		var rootChanged = new BaseFileSystem( Path.Combine( root, "replacement" ) ); Assert.IsFalse( Session( currentFiles: () => rootChanged ).TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) );
		Assert.IsFalse( File.Exists( Auto ) );
	}
	[TestMethod]
	public void CaptureFailureKeepsCommittedProgressAndRetryCapturesFreshState()
	{
		var saved = Snapshot(); ParkResumeSaveFile.Save( Auto, saved ); var before = File.ReadAllBytes( Auto );
		var path = files.GetAbsolutePath( PlayerTicketFile.FileName ); var session = Session(); var progress = new PlayerTicketLedger( ParkGameMode.FullSimulation );
		var captures = 0; var retired = 0;
		Assert.ThrowsException<NotSupportedException>( () => session.TryCheckpoint( () => { var snapshot = progress.Capture(); PlayerTicketFile.Save( path, snapshot ); return Convert.ToHexString( SHA256.HashData( PlayerTicketFile.Serialize( snapshot ) ) ); }, () => { captures++; throw new NotSupportedException( "live capture" ); }, () => retired++ ) );
		Assert.IsTrue( File.Exists( path ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Auto ) ); Assert.AreEqual( 0, retired );
		var updated = saved with { DisplayName = "fresh retry" }; Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest, () => { captures++; return updated; }, () => retired++ ) );
		Assert.AreEqual( 2, captures ); Assert.AreEqual( 1, retired ); Assert.AreEqual( "fresh retry", ParkResumeSaveFile.Load( Auto ).DisplayName );
	}
	[TestMethod]
	public void InvalidSerializationAndLateMoveFailureDoNotRetireOrLoseForeignFiles()
	{
		var saved = Snapshot(); ParkResumeSaveFile.Save( Manual, saved ); var manual = File.ReadAllBytes( Manual );
		Assert.ThrowsException<InvalidDataException>( () => Session().TryCheckpoint( () => null, () => saved with { AssetHashes = null! }, () => Assert.Fail() ) );
		Directory.CreateDirectory( Auto ); var foreign = Path.Combine( Auto, "foreign" ); File.WriteAllText( foreign, "keep" );
		Exception? failure = null; try { Session().TryCheckpoint( () => null, () => saved, () => Assert.Fail() ); } catch ( Exception error ) { failure = error; }
		Assert.IsTrue( failure is IOException or UnauthorizedAccessException ); Assert.AreEqual( "keep", File.ReadAllText( foreign ) );
		Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) );
	}
	[TestMethod]
	public void ReentrantAndCancelledCaptureCannotProduceNestedOrLateWrites()
	{
		var saved = Snapshot(); var session = Session(); var captures = 0; var retired = 0;
		Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest, () =>
		{
			captures++; Assert.IsFalse( session.TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) ); return saved;
		}, () => retired++ ) );
		Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired );
		var prior = File.ReadAllBytes( Auto ); var cancelled = Session();
		Assert.IsFalse( cancelled.TryCheckpoint( () => cancelled.BoundProfileDigest, () => { cancelled.Cancel(); return saved with { DisplayName = "cancelled" }; }, () => Assert.Fail() ) );
		CollectionAssert.AreEqual( prior, File.ReadAllBytes( Auto ) );
	}
	[TestMethod]
	public void RetirementFailureIsCommittedAndCannotBeRetriedAsPrewriteFailure()
	{
		var session = Session(); Assert.ThrowsException<InvalidOperationException>( () => session.TryCheckpoint( () => session.BoundProfileDigest, Snapshot, () => throw new InvalidOperationException( "retirement" ) ) );
		Assert.IsTrue( session.CheckpointCommitted ); Assert.IsFalse( session.Completed ); Assert.IsTrue( File.Exists( Auto ) );
		Assert.IsFalse( session.TryCheckpoint( () => throw new Exception( "Unexpected flush" ), () => throw new Exception(), () => Assert.Fail() ) );
	}
	[TestMethod]
	public void ActualLevelCaptureGuardAndAtomicSaveRunThroughExistingSyntheticFixture()
	{
		// Reuse the existing real object/VM/cell capture fixture without widening shared tests or loading original assets.
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var original = world.Level.ComputeStateHash(); var session = Session(); var retired = 0;
			Assert.IsTrue( session.TryCheckpoint( () => session.BoundProfileDigest, () => world.Level.CaptureResumeSnapshot( null, "actual", assets ), () => retired++ ) );
			Assert.AreEqual( 1, retired ); Assert.AreEqual( original, world.Level.ComputeStateHash() );
			var saved = AutomaticThemeContinuation.Read( files, AutomaticThemeContinuation.Probe( files, "jungle" )! );
			Assert.AreEqual( 2, saved.Layout.Objects.Count ); Assert.IsNotNull( saved.Staff ); Assert.IsNotNull( saved.Gate );
			var previous = File.ReadAllBytes( Auto ); world.One.Visitors.Parameters = world.One.Visitors.Parameters with { };
			Assert.ThrowsException<NotSupportedException>( () => Session().TryCheckpoint( () => null, () => world.Level.CaptureResumeSnapshot( null, "unsupported", assets ), () => Assert.Fail() ) );
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( Auto ) ); Assert.AreEqual( original, world.Level.ComputeStateHash() );
		}
		finally { fixture.Cleanup(); }
	}
}
