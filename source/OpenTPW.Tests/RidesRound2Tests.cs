using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// RIDES round 2 (docs/OBJECTS.md): the object-name table (RIDES-010), the shape symbol table (RIDES-012),
/// the low-detail texture folders (RIDES-022) and the upgrade-completion speed (RIDES-033), checked on synthetic data.
/// </summary>
[TestClass, DoNotParallelize]
public class RidesRound2Tests
{
	[TestMethod]
	public void ObjectNameTableHoldsTheExecutablesRowsKeyedByInfoId()
	{
		var ids = ObjectNameTable.InfoIds().ToArray();
		Assert.AreEqual( ObjectNameTable.RowCount, ids.Length );
		Assert.AreEqual( ids.Length, ids.Distinct().Count(), "every Info.Id has one row" );
		// Every row names one entry (second index 1 = the empty string) or two consecutive ones.
		foreach ( var id in ids )
		{
			Assert.IsTrue( ObjectNameTable.TryGet( id, out var first, out var second ), id.ToString() );
			Assert.IsTrue( second == ObjectNameTable.EmptyIndex || second == first + 1, $"{id}: {first}, {second}" );
		}
		// Rows read from the table at 0x7488d8: the Inca Totem, the fantasy Fountain and the space Crater (the two the English matcher bound wrongly).
		Assert.IsTrue( ObjectNameTable.TryGet( 1110, out var totem, out var totemSecond ) );
		Assert.AreEqual( (28, 29), (totem, totemSecond) );
		Assert.IsTrue( ObjectNameTable.TryGet( 4417, out var fountain, out var fountainSecond ) );
		Assert.AreEqual( (246, 1), (fountain, fountainSecond) );
		Assert.IsTrue( ObjectNameTable.TryGet( 3407, out var crater, out var craterSecond ) );
		Assert.AreEqual( (359, 1), (crater, craterSecond) );
		Assert.IsTrue( ObjectNameTable.TryGet( 3602, out var last, out _ ) );
		Assert.AreEqual( 371, last );
		Assert.IsFalse( ObjectNameTable.TryGet( 99999, out _, out _ ), "an id outside the table has no name entry" );
		Assert.IsFalse( ObjectNameTable.TryGet( 0, out _, out _ ) );
	}

	[TestMethod]
	public void ObjectNamesBindByInfoIdAndLeaveUnlistedIdsUnbound()
	{
		ObjectCatalogEntry Entry( int id, string name ) => new( "jungle", ObjectCategory.Feature, "/levels/jungle/features/x", new ObjectSettings( new[]
		{
			new ObjectSettingsFile( $"Info.Id\t{id}\nInfo.Name\t\"{name}\"\n", "x.sam" )
		} ), ObjectShape.Single, "/x.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );
		// The name text is irrelevant: the id alone binds (the English matcher needed the text to equal OBJECT_NAMES).
		var bound = ObjectNames.Bind( new[] { Entry( 1110, "unrelated" ), Entry( 4417, "Fountain" ), Entry( 99999, "Inca Totem" ), Entry( 1203, "x" ) } );
		CollectionAssert.AreEqual( new[] { (1110, 28, 2), (4417, 246, 1), (1203, 44, 1) }, bound.Select( item => (item.Entry.InfoId, item.Index, item.Length) ).ToArray() );
	}

	[TestMethod]
	public void ShapeSymbolsFollowTheExecutablesTable()
	{
		// (symbol, cell type, direction bit, kind): the 19 rows at 0x7396c8.
		var rows = new (char Symbol, int Type, byte Direction, ObjectCellKind Kind)[]
		{
			('.', 0, 0, ObjectCellKind.Free), ('*', 4, 0, ObjectCellKind.Occupied), ('Q', 3, 0, ObjectCellKind.Occupied), ('@', 1, 0, ObjectCellKind.Occupied),
			('+', 11, 0, ObjectCellKind.Overlay), ('#', 16, 0, ObjectCellKind.Occupied),
			('8', 9, 0x01, ObjectCellKind.Entrance), ('6', 9, 0x04, ObjectCellKind.Entrance), ('2', 9, 0x10, ObjectCellKind.Entrance), ('4', 9, 0x40, ObjectCellKind.Entrance),
			('N', 10, 0x01, ObjectCellKind.Exit), ('E', 10, 0x04, ObjectCellKind.Exit), ('S', 10, 0x10, ObjectCellKind.Exit), ('W', 10, 0x40, ObjectCellKind.Exit),
			('D', 23, 0, ObjectCellKind.TrackConnection), ('>', 23, 0x04, ObjectCellKind.TrackConnection), ('<', 23, 0x40, ObjectCellKind.TrackConnection),
			('O', 9, 0x01, ObjectCellKind.Entrance), ('X', 10, 0x01, ObjectCellKind.Exit)
		};
		foreach ( var (symbol, type, direction, kind) in rows )
		{
			var cell = new ObjectShape( new[] { symbol.ToString() } ).Cells.Single();
			Assert.AreEqual( (type, direction, kind), (cell.CellType, cell.Direction, cell.Kind), $"'{symbol}'" );
		}
		Assert.ThrowsException<InvalidDataException>( () => new ObjectShape( new[] { "*x" } ) );
		Assert.ThrowsException<InvalidDataException>( () => new ObjectShape( new[] { "*n" } ), "symbols are case sensitive" );
	}

	[TestMethod]
	public void AccessCellsOpenTowardsTheirSymbolsDirectionNotTowardsTheNearestEdge()
	{
		// Text rows top to bottom; V counts from the last row (+V is text-up).
		var shape = new ObjectShape( new[] { "*S*", "4O6", "*8*" } );
		var expected = new Dictionary<char, (int U, int V)>
		{
			// Entrances open opposite to their bit, exits towards it.
			['S'] = (0, 1), ['4'] = (1, 0), ['O'] = (0, 1), ['6'] = (-1, 0), ['8'] = (0, 1)
		};
		foreach ( var cell in shape.Cells.Where( cell => expected.ContainsKey( cell.Symbol ) ) )
			Assert.AreEqual( expected[cell.Symbol], ObjectFootprint.OutwardDirection( shape, cell ), $"'{cell.Symbol}'" );
		// The Easymode entrance 2 (bit S) opens towards -V on the first row, a coaster's exit N next to it also towards -V; E and W open sideways.
		var track = new ObjectShape( new[] { "*E*", "W*X", "*2N" } );
		var outward = track.Cells.Where( cell => cell.Kind is ObjectCellKind.Entrance or ObjectCellKind.Exit ).ToDictionary( cell => cell.Symbol, cell => ObjectFootprint.OutwardDirection( track, cell ) );
		CollectionAssert.AreEquivalent( new Dictionary<char, (int, int)> { ['E'] = (1, 0), ['W'] = (-1, 0), ['X'] = (0, -1), ['2'] = (0, -1), ['N'] = (0, -1) }, outward );
		// Rotation 90 maps cell (u, v) to (X + v, Y - u) and turns the direction like the offsets: local (0, -1) becomes (-1, 0).
		var point = ObjectFootprint.GetAccessPoints( new ObjectShape( new[] { "*2" } ), 10, 10, 90 ).Single();
		Assert.AreEqual( (10, 9, 9, 9), (point.X, point.Y, point.OutsideX, point.OutsideY) );
	}

	private static ObjectCatalogEntry TextureEntry() => new( "jungle", ObjectCategory.Ride, "/levels/jungle/rides/totem", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ),
		ObjectShape.Single, "/x.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );

	/// <summary>RIDES-022: the low-detail set replaces the archive's textures and the theme's sharetex by stexture and ssharete; gtexture stays.</summary>
	[TestMethod]
	public void LowDetailSearchesStextureAndSshareteInsteadOfTexturesAndSharetex()
	{
		var entry = TextureEntry();
		try
		{
			ObjectAssets.LowDetailTexturesOverride = false;
			CollectionAssert.AreEqual( new[] { "/levels/jungle/rides/totem/textures", "/levels/jungle/rides/totem/gtexture", "/levels/jungle/sharetex" },
				ObjectAssets.TextureDirectories( entry ).Select( item => item.Directory ).ToArray() );
			ObjectAssets.LowDetailTexturesOverride = true;
			CollectionAssert.AreEqual( new[] { "/levels/jungle/rides/totem/stexture", "/levels/jungle/rides/totem/textures", "/levels/jungle/rides/totem/gtexture", "/levels/jungle/ssharete", "/levels/jungle/sharetex" },
				ObjectAssets.TextureDirectories( entry ).Select( item => item.Directory ).ToArray() );
		}
		finally
		{
			ObjectAssets.LowDetailTexturesOverride = null;
		}
	}

	/// <summary>RIDES-022: the set follows TEXTUREQUALITY 0/1 of the active detail: low.sam has 1, med.sam 2, high.sam 3, the Enhanced preset 3.</summary>
	[TestMethod]
	public void LowDetailTexturesAreUsedExactlyWhenTheActiveDetailIsLow()
	{
		string Sam( int quality ) => $"GraphicalOptions.TEXTUREQUALITY\t{quality}\nGraphicalOptions.TEXTUREFILTERING\t1\n";
		Stream? Presets( string path ) => path switch
		{
			"/low.sam" => new MemoryStream( System.Text.Encoding.ASCII.GetBytes( Sam( 1 ) ) ),
			"/med.sam" => new MemoryStream( System.Text.Encoding.ASCII.GetBytes( Sam( 2 ) ) ),
			"/high.sam" => new MemoryStream( System.Text.Encoding.ASCII.GetBytes( Sam( 3 ) ) ),
			_ => null
		};
		var previous = IGraphicsSettings.Instance;
		var edition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			IGraphicsSettings.Instance = null;
			Assert.IsFalse( ObjectAssets.UsesLowDetailTextures, "no graphics settings: the full textures" );
			foreach ( var (preset, expected) in new[] { (GraphicsPreset.Low, true), (GraphicsPreset.Medium, false), (GraphicsPreset.High, false), (GraphicsPreset.Enhanced, false) } )
			{
				IGraphicsSettings.Instance = new GraphicsSettingsService( Presets, new GraphicsSettings { Preset = preset }, null, () => CompatibilityFlags.Original );
				Assert.AreEqual( expected, ObjectAssets.UsesLowDetailTextures, preset.ToString() );
			}
			// A custom detail with TEXTUREQUALITY 0 (Minimum) is low as well; a preset whose original file is missing keeps the full textures.
			IGraphicsSettings.Instance = new GraphicsSettingsService( Presets, new GraphicsSettings { Preset = GraphicsPreset.Custom, Detail = new OriginalDetailSettings { TextureQuality = 0 } }, null, () => CompatibilityFlags.Original );
			Assert.IsTrue( ObjectAssets.UsesLowDetailTextures );
			IGraphicsSettings.Instance = new GraphicsSettingsService( _ => null, new GraphicsSettings { Preset = GraphicsPreset.Low }, null, () => CompatibilityFlags.Original );
			Assert.IsFalse( ObjectAssets.UsesLowDetailTextures );
			// Theme Park Inc is not covered by the tp.exe trace: it keeps its textures folders.
			IGraphicsSettings.Instance = new GraphicsSettingsService( Presets, new GraphicsSettings { Preset = GraphicsPreset.Low }, null, () => CompatibilityFlags.Original );
			Assert.IsTrue( ObjectAssets.UsesLowDetailTextures );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( ObjectAssets.UsesLowDetailTextures );
		}
		finally
		{
			IGraphicsSettings.Instance = previous;
			GameEditions.Select( edition );
		}
	}

	[TestMethod]
	public void ObjectNamesBindThroughTheTableOnlyForThemeParkWorld()
	{
		var edition = GameEditions.Current;
		try
		{
			// Theme Park Inc ships another OBJECT_NAMES.str: the table of tp.exe must not name its objects.
			GameEditions.Select( GameEdition.ThemeParkInc );
			var entries = new[] { SyntheticEntry( 1150, "Miniature Dreadnoughts" ) };
			Assert.AreEqual( 0, ObjectNames.Match( entries, new[] { "x", "y" } ).Count );
			GameEditions.Select( GameEdition.ThemeParkWorld );
			Assert.AreEqual( 1, ObjectNames.Bind( entries ).Count );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}

	private static ObjectCatalogEntry SyntheticEntry( int id, string name ) => new( "jungle", ObjectCategory.Feature, "/levels/jungle/features/x", new ObjectSettings( new[]
	{
		new ObjectSettingsFile( $"Info.Id\t{id}\nInfo.Name\t\"{name}\"\n", "x.sam" )
	} ), ObjectShape.Single, "/x.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
		Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );

	[TestMethod]
	public void ASelectedTexturePackKeepsTheFullTextureFolders()
	{
		var edition = GameEditions.Current;
		var previous = IGraphicsSettings.Instance;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			Stream? Low( string path ) => path == "/low.sam" ? new MemoryStream( System.Text.Encoding.ASCII.GetBytes( "GraphicalOptions.TEXTUREQUALITY\t1\n" ) ) : null;
			IGraphicsSettings.Instance = new GraphicsSettingsService( Low, new GraphicsSettings { Preset = GraphicsPreset.Low }, null, () => CompatibilityFlags.Original );
			Assert.IsTrue( ObjectAssets.UsesLowDetailTextures );
			// A selected pack replaces the full textures by path, so the low set (whose paths the pack does not cover) is skipped.
			var property = typeof( TexturePack ).GetProperty( nameof( TexturePack.Enhanced ) )!;
			property.SetValue( null, true );
			try
			{
				Assert.IsFalse( ObjectAssets.UsesLowDetailTextures );
			}
			finally
			{
				property.SetValue( null, false );
			}
			Assert.IsTrue( ObjectAssets.UsesLowDetailTextures );
		}
		finally
		{
			IGraphicsSettings.Instance = previous;
			GameEditions.Select( edition );
		}
	}

	[TestMethod]
	public void EndItemsKeepTheirSamNameWhenTheBoundNameIsEmpty()
	{
		Assert.IsTrue( ObjectNameTable.TryGet( 1605, out var first, out var second ) );
		Assert.AreEqual( (0, ObjectNameTable.EmptyIndex), (first, second) );
		Assert.AreEqual( "", ObjectNames.Join( new[] { "" } ) );
	}

	[TestMethod]
	public void WIsAnExitCellOfTheShapeNotAnOccupiedOne()
	{
		var snake = new ObjectShape( new[] { "****", "W***", "****" } );
		Assert.AreEqual( 1, snake.Exits.Count() );
		Assert.AreEqual( ObjectCellKind.Exit, snake.Cells.Single( cell => cell.Symbol == 'W' ).Kind );
		var point = ObjectFootprint.GetAccessPoints( snake, 5, 5, 0 ).Single();
		Assert.AreEqual( (5, 6, 4, 6), (point.X, point.Y, point.OutsideX, point.OutsideY) );
		Assert.AreEqual( 12, snake.OccupiedCells.Count(), "the exit cell stays inside the footprint like the other access cells" );
	}
}

/// <summary>RIDES round 2 on the original data; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass, DoNotParallelize]
public class RidesRound2CorpusTests
{
	private BaseFileSystem? previous;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var before ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object catalog corpus." );
		previous = before;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = previous!;
		ObjectCatalog.BonusDataRoot = null;
		ObjectAssets.LowDetailTexturesOverride = null;
	}

	/// <summary>
	/// RIDES-010: every corpus object whose Info.Id is in the executable's table binds to its two entries; the only unbound objects are
	/// the three tools of each theme (Info.Id 100..102, no table row). The English matcher missed the gates, signs, lights, seaplane,
	/// loudspeakers and several rides, and bound the fantasy Fountain (4417) and the space Crater (3407) to the wrong entry.
	/// </summary>
	[TestMethod]
	public void CorpusObjectsBindThroughTheInfoIdTable()
	{
		var unbound = new List<int>();
		var named = new Dictionary<string, int>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			named[theme] = catalog.Entries.Count( entry => entry.ObjectNameIndex != null );
			foreach ( var entry in catalog.Entries )
			{
				var listed = ObjectNameTable.TryGet( entry.InfoId, out var first, out var second );
				Assert.AreEqual( listed, entry.ObjectNameIndex != null, entry.ToString() );
				if ( !listed )
				{
					unbound.Add( entry.InfoId );
					Assert.IsTrue( entry.IsTool, $"{entry} is unlisted but not a tool" );
					continue;
				}
				Assert.AreEqual( (first, second == ObjectNameTable.EmptyIndex ? 1 : 2), (entry.ObjectNameIndex, entry.ObjectNameLength), entry.ToString() );
			}
		}
		CollectionAssert.AreEqual( new[] { 100, 101, 102, 100, 101, 102, 100, 101, 102, 100, 101, 102 }, unbound );
		Assert.AreEqual( "jungle=67 hallow=67 space=65 fantasy=63", string.Join( " ", ObjectCatalog.Themes.Select( theme => $"{theme}={named[theme]}" ) ) );
		var fantasy = ObjectCatalog.Load( "fantasy" ).Get( 4417 );
		Assert.AreEqual( ("Fountain", 246, 1), (fantasy.DisplayName, fantasy.ObjectNameIndex, fantasy.ObjectNameLength) );
		var space = ObjectCatalog.Load( "space" ).Get( 3407 );
		Assert.AreEqual( ("Crater", 359, 1), (space.DisplayName, space.ObjectNameIndex, space.ObjectNameLength) );
		// Objects the English matcher left unbound.
		var jungle = ObjectCatalog.Load( "jungle" );
		Assert.AreEqual( "LOST Kingdom", jungle.Get( 1601 ).DisplayName );
		Assert.AreEqual( "Seaplane", jungle.Get( 1602 ).DisplayName );
		Assert.AreEqual( "Traffic Lights", jungle.Get( 1603 ).DisplayName );
		Assert.AreEqual( "Dino Karts Jump", jungle.Get( 1500 ).DisplayName );
		Assert.AreEqual( "Inca Totem", jungle.Get( 1110 ).DisplayName );
	}

	/// <summary>
	/// RIDES-010: five objects with a sign (the four theme gates and the jungle Chac Atak) had no usable name before the table bound them; their signs
	/// now compose from the table's names. The frozen baseline of the other 75 signs lives in SignRasterCorpusHashTests.
	/// </summary>
	[TestMethod]
	public void SignsOfTheObjectsTheTableNamedComposeFromTheirTableNames()
	{
		var expected = new (string Theme, int Id, string Name, string Left, string Right, int Pixels)[]
		{
			("jungle", 1185, "Chac Atak", "f0bd75a413672e111849ed2888e413feccf9dd89349e149c421028dd588cd5cc", "4e7dcbe92d73b2b214657560b870a61cb9237c3ca3242b682e020d60cd3bbbcf", 8089),
			("jungle", 1601, "LOST Kingdom", "be165882a22de8d23fe2cc12fc2fd754f672aaab0a6b73e21a87c73d385db8fe", "360e79e5a4e588ef66f7bad5030a0503a1d254225c60f9ad380bd11dd7478f6a", 10785),
			("hallow", 2601, "HALLOWEEN world", "1bcc6b1e74e5d409cbdb65546ceda738e238e9b8b70e80fc0cae01a361c36d5f", "704c8e8b5a064462e097ad8878f91e193ebc082347c7b3eff68aad88be1b5326", 6329),
			("fantasy", 4601, "Wonder Land", "3668327b89027c0237eb8cca023ef6187c1227784884ad708c350049c1997911", "7376e258aa561c1adb7d17537814c0c56807cb22bd16e6c745e723270d37f937", 6205),
			("space", 3601, "Space Zone", "4f6b47e9fa9de846eec65b015d3cf05af04f59032b8125ef267236450bdf50e9", "d4eae4bed28beb5039e259ea21aefdf50d7c1488ed95f3fa94bf957615f4ef84", 7255)
		};
		string Hash( byte[] bytes ) => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ) ).ToLowerInvariant();
		var mismatches = new List<string>();
		foreach ( var (theme, id, name, left, right, pixels) in expected )
		{
			var entry = ObjectCatalog.Load( theme ).Get( id );
			Assert.AreEqual( name, entry.DisplayName, $"{theme}/{id}" );
			var images = ObjectSigns.Get( entry );
			Assert.IsNotNull( images, $"{theme}/{id}" );
			if ( (left, right, pixels) != (Hash( images.Left ), Hash( images.Right ), images.TextPixelCount) )
				mismatches.Add( $"{theme}/{id}: (\"{Hash( images.Left )}\", \"{Hash( images.Right )}\", {images.TextPixelCount})" );
		}
		CollectionAssert.AreEqual( Array.Empty<string>(), mismatches, string.Join( "\n", mismatches ) );
	}

	/// <summary>
	/// RIDES-009: coaster.sam (the only shared .sam of the corpus, in the 12 TrackMaster archives) is not part of the object's settings stack;
	/// its values reach the coaster definition on their own, and they equal what the old merged stack gave (no key is set by both files).
	/// </summary>
	[TestMethod]
	public void CorpusCoasterSamIsItsOwnSettingsAndDefinesTheSameCoaster()
	{
		int shared = 0, coasters = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				if ( entry.SharedSettings is not { } coaster )
				{
					Assert.IsFalse( CoasterDefinition.IsCoaster( entry.Settings ), entry.ToString() );
					continue;
				}
				shared++;
				coasters += CoasterDefinition.IsCoaster( entry.Settings ) ? 1 : 0;
				Assert.IsTrue( coaster.Values.Keys.Any( key => key.StartsWith( "sCoasterType", StringComparison.Ordinal ) || key.StartsWith( "asCarTypes", StringComparison.Ordinal ) ), entry.ToString() );
				var overlap = coaster.Values.Keys.Where( key => entry.Settings.Values.ContainsKey( key ) ).ToArray();
				CollectionAssert.AreEqual( Array.Empty<string>(), overlap, $"{entry}: the object's own stack must not hold coaster.sam keys" );
				var merged = new Dictionary<string, string>( entry.Settings.Values, StringComparer.OrdinalIgnoreCase );
				foreach ( var pair in coaster.Values )
					merged[pair.Key] = pair.Value;
				var own = CoasterDefinition.FromEntry( entry );
				var old = CoasterDefinition.FromValues( merged );
				foreach ( var property in typeof( CoasterDefinition ).GetProperties().Where( property => property.PropertyType.IsPrimitive ) )
					Assert.AreEqual( property.GetValue( old ), property.GetValue( own ), $"{entry} {property.Name}" );
			}
		}
		Assert.AreEqual( (12, 12), (shared, coasters) );
	}

	/// <summary>
	/// RIDES-038 census: every TRIGANIMSPEED of the corpus scripts has a literal speed operand (per mille) above 0, so the guard against 0 and
	/// negative speeds (a divide fault in the original for 0) never fires on the original data.
	/// </summary>
	[TestMethod]
	public void CorpusTrigAnimSpeedOperandsAreAllPositiveLiterals()
	{
		int count = 0, variable = 0, minimum = int.MaxValue, maximum = int.MinValue;
		foreach ( var (name, data) in RseScriptTests.LoadCorpusBytes() )
		{
			foreach ( var instruction in new RideScriptFile( new MemoryStream( data ) ).Instructions.Where( instruction => instruction.Opcode == (ushort)Opcode.TRIGANIMSPEED ) )
			{
				count++;
				var operand = instruction.Operands[3];
				if ( operand.Kind != RideScriptOperandKind.Literal )
				{
					variable++;
					continue;
				}
				minimum = Math.Min( minimum, (short)operand.Value );
				maximum = Math.Max( maximum, (short)operand.Value );
			}
		}
		Assert.AreEqual( (4, 0, 1800, 4000), (count, variable, minimum, maximum) );
	}

	/// <summary>The edge rule the English-matcher era used (kept here to show that the corpus agrees with the symbol rule).</summary>
	private static (int U, int V) EdgeRule( ObjectShape shape, ObjectShapeCell cell )
	{
		if ( cell.V == 0 )
			return (0, -1);
		if ( cell.V == shape.Height - 1 )
			return (0, 1);
		if ( cell.U == 0 )
			return (-1, 0);
		if ( cell.U == shape.Width - 1 )
			return (1, 0);
		return (0, -1);
	}

	[TestMethod]
	public void CorpusAccessCellsOpenTheSameWayBySymbolAndByEdge()
	{
		int access = 0;
		var different = new List<string>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				foreach ( var cell in entry.Shape.Cells.Where( cell => cell.Kind is ObjectCellKind.Entrance or ObjectCellKind.Exit ) )
				{
					access++;
					if ( ObjectFootprint.OutwardDirection( entry.Shape, cell ) != EdgeRule( entry.Shape, cell ) )
						different.Add( $"{entry} '{cell.Symbol}' ({cell.U}, {cell.V})" );
				}
			}
		}
		// RIDES-012: the symbol rule gives the same outward side as the edge rule on every corpus shape.
		Assert.AreEqual( 209, access );
		CollectionAssert.AreEqual( Array.Empty<string>(), different );
	}

	/// <summary>Objects that name textures present only in `textures` still resolve them in the low-detail set (the fallback).</summary>
	[TestMethod]
	public void CorpusLowDetailSetFallsBackToTexturesForTextureOnlyNames()
	{
		try
		{
			ObjectAssets.LowDetailTexturesOverride = true;
			foreach ( var (theme, archive, texture) in new[] { ("fantasy", "gokarts", "hivesign01"), ("space", "gates", "Sen_sgn1"), ("jungle", "totem", "tp_sign") } )
			{
				var entry = ObjectCatalog.Load( theme ).Entries.Single( candidate => candidate.ArchiveName == archive );
				var path = ObjectAssets.ResolveTexturePath( entry, texture );
				Assert.IsNotNull( path, $"{theme}/{archive}:{texture}" );
				StringAssert.Contains( path, "/textures/", $"{theme}/{archive}:{texture}" );
			}
		}
		finally
		{
			ObjectAssets.LowDetailTexturesOverride = null;
		}
	}

	[TestMethod]
	public void CorpusLowDetailSetResolvesEveryTextureTheFullSetResolves()
	{
		int slots = 0, fullMissing = 0, lowMissing = 0, lowDirectory = 0;
		var lowOnlyMissing = new SortedSet<string>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				ObjectAssets.LowDetailTexturesOverride = false;
				var full = model.Meshes.SelectMany( mesh => mesh.Materials ).Select( material => (material.Name, Path: ObjectAssets.ResolveTexturePath( entry, material.Name )) ).ToArray();
				ObjectAssets.LowDetailTexturesOverride = true;
				var low = model.Meshes.SelectMany( mesh => mesh.Materials ).Select( material => (material.Name, Path: ObjectAssets.ResolveTexturePath( entry, material.Name )) ).ToArray();
				ObjectAssets.LowDetailTexturesOverride = null;
				for ( var index = 0; index < full.Length; index++ )
				{
					slots++;
					fullMissing += full[index].Path == null ? 1 : 0;
					lowMissing += low[index].Path == null ? 1 : 0;
					lowDirectory += low[index].Path is { } path && (path.Contains( "/stexture/" ) || path.Contains( "/ssharete/" )) ? 1 : 0;
					if ( full[index].Path != null && low[index].Path == null )
						lowOnlyMissing.Add( $"{entry.ArchivePath}:{low[index].Name}" );
				}
			}
		}
		// RIDES-022: the low-detail folders hold every texture the full folders hold (the 4 missing slots are the registered ogre/rocket cases; 6 slots found only via the textures fallback).
		Assert.AreEqual( (5629, 4, 4, 5619), (slots, fullMissing, lowMissing, lowDirectory) );
		CollectionAssert.AreEqual( Array.Empty<string>(), lowOnlyMissing.ToArray() );
	}
}
