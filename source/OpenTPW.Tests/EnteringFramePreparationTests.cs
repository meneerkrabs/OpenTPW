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
/// (docs/BROWSER-PERFORMANCE.md, "The entering frame"): the HUD's interface textures, its input seeds read again on entering and the sky.
/// No original art or GPU is required.
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
		var (hud, _, _) = Create();
		// as if the C key and the mouse button had been held in the frame of the build step that made it
		Set( hud, "camcorderKeyDown", true );
		Set( hud, "camcorderMouseDown", true );
		hud.RefreshEntryInput();
		// nothing is held in this frame (no input in the test process), as a HUD made now reads it
		var made = Create().Hud;
		Assert.AreEqual( Get<bool>( made, "camcorderKeyDown" ), Get<bool>( hud, "camcorderKeyDown" ) );
		Assert.AreEqual( Get<bool>( made, "camcorderMouseDown" ), Get<bool>( hud, "camcorderMouseDown" ) );
	}

	[TestMethod]
	public void ThePreparedSkyIsTheOneTheFirstDrawWouldLoadAndIsLoadedOnce()
	{
		var (_, level, _) = Create();
		Assert.IsFalse( level.SkyLoaded );
		// without a renderer the level has no sky to load (Sky.TryCreate), as on its first draw
		level.PrepareSky();
		Assert.IsTrue( level.SkyLoaded );
		Assert.IsNull( Get<Sky?>( level, "sky" ) );
		level.PrepareSky();
		Assert.IsTrue( level.SkyLoaded );
	}
}
