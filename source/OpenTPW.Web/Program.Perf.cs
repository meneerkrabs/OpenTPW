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
