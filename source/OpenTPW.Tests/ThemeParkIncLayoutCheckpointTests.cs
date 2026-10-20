using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Real CPU placement/runtime reconstruction using a valid synthetic dummy-only model: no render parts or GPU resources.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncLayoutCheckpointTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private List<Entity> previousEntities = null!;
	private GameEdition previousEdition;
	private Logger previousLog = null!;
	private ObjectCatalog catalog = null!;
	private ObjectCatalogEntry entry = null!;
	private readonly WorldSeed seed = new(123);
	private static readonly IReadOnlyDictionary<string, string> assets = new Dictionary<string, string> { ["data:/model.MD2"] = new string('a', 64) };

	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousEntities = Entity.All; previousEdition = GameEditions.Current; previousLog = Log;
		Entity.All = new(); GameEditions.Select(GameEdition.ThemeParkInc); Log = new Logger();
		root = Path.Combine(Path.GetTempPath(), "opentpw-tpi-layout-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var model = Md2ModelFileTests.CreateGeometry();
		// A valid single dummy root with no mesh: OriginalObject still constructs its actual runtime and animator.
		BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x42), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x44), 0);
		BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x268), 0);
		BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x78), 0x264);
		foreach (var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a }) BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(offset), 0);
		Directory.CreateDirectory(Path.Combine(root, "synthetic"));
		File.WriteAllBytes(Path.Combine(root, "synthetic/model.MD2"), model);
		File.WriteAllBytes(Path.Combine(root, "synthetic/script.RSE"), new RideVMTests.Asm().I(Opcode.END).Build(Enum.GetNames<RideVariables>()));
		FileSystem = new BaseFileSystem(root);
		var settings = new ObjectSettings(new[] { new ObjectSettingsFile("Info.Id 1100\nInfo.Name Synthetic\nInfo.IsChoosable 1\n", "synthetic.sam") });
		entry = new ObjectCatalogEntry("science", ObjectCategory.Ride, "/synthetic", settings, new ObjectShape(new[] { "*" }),
			"/synthetic/model.MD2", null, "/synthetic/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), FileSystem, null);
		catalog = (ObjectCatalog)Activator.CreateInstance(typeof(ObjectCatalog), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", new[] { entry } }, null)!;
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach (var entity in Entity.All.ToArray()) entity.Delete();
		Entity.All = previousEntities; FileSystem = previousFiles; GameEditions.Select(previousEdition); Log = previousLog;
		Directory.Delete(root, true);
	}

	private ThemeParkIncLayoutCheckpoint Checkpoint()
	{
		var objects = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		var removed = objects.Add(entry, 2, 2, 0);
		objects.Remove(removed);
		var live = objects.Add(entry, 3, 4, 90, open: false);
		live.Runtime.SetVariable(nameof(RideVariables.VAR_RUNNING), 77);
		var cells = new ParkCellMap(16, 16);
		cells.SetType(4, 6, ParkCellType.Path); cells.SetFlags(4, 6, ParkCellFlags.NoModify);
		cells.SetType(4, 5, ParkCellType.Queue); cells.SetQueueLink(4, 5, 64);
		return new ThemeParkIncLayoutCheckpoint
		{
			Format = ThemeParkIncLayoutCheckpoint.FormatName, Version = ThemeParkIncLayoutCheckpoint.CurrentVersion, RestartPolicy = ThemeParkIncLayoutCheckpoint.FreshSession,
			Theme = "science", Seed = seed.Value, SavedUtc = DateTimeOffset.UnixEpoch,
			Layout = ParkLayoutSnapshot.CaptureSnapshot(cells, objects), AssetHashes = assets
		};
	}

	[TestMethod]
	public void LegacyEnvelopeMigratesButCannotSmuggleNewQueueFields()
	{
		var source = Checkpoint() with { Version = 1 };
		var migrated = ThemeParkIncLayoutCheckpoint.Deserialize(ParkResumeJson.Serialize(source));
		Assert.AreEqual(ThemeParkIncLayoutCheckpoint.CurrentVersion, migrated.Version);
		var cells = source.Layout.Cells with { QueuePassSchemaVersion = 1, QueuePassResidues = new() };
		Assert.ThrowsException<InvalidDataException>(() => ThemeParkIncLayoutCheckpoint.Deserialize(ParkResumeJson.Serialize(source with { Layout = source.Layout with { Cells = cells } })));
	}

	[TestMethod]
	public void VersionTwoRestoresResidueAfterActualObjectReconstruction()
	{
		var source = Checkpoint();
		var map = ParkLayoutSnapshot.RestoreCellMap(source.Layout);
		map.ApplyQueuePassMetadata(12, 12, 64, 17);
		map.SetType(12, 13, ParkCellType.Path); map.ApplyQueuePassMetadata(12, 13, 1, 17);
		var mapData = ParkLayoutSnapshot.CaptureSnapshot(map, new ParkObjects(catalog, new SandboxParkGrid(), seed)).Cells;
		source = source with { Layout = source.Layout with { Cells = mapData } };
		var loaded = ThemeParkIncLayoutCheckpoint.Deserialize(ThemeParkIncLayoutCheckpoint.Serialize(source));
		var target = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		var restored = loaded.RestoreInto(target, assets, 16, 16);
		Assert.AreEqual(1, target.Objects.Count);
		Assert.AreEqual((byte)0, restored.RawTypeAt(12, 12));
		Assert.AreEqual((ushort)17, restored.OwnerAt(12, 12));
		Assert.AreEqual((byte)1, restored.QueueLinkAt(12, 13));
		Assert.IsTrue(restored.HasQueuePassResidues);
	}

	[TestMethod]
	public void FileRoundTripReconstructsActualObjectsCellsAndStartsFreshRuntime()
	{
		var checkpoint = Checkpoint();
		var path = Path.Combine(root, "science.tpilayout");
		ThemeParkIncLayoutCheckpoint.Save(path, checkpoint);
		var loaded = ThemeParkIncLayoutCheckpoint.Load(path);
		var target = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		var cells = loaded.RestoreInto(target, assets, 16, 16);
		Assert.AreEqual(ParkCellType.Path, cells.TypeAt(4, 6));
		Assert.AreEqual(ParkCellFlags.NoModify, cells.FlagsAt(4, 6));
		Assert.AreEqual(64, cells.QueueLinkAt(4, 5));
		Assert.AreEqual(checkpoint.Layout.Cells.Version, cells.Version);
		Assert.AreEqual(1, target.Objects.Count);
		var restored = target.Objects[0];
		Assert.AreEqual((3, 4, 90), (restored.Placement.X, restored.Placement.Y, restored.Placement.Rotation));
		Assert.AreEqual(2, restored.Visitors.AttractionId, "demolished id holes stay preserved");
		Assert.AreEqual(checkpoint.Layout.NextAttractionId, target.NextAttractionId);
		Assert.AreEqual(checkpoint.Layout.NextSeed, target.NextPlacementSeed);
		Assert.IsFalse(restored.IsOpen);
		Assert.AreEqual(0, restored.Runtime.GetVariable(RideVariables.VAR_RUNNING), "script execution data restarts rather than pretending continuation");
		Assert.AreEqual(0, restored.PartCount, "valid dummy-only synthetic model requires no GPU render resources");
		Assert.AreEqual(0, new GuestSimulation(new GuestPathGrid(cells), GuestTests.CreateSettings(), seed.GuestStream).Guests.Count);
	}

	[TestMethod]
	public void ChangedAssetsAndMalformedLayoutAreRefusedBeforeAnyPlacement()
	{
		var checkpoint = Checkpoint();
		foreach (var current in new IReadOnlyDictionary<string, string>[] { new Dictionary<string, string>(), new Dictionary<string, string> { ["data:/model.MD2"] = new string('b', 64) } })
		{
			var target = new ParkObjects(catalog, new SandboxParkGrid(), seed);
			Assert.ThrowsException<InvalidDataException>(() => checkpoint.RestoreInto(target, current, 16, 16));
			Assert.AreEqual(0, target.Objects.Count); Assert.AreEqual(1, target.NextAttractionId);
		}
		var corrupt = checkpoint with { Layout = checkpoint.Layout with { Objects = checkpoint.Layout.Objects.Select(item => item with { InfoId = -1 }).ToList() } };
		Assert.ThrowsException<InvalidDataException>(() => corrupt.RestoreInto(new ParkObjects(catalog, new SandboxParkGrid(), seed), assets, 16, 16));
		Assert.ThrowsException<InvalidDataException>(() => checkpoint.Validate(catalog, assets, 15, 16));
	}

	[TestMethod]
	public void FailedStagedObjectRestorationRetainsLiveSceneAndSuccessfulCandidateCanCommit()
	{
		var checkpoint = Checkpoint();
		var liveEntities = Entity.All;
		var liveObjects = liveEntities.ToArray();
		var bad = checkpoint with { Layout = checkpoint.Layout with { Objects = checkpoint.Layout.Objects.Select(item => item with { BaseHeight = 1 }).ToList() } };
		Assert.ThrowsException<InvalidDataException>(() =>
		{
			using var scene = new EntityConstructionScope();
			bad.RestoreInto(new ParkObjects(catalog, new SandboxParkGrid(), seed), assets, 16, 16);
		});
		Assert.AreSame(liveEntities, Entity.All);
		CollectionAssert.AreEqual(liveObjects, Entity.All.ToArray());
		Assert.IsFalse(liveObjects.OfType<OriginalObject>().Any(item => item.IsDeleted));
		using var successful = new EntityConstructionScope();
		var target = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		checkpoint.RestoreInto(target, assets, 16, 16);
		successful.Detach();
		Assert.AreSame(liveEntities, Entity.All);
		foreach (var entity in Entity.All.ToArray()) entity.Delete();
		successful.Activate();
		CollectionAssert.AreEqual(target.Objects.Cast<Entity>().ToArray(), Entity.All.ToArray());
	}

	[TestMethod]
	public void CodecRejectsUnknownDuplicateMissingFieldsVersionsAndNativeSuffixes()
	{
		var checkpoint = Checkpoint();
		var json = Encoding.UTF8.GetString(ThemeParkIncLayoutCheckpoint.Serialize(checkpoint));
		foreach (var invalid in new[] { json.Replace("\"Version\":2", "\"Version\":99"), json.Replace("\"Version\":2", "\"Version\":2,\"Version\":2"),
			json.Replace("\"Version\":2", "\"Unknown\":1,\"Version\":2"), json.Replace("\"Version\":2,", ""), json.Replace("new-session", "resume"), "{" })
			Assert.ThrowsException<InvalidDataException>(() => ThemeParkIncLayoutCheckpoint.Deserialize(Encoding.UTF8.GetBytes(invalid)));
		foreach (var suffix in new[] { ".tpws", ".tpwi", ".gms", ".gmi", ".cos", ".json" })
		{
			var path = Path.Combine(root, "original" + suffix);
			Assert.ThrowsException<InvalidDataException>(() => ThemeParkIncLayoutCheckpoint.Save(path, checkpoint));
			Assert.IsFalse(File.Exists(path));
		}
	}

	[TestMethod]
	public void InvalidOverwriteAndLateMoveFailureRetainPreviousAndForeignFiles()
	{
		var checkpoint = Checkpoint(); var path = Path.Combine(root, "science.tpilayout");
		ThemeParkIncLayoutCheckpoint.Save(path, checkpoint); var original = File.ReadAllBytes(path);
		Assert.ThrowsException<InvalidDataException>(() => ThemeParkIncLayoutCheckpoint.Save(path, checkpoint with { Version = 99 }));
		CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
		var directory = Path.Combine(root, "blocked.tpilayout"); Directory.CreateDirectory(directory);
		var foreign = directory + ".foreign.tmp"; File.WriteAllText(foreign, "foreign");
		// The replacing move onto a directory fails as IOException on Unix and UnauthorizedAccessException on Windows; the HUD reports both.
		Exception? failure = null;
		try { ThemeParkIncLayoutCheckpoint.Save(directory, checkpoint); }
		catch (Exception error) { failure = error; }
		Assert.IsTrue(failure is IOException or UnauthorizedAccessException, $"the move onto a directory fails ({failure?.GetType().Name ?? "no exception"})");
		Assert.AreEqual("foreign", File.ReadAllText(foreign));
		CollectionAssert.AreEqual(new[] { foreign }, Directory.GetFiles(root, "*.tmp"));
	}

	private static void Set(object target, string property, object value) =>
		target.GetType().GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

	[TestMethod]
	public void TrustedManifestOverloadPinsSyntheticBytesWithoutTpiEconomyAndPreservesTpwOverload()
	{
		var trusted = ParkResumeAssetManifest.Capture(catalog, "science", false);
		Assert.AreEqual(ParkResumeAssets.Sha256(Path.Combine(root, "synthetic/model.MD2")), trusted["data:/synthetic/model.MD2"]);
		Assert.AreEqual(ParkResumeAssets.Sha256(Path.Combine(root, "synthetic/script.RSE")), trusted["data:/synthetic/script.RSE"]);
		var checkpoint = Checkpoint() with { AssetHashes = trusted };
		checkpoint.Validate(catalog, trusted, 16, 16);
		File.AppendAllText(Path.Combine(root, "synthetic/script.RSE"), "changed");
		Assert.ThrowsException<InvalidDataException>(() => checkpoint.Validate(catalog, ParkResumeAssetManifest.Capture(catalog, "science", false), 16, 16));
		File.Delete(Path.Combine(root, "synthetic/script.RSE"));
		Assert.ThrowsException<InvalidDataException>(() => checkpoint.Validate(catalog, ParkResumeAssetManifest.Capture(catalog, "science", false), 16, 16));
		CollectionAssert.AreEqual(ParkResumeAssetManifest.Capture(catalog, "jungle", false).ToArray(), ParkResumeAssetManifest.Capture(catalog, EconomyTestData.Settings()).ToArray());
	}

	[TestMethod]
	public void ActualLevelCaptureIsLayoutOnlyAndPinsTrustedAssets()
	{
		var objects = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		objects.Add(entry, 3, 4, 90);
		var cells = new ParkCellMap(16, 16); cells.SetType(4, 6, ParkCellType.Path);
		var guests = new GuestSimulation(new GuestPathGrid(cells), GuestTests.CreateSettings(), seed.GuestStream);
		var level = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
		Set(level, "OriginalPark", RuntimeHelpers.GetUninitializedObject(typeof(OriginalPark)));
		Set(level, "Objects", objects); Set(level, "Guests", guests); Set(level, "LevelName", "science"); Set(level, "Seed", seed);
		var checkpoint = level.CaptureThemeParkIncLayout(DateTimeOffset.UnixEpoch);
		Assert.AreEqual(seed.Value, checkpoint.Seed);
		Assert.AreEqual(1, checkpoint.Layout.Objects.Count);
		Assert.AreEqual(ThemeParkIncLayoutCheckpoint.FreshSession, checkpoint.RestartPolicy);
		Assert.AreEqual(ParkResumeAssets.Sha256(Path.Combine(root, "synthetic/model.MD2")), checkpoint.AssetHashes["data:/synthetic/model.MD2"]);
		var json = Encoding.UTF8.GetString(ThemeParkIncLayoutCheckpoint.Serialize(checkpoint));
		Assert.IsFalse(json.Contains("\"Guests\"") || json.Contains("\"Scripts\"") || json.Contains("\"Economy\"") || json.Contains("\"Clock\""));
	}

	[TestMethod]
	public void PrototypeCaptureAndCoasterRestoreRefuseRatherThanOmitTheirState()
	{
		var checkpoint = Checkpoint();
		var level = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
		Set(level, "OriginalPark", RuntimeHelpers.GetUninitializedObject(typeof(OriginalPark)));
		Set(level, "Guests", new GuestSimulation(new GuestPathGrid(16, 16), GuestTests.CreateSettings(), seed.GuestStream));
		Set(level, "Objects", new ParkObjects(catalog, new SandboxParkGrid(), seed));
		Set(level, "PlacedRide", RuntimeHelpers.GetUninitializedObject(typeof(PrototypeRide)));
		Assert.ThrowsException<NotSupportedException>(() => level.CaptureThemeParkIncLayout(DateTimeOffset.UnixEpoch));
		Set(entry, "Settings", new ObjectSettings(new[] { new ObjectSettingsFile("Bumper.WhichTrackType 3\n", "coaster.sam") }));
		var target = new ParkObjects(catalog, new SandboxParkGrid(), seed);
		Assert.ThrowsException<NotSupportedException>(() => checkpoint.RestoreInto(target, assets, 16, 16));
		Assert.AreEqual(0, target.Objects.Count);
		Assert.IsFalse(GameEditions.Capabilities.ParkSaves || GameEditions.Capabilities.Economy || GameEditions.Capabilities.Sharing);
	}

	[DataTestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void ConfirmedLoadStopsRetiredHudAndFrameButFailedLoadKeepsCurrentWorld(bool successful)
	{
		var previousCamera = Camera.Mode;
		var previousCurrent = Level.Current;
		try
		{
			var live = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
			Set(live, "Objects", new ParkObjects(catalog, new SandboxParkGrid(), seed));
			Set(live, "CellTool", new CellBuildTool());
			Set(live, "LastActionMessage", "");
			var replacement = (Level)RuntimeHelpers.GetUninitializedObject(typeof(Level));
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject(typeof(GameFlow));
			Set(flow, "Level", live);
			var font = new FontAtlas(FontAtlasTests.CreateFont(8, new FontAtlasTests.SyntheticGlyph('A', 2, 2, 0, 0, 3)));
			var context = new UiContext(OriginalUiTests.FakeStrings(), new UiFonts(_ => font), new UiModels(name => throw new FileNotFoundException(name))) { Canvas = new UiCanvas(1024, 768) };
			Set(flow, "Context", context);
			var candidateCamera = new ParkCameraMode();
			var calls = 0;
			var hud = new ThemeParkIncHud(live, context.Strings, new ThemeParkIncHudHost
			{
				LoadLayout = () =>
				{
					calls++;
					if (!successful) throw new InvalidDataException("synthetic staging failure");
					Set(flow, "Level", replacement);
					Camera.Mode = candidateCamera;
				}
			});
			typeof(ThemeParkIncHud).GetMethod("OpenPauseMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hud, null);
			((UiButton)hud.Stack.Top!.Find("loadLayout")!).Clicked!();
			Assert.AreEqual("confirmLayoutLoad", hud.Stack.Top!.Name);
			var text = ((UiLabel)hud.Stack.Top.Find("message")!).Text();
			StringAssert.Contains(text, "NEW experimental session");
			StringAssert.Contains(text, "not a continuation");
			var confirm = (UiButton)hud.Stack.Top.Find("choice0")!;
			var bounds = confirm.ScreenRect(context.Canvas);
			var click = UiInput.Click(new System.Numerics.Vector2(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
			if (successful)
			{
				// Invoke the actual flow frame: without either guard it touches the incomplete retired level and fails.
				typeof(GameFlow).GetMethod("UpdateThemeParkIncSlice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(flow, new object[] { live, hud, click });
				Assert.AreSame(replacement, flow.Level);
				Assert.IsFalse(candidateCamera.UiCapturesKeyboard || candidateCamera.ModalOpen, "retired HUD cannot overwrite new camera ownership");
			}
			else
			{
				Camera.Mode = candidateCamera;
				hud.Update(context, click);
				Assert.AreSame(live, flow.Level);
				Assert.AreEqual("layoutFailed", hud.Stack.Top!.Name);
				Assert.IsTrue(candidateCamera.UiCapturesKeyboard && candidateCamera.ModalOpen, "current HUD still owns failed-load dialog");
			}
			Assert.AreEqual(1, calls);
		}
		finally { Camera.Mode = previousCamera; Level.Current = previousCurrent; }
	}
}
