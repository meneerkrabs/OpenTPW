using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// Hooks for the browser performance harness (tools/web-perf, docs/BROWSER-PERFORMANCE.md). They drive the game through the
/// same public UI calls as the native front-end smoke test (a click on an element by id, a key, a buy-window selection) and read
/// the managed memory counters; the page measures the frames. Nothing here changes what a player sees unless the harness calls it.
/// </summary>
public static partial class Program
{
	/// <summary>Where the game is: the front-end screen, the park and its HUD, the buy window and its selection, as JSON.</summary>
	[JSExport]
	public static string PerfState()
	{
		var state = new JsonObject
		{
			["started"] = flow != null,
			["menu"] = flow?.Menu?.Stack.Top?.Name,
			["island"] = flow?.Menu?.Selected.Level,
			["level"] = flow?.Level?.OriginalPark?.LevelName,
			["hud"] = flow?.Hud != null,
			["hudScreen"] = flow?.Hud?.Stack.Top?.Name,
			["buyOpen"] = flow?.Hud?.BuyWindowOpen ?? false,
			["selected"] = flow?.Hud?.SelectedBuyItem?.Entry?.ArchiveName,
			["turn"] = flow?.Level?.Park?.Economy.Turn,
		};
		return state.ToJsonString();
	}

	/// <summary>Frames the renderer has drawn; a frame the cap skips leaves it unchanged, so the page can tell drawn frames from skipped calls.</summary>
	[JSExport]
	public static double PerfFramesDrawn() => renderer?.FramesDrawn ?? 0;

	/// <summary>Clicks the centre of the visible element <paramref name="id"/> on the topmost front-end or HUD screen; false when there is none.</summary>
	[JSExport]
	public static bool PerfClick( string id )
	{
		if ( flow == null )
			return false;
		var screens = new[] { flow.Menu?.Stack.Top, flow.Menu?.Main, flow.Hud?.Stack.Top, flow.Hud?.Screen };
		foreach ( var screen in screens )
		{
			if ( screen?.Find( id ) is { Visible: true } element )
			{
				flow.InjectedInput = UiInput.Click( element.ScreenRect( flow.Context.Canvas ).Center );
				return true;
			}
		}
		return false;
	}

	/// <summary>Presses a UI key by its <see cref="UiKeys"/> name (Accept, Back, Left, ...); false for an unknown name.</summary>
	[JSExport]
	public static bool PerfKey( string key )
	{
		if ( flow == null || !Enum.TryParse<UiKeys>( key, ignoreCase: true, out var parsed ) )
			return false;
		flow.InjectedInput = UiInput.Key( parsed );
		return true;
	}

	/// <summary>The items of a buy-window category (Rides, Shops, Sideshows, Features) as JSON: archive name and shown name.</summary>
	[JSExport]
	public static string PerfBuyItems( string category )
	{
		var items = new JsonArray();
		if ( flow?.Hud is { } hud && Enum.TryParse<BuildCategory>( category, ignoreCase: true, out var parsed ) )
			foreach ( var item in hud.Catalog.GetItems( parsed ) )
				items.Add( new JsonObject { ["archive"] = item.Entry?.ArchiveName, ["name"] = hud.ItemName( item ) } );
		return items.ToJsonString();
	}

	/// <summary>Shows the item with archive <paramref name="archive"/> in the open buy window, as a click on its category and row does; false when it is not listed.</summary>
	[JSExport]
	public static bool PerfShowBuyItem( string category, string archive )
	{
		if ( flow?.Hud is not { BuyWindowOpen: true } hud || !Enum.TryParse<BuildCategory>( category, ignoreCase: true, out var parsed ) )
			return false;
		var item = hud.Catalog.GetItems( parsed ).FirstOrDefault( entry => string.Equals( entry.Entry?.ArchiveName, archive, StringComparison.OrdinalIgnoreCase ) );
		if ( item == null )
			return false;
		hud.SelectCategory( item.Category );
		var index = hud.BuyItems.ToList().IndexOf( item );
		if ( index < 0 )
			return false;
		hud.SelectBuyItem( index );
		return true;
	}

	/// <summary>
	/// Deterministic work whose result must not depend on how the runtime executes it (interpreter or AOT, docs/BROWSER-PERFORMANCE.md):
	/// every texture of a theme decoded and hashed, and the fixed run of the original-data determinism test (the theme's park with its
	/// guests from seed 2026, 3000 ticks at the fastest speed) reduced to its world state hash. Both durations come back too; they are
	/// pure computation, without drawing. Needs the theme's files, so the harness calls it after the park has loaded, as its last step:
	/// the call blocks the page for its duration (the running park drops that time, FixedStepClock) and fills the archive cache. The
	/// fixed run covers the economy and the guests, not rides, object scripts, weather or staff.
	/// </summary>
	[JSExport]
	public static string PerfDigest( string theme )
	{
		var result = new JsonObject();
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var textures = new List<string>();
		CollectTextures( $"levels/{theme}", textures );
		// The listing mixes absolute disk paths and root-relative archive paths; both open as they are, and the key is the same for both.
		var root = Path.TrimEndingDirectorySeparator( FileSystem.GetAbsolutePath( "/" ) ).Replace( '\\', '/' );
		string Key( string path )
		{
			var normalized = path.Replace( '\\', '/' );
			return (normalized.StartsWith( root + "/", StringComparison.Ordinal ) ? normalized[root.Length..] : normalized).TrimStart( '/' );
		}
		textures.Sort( ( left, right ) => string.CompareOrdinal( Key( left ), Key( right ) ) );
		var hash = Fnv1a.Start;
		foreach ( var path in textures )
		{
			hash = Fnv1a.Add( hash, System.Text.Encoding.UTF8.GetBytes( Key( path ) ) );
			try
			{
				using var stream = FileSystem.OpenRead( path );
				var data = new TextureFile( stream ).Data;
				hash = Fnv1a.Add( Fnv1a.Add( Fnv1a.Add( hash, BitConverter.GetBytes( data.Width ) ), BitConverter.GetBytes( data.Height ) ), data.Data );
			}
			catch ( Exception exception )
			{
				// A malformed file must fail the same way in both builds.
				hash = Fnv1a.Add( hash, System.Text.Encoding.UTF8.GetBytes( exception.GetType().Name ) );
			}
		}
		result["textures"] = textures.Count;
		result["textureHash"] = hash.ToString( "X16" );
		result["textureMs"] = watch.ElapsedMilliseconds;

		watch.Restart();
		var seed = new WorldSeed( 2026 );
		var original = OriginalPark.Load( theme );
		var park = ParkEconomyRuntime.ForOriginalLevel( original, ParkStartKind.OriginalSaveReference, seed );
		var guests = new GuestSimulation( GuestPathGrid.FromOriginal( original.Map, original.Save ), GuestSettings.Load( theme ), seed.GuestStream );
		park.AttachGuests( guests );
		var streams = new ParkWorldStreams( seed, park, guests, new RideScriptWorld( seed.ScriptStream ), new SoundEventSystem( new AudioMixer( null ), seed.SoundStream ) );
		park.Economy.Speed = GameSpeed.Fastest;
		for ( var tick = 0; tick < 3000; tick++ )
		{
			guests.Tick( 1f / FixedStepClock.TicksPerSecond );
			park.Economy.Advance( (int)park.Economy.Speed );
		}
		result["simulationHash"] = streams.ComputeStateHash().ToString( "X16" );
		result["simulationMs"] = watch.ElapsedMilliseconds;
		return result.ToJsonString();
	}

	/// <summary>Every .wct below <paramref name="directory"/>, archives included, as the file system lists them.</summary>
	private static void CollectTextures( string directory, List<string> found )
	{
		foreach ( var file in FileSystem.GetFiles( directory ) )
			if ( file.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) )
				found.Add( file );
		foreach ( var child in FileSystem.GetDirectories( directory ) )
			CollectTextures( child, found );
	}

	/// <summary>64-bit FNV-1a: enough to tell two builds' outputs apart, and available on every runtime.</summary>
	private static class Fnv1a
	{
		public const ulong Start = 14695981039346656037;

		public static ulong Add( ulong hash, ReadOnlySpan<byte> bytes )
		{
			foreach ( var value in bytes )
				hash = unchecked( (hash ^ value) * 1099511628211 );
			return hash;
		}
	}

	/// <summary>Managed memory as JSON: live bytes, the GC heap, bytes allocated since start and the collection counts.</summary>
	[JSExport]
	public static string PerfCounters()
	{
		var info = GC.GetGCMemoryInfo();
		return new JsonObject
		{
			["managedBytes"] = GC.GetTotalMemory( false ),
			["heapBytes"] = info.HeapSizeBytes,
			["allocatedBytes"] = GC.GetTotalAllocatedBytes(),
			["gen0"] = GC.CollectionCount( 0 ),
			["gen1"] = GC.CollectionCount( 1 ),
			["gen2"] = GC.CollectionCount( 2 ),
		}.ToJsonString();
	}
}
