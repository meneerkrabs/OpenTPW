using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:park-entry-preparation] What a park built over frames prepares for its first draw and HUD before it is entered
/// (docs/BROWSER-PERFORMANCE.md, "The entering frame"): the HUD's interface textures, its input seeds read again on entering, a HUD that
/// fails partway, the sky, and the deletions spread over frames. No original art or GPU is required.
/// </summary>
[TestClass, DoNotParallelize]
public class EnteringFramePreparationTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static void Set( object owner, string field, object? value ) => owner.GetType().GetField( field, Private )!.SetValue( owner, value );
	private static T Get<T>( object owner, string field ) => (T)owner.GetType().GetField( field, Private )!.GetValue( owner )!;

	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	/// <summary>A HUD over a small park, with synthetic models whose one texture is named after the model, and one synthetic font.</summary>
	private static (ParkHud Hud, Level Level, UiContext Context) Create()
	{
		var fixture = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var level = Get<Level>( fixture, "level" );
		var park = (OriginalPark)RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) );
		Set( park, "<Heightfield>k__BackingField", new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100] ) );
		Set( level, "<OriginalPark>k__BackingField", park );
		Set( level, "<PlacedRide>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( PrototypeRide ) ) );
		var economy = EconomyTestData.Park();
		economy.OpenPark();
		var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => economy ), new EmptyCatalog(), new HudHost() );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name =>
		{
			var data = Md2ModelFileTests.CreateGeometry();
			Array.Clear( data, 0x1a4, 12 );
			Encoding.ASCII.GetBytes( name + "\0" ).CopyTo( data, 0x1a4 );
			return new UiModel( new ModelFile( new MemoryStream( data ) ), name );
		} ) ) { Canvas = new UiCanvas( 2048, 1536 ), PopupHelp = false, ResolveTexture = name => $"/synthetic/{name}.png" };
		return (hud, level, context);
	}

	private static IEnumerable<UiTexture> DrawnTextures( UiContext context ) =>
		context.Batch.Draws.Select( draw => draw.Texture ).Where( texture => texture.ImagePath != null || texture.Atlas != null ).Distinct();

	[TestMethod]
	public void EveryImageAndFontOfTheHudsFirstDrawIsPreparedBeforeEntering()
	{
		var (hud, _, context) = Create();
		var prepared = hud.EntryTextures( context ).ToList();
		Assert.AreEqual( prepared.Count, prepared.Distinct().Count(), "each texture is named once" );
		Assert.IsTrue( prepared.All( texture => texture.ImagePath != null || texture.Atlas != null ), "only images and font atlases are prepared" );
		Assert.IsTrue( prepared.Any( texture => texture.Atlas != null ), "the labels' fonts are prepared" );
		Assert.IsTrue( prepared.Contains( UiTexture.Image( ParkHud.HappinessGaugeFill.ArtPath ) ), "the happiness meter is prepared" );
		// the first draw, as the entering frame makes it
		context.Batch.Clear();
		hud.Draw( context );
		var drawn = DrawnTextures( context ).ToList();
		Assert.IsTrue( drawn.Count > 0 );
		var missing = drawn.Except( prepared ).Select( texture => texture.ImagePath ?? "font atlas" ).ToList();
		Assert.AreEqual( 0, missing.Count, $"drawn but not prepared: {string.Join( ", ", missing )}" );
	}

	[TestMethod]
	public void NamingTheEntryTexturesChangesNothingTheHudDraws()
	{
		var (hud, _, context) = Create();
		context.Batch.Clear();
		hud.Draw( context );
		var before = context.Batch.Draws.Select( draw => (draw.Texture, draw.Vertices.Count) ).ToList();
		_ = hud.EntryTextures( context ).ToList();
		context.Batch.Clear();
		hud.Draw( context );
		CollectionAssert.AreEqual( before, context.Batch.Draws.Select( draw => (draw.Texture, draw.Vertices.Count) ).ToList() );
	}

	[TestMethod]
	public void AHudMadeBeforeEnteringReadsTheCamcorderSeedsAgainOnEntering()
	{
		var frameKeys = (HashSet<Veldrid.Key>)typeof( Input ).GetField( "frameKeys", BindingFlags.Static | BindingFlags.NonPublic )!.GetValue( null )!;
		var mouse = Input.Mouse;
		var keys = frameKeys.ToArray();
		try
		{
			// made in a build step while nothing was held
			frameKeys.Clear();
			Input.Mouse = new Input.MouseInfo();
			var (hud, _, _) = Create();
			Assert.IsFalse( Get<bool>( hud, "camcorderKeyDown" ) );
			Assert.IsFalse( Get<bool>( hud, "camcorderMouseDown" ) );
			// the entering frame holds the camcorder key and the mouse button: entering reads them, as a HUD made in that frame does
			frameKeys.Add( Input.ChordKey( InputButton.CamcorderMode ) );
			Input.Mouse = new Input.MouseInfo { Left = true };
			hud.RefreshEntryInput();
			var made = Create().Hud;
			Assert.IsTrue( Get<bool>( made, "camcorderKeyDown" ) );
			Assert.IsTrue( Get<bool>( made, "camcorderMouseDown" ) );
			Assert.IsTrue( Get<bool>( hud, "camcorderKeyDown" ), "a key held through entering is no new press" );
			Assert.IsTrue( Get<bool>( hud, "camcorderMouseDown" ) );
			// and released again before entering
			frameKeys.Clear();
			Input.Mouse = new Input.MouseInfo();
			hud.RefreshEntryInput();
			Assert.IsFalse( Get<bool>( hud, "camcorderKeyDown" ) );
			Assert.IsFalse( Get<bool>( hud, "camcorderMouseDown" ) );
		}
		finally
		{
			frameKeys.Clear();
			frameKeys.UnionWith( keys );
			Input.Mouse = mouse;
		}
	}

	[TestMethod]
	public void APreparedSkyIsLoadedOnceAndTheFirstDrawDoesNotLoadItAgain()
	{
		var (_, level, _) = Create();
		Assert.IsFalse( level.SkyLoaded );
		// without a renderer the level has no sky to load (Sky.TryCreate returns null), as on its first draw
		level.PrepareSky();
		Assert.IsTrue( level.SkyLoaded );
		Assert.IsNull( Get<Sky?>( level, "sky" ) );
		// a sky already set stays: neither a second preparation nor the first draw loads another
		var marker = (Sky)RuntimeHelpers.GetUninitializedObject( typeof( Sky ) );
		Set( level, "sky", marker );
		level.PrepareSky();
		Assert.AreSame( marker, Get<Sky?>( level, "sky" ) );
		Set( level, "sky", null );
		level.DrawSky();
		Assert.IsNull( Get<Sky?>( level, "sky" ), "the first draw after the preparation loads nothing" );
	}

	[TestMethod]
	public void AHudThatFailsPartwayLeavesNoHandlerOnTheLevel()
	{
		var (_, level, _) = Create();
		var placed = typeof( ParkObjects ).GetField( "ObjectPlaced", BindingFlags.Instance | BindingFlags.NonPublic )!;
		var ended = typeof( Level ).GetField( "BuildToolsEnded", BindingFlags.Instance | BindingFlags.NonPublic )!;
		int Handlers( FieldInfo field, object owner ) => (field.GetValue( owner ) as Delegate)?.GetInvocationList().Length ?? 0;
		var placedBefore = Handlers( placed, level.Objects );
		var endedBefore = Handlers( ended, level );
		// without a string table the constructor fails in Build, after it has hooked the level
		Assert.ThrowsException<NullReferenceException>( () => new ParkHud( level, null!, new EconomyParkStatus( () => EconomyTestData.Park() ), new EmptyCatalog(), new HudHost() ) );
		Assert.AreEqual( placedBefore, Handlers( placed, level.Objects ) );
		Assert.AreEqual( endedBefore, Handlers( ended, level ) );
	}

	[TestMethod]
	public void DeletionsRunInOrderWithinTheirBudgetAndTheRestWaitsForTheNextFrame()
	{
		var ran = new List<int>();
		var queue = Enumerable.Range( 0, 5 ).Select( index => (Action)(() => ran.Add( index )) ).ToList();
		var elapsed = 0.0;
		Action Timed( Action action ) => () => { action(); elapsed += 0.4; };
		queue = queue.Select( Timed ).ToList();
		// 1 ms: three deletions start before the budget has passed
		Assert.AreEqual( 3, Renderer.RunDeletions( queue, () => elapsed, 1 ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, ran );
		Assert.AreEqual( 2, queue.Count );
		// a frame whose budget is already spent still runs one, so the queue always drains
		Assert.AreEqual( 1, Renderer.RunDeletions( queue, () => 5, 1 ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, ran );
		// without a budget (the desktop) every deletion runs at once
		queue.Add( () => ran.Add( 9 ) );
		Assert.AreEqual( 2, Renderer.RunDeletions( queue, () => 5, double.PositiveInfinity ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 9 }, ran );
		Assert.AreEqual( 0, queue.Count );
	}

	[TestMethod]
	public void AThrowingDeletionIsRemovedAndNotRunAgain()
	{
		var runs = 0;
		var queue = new List<Action> { () => { runs++; throw new InvalidOperationException( "Injected deletion failure." ); }, () => runs += 10 };
		Assert.ThrowsException<InvalidOperationException>( () => Renderer.RunDeletions( queue, () => 0, double.PositiveInfinity ) );
		Assert.AreEqual( 1, queue.Count, "the deletion that threw is removed, the next waits" );
		Renderer.RunDeletions( queue, () => 0, double.PositiveInfinity );
		Assert.AreEqual( 11, runs );
		Assert.AreEqual( 0, queue.Count );
	}

	[TestMethod]
	public void ADeletionScheduledByADeletionRunsAfterTheOthers()
	{
		var ran = new List<string>();
		var queue = new List<Action>();
		queue.Add( () => { ran.Add( "a" ); queue.Add( () => ran.Add( "c" ) ); } );
		queue.Add( () => ran.Add( "b" ) );
		Renderer.RunDeletions( queue, () => 0, double.PositiveInfinity );
		CollectionAssert.AreEqual( new[] { "a", "b", "c" }, ran );
		Assert.AreEqual( 0, queue.Count );
	}
}
