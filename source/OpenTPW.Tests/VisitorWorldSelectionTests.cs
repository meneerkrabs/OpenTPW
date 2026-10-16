using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class VisitorWorldSelectionTests
{
	private static void Set(object o, string field, object value) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

	[TestMethod]
	public void ParkPressOpensExistingRosterWithClickedVisitorSelected()
	{
		var hud = Fixture(out var simulation, out var renderer);
		var first = new Guest(1, 0) { State = GuestState.WalkingAround };
		var clicked = new Guest(8, 0) { State = GuestState.Queueing };
		Set(simulation, "guests", new List<Guest> { first, clicked });
		var method = typeof(ParkHud).GetMethod("SelectWorldVisitor", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.IsNotNull(method, "There is no world visitor action yet.");
		Assert.IsTrue((bool)method!.Invoke(hud, new object[] { clicked })!);
		Assert.IsTrue(hud.VisitorWindowOpen);
		Assert.AreEqual(8, hud.SelectedVisitorId);
		Assert.AreSame(clicked, hud.VisitorItems[1]);
		clicked.State = GuestState.Gone;
		Assert.IsFalse((bool)method.Invoke(hud, new object[] { clicked })!, "Removed visitor cannot reopen stale details.");
	}

	[TestMethod]
	public void CurrentFrameHotspotMirroringAndViewportProjectionMatchBillboard()
	{
		var frame = new GuestAtlasFrame(0, 0, 10, 20, 5, -20);
		// Identity projection: feet at centre, positive origin lies entirely to the right.
		Assert.IsTrue(GuestWorldPicker.TryHit(new N.Vector3(0, 0, .5f), frame, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
			new N.Vector2(1000), new N.Vector2(650, 300), out var depth));
		Assert.AreEqual(.5f, depth);
		Assert.IsFalse(GuestWorldPicker.TryHit(new N.Vector3(0, 0, .5f), frame, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
			new N.Vector2(1000), new N.Vector2(350, 300), out _));
		Assert.IsTrue(GuestWorldPicker.TryHit(new N.Vector3(0, 0, .5f), frame, true, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
			new N.Vector2(1000), new N.Vector2(350, 300), out _));
		Assert.IsFalse(GuestWorldPicker.TryHit(new N.Vector3(float.NaN, 0, .5f), frame, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
			new N.Vector2(1000), new N.Vector2(650, 300), out _));
		Assert.IsFalse(GuestWorldPicker.TryHit(new N.Vector3(0, 0, 1.1f), frame, false, N.Matrix4x4.Identity, N.Matrix4x4.Identity,
			new N.Vector2(1000), new N.Vector2(650, 300), out _));
		var behind = N.Matrix4x4.Identity; behind.M44 = -1;
		Assert.IsFalse(GuestWorldPicker.TryHit(new N.Vector3(0, 0, .5f), frame, false, N.Matrix4x4.Identity, behind,
			new N.Vector2(1000), new N.Vector2(650, 300), out _));
	}

	[TestMethod]
	public void RendererUsesInterpolatedCoordinatesDepthThenIdAndSkipsInvisibleOrRemovedGuests()
	{
		Fixture(out var simulation, out var renderer);
		var near = new Guest(9, 0) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
		var tied = new Guest(2, 0) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
		Set(simulation, "guests", new List<Guest> { near, tied });
		var view = N.Matrix4x4.Identity;
		var heights = Enumerable.Range(0, 121).Select(i => (i % 11) * .5f).ToArray();
		Set(renderer, "field", new ModelHeightfield(10, 10, 10, 10, 0, 0, Array.Empty<uint>(), heights, new uint[100]));
		tied.DrawX = 5.2f;
		Assert.AreSame(near, renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view), "Nearer guest wins even with a higher id.");
		tied.DrawX = 5;
		Assert.AreSame(tied, renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view));
		tied.State = GuestState.Using;
		Assert.AreSame(near, renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view));
		near.State = GuestState.CrossingRoadHome;
		Assert.IsNull(renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view));
		near.State = GuestState.Queueing; near.DrawX = float.PositiveInfinity;
		Assert.IsNull(renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view));
		near.DrawX = 5;
		var invalid = view; invalid.M11 = float.NaN;
		Assert.IsNull(renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), invalid, view));
		var behind = view; behind.M44 = -1;
		Assert.IsNull(renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, behind));
		renderer.Visible = false;
		Assert.IsNull(renderer.PickVisitor(new N.Vector2(500, 400), new N.Vector2(1000), view, view));
	}

	[DataTestMethod]
	[DataRow("success")]
	[DataRow("building")]
	[DataRow("removing")]
	[DataRow("overlay")]
	[DataRow("mouse")]
	[DataRow("keyboard")]
	[DataRow("text")]
	[DataRow("release")]
	[DataRow("inputText")]
	[DataRow("hud")]
	[DataRow("armed")]
	[DataRow("readonly")]
	[DataRow("miss")]
	[DataRow("ride")]
	[DataRow("rideExit")]
	[DataRow("cell")]
	[DataRow("coaster")]
	[DataRow("locator")]
	[DataRow("plain")]
	[DataRow("shiftLocator")]
	[DataRow("shiftStaffLocator")]
	public void FullUpdatePressRespectsInputOwnersAndDoesNotSelectOnRelease(string gate)
	{
		var previousMode = Camera.Mode;
		var previousView = Camera.ViewMatrix;
		var previousProjection = Camera.ProjMatrix;
		var previousText = Input.TextEntryActive;
		var previousDown = Input.KeysDown; var previousLast = Input.LastKeysDown;
		try
		{
			Camera.Mode = new ParkCameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
			Input.TextEntryActive = gate == "inputText"; Input.KeysDown = new(); Input.LastKeysDown = new();
			var hud = Fixture(out var simulation, out _);
			var guest = new Guest(8, 0) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
			Set(simulation, "guests", new List<Guest> { guest });
			var level = (Level)typeof(ParkHud).GetField("level", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hud)!;
			level.IsPlacing = gate == "building";
			level.IsRemovingObjects = gate == "removing";
			if (gate is "ride" or "rideExit")
			{
				Set(level, "<RideView>k__BackingField", Empty<RideCameraMode>());
				if (gate == "rideExit") Input.KeysDown.Add(InputButton.RideView);
			}
			if (gate == "cell") Set(level.CellTool, "<Mode>k__BackingField", CellToolMode.Path);
			if (gate == "coaster")
			{
				var station = Empty<OriginalObject>();
                Set(station, "<Runtime>k__BackingField", Empty<OriginalObjectRuntime>());
                Set(level, "<CoasterToolStation>k__BackingField", station);
				Set(level, "coasterBuilders", new Dictionary<OriginalObject, CoasterBuilder>());
			}
			if (gate == "armed") level.BuildEntry = Empty<ObjectCatalogEntry>();
			if (gate == "readonly") Set(level, "<Visit>k__BackingField", Empty<ParkVisitInfo>());
			if (gate == "hud") hud.Screen.Add(new UiButton { Bounds = new UiRect(980, 530, 100, 100), Anchor = UiAnchor.Center });
			if (gate == "overlay") hud.Stack.Push(new UiScreen("modal"));
			if (gate is "locator" or "shiftLocator") hud.Locator.Open(LocatorMode.Visitor, LocatorModel.VisitorRole, 0);
			if (gate == "shiftStaffLocator") hud.Locator.Open(LocatorMode.Staff, 1, 0);
			var font = new FontAtlas(FontAtlasTests.CreateFont(8, new FontAtlasTests.SyntheticGlyph('A', 2, 2, 0, 0, 3)));
			var context = new UiContext(OriginalUiTests.FakeStrings(), new UiFonts(_ => font), new UiModels(name => throw new FileNotFoundException(name))) { Canvas = new UiCanvas(1000, 1000) };
			var input = new UiInput(gate == "miss" ? new N.Vector2(950, 950) : new N.Vector2(500, 400), true, gate != "release", gate == "release", false, UiKeys.None);
			// [BIN:TPW-EXE:0x487013 hover mode 7] a visitor is picked with Shift alone and no locator panel, or with the visitor locator panel out and no modifier
			var shift = gate is not ("locator" or "plain");
			using (shift ? Input.InjectKeyDown(Key.ShiftLeft) : null)
			{
				Input.UpdateButtons();
				hud.Update(context, input, gate == "mouse", gate == "keyboard", gate == "text");
			}
			Assert.AreEqual(gate is "success" or "readonly" or "locator", hud.VisitorWindowOpen);
			if (gate is "miss" or "plain") Assert.IsTrue(level.CellTool.IsActive, "A visitor miss, or a plain press on one, preserves automatic path activation.");
			if (gate is "success" or "readonly" or "locator")
			{
				Assert.AreEqual(8, hud.SelectedVisitorId);
				hud.Update(context, UiInput.Click(input.Mouse), false, false, false);
				Assert.AreEqual(8, hud.SelectedVisitorId, "Release keeps visitor selection and cannot fall through into object selection.");
				Assert.IsFalse(level.CellTool.IsActive, "Visitor press wins before automatic path activation.");
				guest.State = GuestState.Gone;
				var replacement = new Guest(3, 0) { State = GuestState.WalkingAround };
				Set(simulation, "guests", new List<Guest> { replacement });
				hud.Update(context, UiInput.Idle(input.Mouse), false, false, false);
				Assert.AreEqual(3, hud.SelectedVisitorId, "Actual visitor screen heals its selection after removal.");
			}
		}
		finally { Camera.Mode = previousMode; Camera.ViewMatrix = previousView; Camera.ProjMatrix = previousProjection; Input.TextEntryActive = previousText; Input.KeysDown = previousDown; Input.LastKeysDown = previousLast; Input.UpdateButtons(); }
	}

	internal static ParkHud Fixture(out GuestSimulation simulation, out GuestRenderer renderer)
	{
		simulation = new GuestSimulation(new GuestPathGrid(32, 32), GuestTests.CreateSettings(), 1) { ArrivalsEnabled = false };
		renderer = Empty<GuestRenderer>();
		var field = new ModelHeightfield(10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Repeat(2.5f, 121).ToArray(), new uint[100]);
		var data = new byte[SpriteAnimationFile.FileBytes];
		"ESP_FILE2.00SPR_XX.TPS"u8.CopyTo(data);
		data[SpriteAnimationFile.SlotOffset + 2] = 1; data[SpriteAnimationFile.SlotOffset + 3] = 5;
		var atlas = Empty<GuestSpriteAtlas>();
		Set(atlas, "<Frames>k__BackingField", new[] { new GuestAtlasFrame(0, 0, 40, 40, -20, -40) });
		Set(atlas, "<Animations>k__BackingField", new SpriteAnimationFile(new MemoryStream(data)));
		Set(renderer, "simulation", simulation);
		Set(renderer, "field", field);
		Set(renderer, "atlases", new[] { atlas });
		Set(renderer, "material", Empty<Material<ObjectUniformBuffer>>());
		renderer.Visible = true;
		var level = Empty<Level>();
		Set(level, "<Guests>k__BackingField", simulation);
		Set(level, "<GuestRenderer>k__BackingField", renderer);
		Set(level, "<CellTool>k__BackingField", new CellBuildTool());
		var hud = Empty<ParkHud>();
		Set(hud, "level", level);
		Set(hud, "<Stack>k__BackingField", new UiScreenStack());
		var root = new UiScreen("hud") { Modal = false };
		Set(hud, "hud", root);
		hud.Stack.Push(root);
		Set(hud, "strings", OriginalUiTests.FakeStrings());
		Set(hud, "visitorScreen", typeof(ParkHud).GetMethod("CreateVisitorScreen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hud, null)!);
		Set(hud, "visitorRows", Array.Empty<Guest>());
		Set(hud, "infoElements", new List<UiElement>());
		Set(hud, "economyElements", new List<UiElement>());
		Set(hud, "messages", new List<(string Text, float Age)>());
		Set(hud, "cashTracker", new CashChangeTracker(0));
		Set(hud, "<Status>k__BackingField", new NoEconomyStatus());
		Set(hud, "lastLevelMessage", "");
		Set(level, "<LastActionMessage>k__BackingField", "");
		Set(level, "<Objects>k__BackingField", new ParkObjects(null!, new SandboxParkGrid()));
		Set(level, "<Paths>k__BackingField", new ParkPathBuilder(simulation.Grid.Cells, simulation.Grid, (ParkEconomy?)null, new SandboxParkGrid()));
		return hud;
	}
}
