using System;
using System.Linq;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic raw-map fixtures informed by tp.exe's queue passes, not executed native recordings.</summary>
[TestClass]
public class QueuePassResidueSnapshotTests
{
	private static ParkLayoutSnapshot.Data Capture( ParkCellMap map ) => ParkLayoutSnapshot.CaptureSnapshot( map, new ParkObjects( null!, new SandboxParkGrid() ) );

	[TestMethod]
	public void EmptyOwnerOnlyAndTerminalPathMetadataRoundTripWithoutRelaxingOrdinaryRecords()
	{
		var map = new ParkCellMap( 8, 8 );
		map.ApplyQueuePassMetadata( 2, 2, 0, 17 );
		map.SetType( 3, 2, ParkCellType.Path );
		map.SetLinks( 3, 2, 0x44 );
		map.ApplyQueuePassMetadata( 3, 2, 64, 17 );
		var data = Capture( map );
		Assert.AreEqual( 2, data.Cells.QueuePassResidues!.Count );
		Assert.IsTrue( data.Cells.Cells.All( c => c.QueueLink == 0 && c.Owner == 0 ) );
		var copy = ParkLayoutSnapshot.RestoreCellMap( ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( data ) ) );
		Assert.AreEqual( map.Version, copy.Version );
		Assert.AreEqual( (ushort)17, copy.OwnerAt( 2, 2 ) );
		Assert.AreEqual( (byte)0, copy.RawTypeAt( 2, 2 ) );
		Assert.AreEqual( (byte)64, copy.QueueLinkAt( 3, 2 ) );
		Assert.AreEqual( (byte)0x44, copy.LinksAt( 3, 2 ) );
		Assert.IsTrue( copy.HasQueuePassResidues );
	}

	[TestMethod]
	public void NeighbourUnlinkChangesRawBytesButNotOwnerAndTypingRetiresResidue()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 2, ParkCellType.Queue );
		map.SetLinks( 2, 2, 4 );
		map.SetLinks( 3, 2, 64 );
		map.ApplyQueuePassMetadata( 3, 2, 64, 17 );
		PathLinkWriter.Unlink( map, 2, 2, cardinalOnly: true );
		Assert.AreEqual( (byte)0, map.LinksAt( 3, 2 ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 3, 2 ) );
		Assert.IsTrue( map.HasQueuePassResidues );
		map.SetType( 3, 2, ParkCellType.Queue );
		Assert.IsFalse( map.HasQueuePassResidues );
	}

	[TestMethod]
	public void MalformedCompositesCannotAuthorizeArbitraryEmptyMetadata()
	{
		var map = new ParkCellMap( 8, 8 );
		map.ApplyQueuePassMetadata( 2, 2, 64, 17 );
		var data = Capture( map );
		var residue = data.Cells.QueuePassResidues![0];
		foreach ( var changed in new[] { residue with { Type = 3 }, residue with { X = -1 }, residue with { QueueLink = 3 }, residue with { Owner = ushort.MaxValue } } )
			Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data with { Cells = data.Cells with { QueuePassResidues = new() { changed } } } ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data with { Cells = data.Cells with { QueuePassSchemaVersion = 2 } } ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data with { Cells = data.Cells with { QueuePassResidues = new() { residue, residue } } } ) );
		var illegal = data.Cells.Cells[0] with { Owner = 17 };
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data with { Cells = data.Cells with { QueuePassResidues = null, QueuePassSchemaVersion = 0, Cells = new() { illegal } } } ) );
	}

	[TestMethod]
	public void SharedExportRefusesInsteadOfDroppingOwnerOnlyEmptyCells()
	{
		var map = new ParkCellMap( 8, 8 );
		Assert.AreEqual( 0, SharedParkLayout.CaptureCells( map ).Count );
		map.ApplyQueuePassMetadata( 2, 2, 0, 17 );
		Assert.ThrowsException<NotSupportedException>( () => SharedParkLayout.CaptureCells( map ) );
		Assert.AreEqual( (ushort)17, map.OwnerAt( 2, 2 ) );
	}
	[TestMethod]
	public void RawOwnerResidueParticipatesInHashAndRoundTripPreservesHash()
	{
		static ulong Digest( ParkCellMap map ) { var hash = new StateHasher(); map.AddCanonicalState( hash ); return hash.Value; }
		var map = new ParkCellMap( 8, 8 );
		var empty = Digest( map );
		map.ApplyQueuePassMetadata( 2, 2, 0, 17 );
		Assert.AreNotEqual( empty, Digest( map ) );
		Assert.AreEqual( Digest( map ), Digest( ParkLayoutSnapshot.RestoreCellMap( Capture( map ) ) ) );
		var old = Digest( map );
		map.SetOwner( 2, 2, 18 );
		Assert.AreNotEqual( old, Digest( map ) );
		map.SetOwner( 2, 2, 0 );
		Assert.AreEqual( empty, Digest( map ) );
	}

	[TestMethod]
	public void ResumeOneMigratesWithoutInventingResidueAndRejectsNewFieldsInOldEnvelope()
	{
		var source = ParkResumeSaveFileTests.Envelope();
		var migrated = ParkResumeSaveFile.Deserialize( ParkResumeJson.Serialize( source with { Version = 1 } ) );
		Assert.AreEqual( ParkResumeSaveFile.CurrentVersion, migrated.Version );
		Assert.IsNull( migrated.Layout.Cells.QueuePassResidues );
		var layout = source.Layout with { Cells = source.Layout.Cells with { QueuePassSchemaVersion = 1, QueuePassResidues = new() } };
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( ParkResumeJson.Serialize( source with { Version = 1, Layout = layout } ) ) );
	}

	[TestMethod]
	public void MissingResidueMembersAndUnclassifiedRawMetadataAreNotSilentlyAccepted()
	{
		var map = new ParkCellMap( 8, 8 ); map.ApplyQueuePassMetadata( 2, 2, 64, 17 );
		var json = JsonNode.Parse( ParkResumeJson.Serialize( Capture( map ) ) )!;
		foreach ( var member in new[] { "X", "Y", "Type", "QueueLink", "Owner" } )
		{
			var copy = JsonNode.Parse( json.ToJsonString() )!;
			copy["Cells"]!["QueuePassResidues"]![0]!.AsObject().Remove( member );
			Assert.ThrowsException<JsonException>( () => ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( System.Text.Encoding.UTF8.GetBytes( copy.ToJsonString() ) ) );
		}
		var forged = new ParkCellMap( 8, 8 ); forged.SetOwner( 2, 2, 17 );
		Assert.ThrowsException<InvalidDataException>( () => Capture( forged ) );
		Assert.ThrowsException<NotSupportedException>( () => SharedParkLayout.CaptureCells( forged ) );
		var a = new StateHasher(); var b = new StateHasher();
		map.SetQueueLink( 2, 2, 0 ); map.AddCanonicalState( a ); forged.AddCanonicalState( b );
		Assert.AreEqual( a.Value, b.Value, "equal raw cells hash equally regardless classification" );
	}

}
