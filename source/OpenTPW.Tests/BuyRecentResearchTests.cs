using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Actual buy-list DrawModel output with synthetic frames; no renderer changes, original assets or rendered-parity claims.</summary>
[TestClass, DoNotParallelize]
public class BuyRecentResearchTests
{
	private static void Set( object target, string field, object value ) => target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static BuildItem Item( int id, BuildCategory category = BuildCategory.Rides ) => new( $"item:{id}", id, category, 0, 100, null, Array.Empty<string>() );
	private sealed class Catalog( IReadOnlyList<BuildItem> items ) : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => items.Where( item => item.Category == category ).ToArray();
	}

	private static (ParkHud Hud, UiElement List, UiContext Context, List<OriginalObject> Objects) Fixture( ParkEconomy park, params BuildItem[] rows )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		Set( hud, "<Status>k__BackingField", new EconomyParkStatus( () => park ) );
		var catalog = new Catalog( rows );
		Set( hud, "<Catalog>k__BackingField", catalog );
		Set( hud, "buy", new BuyListModel( catalog, item => item.Id, item => item.Cost ) );
		hud.SelectBuyItem( 0 );
		var type = typeof( ParkHud ).GetNestedType( "BuyItemList", BindingFlags.NonPublic )!;
		var list = (UiElement)Activator.CreateInstance( type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { hud }, null )!;
		list.Bounds = new UiRect( 1009, 179, 804, 723 );
		list.Anchor = UiAnchor.Center;
		// Opening the window finds this private list by its init-only ID.
		typeof( UiElement ).GetProperty( nameof( UiElement.Id ) )!.SetValue( list, "buyList" );
		var screen = new UiScreen( "buy" ); screen.Add( list ); Set( hud, "buyScreen", screen );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var models = new UiModels( name =>
		{
			Assert.AreEqual( "i_boxtick", name );
			var model = new UiModel( new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) ), name );
			var vertices = new[] { new UiModelVertex( Vector2.Zero, 0, Vector2.Zero ), new UiModelVertex( new Vector2( 51, 0 ), 0, Vector2.Zero ), new UiModelVertex( new Vector2( 0, 51 ), 0, Vector2.Zero ) };
			Set( model, "<Frames>k__BackingField", Enumerable.Range( 0, 3 ).Select( frame => new UiModelFrame( $"synthetic-{frame}",
				new[] { new UiModelPart( $"frame-{frame}", vertices, new[] { 0, 1, 2 } ) }, 0, 0, 51, 51 ) ).ToArray() );
			return model;
		} );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), models ) { Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = name => name };
		return (hud, list, context, (List<OriginalObject>)level.Objects.Objects);
	}

	/// <summary>Only the placed object's descriptor is needed by NumberOwned; no original model or script is loaded.</summary>
	private static OriginalObject Placed( int id )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.WhichUIType 0\n", "synthetic.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", settings, new ObjectShape( new[] { "*" } ), "/unused.MD2", null, null,
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), null!, null );
		var runtime = Empty<OriginalObjectRuntime>(); Set( runtime, "<Entry>k__BackingField", entry );
		var item = Empty<OriginalObject>(); Set( item, "<Runtime>k__BackingField", runtime );
		return item;
	}

	private static int[] DrawFrames( UiElement list, UiContext context )
	{
		context.Batch.Clear();
		list.Draw( context, focused: false, pressed: false );
		return context.Batch.Draws.Where( draw => draw.Texture.ImagePath?.StartsWith( "frame-", StringComparison.Ordinal ) == true )
			.Select( draw => int.Parse( draw.Texture.ImagePath![6..], System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
	}

	[TestMethod]
	public void ActualBuyListDrawsEmptyOwnedAndRecentFramesAtTheExistingSize()
	{
		var park = RecentResearchTests.Park();
		foreach ( var id in new[] { RecentResearchTests.A, RecentResearchTests.B, RecentResearchTests.C, RecentResearchTests.D } )
			RecentResearchTests.Complete( park.Research, id );
		var (hud, list, context, objects) = Fixture( park, Item( RecentResearchTests.A ), Item( RecentResearchTests.B ), Item( RecentResearchTests.C ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( RecentResearchTests.B, out _ ) );
		objects.Add( Placed( RecentResearchTests.B ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, DrawFrames( list, context ), "the production row DrawModel path selects all three real frame indices" );
		foreach ( var draw in context.Batch.Draws.Where( draw => draw.Texture.ImagePath?.StartsWith( "frame-", StringComparison.Ordinal ) == true ) )
		{
			// Allow subpixel rounding from the float canvas/model transforms.
			Assert.AreEqual( 51f, draw.Vertices.Max( vertex => vertex.Position.X ) - draw.Vertices.Min( vertex => vertex.Position.X ), 0.001f );
			Assert.AreEqual( 51f, draw.Vertices.Max( vertex => vertex.Position.Y ) - draw.Vertices.Min( vertex => vertex.Position.Y ), 0.001f );
		}
		Assert.AreEqual( 0, hud.BuyItemState( ParkHud.BuyLandRow ) );
		Assert.AreEqual( 0, hud.BuyItemState( ParkHud.ClearLandRow ) );
	}

	[TestMethod]
	public void PurchasesOnlyMaskTheStarAndSellingTheLastCopyExposesItUntilEviction()
	{
		var park = RecentResearchTests.Park(); RecentResearchTests.Complete( park.Research, RecentResearchTests.A );
		var row = Item( RecentResearchTests.A );
		var (hud, list, context, objects) = Fixture( park, row );
		var history = RecentResearchTests.State( park.Research );
		CollectionAssert.AreEqual( new[] { 2 }, DrawFrames( list, context ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( row.InfoId, out var first ) );
		var placedFirst = Placed( row.InfoId ); objects.Add( placedFirst );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( row.InfoId, out var second ) );
		var placedSecond = Placed( row.InfoId ); objects.Add( placedSecond );
		CollectionAssert.AreEqual( new[] { 1 }, DrawFrames( list, context ) );
		park.Sell( first!.Id ); Set( placedFirst, "deleted", true );
		CollectionAssert.AreEqual( new[] { 1 }, DrawFrames( list, context ) );
		park.Sell( second!.Id ); Set( placedSecond, "deleted", true );
		CollectionAssert.AreEqual( new[] { 2 }, DrawFrames( list, context ) );
		Assert.AreEqual( history, RecentResearchTests.State( park.Research ) );
		hud.SortBuyList( BuySortColumn.Price ); hud.SelectBuyItem( 0 ); hud.UpdateBuyPreview( 1 ); hud.UpdateBuyPreview( 2 );
		hud.SelectCategory( BuildCategory.Shops ); hud.SelectCategory( BuildCategory.Rides );
		hud.OpenBuyWindow();
		hud.Stack.Top!.Update( context, UiInput.Idle( new Vector2( 1100, 440 ) ) );
		hud.CloseBuyWindow(); hud.OpenBuyWindow(); hud.CloseBuyWindow();
		Assert.AreEqual( history, RecentResearchTests.State( park.Research ), "hover, selection, preview, tabs, sorting and reopening never acknowledge history" );
		foreach ( var id in new[] { RecentResearchTests.B, RecentResearchTests.C, RecentResearchTests.D } ) RecentResearchTests.Complete( park.Research, id );
		CollectionAssert.AreEqual( new[] { 0 }, DrawFrames( list, context ) );
	}

	[TestMethod]
	public void AuxiliaryRowsRenderTheEmptyFrameAndTpiHasNoResearchStar()
	{
		var park = RecentResearchTests.Park(); park.Research.RestoreRecentItems( RecentResearchTests.RawRings() );
		var (hud, list, context, _) = Fixture( park, ParkHud.BuyLandRow, ParkHud.ClearLandRow );
		hud.SelectCategory( BuildCategory.Features );
		Assert.IsTrue( DrawFrames( list, context ).All( frame => frame == 0 ) );
		Assert.AreEqual( 6, context.Batch.Draws.Where( draw => draw.Texture.ImagePath == "frame-0" ).Sum( draw => draw.Vertices.Count ), "both auxiliary rows use frame zero" );
		var tpi = RecentResearchTests.Park( EconomyRules.ThemeParkIncMoney ); tpi.Research.RestoreRecentItems( RecentResearchTests.RawRings() );
		var (_, tpiList, tpiContext, _) = Fixture( tpi, Item( RecentResearchTests.A ) );
		CollectionAssert.AreEqual( new[] { 0 }, DrawFrames( tpiList, tpiContext ) );
	}
}
