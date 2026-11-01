using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>Production picker queries with synthetic atlases and managed resource-lifetime sentinels; no original media/GPU.</summary>
[TestClass, DoNotParallelize]
public class VisitorCircleSelectionTests
{
	private List<Entity> entities = null!;
	private Level level = null!;
	private CameraMode camera = null!;
	private N.Matrix4x4 view, projection;
	private Logger log = null!;
	private static readonly N.Vector2 Viewport = new(1024, 768);
	private static readonly N.Vector2 CircleOnly = new(512, 405);
	private static readonly N.Vector2 RectangleOnly = new(512, 320);
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	private static FieldInfo Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
	private static void Set(object o, string name, object? value) => Field(o, name).SetValue(o, value);
	private static T Get<T>(object o, string name) => (T)Field(o, name).GetValue(o)!;

	[TestInitialize]
	public void Setup()
	{
		entities = Entity.All; level = Level.Current; camera = Camera.Mode; view = Camera.ViewMatrix; projection = Camera.ProjMatrix; log = Log;
		Entity.All = new(); Level.Current = null!; Log ??= new();
		Camera.Mode = new ParkCameraMode();
		Camera.ViewMatrix = N.Matrix4x4.CreateTranslation(0, 0, -20);
		Camera.ProjMatrix = OriginalProjection.Create(Viewport.X / Viewport.Y);
	}
	[TestCleanup]
	public void Cleanup()
	{
		Entity.All = entities; Level.Current = level; Camera.Mode = camera; Camera.ViewMatrix = view; Camera.ProjMatrix = projection; Log = log;
	}

	// Constructor-free Vulkan objects are managed lifetime sentinels only; never call native operations or Dispose.
	private static T NativeSentinel<T>(string type) => (T)RuntimeHelpers.GetUninitializedObject(typeof(Veldrid.Texture).Assembly.GetType(type, true)!);
	private static GuestSpriteAtlas Atlas()
	{
		var builder = new SpriteSetBuilder("SPR_PIC") { FullDetail = false };
		foreach (var (slot, count) in new[] { (0, 1), (1, 8), (2, 8), (14, 2) })
		{
			var directions = Enumerable.Range(0, 5).Select(direction => (IReadOnlyList<SpriteRgbaFrame>)Enumerable.Range(0, count)
				.Select(step => new SpriteRgbaFrame(30, 43, new byte[30 * 43 * 4], -15, -43)).ToArray()).ToArray();
			builder.AddAnimation(slot, directions);
		}
		var data = builder.Build();
		return new GuestSpriteAtlas("SPR_PIC", new SpriteBankFile(new MemoryStream(data.Files[".TPC"])), new SpriteAnimationFile(new MemoryStream(data.Files[".ESP"])));
	}
	private static Guest Guest(int id = 1, bool costume = false)
	{
		var guest = new Guest(id, 0) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5, HeadingX = 1, HeadingY = 0, InCostume = costume, BodySpriteInCostume = costume };
		guest.Navigation.X = (5 << 16) + 0x8000; guest.Navigation.Y = (5 << 16) + 0x8000;
		guest.BodySprite.CreatePlayer(VisitorSpriteTemplate.Stand, 0); guest.BodySprite.Tick(63);
		return guest;
	}

	private sealed class Rig
	{
		internal readonly Level Level;
		internal readonly GuestSimulation Simulation;
		internal readonly ModelHeightfield Field;
		internal readonly GuestRenderer Renderer;
		internal readonly GuestSpriteAtlas Atlas;
		internal readonly Texture Texture;
		internal readonly Material<ObjectUniformBuffer> Material;
		internal List<Guest> Guests => (List<Guest>)Simulation.Guests;
		internal Rig(Guest guest, bool costume = false, Level? level = null, GuestSimulation? simulation = null)
		{
			Simulation = simulation ?? new GuestSimulation(new GuestPathGrid(32, 32), GuestTests.CreateSettings(), 1) { ArrivalsEnabled = false };
			if (!Guests.Contains(guest)) Guests.Add(guest);
			Level = level ?? Empty<Level>(); OpenTPW.Level.Current = Level;
			Set(Level, "<Guests>k__BackingField", Simulation);
			Field = new ModelHeightfield(10, 10, 10, 10, 0, 0, Array.Empty<uint>(), new float[121], new uint[100]);
			var park = Empty<OriginalPark>(); Set(park, "<Heightfield>k__BackingField", Field); Set(Level, "<OriginalPark>k__BackingField", park);
			Atlas = VisitorCircleSelectionTests.Atlas();
			Renderer = new GuestRenderer(Simulation, Field, Array.Empty<GuestSpriteAtlas>()) { Draws = item => item.InCostume == costume };
			Set(Renderer, "atlases", new[] { Atlas });
			Material = Empty<Material<ObjectUniformBuffer>>();
			var pipeline = NativeSentinel<Veldrid.Pipeline>("Veldrid.Vk.VkPipeline"); Assert.IsFalse(pipeline.IsDisposed);
			typeof(Material).GetProperty("Pipeline")!.SetValue(Material, pipeline);
			Set(Renderer, "material", Material);
			Texture = Empty<Texture>(); Texture.NativeTexture = NativeSentinel<Veldrid.Texture>("Veldrid.Vk.VkTexture"); Assert.IsFalse(Texture.NativeTexture.IsDisposed);
			Get<List<Texture>>(Renderer, "textures").Add(Texture);
			Set(Level, costume ? "<CostumeRenderer>k__BackingField" : "<GuestRenderer>k__BackingField", Renderer);
		}
		internal Guest? Pick(N.Vector2 mouse) => Renderer.PickVisitor(mouse, Viewport, Camera.ViewMatrix, Camera.ProjMatrix);
		internal void SetFrames(Func<GuestAtlasFrame, GuestAtlasFrame> edit)
		{
			for (var i = 0; i < Atlas.Frames.Length; i++) Atlas.Frames[i] = edit(Atlas.Frames[i]);
		}
	}

	[DataTestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void ActualRegisteredKidAndCostumeQueriesUseCircleAndNeverRetryRectangleAfterCircleMiss(bool costume)
	{
		var guest = Guest(costume: costume); var rig = new Rig(guest, costume);
		Assert.IsTrue(VisitorCirclePicker.IsCanonicalView(Camera.ViewMatrix, Camera.ProjMatrix, Viewport));
		Assert.AreSame(guest, rig.Pick(CircleOnly), "outside the old rectangle but inside the literal circle");
		Assert.IsNull(rig.Pick(RectangleOnly), "an eligible circle miss must not retry the old rectangle");
		var selected = rig.Renderer.SelectSprite(guest, N.Vector3.UnitX, -N.Vector3.UnitZ);
		var frame = rig.Atlas.Frames[selected.Frame];
		Assert.IsFalse(GuestWorldPicker.TryHit(N.Vector3.Zero, frame, selected.Mirror, Camera.ViewMatrix, Camera.ProjMatrix, Viewport, CircleOnly, out _));
		Assert.IsTrue(GuestWorldPicker.TryHit(N.Vector3.Zero, frame, selected.Mirror, Camera.ViewMatrix, Camera.ProjMatrix, Viewport, RectangleOnly, out _));
		// Every texture byte is transparent: this route never reads alpha.
		Assert.IsTrue(rig.Atlas.Pixels.All(value => value == 0));
	}

	[DataTestMethod]
	[DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)]
	public void EveryBoundedProgramAndFiniteReturnToStandRetainsEligibilityWithPendingRequests(int program)
	{
		var guest = Guest(); guest.BodySprite.DeletePlayer();
		var player = guest.BodySprite.CreatePlayer((VisitorSpriteTemplate)program, 0); player.Tick(63);
		guest.BodySprite.QueueTemplate(VisitorSpriteTemplate.Run); guest.BodySprite.QueueInterval(117);
		var rig = new Rig(guest); var before = guest.BodySprite.CaptureSnapshot();
		Assert.AreSame(guest, rig.Pick(CircleOnly)); Assert.AreEqual(before, guest.BodySprite.CaptureSnapshot());
		if (program >= 4)
		{
			while (player.CurrentProgram != VisitorSpriteTemplate.Stand) player.Tick(unchecked(player.NextDue + 1));
			Assert.AreSame(guest, rig.Pick(CircleOnly), "a finite program's final output is still valid after its JMP");
			player.Tick(unchecked(player.NextDue + 1)); Assert.AreEqual(new VisitorSpriteFrame(0, 0), player.Output!.Value);
			Assert.AreSame(guest, rig.Pick(CircleOnly));
		}
	}

	[DataTestMethod]
	[DataRow("missing")] [DataRow("firstFrame")] [DataRow("deleted")] [DataRow("reconstructed")] [DataRow("costumeSource")]
	[DataRow("active")] [DataRow("current")] [DataRow("outputSlot")] [DataRow("outputFrame")] [DataRow("pending")]
	[DataRow("textureMissing")] [DataRow("textureDisposed")] [DataRow("pipelineMissing")] [DataRow("atlasBounds")]
	[DataRow("zeroA")] [DataRow("zeroB")] [DataRow("unregistered")] [DataRow("replaced")] [DataRow("otherLevel")]
	[DataRow("otherSimulation")] [DataRow("sandbox")] [DataRow("swimming")] [DataRow("category")]
	[DataRow("typeRange")] [DataRow("negativeDistance")] [DataRow("nonfiniteTime")]
	public void UnsupportedOwnedStatePreservesTheRealLegacyRectanglePath(string reason)
	{
		var guest = Guest(); var rig = new Rig(guest); Assert.AreSame(guest, rig.Pick(CircleOnly));
		switch (reason)
		{
			case "missing": case "deleted": guest.BodySprite.DeletePlayer(); break;
			case "firstFrame": guest.BodySprite.DeletePlayer(); guest.BodySprite.CreatePlayer(VisitorSpriteTemplate.Stand, 0); break;
			case "reconstructed": guest.BodySpriteHistoryReconstructed = true; break;
			case "costumeSource": guest.BodySpriteInCostume = true; break;
			case "active": Set(guest.BodySprite.Player!, "<ActiveTemplate>k__BackingField", (VisitorSpriteTemplate)9); break;
			case "current": Set(guest.BodySprite.Player!, "<CurrentProgram>k__BackingField", (VisitorSpriteTemplate)9); break;
			case "outputSlot": Set(guest.BodySprite.Player!, "<Output>k__BackingField", new VisitorSpriteFrame(99, 0)); break;
			case "outputFrame": Set(guest.BodySprite.Player!, "<Output>k__BackingField", new VisitorSpriteFrame(1, 8)); break;
			case "pending": Set(guest.BodySprite, "<PendingTemplate>k__BackingField", (VisitorSpriteTemplate)9); break;
			case "textureMissing": Get<List<Texture>>(rig.Renderer, "textures").Clear(); break;
			case "textureDisposed": Set(rig.Texture.NativeTexture, "_destroyed", true); break;
			case "pipelineMissing": typeof(Material).GetProperty("Pipeline")!.SetValue(rig.Material, null); break;
			case "atlasBounds": rig.SetFrames(f => f with { X = rig.Atlas.Width }); break;
			case "zeroA": rig.SetFrames(f => f with { PixelsPerUnitX = 0 }); break;
			case "zeroB": rig.SetFrames(f => f with { PixelsPerUnitY = 0 }); break;
			case "unregistered": Entity.All.Remove(rig.Renderer); break;
			case "replaced": Set(rig.Level, "<GuestRenderer>k__BackingField", null); break;
			case "otherLevel": Level.Current = Empty<Level>(); break;
			case "otherSimulation": Set(rig.Level, "<Guests>k__BackingField", null); break;
			case "sandbox": Set(rig.Level, "<OriginalPark>k__BackingField", null); break;
			case "swimming": guest.State = GuestState.Swimming; break;
			case "category": guest.InCostume = true; Set(rig.Level, "<CostumeRenderer>k__BackingField", Empty<GuestRenderer>()); Set(rig.Renderer, "<Draws>k__BackingField", null); break;
			case "typeRange": Set(guest, "<Type>k__BackingField", rig.Simulation.Settings.Types.Count); break;
			case "negativeDistance": guest.DrawDistanceWalked = -1; break;
			case "nonfiniteTime": Set(rig.Simulation, "<TimeSeconds>k__BackingField", double.NaN); break;
		}
		Assert.IsNull(rig.Pick(CircleOnly), reason);
		Assert.AreSame(guest, rig.Pick(RectangleOnly), reason);
	}

	private static GuestSimulation HistorySimulation()
	{
		var grid = new GuestPathGrid(32, 32); grid.SetPath(5, 5, true);
		return new GuestSimulation(grid, GuestTests.CreateSettings(), 1) { ArrivalsEnabled = false };
	}

	[DataTestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void ActualSnapshotRestoreDistinguishesFaithfulAndReconstructedLegacyHistory(bool legacy)
	{
		var original = HistorySimulation(); var saved = original.SpawnInPark(5, 5);
		saved.BodySprite.Tick(63);
		var snapshot = original.CaptureSnapshot();
		if (legacy) snapshot = snapshot with { BodySpriteClockMilliseconds = null, Balloons = null, Guests = snapshot.Guests.Select(g => g with { BodySprite = null }).ToArray() };
		var restored = HistorySimulation(); restored.RestoreSnapshot(ParkResumeJson.Deserialize<GuestSimulationSnapshot>(ParkResumeJson.Serialize(snapshot)));
		var guest = restored.Guests.Single(); guest.DrawX = guest.DrawY = 5;
		Assert.AreEqual(legacy, guest.BodySpriteHistoryReconstructed);
		var player = guest.BodySprite.Player!; player.Tick(unchecked(player.NextDue + 1));
		Assert.IsNotNull(player.Output, "reconstructed history remains unsupported even after a real FRAME");
		var rig = new Rig(guest, simulation: restored);
		Assert.AreEqual(!legacy, ReferenceEquals(guest, rig.Pick(CircleOnly)));
		Assert.AreEqual(legacy, ReferenceEquals(guest, rig.Pick(RectangleOnly)));
	}

	[TestMethod]
	public void ActualSyntheticOriginalImportKeepsFallbackAfterItsFirstReconstructedFrame()
	{
		var simulation = HistorySimulation();
		var bytes = SaveTestPayloads.Guest(new SaveTestPayloads.GuestFields { Position = (1280, 1280), State = 6 });
		var payload = new byte[8 + bytes.Length]; bytes.CopyTo(payload, 8);
		var saved = SaveGuest.Read(payload, new SaveThing(40, SaveThingModel.Guest, 0, 0, 8, bytes.Length));
		simulation.ImportOriginal(new[] { saved }, 0, _ => 0, _ => 0);
		var guest = simulation.Guests.Single(); guest.DrawX = guest.DrawY = 5;
		Assert.IsTrue(guest.BodySpriteHistoryReconstructed);
		var player = guest.BodySprite.Player!; player.Tick(unchecked(player.NextDue + 1)); Assert.IsNotNull(player.Output);
		var rig = new Rig(guest, simulation: simulation);
		Assert.IsNull(rig.Pick(CircleOnly)); Assert.AreSame(guest, rig.Pick(RectangleOnly));
	}

	[DataTestMethod]
	[DataRow("hidden")] [DataRow("deletedRenderer")] [DataRow("noMaterial")] [DataRow("gone")] [DataRow("removed")]
	[DataRow("held")] [DataRow("ride")] [DataRow("outsidePark")] [DataRow("drawFilter")] [DataRow("nanPosition")]
	public void ExistingFiltersStillRejectWithoutKeepingAStaleWinner(string reason)
	{
		var guest = Guest(); var rig = new Rig(guest); Assert.AreSame(guest, rig.Pick(CircleOnly));
		switch (reason)
		{
			case "hidden": rig.Renderer.Visible = false; break;
			case "deletedRenderer": Set(rig.Renderer, "deleted", true); break;
			case "noMaterial": Set(rig.Renderer, "material", null); break;
			case "gone": guest.State = GuestState.Gone; break;
			case "removed": rig.Guests.Clear(); break;
			case "held": guest.State = GuestState.HeldByGuard; break;
			case "ride": guest.State = GuestState.Using; break;
			case "outsidePark": guest.State = GuestState.CrossingRoadHome; break;
			case "drawFilter": Set(rig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(_ => false)); break;
			case "nanPosition": guest.DrawX = float.NaN; break;
		}
		Assert.IsNull(rig.Pick(CircleOnly)); Assert.IsNull(rig.Pick(RectangleOnly));
	}

	[DataTestMethod]
	[DataRow("slot")] [DataRow("feet")] [DataRow("depth")] [DataRow("depthSlots")]
	[DataRow("rotated")] [DataRow("anchored")] [DataRow("directions")] [DataRow("swimFallback")]
	public void ExtensionHooksStayOnRectangleWithoutNewOrDuplicatedCallbacks(string hook)
	{
		var guest = Guest(); var rig = new Rig(guest); var draws = 0; var chooser = 0;
		Set(rig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(_ => { draws++; return true; }));
		switch (hook)
		{
			case "slot": Set(rig.Renderer, "<SlotChooser>k__BackingField", (Func<Guest, (int, int)?>)(_ => { chooser++; return (0, 0); })); break;
			case "feet": Set(rig.Renderer, "<FeetChooser>k__BackingField", (Func<Guest, N.Vector3>)(_ => { chooser++; return N.Vector3.Zero; })); break;
			case "depth": Set(rig.Renderer, "<DepthPullChooser>k__BackingField", (Func<Guest, float>)(_ => { chooser++; return 0; })); break;
			case "depthSlots": Set(rig.Renderer, "<DepthPulledSlots>k__BackingField", (Func<int, bool>)(_ => { chooser++; return true; })); break;
			case "rotated": Set(rig.Renderer, "<RotatedSlots>k__BackingField", new HashSet<int>()); break;
			case "anchored": Set(rig.Renderer, "<BottomAnchoredSlots>k__BackingField", new HashSet<int>()); break;
			case "directions": Set(rig.Renderer, "<ExactDirections>k__BackingField", true); break;
			case "swimFallback": Set(rig.Renderer, "<SwimSlotFallback>k__BackingField", true); break;
		}
		Assert.IsNull(rig.Pick(CircleOnly)); Assert.AreSame(guest, rig.Pick(RectangleOnly));
		Assert.AreEqual(2, draws); Assert.AreEqual(hook is "slot" or "feet" or "depth" ? 2 : 0, chooser);
	}

	[TestMethod]
	public void RendererFrameAndMirrorRemainIndependentOfPlayerOutputAndTransparentAtlasPixels()
	{
		var guest = Guest(); var rig = new Rig(guest);
		for (var direction = 0; direction < 5; direction++)
		{
			var index = rig.Atlas.GetFrame(0, direction, 0); rig.Atlas.Frames[index] = rig.Atlas.Frames[index] with { Width = 2, Height = 2, OriginX = -1, OriginY = -2 };
		}
		guest.IsMoving = true; Assert.AreEqual(new VisitorSpriteFrame(0, 0), guest.BodySprite.Player!.Output!.Value);
		Assert.AreSame(guest, rig.Pick(CircleOnly), "the walk frame chosen by the current renderer supplies geometry");
		guest.IsMoving = false; Assert.IsNull(rig.Pick(CircleOnly), "the unchanged player output cannot replace the current small stand frame");
	}

	[TestMethod]
	public void RepeatedRealQueriesLeaveSimulationPlayerClockPublicationsSelectionAndRngUntouched()
	{
		var guest = Guest(); var hud = VisitorWorldSelectionTests.Fixture(out var simulation, out _);
		((List<Guest>)simulation.Guests).Clear(); var rig = new Rig(guest, level: Get<Level>(hud, "level"), simulation: simulation);
		Assert.AreEqual(12, rig.Renderer.PrepareWorld()); Assert.IsTrue(hud.OpenVisitorInfoWindow(guest)); var selectedVisitor = hud.InfoVisitor;
		guest.BodySprite.QueueTemplate(VisitorSpriteTemplate.Indecision); guest.BodySprite.QueueInterval(117);
		var draws = 0; var figures = 0;
		Set(rig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(_ => { draws++; return true; }));
		rig.Renderer.Figures = () => { figures++; return new[] { new SpriteFigure(5, 5, 1, 0, 0, false, 0) }; };
		var snapshot = JsonSerializer.Serialize(rig.Simulation.CaptureSnapshot()); var hash = rig.Simulation.ComputeStateHash(); var random = rig.Simulation.RandomState;
		var body = guest.BodySprite.CaptureSnapshot(); var clock = (Time.Now, Time.Delta, rig.Simulation.TimeSeconds);
		var counters = (rig.Renderer.DrawnGuests, rig.Renderer.DrawnFigures, rig.Level.RenderPass);
		var poseCount = Get<Dictionary<int, BounceRiderPose>>(rig.Renderer, "bounceRiders").Count;
		for (var i = 0; i < 100; i++) { Assert.AreSame(guest, rig.Pick(CircleOnly)); Assert.IsNull(rig.Pick(RectangleOnly)); }
		Assert.AreEqual(200, draws); Assert.AreEqual(0, figures, "picking cannot collect presentation-only figures");
		Assert.AreEqual(body, guest.BodySprite.CaptureSnapshot()); Assert.AreEqual(snapshot, JsonSerializer.Serialize(rig.Simulation.CaptureSnapshot()));
		Assert.AreEqual(hash, rig.Simulation.ComputeStateHash()); Assert.AreEqual(random, rig.Simulation.RandomState); Assert.AreEqual(clock, (Time.Now, Time.Delta, rig.Simulation.TimeSeconds));
		Assert.AreEqual(counters, (rig.Renderer.DrawnGuests, rig.Renderer.DrawnFigures, rig.Level.RenderPass));
		Assert.AreSame(selectedVisitor, hud.InfoVisitor); Assert.AreEqual(poseCount, Get<Dictionary<int, BounceRiderPose>>(rig.Renderer, "bounceRiders").Count);
	}

	[TestMethod]
	public void CurrentProjectedDepthThenIdAndPostWinnerNeighbourhoodStayInForce()
	{
		var far = Guest(1); var near = Guest(9); var rig = new Rig(far); rig.Guests.Add(near);
		// Terrain slope makes the second sprite nearer while both circles include the pointer.
		Set(rig.Renderer, "field", new ModelHeightfield(10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Range(0, 121).Select(i => (i % 11) * 2f).ToArray(), new uint[100]));
		near.DrawX = 5.1f;
		Assert.AreSame(near, rig.Pick(CircleOnly));
		near.DrawX = 5; Assert.AreSame(far, rig.Pick(CircleOnly), "equal projected depth retains lowest id");
		near.DrawX = 5.1f; near.Navigation.X = (9 << 16) + 0x8000;
		Assert.IsNull(rig.Level.PickVisitor(CircleOnly, Viewport, Camera.ViewMatrix, Camera.ProjMatrix), "out-of-neighbourhood winner is rejected without retrying the farther valid guest");
		rig.Guests.Remove(near); Assert.AreSame(far, rig.Level.PickVisitor(CircleOnly, Viewport, Camera.ViewMatrix, Camera.ProjMatrix));
	}

	[TestMethod]
	public void KidCostumeAndSwimmerKeepExistingDepthUnitsAndEqualDepthPrecedence()
	{
		var kid = Guest(8); var costume = Guest(2, true); var kidRig = new Rig(kid); var costumeRig = new Rig(costume, true, kidRig.Level, kidRig.Simulation);
		Assert.AreSame(costume, kidRig.Level.PickVisitor(CircleOnly, Viewport, Camera.ViewMatrix, Camera.ProjMatrix));
		// An unregistered third renderer is the swimmers' legacy route. Put its rectangle over the circle point.
		var swimmer = Guest(1); swimmer.State = GuestState.Swimming;
		var swimRig = new Rig(swimmer, level: kidRig.Level, simulation: kidRig.Simulation);
		Set(kidRig.Level, "<GuestRenderer>k__BackingField", kidRig.Renderer);
		Set(kidRig.Level, "<SwimmerRenderer>k__BackingField", swimRig.Renderer);
		Set(swimRig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(item => ReferenceEquals(item, swimmer)));
		Set(kidRig.Renderer, "<Draws>k__BackingField", (Func<Guest, bool>)(item => ReferenceEquals(item, kid)));
		swimRig.SetFrames(f => f with { OriginY = -20 });
		Assert.AreSame(swimmer, swimRig.Pick(CircleOnly));
		Assert.AreSame(costume, kidRig.Level.PickVisitor(CircleOnly, Viewport, Camera.ViewMatrix, Camera.ProjMatrix), "equal-depth swimmer does not replace ordinary winner");
		Set(swimRig.Renderer, "<FeetChooser>k__BackingField", (Func<Guest, N.Vector3>)(_ => new N.Vector3(0, 0, 2)));
		Assert.AreSame(swimmer, kidRig.Level.PickVisitor(CircleOnly, Viewport, Camera.ViewMatrix, Camera.ProjMatrix), "strictly nearer swimmer still wins");
	}

	[TestMethod]
	public void ActualHudShiftPressUsesTheCircleAndReleaseKeepsInputOwnership()
	{
		var previousDown = Input.KeysDown; var previousLast = Input.LastKeysDown; var text = Input.TextEntryActive;
		try
		{
			Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false;
			var hud = VisitorWorldSelectionTests.Fixture(out var simulation, out _);
			var guest = Guest(); ((List<Guest>)simulation.Guests).Clear();
			var rig = new Rig(guest, level: Get<Level>(hud, "level"), simulation: simulation);
			var font = new FontAtlas(FontAtlasTests.CreateFont(8, new FontAtlasTests.SyntheticGlyph('A', 2, 2, 0, 0, 3)));
			var context = new UiContext(OriginalUiTests.FakeStrings(), new UiFonts(_ => font), new UiModels(name => throw new FileNotFoundException(name))) { Canvas = new UiCanvas(1024, 768) };
			var body = guest.BodySprite.CaptureSnapshot();
			using (Input.InjectKeyDown(Veldrid.Key.ShiftLeft))
			{
				Input.UpdateButtons();
				hud.Update(context, new UiInput(CircleOnly, true, true, false, false, UiKeys.None), false, false, false);
			}
			Assert.IsTrue(hud.VisitorInfoWindowOpen); Assert.AreSame(guest, hud.InfoVisitor);
			Assert.AreEqual(body, guest.BodySprite.CaptureSnapshot()); Assert.IsFalse(rig.Level.CellTool.IsActive);
			hud.Update(context, UiInput.Click(CircleOnly), false, false, false);
			Assert.AreSame(guest, hud.InfoVisitor); Assert.IsFalse(rig.Level.CellTool.IsActive, "release cannot become an object/path click");
		}
		finally { Input.KeysDown = previousDown; Input.LastKeysDown = previousLast; Input.TextEntryActive = text; Input.UpdateButtons(); }
	}

	[TestMethod]
	public void UnsupportedViewsUseRectangleAndClippedCircleCandidatesCannotBeRevivedByFallback()
	{
		var guest = Guest(); var rig = new Rig(guest);
		Camera.Mode = new CameraMode();
		Assert.IsNull(rig.Pick(CircleOnly)); Assert.AreSame(guest, rig.Pick(RectangleOnly));
		Camera.Mode = new ParkCameraMode();
		foreach (var depth in new[] { -1f, 0f, 0.01f, 20000f, float.NaN, float.PositiveInfinity })
		{
			Camera.ViewMatrix = N.Matrix4x4.CreateTranslation(0, 0, -depth);
			Assert.IsNull(rig.Pick(CircleOnly), $"depth {depth}"); Assert.IsNull(rig.Pick(RectangleOnly), $"depth {depth}");
		}
	}
}
