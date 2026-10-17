using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.ContentKit;
using OpenTPW.Mods;
using StbImageSharp;

namespace OpenTPW.Tests;

/// <summary>
/// The content-kit command line (docs/CONTENT-KIT.md): the three commands over hand-made input, and the end-to-end flow of phase 0: a pool exported
/// from Blender (or, without Blender, a hand-built glb of the same shape) goes through <c>object</c> and <c>mod</c> into a mod folder that the game's own mod
/// pipeline loads, whose object's model, textures and script then load and run. No GPU and no game run.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContentKitTests
{
	private static readonly IOpenTpwExtension Core = new OpenTpwExtension( "opentpw.core-test", "Core test", new ModVersion( 1, 0, 0 ), true, false );
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-kit-" ).FullName;
		previousFileSystem = FileSystem;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private static (int Code, string Output, string Error) Run( params string[] args )
	{
		var output = new StringWriter();
		var error = new StringWriter();
		return (ContentKitCli.Run( args, output, error ), output.ToString(), error.ToString());
	}

	private string Path2( params string[] parts ) => Path.Combine( new[] { root }.Concat( parts ).ToArray() );

	// ---- The sample pool, from Blender or by hand ---------------------------------------------------------------------

	/// <summary>The Blender install that <c>OPENTPW_BLENDER</c> names, else the macOS application, or null.</summary>
	private static string? Blender()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_BLENDER" );
		if ( string.IsNullOrWhiteSpace( path ) )
			path = "/Applications/Blender.app/Contents/MacOS/Blender";
		return File.Exists( path ) ? path : null;
	}

	private static string RepositoryFile( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = System.IO.Path.Combine( directory.FullName, relative );
			if ( File.Exists( candidate ) )
				return candidate;
		}
		throw new FileNotFoundException( $"{relative} was not found above {AppContext.BaseDirectory}." );
	}

	/// <summary>Runs <c>tools/blender/opentpw_sample_pool.py</c> and returns the glb it exported.</summary>
	private string BlenderPool( string blender )
	{
		var output = Path2( "blender-pool.glb" );
		var start = new ProcessStartInfo( blender ) { RedirectStandardOutput = true, RedirectStandardError = true };
		foreach ( var argument in new[] { "-b", "--factory-startup", "--python", RepositoryFile( "tools/blender/opentpw_sample_pool.py" ), "--", "--out", output } )
			start.ArgumentList.Add( argument );
		using var process = Process.Start( start )!;
		var standardOutput = process.StandardOutput.ReadToEndAsync();
		var standardError = process.StandardError.ReadToEndAsync();
		Assert.IsTrue( process.WaitForExit( 120_000 ), "Blender did not finish" );
		Assert.AreEqual( 0, process.ExitCode, standardOutput.Result + standardError.Result );
		return output;
	}

	/// <summary>
	/// A pool of the sample's shape without Blender: a 30 x 30 footprint (3 x 3 cells at X 0..30 and, as glTF has it, Z 0..-30), a tiled rim and a self-lit water
	/// quad that face up (+Y, counter-clockwise), six head points above the water and the sample's texture names.
	/// </summary>
	private string HandBuiltPool()
	{
		var glb = new GlbBuilder();
		var tile = glb.AddMaterial( "pool_tile", glb.AddImage( "pooltile", 8, 8, 200, 220, 210 ), wrap: true );
		var water = glb.AddMaterial( "pool_water", glb.AddImage( "poolwatr", 8, 8, 50, 120, 220 ), wrap: true, selfLit: true );
		GlbBuilder.Primitive Up( float y, int material ) => new(
			new float[] { 0, y, 0, 30, y, 0, 30, y, -30, 0, y, -30 }, new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0 },
			new float[] { 0, 1, 1, 1, 1, 0, 0, 0 }, new[] { 0, 1, 2, 0, 2, 3 }, material );
		var rim = glb.AddNode( "rim", glb.AddMesh( "rim", Up( 1.0f, tile ) ) );
		var surface = glb.AddNode( "water", glb.AddMesh( "water", Up( 0.6f, water ) ) );
		var heads = new List<int>();
		for ( var index = 1; index <= 6; index++ )
			heads.Add( glb.AddNode( $"head{index:00}", -1, new Vector3( 4.5f + (index - 1) % 3 * 10.5f, 0.9f, -(7.5f + (index - 1) / 3 * 15f) ), null, null, $"\"opentpw_head\": {index}" ) );
		var pool = glb.AddNode( "pool", -1, null, null, new[] { rim, surface }.Concat( heads ).ToArray() );
		var path = Path2( "handbuilt-pool.glb" );
		File.WriteAllBytes( path, glb.Build( pool ) );
		return path;
	}

	/// <summary>The pools the end-to-end test runs on: the hand-built one always, the Blender one when Blender is installed.</summary>
	private IEnumerable<(string Source, string Glb)> Pools()
	{
		yield return ("hand-built", HandBuiltPool());
		if ( Blender() is { } blender )
			yield return ("blender", BlenderPool( blender ));
	}

	// ---- Commands --------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheObjectCommandWritesTheArchiveLayoutOfAnOriginalObject()
	{
		var glb = HandBuiltPool();
		var result = Run( "object", "--glb", glb, "--name", "pool", "--out", Path2( "pool" ) );
		Assert.AreEqual( 0, result.Code, result.Error );
		var files = Directory.EnumerateFiles( Path2( "pool" ), "*", SearchOption.AllDirectories ).Select( file => System.IO.Path.GetRelativePath( Path2( "pool" ), file ).Replace( '\\', '/' ) ).OrderBy( file => file, StringComparer.Ordinal ).ToArray();
		CollectionAssert.AreEqual( new[] { "Pool.RSE", "Pool.sam", "pool.MD2", "textures/pooltile.tga", "textures/poolwatr.tga" }, files );
		StringAssert.Contains( result.Output, "wrote pool.MD2" );
		Assert.IsFalse( result.Output.Contains( "warning" ), result.Output );

		var model = new ModelFile( new MemoryStream( File.ReadAllBytes( Path2( "pool", "pool.MD2" ) ) ) );
		Assert.AreEqual( 2, model.Meshes.Count );
		Assert.AreEqual( "pool", model.Nodes[model.RootNodeIndex].Name );
		var tga = ImageResult.FromMemory( File.ReadAllBytes( Path2( "pool", "textures", "pooltile.tga" ) ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (8, 8, (byte)200), (tga.Width, tga.Height, tga.Data[0]) );
		using var script = File.OpenRead( Path2( "pool", "Pool.RSE" ) );
		Assert.AreEqual( "Pool", new RideScriptFile( script ).Strings[0] );
		var sam = File.ReadAllText( Path2( "pool", "Pool.sam" ) );
		StringAssert.Contains( sam, "Info.Id" );
		StringAssert.Contains( sam, "Info.Name\t\t\"Pool\"" );
	}

	[TestMethod]
	public void TheObjectCommandTakesYourOwnSettingsAndScriptAndRefusesWhatItCannotUse()
	{
		var glb = HandBuiltPool();
		File.WriteAllText( Path2( "own.sam" ), "Info.Id 7\nInfo.Name \"Own\"\nUpgrades[0].CostOfUpgrade 5\n" );
		File.WriteAllText( Path2( "own.rse" ), ".var A\n\tNAME \"Own\"\nloop:\n\tENDSLICE\n\tBRANCH @loop\n" );
		var ok = Run( "object", "--glb", glb, "--name", "own", "--out", Path2( "own" ), "--sam", Path2( "own.sam" ), "--script", Path2( "own.rse" ) );
		Assert.AreEqual( 0, ok.Code, ok.Error );
		Assert.AreEqual( File.ReadAllText( Path2( "own.sam" ) ), File.ReadAllText( Path2( "own", "Own.sam" ) ) );
		Assert.IsFalse( ok.Output.Contains( "warning" ), "heads without ADDHEAD are fine" );

		// ADDHEAD without head records in the model is a warning, not an error
		var plain = new GlbBuilder();
		var material = plain.AddMaterial( "m", plain.AddImage( "tile" ) );
		File.WriteAllBytes( Path2( "plain.glb" ), plain.Build( plain.AddNode( "m", plain.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) );
		var headless = Run( "object", "--glb", Path2( "plain.glb" ), "--name", "plain", "--out", Path2( "plain" ) );
		Assert.AreEqual( 0, headless.Code, headless.Error );
		StringAssert.Contains( headless.Output, "warning: The script runs ADDHEAD" );

		// the same archive again needs --force
		var again = Run( "object", "--glb", glb, "--name", "own", "--out", Path2( "own" ) );
		Assert.AreEqual( 1, again.Code );
		StringAssert.Contains( again.Error, "--force" );
		Assert.AreEqual( 0, Run( "object", "--glb", glb, "--name", "own", "--out", Path2( "own" ), "--force" ).Code );

		// a script with an error names its line
		File.WriteAllText( Path2( "bad.rse" ), "\tNAME \"x\"\n\tNOPE 1\n" );
		var bad = Run( "object", "--glb", glb, "--name", "bad", "--out", Path2( "bad" ), "--script", Path2( "bad.rse" ) );
		Assert.AreEqual( 1, bad.Code );
		StringAssert.Contains( bad.Error, "line 2" );
		Assert.IsFalse( Directory.Exists( Path2( "bad" ) ), "nothing is written when the script fails" );

		// settings without an Info.Id are refused
		File.WriteAllText( Path2( "noid.sam" ), "Info.Name \"x\"\n" );
		Assert.AreEqual( 1, Run( "object", "--glb", glb, "--name", "noid", "--out", Path2( "noid" ), "--sam", Path2( "noid.sam" ) ).Code );
		// a name the catalog cannot use
		Assert.AreEqual( 1, Run( "object", "--glb", glb, "--name", "1pool", "--out", Path2( "x" ) ).Code );
	}

	[TestMethod]
	public void ARefusedConversionWritesNothingAndSaysWhy()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "this_name_is_too_long" ) );
		File.WriteAllBytes( Path2( "long.glb" ), glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) );
		var result = Run( "object", "--glb", Path2( "long.glb" ), "--name", "long", "--out", Path2( "long" ) );
		Assert.AreEqual( 1, result.Code );
		StringAssert.Contains( result.Error, "this_name_is_too_long" );
		Assert.IsFalse( Directory.Exists( Path2( "long" ) ) );
	}

	[TestMethod]
	public void UsageErrorsExitWith2AndShowTheUsage()
	{
		Assert.AreEqual( 2, Run().Code );
		var unknown = Run( "paint" );
		Assert.AreEqual( 2, unknown.Code );
		StringAssert.Contains( unknown.Error, "Unknown command" );
		StringAssert.Contains( unknown.Error, "opentpw-kit" );
		Assert.AreEqual( 2, Run( "object", "--glb" ).Code );
		Assert.AreEqual( 2, Run( "object", "--name", "x", "--out", Path2( "x" ) ).Code );
		Assert.AreEqual( 2, Run( "object", "--glb", "a", "--name", "x", "--out", Path2( "x" ), "--colour", "red" ).Code );
		Assert.AreEqual( 0, Run( "--help" ).Code );
	}

	[TestMethod]
	public void TheModCommandWritesManifestAndObjectsThatTheGameParses()
	{
		var glb = HandBuiltPool();
		Assert.AreEqual( 0, Run( "object", "--glb", glb, "--name", "pool", "--out", Path2( "built" ) ).Code );
		var result = Run( "mod", "--out", Path2( "mod" ), "--id", "community.pool", "--name", "Pool", "--version", "1.2.0", "--author", "Ann",
			"--id-first", "20000", "--id-last", "20099",
			"--object", $"archive=objects/pool,theme=jungle,category=sideshow,id=20001,name=Pool,name.German=Becken,src={Path2( "built" )}" );
		Assert.AreEqual( 0, result.Code, result.Error );
		Assert.IsTrue( File.Exists( Path2( "mod", "objects", "pool", "pool.MD2" ) ) );
		Assert.IsTrue( File.Exists( Path2( "mod", "objects", "pool", "textures", "pooltile.tga" ) ) );

		var manifest = ModManifest.Load( Path2( "mod", ModBuilder.ManifestFileName ) );
		Assert.AreEqual( ("community.pool", "Pool", "objects.json"), (manifest.Id, manifest.Name, manifest.Content.Objects) );
		var objects = ModObjectsFile.Load( Path2( "mod", ModBuilder.ObjectsFileName ), Array.Empty<string>() );
		Assert.AreEqual( (20000, 20099), (objects.FirstId, objects.LastId) );
		var item = objects.Objects.Single();
		Assert.AreEqual( ("objects/pool", "jungle", ObjectCategory.Sideshow, 20001), (item.Archive, item.Theme, item.Category, item.InfoId) );
		Assert.AreEqual( "Becken", item.Names["German"] );
	}

	[TestMethod]
	public void TheModCommandChecksYourManifestAndObjectsFilesWithTheGamesRules()
	{
		File.WriteAllText( Path2( "manifest.json" ), """{ "schema": 1, "id": "community.own", "name": "Own", "version": "0.1.0", "category": "community", "content": { "overrides": "data/" } }""" );
		File.WriteAllText( Path2( "objects.json" ), """{ "schema": 1, "idRange": { "first": 30000, "last": 30009 }, "objects": [ { "archive": "objects/own", "theme": "space", "category": "rides", "infoId": 30001, "names": { "English": "Own" } } ] }""" );
		var ok = Run( "mod", "--out", Path2( "own-mod" ), "--manifest", Path2( "manifest.json" ), "--objects", Path2( "objects.json" ) );
		Assert.AreEqual( 0, ok.Code, ok.Error );
		var manifest = ModManifest.Load( Path2( "own-mod", ModBuilder.ManifestFileName ) );
		Assert.AreEqual( "objects.json", manifest.Content.Objects, "content.objects is added, the other content is kept" );
		StringAssert.Contains( File.ReadAllText( Path2( "own-mod", ModBuilder.ManifestFileName ) ), "\"overrides\": \"data/\"" );

		// the rules of the game's strict loaders, enforced before the folder exists
		foreach ( var (manifestText, objectsText, expected) in new (string? Manifest, string? Objects, string Expected)[]
		{
			("""{ "schema": 1, "id": "Community.Own", "name": "x", "version": "1.0.0", "category": "community" }""", null, "mod id"),
			("""{ "schema": 1, "id": "community.own", "name": "x", "version": "1.0", "category": "community" }""", null, "version"),
			("""{ "schema": 1, "id": "community.own", "name": "x", "version": "1.0.0", "category": "community", "colour": 1 }""", null, "unknown field"),
			(null, """{ "schema": 1, "idRange": { "first": 100, "last": 200 }, "objects": [] }""", "id range"),
			(null, """{ "schema": 1, "idRange": { "first": 6000, "last": 6009 }, "objects": [ { "archive": "../x", "theme": "space", "category": "rides", "infoId": 6001 } ] }""", "archive path"),
			(null, """{ "schema": 1, "idRange": { "first": 6000, "last": 6009 }, "objects": [ { "archive": "a", "theme": "mars", "category": "rides", "infoId": 6001 } ] }""", "theme"),
			(null, """{ "schema": 1, "idRange": { "first": 6000, "last": 6009 }, "objects": [ { "archive": "a", "theme": "space", "category": "rides", "infoId": 7000 } ] }""", "outside the range"),
		} )
		{
			File.WriteAllText( Path2( "m.json" ), manifestText ?? """{ "schema": 1, "id": "community.own", "name": "x", "version": "1.0.0", "category": "community" }""" );
			File.WriteAllText( Path2( "o.json" ), objectsText ?? """{ "schema": 1, "idRange": { "first": 6000, "last": 6009 }, "objects": [] }""" );
			var result = Run( "mod", "--out", Path2( "refused" ), "--manifest", Path2( "m.json" ), "--objects", Path2( "o.json" ), "--force" );
			Assert.AreEqual( 1, result.Code, expected );
			StringAssert.Contains( result.Error, expected );
		}
		Assert.IsFalse( Directory.Exists( Path2( "refused" ) ) );
	}

	[TestMethod]
	public void TheSpritesCommandWrapsSwimmerFramesIntoASpriteSet()
	{
		var frames = Path2( "frames" );
		Directory.CreateDirectory( frames );
		var counts = new Dictionary<char, int> { ['A'] = 8, ['B'] = 8, ['C'] = 4 };
		var shade = 0;
		foreach (var (slot, count) in counts)
			for ( var direction = 0; direction < 5; direction++ )
				for ( var frame = 0; frame < count; frame++ )
				{
					var pixels = new byte[64 * 48 * 4];
					for ( var index = 0; index < pixels.Length; index += 4 )
						(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]) = ((byte)(shade % 250), (byte)(slot * 3), (byte)(direction * 40), (index / 4 % 7 == 0) ? (byte)0 : (byte)255);
					shade++;
					using var stream = File.Create( System.IO.Path.Combine( frames, $"{slot}_d{direction}_f{frame}.png" ) );
					PngWriter.Write( stream, pixels, 64, 48 );
				}
		var result = Run( "sprites", "--frames", frames, "--name", "SPR_SW", "--out", Path2( "sprites" ) );
		Assert.AreEqual( 0, result.Code, result.Error );
		CollectionAssert.AreEquivalent( new[] { "SPR_SW.ESP", "SPR_SW.FPC", "SPR_SW.TPC" }, Directory.EnumerateFiles( Path2( "sprites" ) ).Select( System.IO.Path.GetFileName ).ToArray() );

		var esp = new SpriteAnimationFile( new MemoryStream( File.ReadAllBytes( Path2( "sprites", "SPR_SW.ESP" ) ) ) );
		// A -> slot 1 (8 frames, 5 directions), B -> slot 2, C -> slot 4; the others stay empty
		Assert.AreEqual( new SpriteAnimation( 1, 0, 8, 5 ), esp.Animations[1] );
		Assert.AreEqual( new SpriteAnimation( 2, 40, 8, 5 ), esp.Animations[2] );
		Assert.AreEqual( new SpriteAnimation( 4, 80, 4, 5 ), esp.Animations[4] );
		Assert.IsTrue( esp.Animations.Where( animation => animation.Slot is not (1 or 2 or 4) ).All( animation => animation.IsEmpty ) );
		var bank = new SpriteBankFile( new MemoryStream( File.ReadAllBytes( Path2( "sprites", "SPR_SW.FPC" ) ) ) );
		Assert.AreEqual( 100, bank.Frames.Count );
		// the 64 x 48 canvas has its hotspot at (32, 27): the frame's left/top edge lies at -32/-27 from it
		Assert.IsTrue( bank.Frames.All( frame => (frame.Width, frame.Height, frame.OriginX, frame.OriginY) == (64, 48, -32, -27) ) );
		// frame (slot A, direction 2, frame 3) is sprite 2 * 8 + 3 and keeps its pixels' transparency
		var decoded = new byte[64 * 48 * 4];
		bank.DecodeRgba( bank.Frames[2 * 8 + 3], decoded, 64 * 4, 0, 0 );
		Assert.AreEqual( 0, decoded[3], "pixel 0 is transparent" );
		Assert.AreEqual( 255, decoded[4 * 1 + 3], "pixel 1 is opaque" );

		// another slot map, a frame size without a default hotspot, and a gap in the frames
		Assert.AreEqual( 0, Run( "sprites", "--frames", frames, "--name", "SPR_S2", "--out", Path2( "s2" ), "--slot", "A=3", "--slot", "B=5", "--slot", "C=6" ).Code );
		File.Delete( System.IO.Path.Combine( frames, "C_d1_f2.png" ) );
		var gap = Run( "sprites", "--frames", frames, "--name", "SPR_S3", "--out", Path2( "s3" ) );
		Assert.AreEqual( 1, gap.Code );
		StringAssert.Contains( gap.Error, "frames 0..3" );
	}

	[TestMethod]
	public void SpriteFramesOfAnotherSizeNeedAHotspot()
	{
		var frames = Path2( "odd" );
		Directory.CreateDirectory( frames );
		using ( var stream = File.Create( System.IO.Path.Combine( frames, "A_d0_f0.png" ) ) )
			PngWriter.Write( stream, new byte[10 * 10 * 4], 10, 10 );
		var missing = Run( "sprites", "--frames", frames, "--name", "SPR_O", "--out", Path2( "o" ) );
		Assert.AreEqual( 1, missing.Code );
		StringAssert.Contains( missing.Error, "--hotspot" );
		Assert.AreEqual( 0, Run( "sprites", "--frames", frames, "--name", "SPR_O", "--out", Path2( "o" ), "--hotspot", "5,9" ).Code );
	}

	[TestMethod]
	public void TheBuiltInSettingsAndScriptStayInstalledAndValid()
	{
		var pairs = new SAMParser( Templates.RenderSam( "Pool" ) ).Parse();
		// the keys of the original Puzzle.sam, with the kit's values
		foreach ( var key in new[] { "Info.Id", "Info.Name", "Info.RideTypeStringIndex", "UsageInfo.InitPricePerUse", "UsageInfo.InitCostOfGoods", "UsageInfo.InitChanceOfLoosing", "UsageInfo.EntryCellStandPosX",
			"UsageInfo.EntryCellStandPosY", "UsageInfo.ExitCellAppearPosX", "UsageInfo.ExitCellAppearPosY", "UsageInfo.ExcitementLevel", "UsageInfo.MinCapacity", "UsageInfo.MaxCapacity",
			"UsageInfo.RideHandlesSprite", "Upgrades[0].CostOfUpgrade", "Upgrades[0].CostOfResearch", "Upgrades[0].InitCapacity", "Research.Group" } )
			Assert.IsTrue( pairs.Any( pair => pair.Key == key ), key );
		Assert.ThrowsException<ArgumentException>( () => Templates.RenderSam( "Say \"hi\"" ) );

		// the script disassembles to the text it was assembled from (the lossless text form) and uses the standard variables first
		var bytes = RideScriptAssembler.Assemble( Templates.PoolScript );
		CollectionAssert.AreEqual( bytes, RideScriptAssembler.Assemble( RideScriptDisassembler.Disassemble( bytes ) ) );
		using var stream = new MemoryStream( bytes );
		var script = new RideScriptFile( stream );
		CollectionAssert.AreEqual( Enum.GetNames<RideVariables>(), script.VariableNames.Take( 12 ).ToArray() );
	}

	// ---- End to end ---------------------------------------------------------------------------------------------------------

	/// <summary>Builds the pool through the command line into a mod folder and resolves the folder as the game does.</summary>
	private (string Mod, ModSet Set) BuildMod( string glb, string label ) => BuildModFor( glb, label, null, "sideshow" );

	/// <summary>As <see cref="BuildMod"/>; with <paramref name="fileSystem"/> (the game data) the mod is resolved over it, else over a stand-in game folder. <paramref name="category"/> is the object's category.</summary>
	private (string Mod, ModSet Set) BuildModFor( string glb, string label, BaseFileSystem? fileSystem, string category )
	{
		var archive = Path2( label, "pool" );
		var mod = Path2( label, "mod" );
		var built = Run( "object", "--glb", glb, "--name", "pool", "--out", archive, "--display-name", "Kit Pool" );
		Assert.AreEqual( 0, built.Code, built.Error );
		Assert.IsFalse( built.Output.Contains( "warning" ), built.Output );
		var made = Run( "mod", "--out", mod, "--id", "community.pool", "--name", "Pool", "--id-first", "20000", "--id-last", "20099",
			"--object", $"archive=objects/pool,theme=jungle,category={category},id=20001,name=Kit Pool,src={archive}" );
		Assert.AreEqual( 0, made.Code, made.Error );

		if ( fileSystem == null )
		{
			// a stand-in game folder (the mod pipeline needs a file system; the object's data comes from the mod)
			var game = Path2( label, "game" );
			var totem = System.IO.Path.Combine( game, "levels", "jungle", "rides", "totem" );
			Directory.CreateDirectory( totem );
			File.WriteAllText( System.IO.Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
			File.WriteAllBytes( System.IO.Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
			fileSystem = new BaseFileSystem( game );
		}
		FileSystem = fileSystem;
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var set = ModSet.Resolve( ModDiscovery.Discover( new[] { mod }, Array.Empty<string>(), null, null ).ToList(),
			new ModSettings { Enabled = new Dictionary<string, bool> { ["community.pool"] = true } }, null, GameEdition.ThemeParkWorld, new[] { Core } );
		Assert.AreEqual( 1, set.Mods.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		return (mod, set);
	}

	private sealed class RecordingEffects : IRideScriptEffects
	{
		public readonly List<Opcode> Calls = new();

		public int Perform( RideEffectCall call )
		{
			Calls.Add( call.Opcode );
			return 0;
		}
	}

	[TestMethod]
	public void AnObjectBuiltByTheKitLoadsThroughTheModPipelineAndRunsItsScript()
	{
		foreach ( var (source, glb) in Pools() )
		{
			var (_, set) = BuildMod( glb, source );
			using ( ModContentRoots.Use( set, FileSystem ) )
			{
				// objects.json -> ObjectCatalog
				var entry = ObjectCatalog.Load( "jungle" ).Get( 20001 );
				Assert.AreEqual( ObjectCategory.Sideshow, entry.Category, source );
				Assert.AreEqual( "/objects/pool", entry.ArchivePath );
				Assert.AreEqual( "Kit Pool", entry.DisplayName );
				Assert.AreEqual( 20001, entry.Settings.GetInt( "Info.Id" ), "objects.json replaces the settings' Info.Id" );
				Assert.AreEqual( (3, 3), (entry.Shape.Width, entry.Shape.Height), "the template's 3 x 3 footprint" );
				Assert.IsNotNull( entry.ScriptPath, "the assembled script is found by the archive name" );

				// the model: LoadModel / ConvertMesh
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
				Assert.IsTrue( model.Meshes.Count >= 2, source );
				var textures = new List<string>();
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{source}: {mesh.Name}" );
					Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
					textures.AddRange( mesh.Materials.Select( material => material.Name ) );
				}
				CollectionAssert.AreEquivalent( new[] { "pooltile", "poolwatr" }, textures.Distinct().ToArray() );
				var water = model.Meshes.SelectMany( mesh => mesh.Materials ).Single( material => material.Name == "poolwatr" );
				Assert.AreEqual( ModelBuilder.SelfLitMaterialFlag, water.Pad & ModelBuilder.SelfLitMaterialFlag, "the water is self-lit" );
				Assert.AreEqual( ModelBuilder.SlotBaseFlags | ModelBuilder.SlotWrapFlags, water.Flags );
				// the footprint: every position lies inside X 0..30, Z 0..30 (the converter maps Blender Y to MD2 Z), height above the ground
				var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
				Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 30.01f && p.Z >= -0.01f && p.Z <= 30.01f && p.Y >= -0.01f ), $"{source}: the pool stands on its 3 x 3 cells" );

				// the textures: ResolveTexture finds the .tga in textures/ and it decodes to the exported image
				foreach ( var name in textures.Distinct() )
				{
					var texture = ObjectAssets.ResolveTexture( entry, name );
					Assert.IsNotNull( texture, $"{source}: {name}" );
					Assert.AreEqual( name + ".tga", System.IO.Path.GetFileName( texture!.Value.Path ) );
					var (data, width, height) = Texture.DecodeImage( texture.Value.FileSystem, texture.Value.Path );
					var exported = ImageResult.FromMemory( GltfScene.Read( File.ReadAllBytes( glb ) ).Images.Single( image => image.Name.StartsWith( name, StringComparison.Ordinal ) ).Data, ColorComponents.RedGreenBlueAlpha );
					Assert.AreEqual( (exported.Width, exported.Height), (width, height) );
					CollectionAssert.AreEqual( exported.Data, data, $"{source}: {name} survives glb -> TGA unchanged" );
				}

				// the heads are found by the ride head space; the script runs in the VM
				Assert.AreEqual( 6, RideHeadSpace.Create( model, seed: 1 )!.Count );
				using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
				var vm = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = new RecordingEffects(), Seed = 1 } );
				Assert.AreEqual( 6, entry.InitialCapacity, "Upgrades[0].InitCapacity of the template" );
				vm[RideVariables.VAR_CAPACITY] = entry.InitialCapacity; // what the object runtime writes from the settings
				var executed = new List<Opcode>();
				vm.InstructionExecuting = ( _, instruction ) => executed.Add( (Opcode)instruction.Opcode );
				for ( var step = 0; step < 300; ++step )
				{
					vm.Advance( 1.0 / 60 );
					Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
				}
				Assert.AreEqual( Opcode.NAME, executed[0] );
				Assert.IsTrue( executed.Contains( Opcode.GETTIMER ), "the fill loop runs" );
			}
		}
	}

	private static Matrix4x4 WorldOf( ModelFile model, int node )
	{
		var world = Matrix4x4.Identity;
		for ( var index = node; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		return world;
	}

	/// <summary>
	/// The pool as the placeholder script plays it with a guest, headless and without game data: the real bridge, simulation and head space with the built object's model
	/// and script (the same rig as the queue tests, as a ride: the M3 gate below places rides only). The guest walks to the ride, a head is hung on a head record, the guest
	/// swims for a few seconds, the head comes down and the visit counts. Placing the object as an <c>OriginalObject</c> entity needs a GPU context and is not done here.
	/// </summary>
	[TestMethod]
	public void AGuestEntersTheBuiltPoolSwimsAndLeaves()
	{
		var (source, glb) = Pools().First();
		var (_, set) = BuildMod( glb, source );
		using var scope = ModContentRoots.Use( set, FileSystem );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 20001 );
		var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );

		const float tick = 1f / FixedStepClock.TicksPerSecond;
		var grid = new GuestPathGrid( 12, 12 );
		for ( var x = 0; x < 10; x++ )
			grid.SetPath( x, 0, true );
		var ride = new RideVisitorBridge( 1, "Pool", RideVisitorKind.Ride, 6, 35, 30, GuestNeeds.None )
		{
			EntranceCell = (2, 0),
			ExitCell = (2, 0),
			QueueFrontCell = (2, 5),
			QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 6, 30 ),
			Heads = RideHeadSpace.Create( model, seed: 5 )
		};
		var effects = new VisitorRideScriptEffects( ride, UnimplementedRideScriptEffects.Instance );
		using var scriptStream = entry.FileSystem.OpenRead( entry.ScriptPath! );
		var script = new RideVM( new RideScriptFile( scriptStream ), new RideVMOptions { Effects = effects, Seed = 3 } );
		script[RideVariables.VAR_CAPACITY] = entry.InitialCapacity; // the object runtime writes it from the settings
		ride.Attach( script, () => true );
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (2, 5 - index) ) ) );
		QueuePaths.TryConnect( grid, ride );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 7 ) { ArrivalsEnabled = false };
		simulation.Register( ride );
		script.Advance( tick );

		var guest = simulation.SpawnInPark( 2, 0 );
		guest.Happiness = 60;
		guest.AttractionId = 1;
		guest.State = GuestState.GoingToRide;
		var sawHead = false;
		var headCameDown = false;
		for ( var step = 0; step < 60 * 120 && !(headCameDown && guest.RidesTaken > 0); step++ )
		{
			simulation.Tick( tick );
			ride.HostStep();
			script.Advance( tick );
			Assert.AreNotEqual( RideVMState.Faulted, script.State, script.FaultMessage );
			sawHead |= ride.Heads!.HasVisitors;
			headCameDown |= sawHead && !ride.Heads.HasVisitors;
		}
		Assert.IsTrue( sawHead, "ADDHEAD hung the guest's head on a head record of the built model" );
		Assert.IsTrue( headCameDown, "DELHEAD took it down" );
		Assert.AreEqual( 1, guest.RidesTaken, "the visit counts" );
		Assert.AreEqual( 0, script[RideVariables.VAR_ONRIDE], "nobody is left on the ride" );
	}

	/// <summary>
	/// The full headless park of the M3 gate (a real level, paths, an entrance, a queue, guests arriving for minutes) with the built pool as the attraction: the
	/// object is bought, built on its footprint, queued and run by its own script, and guests board it. Needs <c>OPENTPW_GAME_PATH</c> (the level, its balance and the guests).
	/// </summary>
	[TestMethod]
	public void AParkOfTheM3GateTakesGuestsThroughTheBuiltPool()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var (source, glb) = Pools().First();
		var (_, set) = BuildModFor( glb, source, new BaseFileSystem( data ), "rides" );
		using var scope = ModContentRoots.Use( set, FileSystem );
		ObjectCatalog.BonusDataRoot = null;
		var report = M3Gate.Run( new M3GateOptions { Level = "jungle", Minutes = 3, ArrivalTimeBetween = 1, DeterminismProbe = false, AttractionFilter = entry => entry.InfoId == 20001 } );
		Assert.AreEqual( M3GateVerdict.Pass, report["build.attraction"].Verdict, report.ToSummary() );
		StringAssert.Contains( report["build.attraction"].Evidence["object"]!.ToString(), "Kit Pool" );
		Assert.IsTrue( (int)report["guests.flow"].Evidence["attractionBoarded"]! > 0, report.ToSummary() );
		Assert.AreEqual( M3GateVerdict.Pass, report["rides.scripts-run"].Verdict, report.ToSummary() );
	}

	/// <summary>When Blender is installed the sample script's own export (what a modeller would hand over) converts without a warning.</summary>
	[TestMethod]
	public void TheBlenderSamplePoolConvertsWithoutWarnings()
	{
		var blender = Blender();
		if ( blender == null )
			Assert.Inconclusive( "Blender is not installed (set OPENTPW_BLENDER)." );
		var glb = BlenderPool( blender! );
		var conversion = GltfModelConverter.Convert( GltfScene.Read( glb ) );
		Assert.AreEqual( 0, conversion.Warnings.Count, string.Join( "; ", conversion.Warnings ) );
		CollectionAssert.AreEquivalent( new[] { "pooltile", "poolwatr" }, conversion.Textures.Select( texture => texture.Name ).ToArray() );
		var model = conversion.Model.Build();
		Assert.AreEqual( 6, RideHeadSpace.Create( model, 1 )!.Count );
		Assert.AreEqual( 12, model.Nodes.Count, "the root, five meshes and six heads" );
	}
}
