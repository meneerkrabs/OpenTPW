using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class FreeWalkCameraTests
{
	private static void Set(object item, string field, object value) => item.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, value);
	private static ModelHeightfield Field() => new(10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Range(0,121).Select(i => (float)(i%11)).ToArray(), new uint[100]);
	private static CameraControlInput Frame(float forward = 0, float right = 0) => new() { HasFocus=true, LogicalSize=new Point2(1000,1000), MousePosition=new Vector2(500,500), DeltaSeconds=.1f, Forward=forward, Right=right };

	[DataTestMethod]
	[DataRow(90f,1f,0f)] [DataRow(0f,0f,1f)] [DataRow(-90f,-1f,0f)] [DataRow(180f,0f,-1f)]
	public void TheWalkStartsLookingWhereTheCameraLookedAndStandsFiveUnitsAboveTheGround(float yawDegrees,float x,float y)
	{
		var field=Field(); Assert.IsTrue(FreeWalkCameraMode.TryGround(field,0,0,out var ground));
		var camera=new FreeWalkCameraMode(field,ground,new Vector3(x,y,0));
		// [BIN:TPW-EXE:0x42AE70 walk yaw] the original's yaw runs from +z (the engine's +Y) toward +x: forward on the ground is (sin yaw, cos yaw)
		Assert.AreEqual(yawDegrees.DegreesToRadians(),camera.Yaw,.001f);
		Assert.AreEqual(x,camera.Rotation.Forward.X,.001f); Assert.AreEqual(y,camera.Rotation.Forward.Y,.001f); Assert.AreEqual(0f,camera.Rotation.Forward.Z,.001f);
		// the eye is 5 original units (one engine unit) above the ground
		Assert.AreEqual(ground.Z+CamcorderWalk.EyeAboveGround*OriginalParkPlacement.ModelScale,camera.Position.Z,.001f);
	}

	[TestMethod]
	public void TheKeysTheMouseButtonAndThePointerMoveAndTurnTheWalkerAsTheOriginalDoes()
	{
		var field=Field(); FreeWalkCameraMode.TryGround(field,0,0,out var ground); var yaw0=MathF.Atan2(1,0);
		var camera=new FreeWalkCameraMode(field,ground,Vector3.Forward); var start=camera.Position;
		// the Up key (the zoom-in action) walks forward along the view, a velocity of half 0.02 x sqrt(110) x 0.1 per millisecond on the first frame (0x42BC2A)
		camera.Update(Frame() with {Zoom=1,DeltaSeconds=.016f});
		var moved=camera.Position-start; Assert.IsTrue(moved.X>0&&Math.Abs(moved.Y)<1e-4f,"forward is the way the walker looks");
		var expected=17f*0.5f*(MathF.Sqrt(110f)*0.02f*0.1f)*OriginalParkPlacement.ModelScale; Assert.AreEqual(expected,moved.X,expected*.01f);
		// the Down key walks back, the right mouse button forward like the Up key
		var back=new FreeWalkCameraMode(field,ground,Vector3.Forward); back.Update(Frame() with {Zoom=-1,DeltaSeconds=.016f}); Assert.IsTrue(back.Position.X<start.X);
		var button=new FreeWalkCameraMode(field,ground,Vector3.Forward); button.Update(Frame() with {RightButton=true,DeltaSeconds=.016f}); Assert.AreEqual(camera.Position,button.Position);
		// the turn keys change the yaw by 0.0012 x the frame time (17 ms here): Left lowers it, Right raises it
		var left=new FreeWalkCameraMode(field,ground,Vector3.Forward); left.Update(Frame() with {RotateLeft=true,RightButton=false,DeltaSeconds=.016f});
		Assert.AreEqual(-17f*0.0012f,left.Yaw-yaw0,1e-6f);
		var right=new FreeWalkCameraMode(field,ground,Vector3.Forward); right.Update(Frame() with {RotateRight=true,RightButton=false,DeltaSeconds=.016f});
		Assert.IsTrue(right.Yaw>right.Yaw-1&&right.Yaw-yaw0>0);
		// the pointer beyond 0.4 of the half width turns by twice the excess, below 0.2 under the centre it pitches down: pointer x 900 of 1000 is 0.8, y 700 is -0.4 (the limit)
		var steered=new FreeWalkCameraMode(field,ground,Vector3.Forward); steered.Update(Frame() with {MousePosition=new Vector2(900,800),DeltaSeconds=.016f});
		Assert.IsTrue(steered.Yaw-yaw0>0,"a pointer right of the dead zone turns right"); Assert.IsTrue(steered.Pitch>0,"below the dead zone it looks down");
		Assert.IsTrue(steered.Rotation.Forward.Z<0);
	}

	[TestMethod]
	public void ExactParkInstanceAndRideReturnChainAreRetained()
	{
		var previous=Camera.Mode; var edition=GameEditions.Current;
		try {
			GameEditions.Select(GameEdition.ThemeParkWorld); var hud=Fixture(out var level);
			var park=new ParkCameraMode(); park.SetPose(new Vector3(2,3,0),23,45); Camera.Mode=park;
			var p=park.Position; var r=park.Rotation; var v=park.Height;
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); var walk=level.FreeWalk; Assert.AreSame(walk,Camera.Mode);
			Assert.IsFalse(level.EnterFreeWalk(Vector3.Zero));
			// Existing ride-exit chain restores the exact camera saved by EnterRideView.
			var ride=(PrototypeRide)RuntimeHelpers.GetUninitializedObject(typeof(PrototypeRide)); Set(level,"<PlacedRide>k__BackingField",ride);
			Assert.IsTrue(level.EnterRideView(ride)); Assert.AreSame(level.RideView,Camera.Mode);
			level.ExitRideView(); Assert.AreSame(walk,Camera.Mode);
			level.ExitFreeWalk(); Assert.AreSame(park,Camera.Mode); Assert.AreEqual(p,park.Position); Assert.AreEqual(r,park.Rotation); Assert.AreEqual(v,park.Height);
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); level.ExitFreeWalk(); Assert.IsNull(level.FreeWalk); Assert.AreSame(park,Camera.Mode);
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); var replacement=new ParkCameraMode(); Camera.Mode=replacement; level.ExitFreeWalk(); Assert.AreSame(replacement,Camera.Mode);
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); Assert.IsTrue(level.EnterRideView(ride)); var newer=new ParkCameraMode(); Camera.Mode=newer; level.ExitFreeWalk(); Assert.AreSame(newer,Camera.Mode,"A retired ride/walk owner cannot overwrite a newer camera.");
			GameEditions.Select(GameEdition.ThemeParkInc); Assert.IsFalse(level.EnterFreeWalk(Vector3.Zero));
		} finally { Camera.Mode=previous; GameEditions.Select(edition); }
	}

	[DataTestMethod]
	[DataRow("plain")] [DataRow("leftCtrl")] [DataRow("rightCtrl")] [DataRow("text")] [DataRow("keyboard")] [DataRow("mouse")] [DataRow("modal")] [DataRow("build")] [DataRow("ride")] [DataRow("readonly")] [DataRow("tpi")]
	public void ActualHudConsumesChordAndBlockedHeldEdges(string gate)
	{
		var oldCamera=Camera.Mode; var oldView=Camera.ViewMatrix; var oldProj=Camera.ProjMatrix; var oldEdition=GameEditions.Current; var oldText=Input.TextEntryActive;
		var oldDown=Input.KeysDown; var oldLast=Input.LastKeysDown; var oldKeyboard=Input.Keyboard;
		try {
			Input.Keyboard=new(); Input.KeysDown=new(); Input.LastKeysDown=new(); Input.TextEntryActive=gate=="text";
			GameEditions.Select(gate=="tpi"?GameEdition.ThemeParkInc:GameEdition.ThemeParkWorld);
			Camera.Mode=new ParkCameraMode(); Camera.ViewMatrix=Camera.ProjMatrix=N.Matrix4x4.Identity;
			var hud=Fixture(out var level); var context=Context();
			if(gate=="modal") hud.Stack.Push(new UiScreen("modal"));
			if(gate=="build") level.IsPlacing=true;
			if(gate=="ride") Set(level,"<RideView>k__BackingField",RuntimeHelpers.GetUninitializedObject(typeof(RideCameraMode)));
			if(gate=="readonly") Set(level,"<Visit>k__BackingField",RuntimeHelpers.GetUninitializedObject(typeof(ParkVisitInfo)));
			var keys=gate=="leftCtrl"?new[]{Key.C,Key.ControlLeft}:gate=="rightCtrl"?new[]{Key.C,Key.ControlRight}:new[]{Key.C};
			Input.Keyboard.KeysDown.AddRange(keys); Input.UpdateButtons();
			hud.Update(context,UiInput.Idle(new N.Vector2(500)),gate=="mouse",gate=="keyboard",false);
			Assert.AreEqual(gate is "plain" or "readonly",hud.CamcorderArmed);
			var messages=(List<(string Text,float Age)>)typeof(ParkHud).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(hud)!;
			Assert.AreEqual(gate is "plain" or "readonly" ? 1 : 0,messages.Count,"Only successful arming shows the prompt.");
			if(messages.Count>0) Assert.AreEqual("Click the ground to walk. C or Escape returns to the park view.",messages[0].Text);
			Input.TextEntryActive=false; level.IsPlacing=false; if(gate=="modal") hud.Stack.Pop(); if(gate=="ride") level.ExitRideView();
			Input.UpdateButtons(); hud.Update(context,UiInput.Idle(new N.Vector2(500)),false,false,false);
			Assert.AreEqual(gate is "plain" or "readonly",hud.CamcorderArmed,"A held C never arms when ownership clears.");
			Assert.AreEqual(gate is "plain" or "readonly" ? 1 : 0,messages.Count,"Holding or clearing a gate cannot repeat or manufacture a prompt.");
			if(gate is "plain" or "readonly") {
				// Ctrl-click cannot complete an armed entry.
				Input.Keyboard.KeysDown.Add(Key.ControlRight); Input.UpdateButtons();
				hud.Update(context,new UiInput(new N.Vector2(500),true,true,false,false,UiKeys.None),false,false,false); Assert.IsNull(level.FreeWalk);
				Input.Keyboard.KeysDown.Remove(Key.ControlRight); Input.UpdateButtons(); hud.Update(context,UiInput.Idle(new N.Vector2(500)),false,false,false);
				// A press begun under mouse capture cannot enter after capture clears during the hold.
				var press=new UiInput(new N.Vector2(500),true,true,false,false,UiKeys.None);
				hud.Update(context,press,true,false,false); hud.Update(context,press,false,false,false); Assert.IsNull(level.FreeWalk);
				hud.Update(context,UiInput.Idle(new N.Vector2(500)),false,false,false);
				hud.Update(context,press,false,false,false); Assert.IsNotNull(level.FreeWalk); Assert.IsFalse(hud.CamcorderArmed);
				Assert.IsFalse(level.CellTool.IsActive); Assert.AreSame(level.FreeWalk,Camera.Mode);
				Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons(); hud.Update(context,UiInput.Idle(new N.Vector2(500)),false,false,false);
				Input.Keyboard.KeysDown.Add(Key.C); Input.UpdateButtons(); hud.Update(context,UiInput.Idle(new N.Vector2(500)),false,false,false);
				Assert.IsNull(level.FreeWalk); Assert.IsInstanceOfType(Camera.Mode,typeof(ParkCameraMode)); Assert.AreEqual(1,messages.Count,"Returning from walking does not announce a new armed entry.");
			}
		} finally { Input.Keyboard=oldKeyboard; Input.TextEntryActive=oldText; Input.UpdateButtons(); Input.KeysDown=oldDown; Input.LastKeysDown=oldLast; Camera.Mode=oldCamera; Camera.ViewMatrix=oldView; Camera.ProjMatrix=oldProj; GameEditions.Select(oldEdition); }
	}

	[TestMethod]
	public void InjectedRightControlCannotArmAndWorldClicksCannotBuildWhileWalking()
	{
		var old=Camera.Mode; var down=Input.KeysDown; var last=Input.LastKeysDown;
		try {
			Input.KeysDown=new(); Input.LastKeysDown=new(); Camera.Mode=new ParkCameraMode(); var hud=Fixture(out var level);
			using(Input.InjectKeyDown(Key.ControlRight,Key.C)) { Input.UpdateButtons(); hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); }
			Assert.IsFalse(hud.CamcorderArmed); Assert.AreEqual(0,((List<(string Text,float Age)>)typeof(ParkHud).GetField("messages",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(hud)!).Count); Input.UpdateButtons();
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); level.IsPlacing=true;
			level.UpdateWorldInput(true,false,new N.Vector2(500),new N.Vector2(1000)); Assert.IsNull(level.PlacedRide);
			level.ExitFreeWalk(); level.UpdateWorldInput(true,false,new N.Vector2(500),new N.Vector2(1000)); Assert.IsNull(level.PlacedRide,"Held click remains consumed on return.");
			// the release of that press, made while walking, is no click of the park's: the HUD never took the press
			level.UpdateWorldInput(false,false,new N.Vector2(500),new N.Vector2(1000)); Assert.IsNull(level.PlacedRide,"Its release after leaving the walk places nothing.");
		} finally { Input.UpdateButtons(); Input.KeysDown=down; Input.LastKeysDown=last; Camera.Mode=old; }
	}

	[TestMethod]
	public void ActualHudRideExitFreezesWalkForTheReturnFrame()
	{
		var previous=Camera.Mode; var down=Input.KeysDown; var last=Input.LastKeysDown;
		try {
			Camera.Mode=new ParkCameraMode(); var hud=Fixture(out var level); Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero)); var walk=level.FreeWalk!;
			var ride=(PrototypeRide)RuntimeHelpers.GetUninitializedObject(typeof(PrototypeRide)); Set(level,"<PlacedRide>k__BackingField",ride); Assert.IsTrue(level.EnterRideView(ride));
			Input.KeysDown=new(){InputButton.RideView}; Input.LastKeysDown=new();
			hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); Assert.IsNull(level.RideView); Assert.AreSame(walk,Camera.Mode); Assert.IsTrue(walk.ModalOpen);
			var pose=walk.Position; walk.Update(Frame() with {Zoom=1,Modal=walk.ModalOpen}); Assert.AreEqual(pose,walk.Position);
			Input.KeysDown.Clear(); hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); Assert.IsFalse(walk.ModalOpen); walk.Update(Frame() with {Zoom=1}); Assert.AreNotEqual(pose,walk.Position);
		} finally { Camera.Mode=previous; Input.KeysDown=down; Input.LastKeysDown=last; }
	}

	[TestMethod]
	public void WhileRidingTheInterfaceDrawsOnlyTheViewfinderAndEject()
	{
		var previous=Camera.Mode; var down=Input.KeysDown; var last=Input.LastKeysDown;
		try {
			Camera.Mode=new ParkCameraMode(); var hud=Fixture(out var level);
			var ride=(PrototypeRide)RuntimeHelpers.GetUninitializedObject(typeof(PrototypeRide)); Set(level,"<PlacedRide>k__BackingField",ride); Assert.IsTrue(level.EnterRideView(ride));
			Input.KeysDown=new(); Input.LastKeysDown=new();
			hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Assert.IsTrue(level.HidesInterface,"the weather banner and the developer panels read this and draw nothing");
			var top=hud.Stack.Top!; Assert.AreEqual("rideView",top.Name); Assert.IsTrue(top.HidesBelow,"the HUD below is not drawn");
			CollectionAssert.AreEquivalent(new[]{"viewfinder","eject"},top.Elements.Where(e=>e.Visible).Select(e=>e.Id).ToArray());
			Assert.IsTrue(hud.RideStaticActive,"entering plays the static");
			level.ExitRideView(); hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Assert.IsFalse(level.HidesInterface); Assert.AreEqual(1,hud.Stack.Screens.Count);
		} finally { Camera.Mode=previous; Input.KeysDown=down; Input.LastKeysDown=last; }
	}

	[TestMethod]
	public void EPressEntersTheRideOnceAndASecondPressLeavesIt()
	{
		var previous=Camera.Mode; var down=Input.KeysDown; var last=Input.LastKeysDown;
		try {
			Camera.Mode=new ParkCameraMode(); var hud=Fixture(out var level);
			var ride=(PrototypeRide)RuntimeHelpers.GetUninitializedObject(typeof(PrototypeRide)); Set(level,"<PlacedRide>k__BackingField",ride); hud.SelectObject(ride);
			Input.KeysDown=new(){InputButton.RideView}; Input.LastKeysDown=new();
			hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Assert.IsNotNull(level.RideView,"the E that boards the ride does not also leave it in the same frame");
			Input.LastKeysDown=new(){InputButton.RideView};
			for(var frame=0;frame<4;frame++) { hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); Assert.IsNotNull(level.RideView); }
			Input.KeysDown=new(); Input.LastKeysDown=new(){InputButton.RideView}; hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Input.KeysDown=new(){InputButton.RideView}; Input.LastKeysDown=new();
			hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Assert.IsNull(level.RideView,"a second press leaves");
		} finally { Camera.Mode=previous; Input.KeysDown=down; Input.LastKeysDown=last; }
	}

	private sealed class EmptyCatalog : IBuildCatalog
	{ public IReadOnlyList<BuildItem> GetItems(BuildCategory category)=>Array.Empty<BuildItem>(); }

	[TestMethod]
	public void RealHudCreationConsumesPriorHoldAndEscapeRestoresPark()
	{
		var oldCamera=Camera.Mode; var oldKeyboard=Input.Keyboard; var oldDown=Input.KeysDown; var oldLast=Input.LastKeysDown;
		try {
			Input.Keyboard=new(); Input.KeysDown=new(); Input.LastKeysDown=new(); Input.Keyboard.KeysDown.Add(Key.C); Input.UpdateButtons();
			Fixture(out var level); Camera.Mode=new ParkCameraMode(); var original=Camera.Mode;
			var hud=new ParkHud(level,OriginalUiTests.FakeStrings(),new NoEconomyStatus(),new EmptyCatalog(),new HudHost());
			hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); Assert.IsFalse(hud.CamcorderArmed);
			Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons(); hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false);
			Input.Keyboard.KeysDown.Add(Key.C); Input.UpdateButtons(); hud.Update(Context(),UiInput.Idle(new N.Vector2(500)),false,false,false); Assert.IsTrue(hud.CamcorderArmed);
			Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons();
			hud.Update(Context(),UiInput.Key(UiKeys.Back),false,false,false); Assert.IsFalse(hud.CamcorderArmed); Assert.AreEqual(1,hud.Stack.Screens.Count);
			Assert.IsTrue(level.EnterFreeWalk(Vector3.Zero));
			hud.Update(Context(),UiInput.Key(UiKeys.Back),false,false,false); Assert.IsNull(level.FreeWalk); Assert.AreSame(original,Camera.Mode); Assert.AreEqual(1,hud.Stack.Screens.Count);
			var returning=(ParkCameraMode)Camera.Mode; var pose=returning.Position; Assert.IsTrue(returning.ModalOpen); returning.Update(Frame(1) with {Modal=returning.ModalOpen},GameOptions.Current); Assert.AreEqual(pose,returning.Position,"Exit frame preserves prior orbit pose.");
		} finally { Input.Keyboard=oldKeyboard; Input.UpdateButtons(); Input.KeysDown=oldDown; Input.LastKeysDown=oldLast; Camera.Mode=oldCamera; }
	}

	[TestMethod]
	public void FrameKeysExpireAndDisposedInjectionNeverAppears()
	{
		var keyboard=Input.Keyboard; var down=Input.KeysDown; var last=Input.LastKeysDown; var text=Input.TextEntryActive;
		try {
			Input.Keyboard=new(); Input.KeysDown=new(); Input.LastKeysDown=new(); Input.TextEntryActive=true;
			Input.InjectKeyDown(Key.C).Dispose(); Input.UpdateButtons(); Assert.IsFalse(Input.IsFrameKeyDown(Key.C));
			using(Input.InjectKeyDown(Key.C,Key.ControlRight)) { Input.UpdateButtons(); Assert.IsTrue(Input.IsFrameKeyDown(Key.C)); Assert.IsTrue(Input.IsFrameKeyDown(Key.ControlRight)); Assert.IsFalse(Input.Down(InputButton.CamcorderMode)); }
			Input.UpdateButtons(); Assert.IsFalse(Input.IsFrameKeyDown(Key.C)); Assert.IsFalse(Input.IsFrameKeyDown(Key.ControlRight));
			Input.Keyboard.KeysDown.Add(Key.C); Input.UpdateButtons(); Assert.IsTrue(Input.IsFrameKeyDown(Key.C));
			Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons(); Assert.IsFalse(Input.IsFrameKeyDown(Key.C));
		} finally { Input.Keyboard=keyboard; Input.UpdateButtons(); Input.KeysDown=down; Input.LastKeysDown=last; Input.TextEntryActive=text; }
	}

	private static ParkHud Fixture(out Level level)
	{
		var hud=VisitorWorldSelectionTests.Fixture(out _,out _); level=(Level)typeof(ParkHud).GetField("level",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hud)!;
		var park=(OriginalPark)RuntimeHelpers.GetUninitializedObject(typeof(OriginalPark)); Set(park,"<Heightfield>k__BackingField",Field()); Set(level,"<OriginalPark>k__BackingField",park);
		return hud;
	}
	private static UiContext Context()
	{
		var font=new FontAtlas(FontAtlasTests.CreateFont(8,new FontAtlasTests.SyntheticGlyph('A',2,2,0,0,3)));
		return new UiContext(OriginalUiTests.FakeStrings(),new UiFonts(_=>font),new UiModels(name=>throw new FileNotFoundException(name))) {Canvas=new UiCanvas(1000,1000)};
	}
}
