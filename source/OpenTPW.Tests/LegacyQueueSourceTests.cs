using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic optional repair sources, without original data, models or a GPU.</summary>
[TestClass]
[DoNotParallelize]
public class LegacyQueueSourceTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private Logger previousLog = null!;
	private GameEdition previousEdition;
	private static readonly ushort Owner = ParkCellMap.CellWord( 3, 3 );
	private static readonly ObjectAccessPoint[] Points = { new( ObjectCellKind.Entrance, 3, 3, 3, 4 ) };

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousLog = Log; previousEdition = GameEditions.Current;
		root = Path.Combine( Path.GetTempPath(), "opentpw-queue-source-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "levels/jungle" ) );
		FileSystem = new BaseFileSystem( root ); Log = new();
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFiles; Log = previousLog; GameEditions.Select( previousEdition );
		Directory.Delete( root, true );
	}

	private static MapFile Map() => OriginalParkImportTests.CreateMap( 6, 6, ( _, _ ) => 0 );

	private static ParkCellMap ResumedCells()
	{
		var cells = new ParkCellMap( 6, 6 );
		cells.SetType( 3, 3, ParkCellTypes.Entrance );
		cells.SetOwner( 3, 3, Owner );
		cells.SetLinks( 3, 3, GuestPathGrid.LinkValue( 2 ) );
		Assert.AreEqual( (3, 4), ObjectAccessCells.EmptyQueueFront( cells, Points, Owner ) );
		return cells;
	}

	private static byte[] Payload( MapFile map )
	{
		byte[] Cell( int x, int y )
		{
			var cell = OriginalParkImportTests.CreateCellRecord( 0, occupied: (x is 3 or 4) && (y is 2 or 3) );
			if ( (x, y) == (3, 4) )
			{
				BinaryPrimitives.WriteUInt32LittleEndian( cell.AsSpan( SaveCell.MapTypeOffset ), (uint)ParkCellType.Queue );
				BinaryPrimitives.WriteUInt16LittleEndian( cell.AsSpan( SaveCell.ParentIdOffset ), Owner );
				cell[SaveCell.PathConnectionsOffset] = GuestPathGrid.LinkValue( 0 );
				cell[SaveCell.DirectionOffset] = GuestPathGrid.LinkValue( 0 );
			}
			return cell;
		}
		var rides = SaveTestPayloads.RideSystem( new[] { SaveTestPayloads.RideSystemRecord( 1411, 3, 3, 2, 2, SaveObject.PlacedObjectKind, 1, rotation: 90 ) } );
		return SaveTestPayloads.Payload( SaveTestPayloads.World( SaveTestPayloads.ParkVariables(), SaveTestPayloads.Grid( map, Cell ), SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ) ), rides );
	}

	[TestMethod]
	public void NoEligibleFrontDoesNotReadOptionalSource()
	{
		var cells = ResumedCells();
		cells.SetType( 3, 4, ParkCellType.Path );
		var version = cells.Version;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, Points, 1411, 90, true, Owner,
			() => throw new AssertFailedException( "A newer park must not read the source." ) ) );
		Assert.AreEqual( version, cells.Version );
	}

	[DataTestMethod]
	[DataRow( "missing" )]
	[DataRow( "container" )]
	[DataRow( "payload" )]
	[DataRow( "unsupported" )]
	public void UnavailableOptionalSourceLeavesResumedCellsUnchanged( string failure )
	{
		if ( failure != "missing" )
		{
			var bytes = failure == "container" ? new byte[] { 1, 2, 3 } : SaveReaderTests.CreateContainer( new byte[] { 1, 2, 3 } );
			if ( failure == "unsupported" ) bytes[0x609] = 1; // online container flag
			File.WriteAllBytes( Path.Combine( root, "levels/jungle/Easymode.TPWI" ), bytes );
		}
		var cells = ResumedCells();
		var version = cells.Version;
		var reads = 0;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, Points, 1411, 90, true, Owner,
			() => { reads++; return OriginalPark.ReadShippedSave( "jungle", Map() ); } ) );
		Assert.AreEqual( 1, reads );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( (3, 4), ObjectAccessCells.EmptyQueueFront( cells, Points, Owner ) );
	}

	[TestMethod]
	public void WrongRotationReadsSourceButOnlyMatchingRotationRestoresCells()
	{
		var map = Map();
		var source = OriginalParkImport.Import( Payload( map ), map );
		Assert.AreEqual( 1, source.PlacedObjects.Count );
		var cells = ResumedCells();
		var version = cells.Version;
		var reads = 0;
		OriginalParkImport Read() { reads++; return source; }
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, Points, 1411, 180, true, Owner, Read ) );
		Assert.AreEqual( 1, reads );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( (3, 4), ObjectAccessCells.EmptyQueueFront( cells, Points, Owner ) );
		Assert.AreEqual( 1, Level.RestoreLegacySavedQueue( cells, Points, 1411, 90, true, Owner, Read ) );
		Assert.AreEqual( 2, reads );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 3, 4 ) );
		Assert.AreEqual( Owner, cells.OwnerAt( 3, 4 ) );
	}


	private sealed class SourceOverride : IFileOverrides
	{
		public string Path = string.Empty;
		public bool FailWithProgrammingError;
		public bool TryGetFile( string relativePath, out string absolutePath )
		{
			if ( FailWithProgrammingError ) throw new InvalidOperationException( "Synthetic programming error" );
			absolutePath = Path;
			return true;
		}
	}

	[TestMethod]
	public void SourceIoFailureLeavesResumedCellsUnchanged()
	{
		// The listed source exists, but its override disappeared before OpenRead.
		File.WriteAllBytes( Path.Combine( root, "levels/jungle/Easymode.TPWI" ), new byte[] { 1 } );
		FileSystem.Overrides = new SourceOverride { Path = Path.Combine( root, "missing.TPWI" ) };
		var cells = ResumedCells();
		var version = cells.Version;
		Assert.AreEqual( 0, Level.RestoreLegacySavedQueue( cells, Points, 1411, 90, true, Owner,
			() => OriginalPark.ReadShippedSave( "jungle", Map() ) ) );
		Assert.AreEqual( version, cells.Version );
		Assert.AreEqual( (3, 4), ObjectAccessCells.EmptyQueueFront( cells, Points, Owner ) );
	}

	[TestMethod]
	public void OptionalSourceDoesNotHideProgrammingErrors()
	{
		File.WriteAllBytes( Path.Combine( root, "levels/jungle/Easymode.TPWI" ), new byte[] { 1 } );
		FileSystem.Overrides = new SourceOverride { FailWithProgrammingError = true };
		Assert.ThrowsException<InvalidOperationException>( () => OriginalPark.ReadShippedSave( "jungle", Map() ) );
	}

	[TestMethod]
	public void MandatoryImportStillReportsMalformedPayload()
	{
		Assert.ThrowsException<InvalidDataException>( () => OriginalPark.ImportOriginalSave( "jungle", new byte[] { 1, 2, 3 }, Map() ) );
	}
}
