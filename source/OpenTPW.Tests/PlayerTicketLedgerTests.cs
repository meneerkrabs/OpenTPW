using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PlayerTicketLedgerTests
{
	[TestMethod]
	public void EmptyOwnProfileHasNoImplicitLobbyAwardAndAbsenceIsUnknown()
	{
		var ledger = new PlayerTicketLedger();
		Assert.AreEqual( 0, ledger.EarnedTickets ); Assert.AreEqual( 0, ledger.ExtraKeys ); Assert.AreEqual( 0, ledger.Keys );
		Assert.AreEqual( ParkGameMode.FullSimulation, ledger.Mode );
		Assert.IsNull( PlayerTicketFile.LoadIfPresent( Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ), PlayerTicketFile.FileName ) ) );
	}

	[TestMethod]
	public void KeysAdvanceOnlyAtEachThirdUniqueIdentity()
	{
		var ledger = new PlayerTicketLedger();
		var locals = Enum.GetValues<GoldenTicketKind>().Take( 6 ).ToArray();
		for ( var count = 1; count <= 6; count++ )
		{
			ledger.RecordEarned( "jungle", locals[count - 1] );
			Assert.AreEqual( count / 3, ledger.Keys );
			Assert.IsFalse( ledger.RecordEarned( "jungle", locals[count - 1] ) );
			Assert.AreEqual( count / 3, ledger.Keys );
		}
	}

	[TestMethod]
	public void AllSixLocalFourGlobalTwoSecretIdentitiesHaveExactScopes()
	{
		var ledger = new PlayerTicketLedger();
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			Assert.AreEqual( theme == "jungle" ? 12 : 6, ledger.Reconcile( theme, Enum.GetValues<GoldenTicketKind>() ) );
			Assert.AreEqual( 0, ledger.Reconcile( theme.ToUpperInvariant(), Enum.GetValues<GoldenTicketKind>() ) );
		}
		Assert.AreEqual( 30, ledger.EarnedTickets ); Assert.AreEqual( 10, ledger.Keys );
		Assert.AreEqual( 24, ledger.Capture().Earned.Count( item => item.Theme != null ) );
		Assert.AreEqual( 6, ledger.Capture().Earned.Count( item => item.Theme == null ) );
	}

	[TestMethod]
	public void SameGlobalsAndSecretsInTwoRestoredParksCountOnceWhileLocalsCountTwice()
	{
		var ledger = new PlayerTicketLedger();
		var identities = new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.GokartExcitement, GoldenTicketKind.WaterLength,
			GoldenTicketKind.CamerasEverywhere, GoldenTicketKind.AllLandOwned, GoldenTicketKind.Visitors };
		Assert.AreEqual( 6, ledger.Reconcile( "jungle", identities ) );
		Assert.AreEqual( 1, ledger.Reconcile( "hallow", identities ) );
		for ( var i = 0; i < 3; i++ )
		{
			ledger = PlayerTicketFile.Deserialize( PlayerTicketFile.Serialize( ledger.Capture() ) );
			Assert.AreEqual( 0, ledger.Reconcile( "jungle", identities ) );
			Assert.AreEqual( 0, ledger.Reconcile( "hallow", identities ) );
		}
		Assert.AreEqual( 7, ledger.EarnedTickets ); Assert.AreEqual( 2, ledger.Keys );
	}

	[TestMethod]
	public void SpentBalanceAndExplicitExtraKeysRoundTripWithoutRemovingEarnedIdentities()
	{
		var original = new PlayerTicketLedger( ParkGameMode.InstantAction );
		original.Reconcile( "space", Enum.GetValues<GoldenTicketKind>() );
		var restored = PlayerTicketLedger.Restore( original.Capture() with { ExtraKeys = 2, SpentTickets = 12 } );
		restored = PlayerTicketFile.Deserialize( PlayerTicketFile.Serialize( restored.Capture() ) );
		Assert.AreEqual( 12, restored.EarnedTickets ); Assert.AreEqual( 0, restored.AvailableTickets ); Assert.AreEqual( 6, restored.Keys );
		Assert.AreEqual( ParkGameMode.InstantAction, restored.Mode ); Assert.AreEqual( 0, original.ExtraKeys );
	}

	[TestMethod]
	public void InvalidReconciliationDoesNotPartiallyAwardValidPrefix()
	{
		var ledger = new PlayerTicketLedger();
		Assert.ThrowsException<InvalidDataException>( () => ledger.Reconcile( "jungle", new[] { GoldenTicketKind.Visitors, (GoldenTicketKind)99 } ) );
		Assert.AreEqual( 0, ledger.EarnedTickets );
		Assert.ThrowsException<InvalidDataException>( () => ledger.RecordEarned( "../space", GoldenTicketKind.CoasterHeight ) );
		Assert.AreEqual( 0, ledger.EarnedTickets );
		Assert.IsTrue( ledger.RecordEarned( "JUNGLE", GoldenTicketKind.Visitors ) );
		Assert.IsFalse( ledger.RecordEarned( "jungle", GoldenTicketKind.Visitors ) );
	}

	[DataTestMethod]
	[DataRow( null, GoldenTicketKind.Visitors )]
	[DataRow( "JUNGLE", GoldenTicketKind.Visitors )]
	[DataRow( "mars", GoldenTicketKind.Visitors )]
	[DataRow( "jungle", GoldenTicketKind.CoasterHeight )]
	[DataRow( "hallow", GoldenTicketKind.CamerasEverywhere )]
	[DataRow( null, (GoldenTicketKind)99 )]
	public void ForgedIdentityScopeOrKindIsRefused( string? theme, GoldenTicketKind kind )
	{
		var snapshot = new PlayerTicketLedger().Capture() with { Earned = new[] { new EarnedTicketIdentity( theme, kind ) } };
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Serialize( snapshot ) );
	}

	[TestMethod]
	public void SnapshotIsCopiedAndDuplicateOrInvalidCountersAreRefused()
	{
		var item = new EarnedTicketIdentity( "jungle", GoldenTicketKind.Visitors ); var caller = new[] { item };
		var snapshot = new PlayerTicketLedger().Capture() with { Earned = caller };
		var ledger = PlayerTicketLedger.Restore( snapshot ); caller[0] = new( "space", GoldenTicketKind.PeopleInPark );
		Assert.AreEqual( item, ledger.Capture().Earned.Single() );
		foreach ( var invalid in new[] { snapshot with { Earned = new[] { item, item } }, snapshot with { ExtraKeys = -1 },
			snapshot with { ExtraKeys = int.MaxValue }, snapshot with { SpentTickets = -1 }, snapshot with { SpentTickets = 2 }, snapshot with { Mode = (ParkGameMode)99 } } )
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Serialize( invalid ) );
	}

	[DataTestMethod]
	[DataRow( "{}" )]
	[DataRow( "null" )]
	[DataRow( "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"FullSimulation\",\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[{\"Kind\":\"Visitors\"}]}" )]
	[DataRow( "{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":0,\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[]}" )]
	public void MissingOrMalformedFieldsAreRefused( string json ) => Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( json ) ) );

	[TestMethod]
	public void UnknownDuplicateFieldsVersionAndOversizeAreRefused()
	{
		var json = Encoding.UTF8.GetString( PlayerTicketFile.Serialize( new PlayerTicketLedger().Capture() ) );
		foreach ( var invalid in new[] { json.Replace( "\"Version\":1", "\"Version\":2" ), json.Replace( "\"Version\":1", "\"Version\":1,\"Version\":1" ), json.Replace( "\"Version\":1", "\"Version\":1,\"LegacyCounts\":{}" ) } )
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( Encoding.UTF8.GetBytes( invalid ) ) );
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( new byte[PlayerTicketFile.MaximumFileSize + 1] ) );
	}

	[TestMethod]
	public void SparseOversizeFileIsRejectedBeforeBufferAllocation()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-ticket-size-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			using ( var stream = File.Create( path ) ) stream.SetLength( 1024L * 1024 * 1024 );
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.LoadIfPresent( path ) );
			var before = GC.GetAllocatedBytesForCurrentThread();
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.LoadIfPresent( path ) );
			Assert.IsTrue( GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024 );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void RealFailedTemporaryWritePreservesPreviousFile()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-ticket-io-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			var ledger = new PlayerTicketLedger(); PlayerTicketFile.Save( path, ledger.Capture() );
			var previous = File.ReadAllBytes( path ); ledger.RecordEarned( "jungle", GoldenTicketKind.Visitors );
			if ( OperatingSystem.IsWindows() )
			{
				using var held = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
				var refused = false;
				try { PlayerTicketFile.Save( path, ledger.Capture() ); }
				catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException ) { refused = true; }
				Assert.IsTrue( refused, "A held destination must refuse replacement." );
			}
			else
			{
				var mode = File.GetUnixFileMode( root );
				try
				{
					File.SetUnixFileMode( root, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite) );
					var probe = Path.Combine( root, "permission-probe.tmp" );
					var writable = false;
					try { using var stream = File.Create( probe ); writable = true; }
					catch ( UnauthorizedAccessException ) { }
					if ( writable )
					{
						File.Delete( probe );
						Assert.Inconclusive( "Current identity bypasses directory write permissions." );
					}
					Assert.ThrowsException<UnauthorizedAccessException>( () => PlayerTicketFile.Save( path, ledger.Capture() ) );
				}
				finally { File.SetUnixFileMode( root, mode ); }
			}
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( path ) );
			Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
			Assert.AreEqual( 0, PlayerTicketFile.LoadIfPresent( path )!.EarnedTickets );
			Assert.AreEqual( 1, ledger.EarnedTickets );
		}
		finally { Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void AtomicOverwriteFailureAndChangedSourcePreserveLiveLedgerAndPreviousFile()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-ticket-file-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName ); var ledger = new PlayerTicketLedger();
			ledger.RecordEarned( "jungle", GoldenTicketKind.Visitors ); PlayerTicketFile.Save( path, ledger.Capture() );
			var previous = File.ReadAllBytes( path ); var foreign = path + ".foreign.tmp"; File.WriteAllText( foreign, "foreign" );
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Save( path, ledger.Capture() with { SpentTickets = 2 } ) );
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( path ) ); Assert.AreEqual( "foreign", File.ReadAllText( foreign ) );
			var target = Path.Combine( root, "occupied.json" ); Directory.CreateDirectory( target ); File.WriteAllText( Path.Combine( target, "marker" ), "retained" );
			try { PlayerTicketFile.Save( target, ledger.Capture() ); Assert.Fail( "Rename should refuse a directory." ); }
			catch ( IOException ) { }
			catch ( UnauthorizedAccessException ) { }
			Assert.AreEqual( "retained", File.ReadAllText( Path.Combine( target, "marker" ) ) ); CollectionAssert.AreEqual( previous, File.ReadAllBytes( path ) );
			Assert.AreEqual( 2, Directory.GetFiles( root ).Length );
			var replacement = new PlayerTicketLedger( ParkGameMode.InstantAction ); replacement.RecordEarned( "space", GoldenTicketKind.Happiness );
			PlayerTicketFile.Save( path, replacement.Capture() ); Assert.AreEqual( replacement.Capture().Earned.Single(), PlayerTicketFile.LoadIfPresent( path )!.Capture().Earned.Single() );
			File.WriteAllText( path, "{}" ); Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.LoadIfPresent( path ) );
			Assert.AreEqual( GoldenTicketKind.Visitors, ledger.Capture().Earned.Single().Kind );
			foreach ( var suffix in new[] { "gms.dat", "native.tpws", "native.tpwi" } )
				Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Save( Path.Combine( root, suffix ), ledger.Capture() ) );
		}
		finally { Directory.Delete( root, true ); }
	}
}
