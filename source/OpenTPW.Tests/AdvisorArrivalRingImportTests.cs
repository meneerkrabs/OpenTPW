using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The arrival group ring of an original save (park <c>+0x216D8</c>, serialized by the analyser after the recent-stays ring), read oldest first, and the advisor taking it over for a park from an original save.
/// The synthetic cases need no game data; the corpus case reads <c>OPENTPW_TPWS_SAVES</c>.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorArrivalRingImportTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	private static SaveThing Thing( int bytes ) => new( 5, SaveThingModel.ParkAnalyser, 0, 0, 0, bytes );

	private static byte[] Body( int current, int entries, int wrapped, params int[] words )
	{
		var body = SaveNewParkThings.ParkAnalyser( 0, 0 );
		var offset = SaveArrivalRing.BodyOffset;
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset, 4 ), current );
		BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset + 4, 4 ), entries );
		body[offset + 8] = (byte)wrapped;
		for ( var slot = 0; slot < words.Length; slot++ )
			BinaryPrimitives.WriteInt32LittleEndian( body.AsSpan( offset + 13 + slot * 4, 4 ), words[slot] );
		return body;
	}

	private static IReadOnlyList<int> Read( byte[] body ) => SaveArrivalRing.Read( body, Thing( body.Length ) );

	[TestMethod]
	public void TheRingOfANewParkIsEmptyAndTheOffsetsAgreeWithTheAnalyserBody()
	{
		Assert.AreEqual( SaveParkStatistics.BodyBytes - 20 * 60 - 16 - 48 - 12, SaveArrivalRing.BodyOffset + SaveArrivalRing.Bytes );
		Assert.AreEqual( 0, Read( SaveNewParkThings.ParkAnalyser( 0, 0 ) ).Count );
	}

	[TestMethod]
	public void AnUnwrappedRingReadsSlotsZeroToCurrentAndAWrappedOneStartsAfterCurrent()
	{
		CollectionAssert.AreEqual( new[] { 40 }, Read( Body( 0, 5, 0, 40 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 10, 20, 30 }, Read( Body( 2, 5, 0, 10, 20, 30, 999, 998 ) ).ToArray(), "slots after current are stale" );
		CollectionAssert.AreEqual( new[] { 30, 40, 50, 10, 20 }, Read( Body( 1, 5, 1, 10, 20, 30, 40, 50 ) ).ToArray(), "oldest first: slots 2 to 4, then 0 and 1" );
		CollectionAssert.AreEqual( new[] { -1, 2, 3, 4, 5 }, Read( Body( 4, 5, 1, -1, 2, 3, 4, 5 ) ).ToArray(), "wrapped with current at the last slot: 0 to 4, signed sizes kept" );
	}

	[TestMethod]
	public void ARingThatIsNotTheFiveEntryShapeIsRefused()
	{
		Assert.ThrowsException<InvalidDataException>( () => Read( Body( 0, 4, 0, 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Body( 5, 5, 0, 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Body( -1, 5, 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => Read( Body( 0, 5, 2, 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveArrivalRing.Read( new byte[10], Thing( 10 ) ) );
	}

	[TestMethod]
	public void AnAdvisorTakesAnEmptySnapshotRingAsFullOnlyForAParkWithAdmissions()
	{
		var established = EconomyTestData.Park();
		established.Counters.Add( ParkCounters.Admissions, 5 );
		var advisor = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0 );
		advisor.AttachEconomy( established );
		advisor.RestoreSnapshot( advisor.CaptureSnapshot() with { ArrivalWaves = Array.Empty<int>() } );
		Assert.AreEqual( AdvisorArrivalRing.Capacity, advisor.ArrivalWaves.Count, "The existing host snapshot policy treats explicit empty plus admissions as legacy." );
		var fresh = new AutomaticAdvisor( new AdvisorController( AdvisorSnapshotTests.Balance() ), () => 0 );
		fresh.AttachEconomy( EconomyTestData.Park() );
		fresh.RestoreSnapshot( fresh.CaptureSnapshot() with { ArrivalWaves = Array.Empty<int>() } );
		Assert.AreEqual( 0, fresh.ArrivalWaves.Count );
	}

	[TestMethod]
	public void EveryOriginalSavesRingIsReadAndEstablishedParksHaveAFullOne()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" )
			.Select( file => (Path: file, Theme: Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant()) )
			.Where( file => Themes.Contains( file.Theme ) ).OrderBy( file => file.Path, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var summary = new List<string>();
		var full = 0;
		foreach ( var (path, theme) in files )
		{
			var name = $"{theme}/{Path.GetFileName( path )}";
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( payload, map );
			Assert.IsNotNull( park.ArrivalGroups, name );
			Assert.IsTrue( park.ArrivalGroups!.Count <= AdvisorArrivalRing.Capacity, name );
			if ( park.ArrivalGroups.Count == AdvisorArrivalRing.Capacity )
				full++;
			summary.Add( $"{name}: [{string.Join( ", ", park.ArrivalGroups )}]" );
		}
		Console.WriteLine( string.Join( "\n", summary ) );
		Assert.IsTrue( full > 0, "an established original park has a full ring:\n" + string.Join( "\n", summary ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}
}
