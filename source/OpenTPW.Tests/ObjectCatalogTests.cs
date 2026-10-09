using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ObjectSettingsTests
{
	[TestMethod]
	public void ParsesQuotedNamesBlocksCommentsAndLayers()
	{
		var defaults = new ObjectSettingsFile( "Info.HasQueue\t1\t\tBoolean comment\r\nUpgrades[0].CostOfUpgrade\t1000\tcash cost\r\nUsageInfo.ExcitementLevel 30 // Ben's fault\r\n", "Rides.sam" );
		var main = new ObjectSettingsFile( "#\n#\tRide\n#\nInfo.Id\t\t\t1110\nInfo.Name\t\t\"Inca Totem\"\nInfo.Shape\n---\n*S*\n***\n*2*\n---\n\n\nInfo.Hoarding\n---\nF.7\nL.J\n---\nUpgrades[0].CostOfUpgrade\t\t3250\nUpgrades[1].InitCapacity 8\nInfo.PreviewAnimType\t\"m\"\t\tm = main loop\n", "Totem.sam" );
		Assert.AreEqual( "Inca Totem", main.Values["Info.Name"] );
		CollectionAssert.AreEqual( new[] { "*S*", "***", "*2*" }, main.Blocks["Info.Shape"].ToArray() );
		CollectionAssert.AreEqual( new[] { "F.7", "L.J" }, main.Blocks["Info.Hoarding"].ToArray() );
		Assert.AreEqual( "30", defaults.Values["UsageInfo.ExcitementLevel"] );
		var settings = new ObjectSettings( new[] { defaults, main } );
		Assert.AreEqual( 1110, settings.GetInt( "info.id" ) );
		Assert.AreEqual( 3250, settings.GetInt( "Upgrades[0].CostOfUpgrade" ) );
		Assert.IsTrue( settings.GetBool( "Info.HasQueue" ) );
		Assert.AreEqual( "m", settings["Info.PreviewAnimType"] );
		Assert.AreEqual( 7, settings.GetInt( "Missing", 7 ) );
		Assert.ThrowsException<InvalidDataException>( () => new ObjectSettingsFile( "Info.Shape\n---\n**\n" ) );
	}

	[TestMethod]
	public void BonusNameFilesAreUtf16Sections()
	{
		var text = "NAME\r\nSlither\r\n\r\nSIGNA\r\nSlither\r\n\r\nSIGNB\r\n\r\n";
		var bytes = new byte[] { 0xFF, 0xFE }.Concat( Encoding.Unicode.GetBytes( text ) ).ToArray();
		var sections = BonusNames.Parse( bytes );
		Assert.AreEqual( "Slither", sections["NAME"] );
		Assert.AreEqual( "Slither", sections["SIGNA"] );
		Assert.AreEqual( "", sections["SIGNB"] );
		Assert.AreEqual( 0, BonusNames.Parse( Array.Empty<byte>() ).Count );
		Assert.AreEqual( "Caf\u00e9", BonusNames.Parse( new byte[] { 0xFF, 0xFE }.Concat( Encoding.Unicode.GetBytes( "NAME\nCaf\u00e9\n" ) ).ToArray() )["NAME"] );
	}

	[TestMethod]
	public void ShapeRowsCountFromTheLastRowAndClassifySymbols()
	{
		var shape = new ObjectShape( new[] { "**", "*2" } );
		Assert.AreEqual( (2, 2), (shape.Width, shape.Height) );
		var entrance = shape.Entrances.Single();
		Assert.AreEqual( (1, 0), (entrance.U, entrance.V) );
		var coaster = new ObjectShape( new[] { "****", "<**>", "*2N*" } );
		Assert.AreEqual( ObjectCellKind.Exit, coaster.Exits.Single().Kind );
		Assert.AreEqual( (2, 0), (coaster.Exits.Single().U, coaster.Exits.Single().V) );
		Assert.AreEqual( 2, coaster.Cells.Count( cell => cell.Kind == ObjectCellKind.TrackConnection ) );
		Assert.AreEqual( 0, new ObjectShape( new[] { "..", ".." } ).OccupiedCells.Count() );
		Assert.ThrowsException<InvalidDataException>( () => new ObjectShape( new[] { "*x" } ) );
	}

	[TestMethod]
	public void FootprintRotationMatchesTheEasymodeRecords()
	{
		// Staff Room "**/*2" saved at (58, 16) rotation 90 covers x 58..59, y 15..16 with the entrance at (58, 15).
		var staff = new ObjectShape( new[] { "**", "*2" } );
		var cells = ObjectFootprint.GetCells( staff, 58, 16, 90 ).ToArray();
		CollectionAssert.AreEquivalent( new[] { (58, 15), (58, 16), (59, 15), (59, 16) }, cells.Select( cell => (cell.X, cell.Y) ).ToArray() );
		Assert.AreEqual( (58, 15), cells.Where( cell => cell.Cell.Kind == ObjectCellKind.Entrance ).Select( cell => (cell.X, cell.Y) ).Single() );
		var staffAccess = ObjectFootprint.GetAccessPoints( staff, 58, 16, 90 ).Single();
		Assert.AreEqual( (58, 15, 57, 15), (staffAccess.X, staffAccess.Y, staffAccess.OutsideX, staffAccess.OutsideY) );
		// Small Toilet "2" at rotation 270 opens towards +X (path at x + 1).
		var toilet = ObjectFootprint.GetAccessPoints( new ObjectShape( new[] { "2" } ), 55, 17, 270 ).Single();
		Assert.AreEqual( (56, 17), (toilet.OutsideX, toilet.OutsideY) );
		// Belly Bounce: entrance on the last text row joins the queue at -Y, exit "S" on the first row the path at +Y.
		var bouncy = ObjectFootprint.GetAccessPoints( new ObjectShape( new[] { "*S*", "***", "***", "*2*" } ), 51, 23, 0 ).ToArray();
		Assert.AreEqual( (52, 23, 52, 22), bouncy.Where( point => point.Kind == ObjectCellKind.Entrance ).Select( point => (point.X, point.Y, point.OutsideX, point.OutsideY) ).Single() );
		Assert.AreEqual( (52, 26, 52, 27), bouncy.Where( point => point.Kind == ObjectCellKind.Exit ).Select( point => (point.X, point.Y, point.OutsideX, point.OutsideY) ).Single() );
		// Drinks Shop "**/2*" at (43, 30) rotation 0: entrance (43, 30), reached from the path at (43, 29).
		var shop = new ObjectShape( new[] { "**", "2*" } );
		Assert.AreEqual( (43, 30), ObjectFootprint.GetCells( shop, 43, 30, 0 ).Single( cell => cell.Cell.Kind == ObjectCellKind.Entrance ) is var e ? (e.X, e.Y) : default );
		// Rotations of a non-square shape keep its cell count and stay rigid; bounds anchor round-trips.
		var log = new ObjectShape( new[] { "***", "*2*" } );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var placed = ObjectFootprint.GetCells( log, 10, 10, rotation ).Select( cell => (cell.X, cell.Y) ).ToArray();
			Assert.AreEqual( 6, placed.Distinct().Count() );
			var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( log, 10, 10, rotation );
			Assert.AreEqual( rotation % 180 == 0 ? (2, 1) : (1, 2), (maxX - minX, maxY - minY) );
			Assert.IsTrue( placed.All( cell => cell.X >= minX && cell.X <= maxX && cell.Y >= minY && cell.Y <= maxY ) );
			var anchor = ObjectFootprint.AnchorForBounds( log, 3, 4, rotation );
			Assert.AreEqual( (3, 4), ObjectFootprint.GetBounds( log, anchor.X, anchor.Y, rotation ) is var b ? (b.MinX, b.MinY) : default );
			// Continuous mapping agrees with the cell mapping at cell centres.
			foreach ( var cell in log.OccupiedCells )
			{
				var centre = ObjectFootprint.ToGrid( 10, 10, rotation, new System.Numerics.Vector2( cell.U + 0.5f, cell.V + 0.5f ) );
				Assert.AreEqual( ObjectFootprint.ToGrid( 10, 10, rotation, cell.U, cell.V ), ((int)MathF.Floor( centre.X ), (int)MathF.Floor( centre.Y )) );
			}
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ObjectFootprint.ToGrid( 0, 0, 45, 0, 0 ) );
	}

	[TestMethod]
	public void AnimationNumbersSelectSuffixLettersAndVariants()
	{
		var members = new[] { "/a/totem.MD2", "/a/totemc.MD2", "/a/totemm1.MD2", "/a/totemm2.MD2", "/a/totemm10.MD2", "/a/totemb1.MD2", "/a/totemr.MD2", "/a/Ptotem.MD2", "/a/totemx.MD2" };
		var animations = ObjectAnimations.Find( "totem", members );
		Assert.AreEqual( 6, animations.Count );
		Assert.AreEqual( "/a/totemm1.MD2", ObjectAnimations.Resolve( animations, 5, 0 )!.Path );
		Assert.AreEqual( "/a/totemm10.MD2", ObjectAnimations.Resolve( animations, 5, 9 )!.Path );
		Assert.AreEqual( "/a/totemc.MD2", ObjectAnimations.Resolve( animations, 0, 0 )!.Path );
		Assert.AreEqual( "/a/totemb1.MD2", ObjectAnimations.Resolve( animations, 9, 0 )!.Path );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 0, 1 ) );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 1, 0 ) );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 2, 0 ) );
		foreach ( var value in Enum.GetValues<ScriptDefs.Animations>() )
			Assert.AreEqual( (int)value, ObjectAnimations.GetAnimation( ObjectAnimations.GetLetter( (int)value )!.Value ) );
	}
}

/// <summary>Catalog of the original objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectCatalogCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	internal static BaseFileSystem? UseOriginalData( out BaseFileSystem previous )
	{
		previous = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !Directory.Exists( Path.Combine( dataPath, "levels", "jungle", "rides" ) ) )
			return null;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return FileSystem;
	}

	[TestInitialize]
	public void Initialize()
	{
		if ( UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object catalog corpus." );
		originalFileSystem = previous;
		initialized = true;
		// The pinned counts are for the game data alone; bonus content is tested separately.
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestMethod]
	public void EveryObjectScriptRunsInItsParkWithOriginalAnimations()
	{
		const int seconds = 60;
		var report = new StringBuilder();
		int objects = 0, animating = 0, played = 0;
		var unresolved = new SortedSet<string>( StringComparer.Ordinal );
		foreach ( var theme in ObjectCatalog.Themes )
		{
			// Every scripted object of a theme in one shared script world, like objects in one park.
			var world = new RideScriptWorld();
			var runtimes = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.ScriptPath != null )
				.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, seed: index + 1, open: true ) ).ToArray();
			for ( var tick = 0; tick < seconds * FixedStepClock.TicksPerSecond; tick++ )
			{
				foreach ( var runtime in runtimes )
					runtime.Simulate( FixedStepClock.TickDuration );
			}
			foreach ( var runtime in runtimes )
			{
				objects++;
				Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, $"{runtime.Entry}: {runtime.Script.FaultMessage}" );
				if ( runtime.AnimationsPlayed > 0 )
					animating++;
				played += runtime.AnimationsPlayed;
				foreach ( var request in runtime.UnresolvedAnimations )
					unresolved.Add( $"{runtime.Entry.ArchivePath} {request}" );
				report.AppendLine( $"{runtime.Entry} {runtime.Script.State} anims {runtime.AnimationsPlayed} channels {runtime.Animator.ActiveChannels} unresolved [{string.Join( " ", runtime.UnresolvedAnimations )}] cycles {runtime.CompletedCycles}" );
			}
			foreach ( var runtime in runtimes )
				runtime.Stop();
		}
		Assert.AreEqual( (262, 258), (objects, animating) );
		// Members these shops/features request but their archives do not ship.
		CollectionAssert.AreEqual( new[] { "/levels/fantasy/features/royaloo 5/0", "/levels/fantasy/shops/fries 5/0", "/levels/fantasy/shops/icecream 5/0", "/levels/fantasy/shops/purse 2/0", "/levels/fantasy/shops/purse 5/0", "/levels/space/features/crys_b 5/0" }, unresolved.ToArray() );
		report.Insert( 0, $"{objects} scripted objects, {animating} played original animations ({played} plays), {unresolved.Count} unresolved requests: {string.Join( ", ", unresolved )}\n" );
		var output = Environment.GetEnvironmentVariable( "OPENTPW_OBJECT_REPORT_OUT" );
		if ( !string.IsNullOrEmpty( output ) )
			File.WriteAllText( output + ".scripts", report.ToString() );
	}

	[TestMethod]
	public void CatalogCountsPerThemeArePinned()
	{
		var expected = new Dictionary<string, (int Entries, int Buildable, int Rides, int Shops, int Sideshows, int Features, int Upgrades, int Named, int Scripts)>
		{
			["jungle"] = (70, 58, 18, 8, 5, 36, 3, 58, 67),
			["hallow"] = (70, 58, 20, 8, 4, 35, 3, 56, 67),
			["space"] = (68, 56, 21, 8, 4, 32, 3, 54, 65),
			["fantasy"] = (66, 55, 17, 8, 4, 35, 2, 52, 63)
		};
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			int Count( ObjectCategory category ) => catalog.Entries.Count( entry => entry.Category == category );
			Assert.AreEqual( expected[theme], (catalog.Entries.Count, catalog.Buildable.Count(), Count( ObjectCategory.Ride ), Count( ObjectCategory.Shop ), Count( ObjectCategory.Sideshow ),
				Count( ObjectCategory.Feature ), Count( ObjectCategory.Upgrade ), catalog.Entries.Count( entry => entry.ObjectNameIndex != null ), catalog.Entries.Count( entry => entry.ScriptPath != null )), theme );
			// Six fixed items per theme (bus, gates, seaplane, lights, ferry, end); three tools (mystery, buy/clear land).
			Assert.AreEqual( 6, catalog.Entries.Count( entry => entry.IsFixedItem ), theme );
			Assert.AreEqual( 3, catalog.Entries.Count( entry => entry.IsTool ), theme );
			Assert.IsTrue( catalog.Buildable.All( entry => entry.WhichUIType is >= 0 and <= 3 && entry.Shape.OccupiedCells.Any() ), theme );
		}
		var jungle = ObjectCatalog.Load( "jungle" );
		var totem = jungle.Get( 1110 );
		Assert.AreEqual( ("Inca Totem", 28, 2, "Inca Totem"), (totem.SettingsName, totem.ObjectNameIndex, totem.ObjectNameLength, totem.DisplayName) );
		Assert.AreEqual( (3, 4, 3250, 6, 12, 4), (totem.Shape.Width, totem.Shape.Height, totem.BuildCost, totem.InitialCapacity, totem.RideTypeIndex, totem.NumSimultaneousAnimations) );
		Assert.AreEqual( "/levels/jungle/rides/totem/totemm1.MD2", totem.ResolveAnimation( 5, 0 )!.Path );
		Assert.AreEqual( "/levels/jungle/rides/totem/Ptotemm.MD2", totem.PreviewAnimationPath );
		Assert.AreEqual( "/levels/jungle/rides/totem/Totem.RSE", totem.ScriptPath );
		// Category defaults apply under the object's own values (Rides.sam HasQueue, Totem.sam capacity).
		Assert.IsTrue( totem.HasQueue && totem.IsChoosable && totem.WhichUIType == 0 );
		Assert.AreEqual( ("The Hot Pot", 34, 2), (jungle.Get( 1140 ).DisplayName, jungle.Get( 1140 ).ObjectNameIndex, jungle.Get( 1140 ).ObjectNameLength) );
		var drinks = jungle.Get( 1203 ).Economy;
		Assert.AreEqual( (650, 30, 20), (drinks.BuildCost, drinks.PricePerUse, drinks.CostOfGoods) );
		Assert.AreEqual( ObjectCategory.Shop, jungle.Get( 1203 ).Category );
		Assert.IsTrue( jungle.Get( 1601 ).IsFixedItem && !jungle.Get( 1601 ).IsBuildable );
		Assert.ThrowsException<KeyNotFoundException>( () => jungle.Get( 17302 ) );
	}

	[TestMethod]
	public void EveryEntryResolvesItsModelAnimationsAndTextures()
	{
		var missingTextures = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		int entries = 0, slots = 0, animations = 0, split = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				entries++;
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind, entry.ToString() );
				Assert.IsTrue( model.Meshes.Count > 0, entry.ToString() );
				var parts = ObjectRenderParts.Build( entry, model );
				Assert.IsTrue( parts.Count > 0 && parts.All( part => part.Textures.Length is > 0 and <= 16 && part.Indices.All( index => index < part.Vertices.Length ) ), entry.ToString() );
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsTrue( ObjectAssets.GetUnrenderableReason( mesh ) is null or "more than 16 texture slots", $"{entry.ModelPath}:{mesh.Name}" );
					if ( mesh.Materials.Length > 16 )
						split++;
					foreach ( var material in mesh.Materials )
					{
						slots++;
						if ( ObjectAssets.ResolveTexturePath( entry, material.Name ) == null )
							missingTextures.Add( $"{entry.ArchivePath}:{material.Name}" );
					}
				}
				foreach ( var animation in entry.Animations )
				{
					var clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path );
					Assert.AreEqual( ModelFileKind.Animation, clip.Kind, animation.Path );
					// Every clip binds to the main model's nodes.
					new ObjectAnimator( model ).Play( 0, clip.Clip!, animation.Name, false );
					animations++;
				}
				if ( entry.PreviewModelPath != null )
					Assert.AreEqual( ModelFileKind.Geometry, ObjectAssets.LoadModel( entry.FileSystem, entry.PreviewModelPath ).Kind, entry.PreviewModelPath );
			}
		}
		Assert.AreEqual( (274, 911, 31), (entries, animations, split) );
		Assert.AreEqual( 5629, slots );
		// The ogre upgrade has an unnamed slot and the space rocket a slot whose texture is not shipped.
		CollectionAssert.AreEqual( new[] { "/levels/hallow/upgrades/ogre:", "/levels/space/rides/rocket:s_leg3" }, missingTextures.ToArray() );
	}

	[TestMethod]
	public void EasymodeObjectsMatchTheirCatalogShapesAndAccessCells()
	{
		var park = OriginalPark.Load( "jungle" );
		var save = park.Save!;
		var catalog = ObjectCatalog.Load( "jungle" );
		foreach ( var item in save.PlacedObjects )
		{
			var entry = catalog.Get( item.Record.InfoId );
			// The saved size equals the shape size; the catalog footprint equals the verified saved footprint.
			Assert.AreEqual( (entry.Shape.Width, entry.Shape.Height), (item.Record.Width, item.Record.Height), entry.ToString() );
			var cells = ObjectFootprint.GetCells( entry.Shape, item.Record.X, item.Record.Y, item.Record.Rotation ).Select( cell => (cell.X, cell.Y) ).OrderBy( cell => cell ).ToArray();
			var expected = Enumerable.Range( item.MinX, item.MaxX - item.MinX + 1 ).SelectMany( x => Enumerable.Range( item.MinY, item.MaxY - item.MinY + 1 ).Select( y => (x, y) ) ).OrderBy( cell => cell ).ToArray();
			CollectionAssert.AreEqual( expected, cells, entry.ToString() );
			// Every entrance/exit opens onto a path cell or an occupied non-object cell (the Belly Bounce queue).
			foreach ( var point in ObjectFootprint.GetAccessPoints( entry.Shape, item.Record.X, item.Record.Y, item.Record.Rotation ) )
			{
				var outside = save.Cells[point.OutsideX, point.OutsideY];
				Assert.IsTrue( outside.IsPath || (outside.IsOccupied && !save.IsOccupiedByObject( point.OutsideX, point.OutsideY )), $"{entry} {point}" );
			}
		}
		Assert.AreEqual( 9, save.PlacedObjects.Sum( item => ObjectFootprint.GetAccessPoints( catalog.Get( item.Record.InfoId ).Shape, item.Record.X, item.Record.Y, item.Record.Rotation ).Count() ) );
		Assert.IsTrue( save.FixedItems.All( item => catalog.Get( item.InfoId ).IsFixedItem ) );
	}

	[TestMethod]
	public void OriginalGridAppliesMapSaveAndShapeRules()
	{
		var park = OriginalPark.Load( "jungle" );
		var grid = new OriginalParkGrid( park );
		var catalog = ObjectCatalog.Load( "jungle" );
		var objects = new ParkObjects( catalog, grid );
		var totem = catalog.Get( 1110 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( totem, 20, 60, 0 ) );
		var bin = catalog.Get( 1406 );
		Assert.AreEqual( OriginalPlacementResult.Water, objects.Check( bin, 51, 50, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, objects.Check( bin, 47, 17, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, objects.Check( totem, 38, 20, 0 ) );
		// The Belly Bounce queue stays blocked; the imported Belly Bounce cells are free until its object is placed.
		Assert.AreEqual( OriginalPlacementResult.Occupied, grid.CheckTerrain( 50, 22 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, grid.CheckTerrain( 52, 25 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, objects.Check( totem, 95, 84, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Blocked, objects.Check( catalog.Get( 100 ), 20, 60, 0 ), "the Mystery ride is a tool" );
		objects.IsReserved = ( x, y ) => x == 21 && y == 61;
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( totem, 20, 60, 0 ) );
		// Placement matrices agree with the terrain mapping: a rotation-0 model point lands on its cell.
		var placement = new ObjectPlacement( 20, 60, 0, grid.Origin, 0 );
		var engine = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( 5, 5, 0 ), placement.ModelToEngine );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( park.Heightfield, engine.X, engine.Y, out var cellX, out var cellY ) );
		Assert.AreEqual( (20, 60), (cellX, cellY) );
		Assert.AreEqual( OriginalParkPlacement.ToEngine( park.Heightfield, new System.Numerics.Vector3( 205, 0, 605 ) ).X, engine.X, 1e-4 );
	}

	/// <summary>Official bonus content (private: OPENTPW_BONUS_DATA, inconclusive without it).</summary>
	[TestMethod]
	public void BonusContentMergesIntoTheCatalogsAndRuns()
	{
		var root = Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" );
		if ( string.IsNullOrWhiteSpace( root ) || ObjectCatalog.FindBonusLevelsParent( root ) == null )
			Assert.Inconclusive( "Set OPENTPW_BONUS_DATA to the extracted official bonus content for the bonus corpus." );
		var baseCounts = ObjectCatalog.Themes.ToDictionary( theme => theme, theme => ObjectCatalog.Load( theme ).Entries.Count );
		ObjectCatalog.BonusDataRoot = root;
		var expected = new Dictionary<string, (int Rides, int Sideshows, int Features)>
		{
			["jungle"] = (4, 0, 4),
			["hallow"] = (4, 1, 4),
			["space"] = (3, 2, 5),
			["fantasy"] = (3, 1, 4)
		};
		var bonus = new List<ObjectCatalogEntry>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			var added = catalog.Entries.Where( entry => entry.IsBonus ).ToArray();
			Assert.AreEqual( baseCounts[theme] + added.Length, catalog.Entries.Count, theme );
			Assert.AreEqual( expected[theme], (added.Count( entry => entry.Category == ObjectCategory.Ride ), added.Count( entry => entry.Category == ObjectCategory.Sideshow ), added.Count( entry => entry.Category == ObjectCategory.Feature )), theme );
			bonus.AddRange( added );
			// Every bonus object runs its script in the park's shared world next to the base objects.
			var world = new RideScriptWorld();
			var runtimes = added.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, seed: index + 1 ) ).ToArray();
			for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; tick++ )
			{
				foreach ( var runtime in runtimes )
					runtime.Simulate( FixedStepClock.TickDuration );
			}
			foreach ( var runtime in runtimes )
			{
				Assert.IsTrue( runtime.Script == null || runtime.Script.State != RideVMState.Faulted, $"{runtime.Entry}: {runtime.Script?.FaultMessage}" );
				runtime.Stop();
			}
		}
		Assert.AreEqual( 35, bonus.Count );
		Assert.AreEqual( 35, bonus.Select( entry => entry.BonusNumber ).Distinct().Count() );
		foreach ( var entry in bonus )
		{
			Assert.IsTrue( entry.ArchiveName.StartsWith( "_" ) && entry.IsBuildable && entry.InfoId > 0 && entry.DisplayName.Length > 0, entry.ToString() );
			Assert.IsNotNull( entry.ScriptPath, entry.ToString() );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.AreEqual( ModelFileKind.Geometry, model.Kind, entry.ToString() );
			Assert.IsTrue( ObjectRenderParts.Build( entry, model ).Count > 0, entry.ToString() );
			foreach ( var animation in entry.Animations )
				new ObjectAnimator( model ).Play( 0, ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip!, animation.Name, false );
		}
		var snake = ObjectCatalog.Load( "jungle" ).Get( 1118 );
		Assert.AreEqual( ("_snake_1", 1, "Snake", "Slither"), (snake.ArchiveName, snake.BonusNumber, snake.SettingsName, snake.DisplayName) );
		Assert.AreEqual( "Rutschbahn", BonusNames.Read( snake, "German" ) );
		Assert.AreEqual( "Slither", BonusNames.Read( snake, "Klingon" ), "unknown languages fall back to English" );
		Assert.IsTrue( snake.HasQueue, "bonus rides use the game's Rides.sam defaults" );
	}
}
