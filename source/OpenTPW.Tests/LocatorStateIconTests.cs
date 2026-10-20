using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>The actual locator state control draws a synthetic four-node chain, including blank native state 9.</summary>
[TestClass, DoNotParallelize]
public class LocatorStateIconTests
{
	private CameraMode previousCamera = null!;
	private Logger previousLog = null!;
	[TestInitialize]
	public void Setup()
	{
		previousCamera = Camera.Mode;
		previousLog = Log;
		Camera.Mode = new CameraMode();
	}
	[TestCleanup]
	public void Cleanup()
	{
		Camera.Mode = previousCamera;
		Log = previousLog;
	}
	private static void Invoke( ParkHud hud, string name ) => typeof( ParkHud ).GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, null );
	private static UiModel FourFrames()
	{
		var builder = new ModelBuilder();
		ModelNodeBuilder? parent = null;
		foreach ( var name in new[] { "staffstat", "busy", "resting", "strike" } )
		{
			var node = builder.AddMesh( name, parent, N.Matrix4x4.Identity );
			var material = node.Mesh!.AddMaterial( name );
			var a = material.AddVertex( N.Vector3.Zero, -N.Vector3.UnitZ, N.Vector2.Zero );
			var b = material.AddVertex( new N.Vector3( 10, 0, 0 ), -N.Vector3.UnitZ, N.Vector2.UnitX );
			var c = material.AddVertex( new N.Vector3( 0, -10, 0 ), -N.Vector3.UnitZ, N.Vector2.UnitY );
			material.AddTriangle( a, b, c );
			parent = node;
		}
		return new UiModel( builder.Build(), "staffstat" );
	}

	[TestMethod]
	public void ActualControlDrawsValidFramesThenBlankThenRestoresWithoutChangingGenericFallback()
	{
		var economy = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( economy, StaffType.Handyman );
		var guard = EconomyTestData.HireBest( economy, StaffType.Guard );
		var hud = StaffWindowTests.Hud( economy );
		Invoke( hud, "BuildLocatorPanels" );
		var icon = hud.Screen.Find( "locatorState" )!;
		Assert.IsFalse( icon.Visible );
		Assert.AreEqual( UiAnchor.BottomLeft, icon.Anchor );
		Assert.AreEqual( OriginalLayoutTables.LocatorStrip.Root.Children.Single( node => node.Id == 0x148 ).Rect, icon.Bounds );
		var model = FourFrames();
		Assert.AreEqual( 4, model.Frames.Count );
		Assert.AreSame( model.Frames[0], model.GetFrame( 9 ), "generic out-of-range fallback stays unchanged" );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, UiModels.FromAssets( new[] { model } ) )
		{
			Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = name => name
		};
		hud.Locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( member.Type ), member.Id );
		foreach ( var (state, texture) in new[]
		{
			(StaffState.Idle, "staffstat"), (StaffState.Working, "busy"), (StaffState.Resting, "resting"), (StaffState.OnStrike, "strike"),
			(StaffState.Working, "busy"), (StaffState.PickedUp, ""), (StaffState.PickedUp, ""), (StaffState.Idle, "staffstat"), (StaffState.Working, "busy")
		} )
		{
			member.State = state; Invoke( hud, "UpdateLocatorPanels" );
			Assert.AreEqual( member.Id, hud.Locator.ActorId, "unpositioned staff retain identity" );
			Assert.IsTrue( icon.Visible && hud.Screen.Find( "locatorName" )!.Visible && hud.Screen.Find( "locatorStrip" )!.Visible );
			Assert.IsFalse( string.IsNullOrWhiteSpace( ((UiLabel)hud.Screen.Find( "locatorName" )!).Text() ), "held staff keep the name text" );
			context.Batch.Clear(); icon.Draw( context, false, false );
			if ( texture.Length == 0 ) Assert.AreEqual( 0, context.Batch.Draws.Count, "no first frame or stale prior mesh" );
			else
			{
				Assert.AreEqual( texture, context.Batch.Draws.Single().Texture.ImagePath );
				var vertices = context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray();
				var expected = new UiModelImage { Model = "staffstat", Frame = () => ParkHud.StaffStateFrame( state ), Bounds = icon.Bounds, Anchor = icon.Anchor };
				context.Batch.Clear(); expected.Draw( context, false, false );
				CollectionAssert.AreEqual( vertices, context.Batch.Draws.SelectMany( draw => draw.Vertices ).ToArray(), "valid-state fitting and anchor match the replaced image" );
			}
		}
		var missing = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
		icon.Draw( missing, false, false );
		Assert.AreEqual( 0, missing.Batch.Draws.Count, "missing state assets draw no replacement rectangle or stale mesh" );
		// Visitor role keeps the existing strip but hides the staff icon; close removes every strip control.
		hud.OpenVisitorLocator(); Invoke( hud, "UpdateLocatorPanels" );
		Assert.IsFalse( icon.Visible ); Assert.IsTrue( hud.Screen.Find( "locatorStrip" )!.Visible );
		hud.CloseLocator(); Invoke( hud, "UpdateLocatorPanels" );
		Assert.IsFalse( icon.Visible || hud.Screen.Find( "locatorStrip" )!.Visible );
		member.State = StaffState.PickedUp;
		hud.Locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( member.Type ), member.Id );
		Invoke( hud, "UpdateLocatorPanels" );
		Assert.IsTrue( icon.Visible ); context.Batch.Clear(); icon.Draw( context, false, false ); Assert.AreEqual( 0, context.Batch.Draws.Count );
		guard.State = StaffState.Resting;
		hud.Locator.Open( LocatorMode.Staff, LocatorModel.RoleOf( guard.Type ), guard.Id );
		Invoke( hud, "UpdateLocatorPanels" );
		Assert.IsTrue( icon.Visible ); context.Batch.Clear(); icon.Draw( context, false, false );
		Assert.AreEqual( "resting", context.Batch.Draws.Single().Texture.ImagePath, "switching staff role restores its own valid state" );
	}
}
