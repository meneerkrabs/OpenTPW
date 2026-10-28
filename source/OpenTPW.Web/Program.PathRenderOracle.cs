using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using OpenTPW.Online.Packages;
using OpenTPW.Tests;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;

namespace OpenTPW;

/// <summary>
/// Diagnostic-only partials, excluded unless OpenTpwPathRenderOracle=true. Synthetic input preparation
/// bypasses full game loading, NEVER terrain construction, Level.Render, PrepareRender, or GPU uploads.
/// This is a single-use isolated page, not an API to attach diagnostics to a running game.
/// </summary>
public static partial class Program
{
	private static Renderer? pathOracleRenderer;
	private static OriginalTerrain? pathOracleTerrain;
	private static Level? pathOracleLevel;
	private static bool pathOracleUsed;
	private static int pathOracleCase;
	private static string? pathOracleRoot, pathOracleWct;
	private static BaseFileSystem? pathOraclePreviousFiles;
	private static IGraphicsSettings? pathOraclePreviousGraphics;
	private static RenderQuality? pathOraclePreviousQuality;
	private static Veldrid.GraphicsDevice? pathOraclePreviousDevice;
	private static CameraMode? pathOraclePreviousMode;
	private static Matrix4x4 pathOraclePreviousView, pathOraclePreviousProjection;
	private static readonly HashSet<(int X, int Y)> pathOracleCells = new();
	private static readonly string[] pathOracleNames = [
		"a-procedural", "b-procedural", "b-suspended-first", "b-suspended-second",
		"b-resumed", "b-edit-procedural", "b-edit-suspended-first", "b-edit-suspended-second",
		"a-restored-procedural", "a-restored-static"
	];
	private static readonly JsonSerializerOptions pathOracleJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	[JSExport]
	public static void PathOracleStart()
	{
		if ( pathOracleUsed || Global.Render != null || Entity.All.Count != 0 || Level.Current != null
			|| TexturePackSwitch.Current != null || Texture.ReloadTargets().Count != 0 || TexturePack.Directory != null
			|| !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" ) ) )
			throw new InvalidOperationException( "The path oracle requires a fresh page without a game or texture-pack override." );
		pathOracleUsed = true;
		pathOraclePreviousFiles = FileSystem;
		pathOraclePreviousGraphics = IGraphicsSettings.Instance;
		pathOraclePreviousQuality = CompatibilityRuntime.RenderQuality;
		pathOraclePreviousDevice = Global.Device;
		pathOraclePreviousMode = Camera.Mode;
		pathOraclePreviousView = Camera.ViewMatrix;
		pathOraclePreviousProjection = Camera.ProjMatrix;
		try
		{
			// Only public shader source is materialized; no game/extension sprites, models or sounds.
			var assembly = typeof( Program ).Assembly;
			foreach ( var name in assembly.GetManifestResourceNames().Where( name => name.StartsWith( "content/shaders/", StringComparison.Ordinal ) ) )
			{
				var file = Path.GetFullPath( name, AppContext.BaseDirectory );
				Directory.CreateDirectory( Path.GetDirectoryName( file )! );
				using var input = assembly.GetManifestResourceStream( name )!;
				using var output = File.Create( file );
				input.CopyTo( output );
			}
			pathOracleRoot = Directory.CreateTempSubdirectory( "opentpw-path-oracle-" ).FullName;
			FileSystem = new BaseFileSystem( Path.Combine( pathOracleRoot, "game" ) );
			IGraphicsSettings.Instance = new GraphicsSettingsService( _ => null, new GraphicsSettings
			{
				Preset = GraphicsPreset.Custom,
				Detail = new OriginalDetailSettings { ProceduralTexturing = 1, ProceduralTextureCache = 0, Lighting = 0, TextureFiltering = 0, Mipmap = 0 }
			}, null, () => CompatibilityFlags.Original );
			CompatibilityRuntime.RenderQuality = IGraphicsSettings.Instance.Effective;
			var park = PathOraclePark();
			var diagnostics = new List<string>();
			TexturePack.Activate( "a", diagnostics, Path.Combine( pathOracleRoot, "packs" ) );
			if ( diagnostics.Count != 0 || TexturePack.Directory == null ) throw new InvalidOperationException( string.Join( "; ", diagnostics ) );
			Global.Render = pathOracleRenderer = new Renderer( new DisplaySettings { Width = 320, Height = 200, FrameCap = 0 }, null );
			pathOracleRenderer.Start();
			Camera.Mode = new CameraMode();
			pathOracleTerrain = new OriginalTerrain( park );
			pathOracleCells.UnionWith( [(1, 1), (4, 1)] );
			pathOracleTerrain.PathSource = (x, y) => pathOracleCells.Contains( (x, y) );
			pathOracleTerrain.RefreshPaths();
			var ground = pathOracleTerrain.ProceduralGround ?? throw new InvalidOperationException( "Synthetic procedural ground was not constructed." );
			if ( !ground.IsProcedural( 1, 1 ) || ground.IsProcedural( 4, 1 ) || ground.ArtOf( 1, 1 ) == null )
				throw new InvalidOperationException( "Fixture does not straddle the real procedural/static boundary." );
			if ( Texture.FindLoaded( pathOracleWct! ) == null || Texture.ReloadTargets().Count != 1 )
				throw new InvalidOperationException( "The outside path must create exactly one real GPU WCT reload target." );
			pathOracleLevel = Level.CreatePathOracleRenderFixture( pathOracleTerrain );
			// Entity.Level stays null: no uninitialized simulation properties enter material uniforms.
			if ( Entity.All.Any( entity => entity.Level != null ) ) throw new InvalidOperationException( "Fixture unexpectedly attached to a gameplay level." );
			pathOracleRenderer.OnRender = () =>
			{
				Camera.ViewMatrix = Matrix4x4.CreateLookAt( new SysVector3( 0, 0, 12 ), SysVector3.Zero, SysVector3.UnitY );
				Camera.ProjMatrix = Matrix4x4.CreatePerspectiveFieldOfView( MathF.PI / 3, 320f / 200, 0.1f, 1000 );
				pathOracleLevel.Render(); // The ONLY call path to PrepareRender and the real entity snapshot.
			};
		}
		catch
		{
			PathOracleStop();
			throw;
		}
	}

	private static OriginalPark PathOraclePark()
	{
		var terrain = Path.Combine( pathOracleRoot!, "game/levels/pathoracle/terrain" );
		Directory.CreateDirectory( Path.Combine( terrain, "textures" ) );
		Directory.CreateDirectory( Path.Combine( terrain, "pathtex" ) );
		File.WriteAllBytes( Path.Combine( terrain, "textures/ground.png" ), PngImage.EncodeRgba( 8, 8, PathOraclePixels( 96, 96, 96 ) ) );
		pathOracleWct = Path.Combine( terrain, "pathtex/tile.wct" );
		File.WriteAllBytes( pathOracleWct, PathOracleBlackWct() );
		File.WriteAllText( Path.Combine( terrain, "base.tct" ), "PathTex\n0 tile.wct\n" );
		File.WriteAllText( Path.Combine( terrain, "../Standard.sam" ),
			"MapInfo.HeightfieldXStart 0\nMapInfo.HeightfieldYStart 0\nMapInfo.HeightfieldWidth 3\nMapInfo.HeightfieldHeight 2\n" );
		File.WriteAllBytes( Path.Combine( terrain, "base.lnd" ), SyntheticLnd.Build( 5, 4,
			[48, 40, 40, 90, 68, 40, 97, 30, 55, 50], (_, _, channel) => channel == 0 ? (byte)255 : (byte)0,
			(_, image, _, _, channel) => image < 6 ? (channel == 0 ? (byte)0 : (byte)96) : (byte)0 ) );
		foreach ( var (name, red, blue) in new[] { ("a", (byte)224, (byte)32), ("b", (byte)32, (byte)224) } )
		{
			var pack = Path.Combine( pathOracleRoot!, "packs", name );
			// Loose BaseFileSystem listings return ABSOLUTE filenames; use the actual decoder input key.
			var replacement = Path.Combine( pack, "textures", TexturePack.RelativeFileName( pathOracleWct ) );
			Directory.CreateDirectory( Path.GetDirectoryName( replacement )! );
			File.WriteAllBytes( replacement, PngImage.EncodeRgba( 8, 8, PathOraclePixels( red, 32, blue ) ) );
			File.WriteAllText( Path.Combine( pack, "pack.json" ), JsonSerializer.Serialize( new TexturePackManifest { Format = TexturePack.FormatVersion, Textures = 1, Scale = 1 } ) );
		}
		var map = new MapFile( new MemoryStream( MapFile.Encode( 4, 5, new byte[MapFile.TitleBytes], new uint[] { 8, 8, 8, 8, 8 }, new byte[20] ) ) );
		var field = new ModelHeightfield( 5, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[6 * 5], new uint[20] );
		var model = new ModelFile { Heightfield = field, Textures = [new ModelTexture( 0, 0, ["ground.png"] )] };
		// Synthetic data construction only: no game loader, saves, catalogue, original media, or private field surgery on terrain.
		return (OriginalPark)Activator.CreateInstance( typeof( OriginalPark ), BindingFlags.Instance | BindingFlags.NonPublic,
			null, new object?[] { "pathoracle", map, model, null, null, new Dictionary<int, OriginalObjectInfo>() }, null )!;
	}

	private static byte[] PathOraclePixels( byte red, byte green, byte blue )
	{
		var pixels = new byte[8 * 8 * 4];
		for ( var i = 0; i < pixels.Length; i += 4 )
			(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (red, green, blue, 255);
		return pixels;
	}

	// Generated black 8x8 WCT, same all-zero coefficients as WctBoundsTests; not a copied game texture.
	private static byte[] PathOracleBlackWct()
	{
		using var compressed = new MemoryStream();
		using ( var zip = new ZLibStream( compressed, CompressionLevel.Optimal, true ) ) zip.Write( new byte[96] );
		using var chunk = new MemoryStream();
		using ( var writer = new BinaryWriter( chunk, Encoding.UTF8, true ) )
		{
			writer.Write( "BILZ"u8 ); writer.Write( 96 ); writer.Write( 0x1C + (int)compressed.Length );
			writer.Write( 15 ); writer.Write( 9 ); writer.Write( 0 ); writer.Write( 0 ); writer.Write( compressed.ToArray() );
		}
		using var output = new MemoryStream();
		using ( var writer = new BinaryWriter( output, Encoding.UTF8, true ) )
		{
			writer.Write( new byte[] { 1, 0, 0, 0 } ); writer.Write( (short)8 ); writer.Write( (short)8 );
			for ( var i = 0; i < 4; i++ ) writer.Write( (short)0 );
			writer.Write( (int)chunk.Length ); writer.Write( 0 ); writer.Write( 0 ); writer.Write( chunk.ToArray() );
		}
		return output.ToArray();
	}

	[JSExport]
	public static string PathOracleDraw( string name )
	{
		var renderer = pathOracleRenderer ?? throw new InvalidOperationException( "Start the path oracle first." );
		var terrain = pathOracleTerrain!;
		if ( pathOracleCase >= pathOracleNames.Length || name != pathOracleNames[pathOracleCase] )
			throw new ArgumentException( "Cases must run exactly once in the declared order." );
		object? change = null;
		if ( name is "b-procedural" or "a-restored-procedural" )
		{
			var loaded = Texture.FindLoaded( pathOracleWct! )!;
			var oldNative = loaded.NativeTexture;
			var diagnostics = new List<string>();
			var next = name == "b-procedural" ? "b" : "a";
			var pending = TexturePackSwitch.Begin( next, diagnostics, Path.Combine( pathOracleRoot!, "packs" ), TimeSpan.FromSeconds( 5 ) )
				?? throw new InvalidOperationException( "Pack switch was refused." );
			TexturePackSwitch.PumpCurrent();
			if ( diagnostics.Count != 0 || !pending.Finished || pending.Total != 1 || pending.Done != pending.Total
				|| TexturePackSwitch.Current != null || ReferenceEquals( oldNative, loaded.NativeTexture ) || loaded.Width != 8 || loaded.Height != 8 )
				throw new InvalidOperationException( "Real same-size GPU texture replacement did not complete." );
			change = new { next, pending.Done, pending.Total, sameDimensions = true, nativeIdentityChanged = true };
		}
		if ( name == "b-edit-procedural" )
		{
			pathOracleCells.Remove( (1, 1) ); pathOracleCells.Add( (2, 1) );
			terrain.RefreshPaths();
		}
		var suspended = name.Contains( "suspended", StringComparison.Ordinal ) || name == "a-restored-static";
		Camera.Mode = suspended ? new FreeWalkCameraMode( terrain.Park.Heightfield, Vector3.Zero, Vector3.Forward ) : new CameraMode();
		var before = terrain.InspectPathOracle();
		var frame = renderer.FramesDrawn;
		var pass = pathOracleLevel!.RenderPass;
		if ( !renderer.Frame() || renderer.FramesDrawn != frame + 1 || pathOracleLevel.RenderPass != pass + 1 )
			throw new InvalidOperationException( "Expected exactly one presented frame and one Level.Render call." );
		var after = terrain.InspectPathOracle();
		var target = pathOracleCells.Contains( (1, 1) ) ? (X: 1, Y: 1) : (X: 2, Y: 1);
		var ground = terrain.ProceduralGround!;
		if ( !suspended && (!ground.VisibleCells.Contains( (target.X, target.Y) ) || ground.ArtOf( target.X, target.Y ) == null) )
			throw new InvalidOperationException( "The sampled cell was not rendered by the actual procedural terrain." );
		object Sample( string role, int x, int y )
		{
			var centre = OriginalParkPlacement.GetCellCenter( terrain.Park.Heightfield, x, y );
			var clip = SysVector4.Transform( new SysVector4( centre, 1 ), Camera.ViewMatrix * Camera.ProjMatrix );
			if ( !(clip.W > 0) ) throw new InvalidOperationException( "Sample is behind the camera." );
			return new { role, x, y, u = (clip.X / clip.W + 1) / 2, v = (clip.Y / clip.W + 1) / 2 };
		}
		pathOracleCase++;
		return JsonSerializer.Serialize( new { name, ordinal = pathOracleCase, frame = renderer.FramesDrawn,
			levelPass = pathOracleLevel.RenderPass, width = renderer.MultisampledFramebuffer.Width, height = renderer.MultisampledFramebuffer.Height,
			suspended, before, after, change, samples = new[] { Sample( "inside", target.X, target.Y ), Sample( "outside", 4, 1 ), Sample( "ground", 0, 0 ), Sample( "previous", 1, 1 ) } }, pathOracleJson );
	}

	[JSExport]
	public static void PathOracleStop()
	{
		if ( !pathOracleUsed ) return;
		try
		{
			TexturePackSwitch.Current?.Cancel();
			pathOracleTerrain?.Delete();
			pathOracleTerrain = null;
			pathOracleLevel = null; // Do not call gameplay Dispose on a render-only fixture.
			if ( pathOracleRenderer is { } renderer )
			{
				renderer.OnRender = null;
				Global.Device.WaitForIdle(); renderer.FlushDeletions(); renderer.Stop(); renderer.Window.SdlWindow.Close();
			}
		}
		finally
		{
			TexturePack.Activate( null, new List<string>() );
			Camera.Mode = pathOraclePreviousMode!;
			Camera.ViewMatrix = pathOraclePreviousView; Camera.ProjMatrix = pathOraclePreviousProjection;
			FileSystem = pathOraclePreviousFiles!; IGraphicsSettings.Instance = pathOraclePreviousGraphics;
			CompatibilityRuntime.RenderQuality = pathOraclePreviousQuality!;
			Global.Render = null!; Global.Device = pathOraclePreviousDevice!; pathOracleRenderer = null;
			if ( pathOracleRoot != null ) { Directory.Delete( pathOracleRoot, true ); pathOracleRoot = null; }
		}
		// The harness closes this one-use target; GPU caches are not reused on another graphics device.
	}
}

public partial class Level
{
	internal static Level CreatePathOracleRenderFixture( OriginalTerrain terrain )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		level.OriginalTerrain = terrain;
		return level;
	}
}

internal sealed record PathOracleTerrainState( int StaticPaths, bool StaticStale, bool GroundStale, bool ProceduralHidden,
	bool StaticGroundHidden, bool AllStaticPathsVisible, bool OutsidePathsVisible, long Builds, int PathCells );

public sealed partial class OriginalTerrain
{
	// Read-only observations, compiled only into the diagnostic assembly. No preparation/render bypass.
	internal PathOracleTerrainState InspectPathOracle() => new( pathPartsAll.Count, pathPartsAllStale, staticHolesStale,
		proceduralPart?.Hidden ?? throw new InvalidOperationException( "Missing procedural part." ),
		heightfieldPart?.Hidden ?? throw new InvalidOperationException( "Missing static ground." ),
		pathPartsAll.Count > 0 && pathPartsAll.All( part => !part.Hidden ),
		pathPartsOutside.Count > 0 && pathPartsOutside.All( part => !part.Hidden ), procedural!.Builds, PathCellCount );
}
