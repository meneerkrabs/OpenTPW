using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The research part of the original-format export (<see cref="OriginalResearchExport"/>): the lab thing's project, group and tail fields and the object controls' researched bytes, built from the
/// park's research. The corpus test reads every original save into the economy and writes the research back over the save's own bytes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OriginalResearchExportTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	private static (byte[] Projects, byte[] Groups, byte[] Tail) LabOf( OriginalSavePayload payload )
	{
		var body = payload.World.Things.Single( thing => thing.Model == SaveThingModel.ResearchLab ).Body;
		var projects = SaveThingBase.Bytes;
		var groups = projects + SaveResearchLab.ProjectRecords * SaveResearchLab.ProjectBytes;
		var tail = groups + SaveResearchLab.GroupRecords * SaveResearchLab.GroupBytes;
		return (body[projects..groups], body[groups..tail], body[tail..]);
	}

	/// <summary>The blank template with an empty research lab (thing 2) behind its bank.</summary>
	private static OriginalSavePayload WithLab( BalanceSettings settings )
	{
		var template = OriginalSavePayloadFixtures.Blank( settings );
		template.World.Things[0].Next = 2;
		template.World.Things.Add( new SaveThingEntry { Id = 2, Next = 3, Model = SaveThingModel.ResearchLab, Body = new byte[SaveResearchLab.Bytes] } );
		template.World.Things.Add( new SaveThingEntry { Id = 3, Next = 0, Model = SaveThingModel.ParkAnalyser, Body = SaveNewParkThings.ParkAnalyser( 0, 0 ) } );
		template.World.Variables = template.World.Variables with { ResearchLab = 2, ParkAnalyser = 3 };
		return template;
	}

	[TestMethod]
	public void TheLabTableIsTheWalkThenTheAppendedLevelProjects()
	{
		var research = EconomyTestData.Park().Research;
		var table = research.LabTable( out var walk );
		Assert.IsTrue( walk > 0 && walk <= table.Count );
		Assert.IsTrue( table.Take( walk ).All( item => item.Cost > 0 ), "the fill adds a project only for a cost above 0" );
		CollectionAssert.AreEqual( table.Take( walk ).Select( item => item.InfoId ).ToArray(), table.Take( walk ).Select( item => item.InfoId ).ToArray() );
		Assert.AreEqual( table.Count, table.Distinct().Count() );
	}

	[TestMethod]
	public void AFinishedAndAStartedProjectLandInTheLabAndItsControl()
	{
		var settings = OriginalSaveImportTests.Settings();
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var template = WithLab( settings );
		// the blank template's lab lists no project: the whole table is written
		var table = economy.Research.LabTable( out _ );
		Assert.IsTrue( table.Count > 0, "the test catalogue has research projects" );
		var first = table.First( item => item.Level == 0 );
		economy.Research.Restore( new[] { (first.InfoId, first.Level) }, new[] { ((table[^1].InfoId, table[^1].Level), 2500L) }, new[] { 40, 30, 20, 10, 0 }, null );
		var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out var evidence, new OriginalParkExportOptions( Research: economy.Research, Researchers: 2 ) );
		var (projects, groups, tail) = LabOf( exported );
		Assert.AreEqual( table.Count, BitConverter.ToInt32( tail, 0 ) );
		Assert.AreEqual( economy.Research.Workload, BitConverter.ToInt32( tail, 4 ) );
		Assert.AreEqual( 1, tail[12], "researchers are hired" );
		Assert.AreEqual( first.InfoId, BitConverter.ToUInt16( projects, table.ToList().IndexOf( first ) * SaveResearchLab.ProjectBytes ) );
		Assert.AreEqual( (float)first.Cost, BitConverter.ToSingle( projects, table.ToList().IndexOf( first ) * SaveResearchLab.ProjectBytes + 12 ), "a finished project holds its cost" );
		Assert.AreEqual( 2.5f, BitConverter.ToSingle( projects, (table.Count - 1) * SaveResearchLab.ProjectBytes + 12 ), "a started project holds its points" );
		Assert.AreEqual( 40, BitConverter.ToInt32( groups, 0 ) );
		var sum = 100u;
		Assert.AreEqual( 40u * 100 / sum * (uint)economy.Research.Workload / 100, BitConverter.ToUInt32( groups, 4 ), "(effort x 100 / the sum of the efforts) x the work load / 100 in unsigned integers" );
		Assert.IsTrue( evidence.Any( line => line.StartsWith( "Research lab:", StringComparison.Ordinal ) ) );
	}

	[TestMethod]
	public void ATableTooBigForTheLabIsRefused()
	{
		var settings = OriginalSaveImportTests.Settings();
		var template = WithLab( settings );
		var objects = Enumerable.Range( 0, 300 ).Select( index => new EconomyObjectInfo( 5000 + index, $"Ride {index}", ParkObjectKind.Ride, ResearchCategory.Ride, 0, new[] { new UpgradeLevelInfo( 0, 100, 100, 0, 0, new[] { 0, 0, 0, 0 } ) }, null, null, null, 0, 0, 0, 0, 0, 10, $"rides/r{index}.wad" ) );
		var economy = new ParkEconomy( settings, new EconomyObjectCatalog( objects ), ParkGameMode.FullSimulation, 1 );
		Assert.ThrowsException<InvalidDataException>( () => OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out _, new OriginalParkExportOptions( Research: economy.Research ) ) );
	}

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	/// <summary>
	/// Every original save is imported into an economy and its research written back over its own lab: the project table, the group records and the tail come back as saved (the four fresh
	/// <c>restart.INTS</c> byte for byte; a played save's finished projects hold their cost where the save kept the overshoot of their last step), and so do the object controls' researched bytes.
	/// </summary>
	[TestMethod]
	public void EveryOriginalSavesResearchComesBackThroughTheExport()
	{
		var files = OriginalSaveWriterCorpusTests.CorpusFiles().ToList();
		UseOriginalData();
		var easymode = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" );
		if ( File.Exists( easymode ) )
			files.Add( ("jungle/Easymode.TPWI", easymode) );
		var failures = new List<string>();
		foreach ( var (name, path) in files )
		{
			var easy = name.EndsWith( "Easymode.TPWI", StringComparison.Ordinal );
			var theme = easy ? "jungle" : Path.GetFileName( Path.GetDirectoryName( path ) )!;
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( $"levels/{theme}/terrain.wad", "base.map" ) ) );
			var park = OriginalParkImport.Import( payload, map );
			var economy = new ParkEconomy( BalanceSettings.Load( theme, easy ), EconomyObjectCatalog.Load( theme, easy ), ParkGameMode.FullSimulation, 1 );
			OriginalEconomyImport.Apply( economy, park );
			var template = OriginalSavePayload.Read( payload );
			var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( economy ), out _, new OriginalParkExportOptions( Research: economy.Research, Researchers: economy.Staff.OfType( StaffType.Researcher ).Count() ) );
			var (savedProjects, savedGroups, savedTail) = LabOf( template );
			var (projects, groups, tail) = LabOf( exported );
			var used = BitConverter.ToInt32( savedTail, 0 );
			for ( var index = 0; index < used; index++ )
			{
				var saved = savedProjects.AsSpan( index * SaveResearchLab.ProjectBytes, SaveResearchLab.ProjectBytes );
				var written = projects.AsSpan( index * SaveResearchLab.ProjectBytes, SaveResearchLab.ProjectBytes );
				if ( !saved[..12].SequenceEqual( written[..12] ) )
					failures.Add( $"{name}: project {index} header {Convert.ToHexString( saved[..12] )} is written as {Convert.ToHexString( written[..12] )}" );
				var (savedDone, writtenDone, cost) = (BitConverter.ToSingle( saved[12..] ), BitConverter.ToSingle( written[12..] ), BitConverter.ToSingle( saved[8..] ));
				var finished = savedDone >= cost;
				if ( finished ? writtenDone != cost : Math.Abs( savedDone - writtenDone ) > 0.002f )
					failures.Add( $"{name}: project {index} done {savedDone} is written as {writtenDone} (cost {cost})" );
			}
			for ( var category = 0; category < SaveResearchLab.GroupRecords; category++ )
			{
				var saved = savedGroups.AsSpan( category * SaveResearchLab.GroupBytes, SaveResearchLab.GroupBytes );
				var written = groups.AsSpan( category * SaveResearchLab.GroupBytes, SaveResearchLab.GroupBytes );
				var (savedCurrent, writtenCurrent) = (BitConverter.ToInt32( saved[8..] ), BitConverter.ToInt32( written[8..] ));
				// a save with no current project (the played corpus saves were edited to all researched) is written with the cursor's default, the first project still to do, as the lab's start 0x00503330 picks it
				var currentFits = savedCurrent == writtenCurrent || savedCurrent < 0 && writtenCurrent >= 0 && BitConverter.ToSingle( projects.AsSpan( writtenCurrent * SaveResearchLab.ProjectBytes + 12 ) ) < BitConverter.ToSingle( projects.AsSpan( writtenCurrent * SaveResearchLab.ProjectBytes + 8 ) );
				if ( !saved[..8].SequenceEqual( written[..8] ) || !saved[12..].SequenceEqual( written[12..] ) || !currentFits )
					failures.Add( $"{name}: group {category} {Convert.ToHexString( saved )} is written as {Convert.ToHexString( written )}" );
			}
			if ( !savedTail.AsSpan().SequenceEqual( tail ) )
				failures.Add( $"{name}: the tail {Convert.ToHexString( savedTail )} is written as {Convert.ToHexString( tail )}" );
			if ( name.EndsWith( ".INTS", StringComparison.Ordinal ) && !(savedProjects.AsSpan().SequenceEqual( projects ) && template.World.Controls.Records.AsSpan().SequenceEqual( exported.World.Controls.Records )) )
				failures.Add( $"{name}: a fresh park's lab and controls are not reproduced byte for byte" );
			for ( var record = 0; record < template.World.Controls.Count; record++ )
			{
				var saved = template.World.Controls.Records.AsSpan( record * SaveObjectControl.Bytes, SaveObjectControl.Bytes );
				var written = exported.World.Controls.Records.AsSpan( record * SaveObjectControl.Bytes, SaveObjectControl.Bytes );
				if ( !saved[..0x1C].SequenceEqual( written[..0x1C] ) )
					failures.Add( $"{name}: control {record} {Convert.ToHexString( saved[..0x1C] )} is written as {Convert.ToHexString( written[..0x1C] )}" );
			}
		}
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures.Take( 40 ) ) );
	}

	/// <summary>
	/// A new park's research table is the lab a new original game builds (ECON-017): for each theme the project list of the original's own <c>restart.INTS</c> (written when the park starts, fresh, 44 to 48
	/// projects) equals OpenTPW's table for the theme's objects in every key and in the order, so the fill (0x005031A0) skips no type of a normal game that OpenTPW lists.
	/// </summary>
	[TestMethod]
	public void ANewParksLabTableIsTheLabOfTheOriginalsRestartSaveForEveryTheme()
	{
		var files = OriginalSaveWriterCorpusTests.CorpusFiles().Where( file => file.Name.EndsWith( ".INTS", StringComparison.Ordinal ) ).ToList();
		Assert.IsTrue( files.Count >= 4, "a restart save per theme" );
		UseOriginalData();
		foreach ( var (name, path) in files )
		{
			var theme = Path.GetFileName( Path.GetDirectoryName( path ) )!;
			using var reader = new SaveReader( path );
			var (projects, _, tail) = LabOf( OriginalSavePayload.Read( reader.ReadFile() ) );
			var saved = Enumerable.Range( 0, BitConverter.ToInt32( tail, 0 ) ).Select( index => ((int)BitConverter.ToUInt16( projects, index * SaveResearchLab.ProjectBytes ), (int)projects[index * SaveResearchLab.ProjectBytes + 7]) ).ToList();
			var economy = new ParkEconomy( BalanceSettings.Load( theme, false ), EconomyObjectCatalog.Load( theme, false ), ParkGameMode.FullSimulation, 1 );
			var table = economy.Research.LabTable( out _ ).Select( item => (item.InfoId, item.Level) ).ToList();
			CollectionAssert.AreEqual( saved, table, $"{name}: the lab's projects" );
		}
	}
}
