using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PlacedEventAudioOriginTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private readonly Dictionary<FieldInfo, object?> audioFields = new();
	private AudioMixer mixer = null!;
	private ObjectCatalogEntry entry = null!;
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	private static void Set(object target, string property, object value) => target.GetType().GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
	[TestInitialize]
	public void Setup()
	{
		Log ??= new(); previousEntities = Entity.All; Entity.All = new();
		root = Path.Combine(Path.GetTempPath(), "opentpw-event-origin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x42), 1); BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x44), 0);
		BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x268), 0); BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x78), 0x264);
		foreach (var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a }) BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(offset), 0);
		File.WriteAllBytes(Path.Combine(root, "model.MD2"), model);
		File.WriteAllBytes(Path.Combine(root, "script.RSE"), new RideVMTests.Asm().I(Opcode.ENDSLICE).Build(Enum.GetNames<RideVariables>()));
		File.WriteAllBytes(Path.Combine(root, "child.RSE"), new RideVMTests.Asm().I(Opcode.EVENT, 3, -1, 44).I(Opcode.ENDSLICE).Build(Enum.GetNames<RideVariables>()));
		entry = new ObjectCatalogEntry("jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings(new[] { new ObjectSettingsFile("Info.Id 1100\n", "synthetic.sam") }),
			new ObjectShape(new[] { "*" }), "/model.MD2", null, "/script.RSE", new[] { "/child.RSE" }, Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem(root), null);
		mixer = new AudioMixer(null); var system = new SoundEventSystem(mixer, 7);
		var category = Empty<SoundCategory>(); var catalog = Empty<SoundCatalog>();
		var sample = new SoundSample(1, 65535, 0, 1, 0);
		var sound = new SoundEntry(100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>());
		Set(catalog, "Events", new[] { new SoundEvent(43, 0, 0, 0, new[] { sound }), new SoundEvent(44, 0, 8, 0, new[] { sound }), new SoundEvent(71, 0, 0x6, 0, new[] { sound }), new SoundEvent(76, 0, 0, 0, new[] { sound }) }); Set(category, "Catalog", catalog);
		((Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof(SoundEventSystem).GetField("decoded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system)!)[(category, 1, 1)] = new Mp2Audio(AudioMixer.OutputRate, 1, new short[] { 1000, 1000, 1000, 1000 }, 1, 0);
		AudioField("events", system);
		foreach (var property in new[] { "LevelRides", "LevelAmbient", "Rides", "Kids", "Staff", "Ambient", "Ui" }) AudioField("<" + property + ">k__BackingField", category);
		AudioListener.Update(Vector3.Zero, new Vector3(0, 0, 1));
	}
	private void AudioField(string name, object value) { var field = typeof(GameAudio).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!; audioFields.Add(field, field.GetValue(null)); field.SetValue(null, value); }
	[TestCleanup]
	public void Cleanup() { foreach (var entity in Entity.All.ToArray()) entity.Delete(); Entity.All = previousEntities; mixer.Dispose(); foreach (var pair in audioFields) pair.Key.SetValue(null, pair.Value); audioFields.Clear(); Directory.Delete(root, true); AudioListener.Update(Vector3.Zero, new Vector3(0, 0, 1)); }
	private void Event(OriginalObjectRuntime owner, int type, int id = 43) => EventKey(owner, type, -1, id);
	private void EventKey(OriginalObjectRuntime owner, int type, int key, int id = 43)
	{
		using var stream = new MemoryStream(new RideVMTests.Asm().I(Opcode.EVENT, type, key, id).I(Opcode.ENDSLICE).Build(Array.Empty<string>()));
		var vm = new RideVM(stream, new RideVMOptions { Effects = owner.Script!.Effects }); vm.Advance(FixedStepClock.TickDuration); Assert.AreNotEqual(RideVMState.Faulted, vm.State); Assert.AreEqual(0, vm.UnimplementedEffects.GetValueOrDefault(Opcode.EVENT), "every EVENT type is served (type 10 from the object type's own sound list, ObjectTypeSoundsTests)"); vm.Stop();
	}
	[DataTestMethod]
	[DataRow(3)][DataRow(4)][DataRow(5)][DataRow(6)][DataRow(7)][DataRow(8)][DataRow(9)]
	public void RealPlacedObjectRoutesSoundAtItsFootprintMiddleForANegativeKey(int type)
	{
		var placed = new OriginalObject(entry, new ObjectPlacement(7, 9, 90, new System.Numerics.Vector2(1, -2), 3));
		Event(placed.Runtime, type);
		var voice = mixer.Voices.Single(); Assert.IsNotNull(voice.Position); Assert.AreEqual(new Vector3(80, 15, 85), voice.Position.Value, "[BIN:TPW-EXE:0x556d42] key < 0: the footprint middle, (7 + 7 + 1) * 5 and (9 + 9 + 1) * 5 less the grid origin, at the base height");
		Assert.AreEqual(100f, voice.Range); Assert.IsFalse(voice.Loops); AudioListener.Update(voice.Position.Value, new Vector3(0, 0, 1)); Assert.IsTrue(mixer.Mix(1).Any(value => value != 0));
	}
	[TestMethod]
	public void HeadlessAndInvalidProvidersRemainFlatAndUnsupportedTypesNeverSampleOrigin()
	{
		var runtime = new OriginalObjectRuntime(entry); Event(runtime, 3); Assert.IsNull(mixer.Voices.Last().Position);
		var calls = 0; Vector3? invalid = null; Func<Vector3?> provider = () => { calls++; return invalid; }; runtime.SetEventAnchors(provider, _ => Vector3.Zero);
		foreach (var value in new Vector3?[] { null, new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), new Vector3(float.NegativeInfinity, 0, 0) })
		{ invalid = value; Event(runtime, 3); Assert.IsNull(mixer.Voices.Last().Position); }
		Assert.AreEqual(4, calls);
		var before = mixer.Voices.Count; foreach (var type in new[] { 0, 1, 2, 10 }) Event(runtime, type);
		Assert.AreEqual(before, mixer.Voices.Count); Assert.AreEqual(4, calls);
		runtime.Stop(); Event(runtime, 3); Assert.IsNull(mixer.Voices.Last().Position); Assert.AreEqual(4, calls);
	}
	[TestMethod]
	public void EventAnchorsAreSubmittedAtWholeUnitsTruncatedTowardZero()
	{
		// [BIN:TPW-EXE:0x5573d0] every sound type truncates the anchor's coordinates with __ftol before 0x51bfc0
		var runtime = new OriginalObjectRuntime(entry);
		runtime.SetEventAnchors(() => new Vector3(-10.9f, 15.5f, 0.75f), _ => Vector3.Zero);
		Event(runtime, 3, 44);
		Assert.AreEqual(new Vector3(-10, 15, 0), mixer.Voices.Last().Position);
		runtime.Stop();
	}
	[TestMethod]
	public void ProviderSamplesPerInstructionAndUsesExistingPositionedLoopPolicy()
	{
		var runtime = new OriginalObjectRuntime(entry);
		var origin = new Vector3(-10, 0, 0); Func<Vector3?> provider = () => origin; runtime.SetEventAnchors(provider, _ => Vector3.Zero);
		Event(runtime, 3, 44); var first = mixer.Voices.Last(); origin = new Vector3(10, 0, 0); Event(runtime, 3, 44); var second = mixer.Voices.Last();
		Assert.AreEqual(new Vector3(-10, 0, 0), first.Position); Assert.AreEqual(origin, second.Position); Assert.IsTrue(first.Loops && second.Loops);
		mixer.Mix(12); Assert.IsFalse(first.IsFinished || second.IsFinished);
		AudioListener.Update(new Vector3(1000, 0, 0), new Vector3(0, 0, 1)); Assert.IsTrue(mixer.Mix(2).All(value => value == 0));
		AudioListener.Update(origin, new Vector3(0, 0, 1)); Assert.IsTrue(mixer.Mix(1).Any(value => value != 0));
		first.Stop(); second.Stop(); Assert.IsTrue(mixer.Mix(1).All(value => value == 0)); runtime.Stop();
	}
	[TestMethod]
	public void DeletingOneObjectStopsOnlyItsLoopsAndParkEntitySweepStopsTheOthers()
	{
		var first = new OriginalObject(entry, new ObjectPlacement(1, 2, 0, System.Numerics.Vector2.Zero, 0));
		var second = new OriginalObject(entry, new ObjectPlacement(3, 4, 180, System.Numerics.Vector2.Zero, 1));
		Event(first.Runtime, 3, 44); var firstVoice = mixer.Voices.Last(); Event(second.Runtime, 3, 44); var secondVoice = mixer.Voices.Last();
		first.Delete(); Assert.IsTrue(firstVoice.IsFinished); Assert.IsFalse(secondVoice.IsFinished); first.Runtime.Stop(); Assert.IsFalse(secondVoice.IsFinished);
		// GameFlow.TearDown retires the park through this actual entity deletion path.
		foreach (var entity in Entity.All.ToArray()) entity.Delete(); Assert.IsTrue(secondVoice.IsFinished); Assert.IsTrue(mixer.Mix(1).All(value => value == 0));
	}
	[TestMethod]
	public void CaptureRefusesOnlyLiveOwnedLoopsAndDoesNotMutateRuntimeOrSaveBytes()
	{
		var placed = new OriginalObject(entry, new ObjectPlacement(1, 2, 0, System.Numerics.Vector2.Zero, 0));
		Event(placed.Runtime, 3); Assert.IsNotNull(placed.Runtime.CaptureSnapshot(), "live one-shots remain eligible");
		var headless = new OriginalObjectRuntime(entry); Event(headless, 3, 44); Assert.IsTrue(mixer.Voices.Last().Loops, "a flat one-shot with flag 0x8 loops too"); StringAssert.Contains(Assert.ThrowsException<NotSupportedException>(() => headless.CaptureSnapshot()).Message, "looping EVENT"); mixer.Voices.Last().Stop(); Assert.IsNotNull(headless.CaptureSnapshot());
		Event(placed.Runtime, 3, 44); var loop = mixer.Voices.Last(); var state = placed.Runtime.State;
		var destination = Path.Combine(root, "prior-save.json"); File.WriteAllText(destination, "previous");
		StringAssert.Contains(Assert.ThrowsException<NotSupportedException>(() => { var snapshot = placed.Runtime.CaptureSnapshot(); File.WriteAllText(destination, snapshot.EntryArchivePath); }).Message, "looping EVENT");
		Assert.AreEqual(state, placed.Runtime.State); Assert.AreEqual("previous", File.ReadAllText(destination));
		loop.Stop(); Assert.IsNotNull(placed.Runtime.CaptureSnapshot(), "finished loops do not block capture");
		Event(placed.Runtime, 3, 44); placed.Runtime.Stop(); Assert.IsNotNull(placed.Runtime.CaptureSnapshot(), "stopped owner clears owned loops"); headless.Stop();
		typeof(GameAudio).GetField("events", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
		var silent = new OriginalObject(entry, new ObjectPlacement(1, 2, 0, System.Numerics.Vector2.Zero, 0)); var voices = mixer.Voices.Count;
		Event(silent.Runtime, 3, 44); Assert.AreEqual(voices, mixer.Voices.Count); Assert.IsNotNull(silent.Runtime.CaptureSnapshot(), "no-audio paths remain eligible");
	}
	[DataTestMethod]
	[DataRow(0, 16, 17)][DataRow(90, 16, 17)][DataRow(180, 16, 17)][DataRow(270, 16, 17)]
	public void AOneCellFootprintMiddleIsTheSameForEveryQuarterTurn(int rotation, int x, int y)
	{
		var placed = new OriginalObject(entry, new ObjectPlacement(7, 9, rotation, new System.Numerics.Vector2(1, -2), 3));
		Event(placed.Runtime, 3); Assert.AreEqual(new Vector3(x * 5, 15, y * 5), mixer.Voices.Single().Position);
	}
	[DataTestMethod]
	[DataRow(0, 7, 9, 3, 2)][DataRow(90, 7, 9, 2, 3)][DataRow(180, 7, 9, 3, 2)][DataRow(270, 7, 9, 2, 3)]
	public void AMultiCellFootprintMiddleFollowsTheRotatedBounds(int rotation, int anchorX, int anchorY, int width, int height)
	{
		// a 3 x 2 shape: the footprint middle is the middle of its rotated bounds, (min + max + 1) * 5 per axis ([BIN:TPW-EXE:0x466b70])
		var wide = new ObjectCatalogEntry("jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings(new[] { new ObjectSettingsFile("Info.Id 1100\n", "synthetic.sam") }),
			new ObjectShape(new[] { "***", "***" }), "/model.MD2", null, "/script.RSE", new[] { "/child.RSE" }, Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem(root), null);
		var placed = new OriginalObject(wide, new ObjectPlacement(anchorX, anchorY, rotation, System.Numerics.Vector2.Zero, 0));
		var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds(wide.Shape, anchorX, anchorY, rotation);
		Assert.AreEqual((width, height), (maxX - minX + 1, maxY - minY + 1));
		Event(placed.Runtime, 3);
		Assert.AreEqual(new Vector3((minX + maxX + 1) * 5, 0, (minY + maxY + 1) * 5), mixer.Voices.Single().Position);
	}
	[TestMethod]
	public void ScreamsPlayAtTheRidesFootprintMiddle()
	{
		// [BIN:TPW-EXE:0x551130 / 0x556af0] STARTSCREAM and SINGLESCREAM are positioned at the footprint middle (0x466b70), not flat
		var placed = new OriginalObject(entry, new ObjectPlacement(7, 9, 0, new System.Numerics.Vector2(1, -2), 3));
		var middle = new Vector3(80, 15, 85);
		void Run(Opcode opcode, params object[] operands)
		{
			using var stream = new MemoryStream(new RideVMTests.Asm().I(opcode, operands).I(Opcode.ENDSLICE).Build(Array.Empty<string>()));
			var vm = new RideVM(stream, new RideVMOptions { Effects = placed.Runtime.Script!.Effects }); vm.Advance(FixedStepClock.TickDuration); Assert.AreNotEqual(RideVMState.Faulted, vm.State); vm.Stop();
		}
		Run(Opcode.SINGLESCREAM, 1, 0);
		Assert.AreEqual(middle, mixer.Voices.Single().Position, "band 1, level 0, speed 50: cat_kids 76 at the footprint middle");
		Run(Opcode.STARTSCREAM, 1, 50);
		Assert.AreEqual(1, mixer.Voices.Count, "the held chain's first child waits for the pooled tick (tp.exe 0x6bdb50)");
		AudioListener.Update(middle, new Vector3(0, 0, 1)); // within the scream's range 95: an inaudible chain makes no child
		((SoundEventSystem)typeof(GameAudio).GetField("events", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Pump();
		Assert.AreEqual(2, mixer.Voices.Count, "the held chain's first child starts in the next tick");
		Assert.AreEqual(middle, mixer.Voices.Last().Position, "the held scream is positioned too");
		Run(Opcode.STOPSCREAM);
	}
	[TestMethod]
	public void AKeyNamingNoModelNodePlaysNothingAndAKnownNodePlaysAtTheNode()
	{
		// [BIN:TPW-EXE:0x556d2c] an unknown key logs "RSSE: Invalid Node ID" and the dispatcher submits nothing
		var placed = new OriginalObject(entry, new ObjectPlacement(7, 9, 0, System.Numerics.Vector2.Zero, 0));
		EventKey(placed.Runtime, 3, 99); Assert.AreEqual(0, mixer.Voices.Count, "the test model has no node with key 99");
		EventKey(placed.Runtime, 3, -1); Assert.AreEqual(1, mixer.Voices.Count, "a negative key is the footprint middle");
	}
	[TestMethod]
	public void ObjectRemovalPreservesExistingOneShotLifetime()
	{
		var placed = new OriginalObject(entry, new ObjectPlacement(1, 2, 0, System.Numerics.Vector2.Zero, 0)); Event(placed.Runtime, 3); var voice = mixer.Voices.Single();
		placed.Delete(); Assert.IsFalse(voice.IsFinished); mixer.Mix(4); Assert.IsTrue(voice.IsFinished); Assert.IsTrue(mixer.Mix(1).All(value => value == 0));
	}
	[TestMethod]
	public void ChildEventsUseTheParentObjectsOriginAndLoopOwner()
	{
		var placed = new OriginalObject(entry, new ObjectPlacement(1, 2, 0, System.Numerics.Vector2.Zero, 0));
		placed.Script!.SpawnChild("child.RSE", sound: false); placed.Runtime.Simulate(FixedStepClock.TickDuration);
		Assert.AreSame(placed.Script.Effects, placed.Script.Child!.Effects); var voice = mixer.Voices.Single(); Assert.AreEqual(new Vector3(15, 0, 25), voice.Position, "the footprint middle of the one cell at (1, 2)"); Assert.IsTrue(voice.Loops);
		placed.Delete(); Assert.IsTrue(voice.IsFinished); Assert.AreEqual(RideVMState.Halted, placed.Script.State);
	}
	[DataTestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void PlacedEventAndListenerAtTheSameEnginePointShareSoundSpace(bool imported)
	{
		var previousLevel = Level.Current;
		try
		{
			var level = Empty<Level>();
			ModelHeightfield? field = null;
			if (imported)
			{
				field = new ModelHeightfield(4, 4, 10, 10, 0, 0, Array.Empty<uint>(), new float[25], new uint[16]);
				var park = Empty<OriginalPark>(); Set(park, "Heightfield", field); Set(level, "OriginalPark", park);
			}
			Level.Current = level;
			var origin = imported ? -OriginalParkPlacement.GetOrigin(field!) : new System.Numerics.Vector2(1, -2);
			var placed = new OriginalObject(entry, new ObjectPlacement(7, 9, 90, origin, 3));
			var middle = placed.FootprintMiddle();
			GameAudio.UpdateListener(middle, new System.Numerics.Vector3(0, 1, 0), field);
			Event(placed.Runtime, 3);
			Assert.AreEqual(AudioListener.Position, mixer.Voices.Single().Position,
				"The actual placed emitter must use the listener's sound axes, scale and imported-terrain centering.");
			var sourceSound = AudioListener.Position;
			GameAudio.UpdateListener(middle + new System.Numerics.Vector3(6, 0, 0), new System.Numerics.Vector3(0, 1, 0), field);
			using var reference = new AudioMixer(null);
			reference.Play(new Mp2Audio(AudioMixer.OutputRate, 1, new short[] { 1000, 1000, 1000, 1000 }, 1, 0), AudioChannel.Effects, gain: 1, position: sourceSound, range: 100);
			CollectionAssert.AreEqual(reference.Mix(1), mixer.Mix(1), "Moving six engine units must attenuate and pan identically to thirty original sound units.");
		}
		finally { Level.Current = previousLevel; }
	}

}
