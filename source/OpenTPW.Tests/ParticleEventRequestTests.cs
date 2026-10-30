using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ParticleEventRequestTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private ObjectCatalogEntry entry = null!;
	private readonly Dictionary<FieldInfo, object?> savedAudio = new();
	private AudioMixer mixer = null!;
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	private static void Set(object target, string property, object value) => target.GetType().GetField("<" + property + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
	[TestInitialize]
	public void Setup()
	{
		Log ??= new(); previousEntities = Entity.All; Entity.All = new(); root = Directory.CreateTempSubdirectory("opentpw-particle-request-").FullName;
		var original = Md2ModelFileTests.CreateGeometry(); var bytes = new byte[original.Length + 176]; original.CopyTo(bytes, 0);
		var first = original.Length; var second = first + 88; original.AsSpan(0x1c4, 88).CopyTo(bytes.AsSpan(first)); original.AsSpan(0x264, 88).CopyTo(bytes.AsSpan(second));
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(first), 0x200); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(first + 12), (uint)second);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(second + 4), (uint)first);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x74), (uint)first); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x78), (uint)first);
		foreach (var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x44, 0x4a }) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), 0);
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1b0), 0x330);
		File.WriteAllBytes(Path.Combine(root, "model.MD2"), bytes);
		File.WriteAllBytes(Path.Combine(root, "script.RSE"), new RideVMTests.Asm().I(Opcode.ENDSLICE).Build(Enum.GetNames<RideVariables>()));
		File.WriteAllBytes(Path.Combine(root, "child.RSE"), new RideVMTests.Asm().I(Opcode.EVENT, 2, 7, 43).I(Opcode.ENDSLICE).Build(Enum.GetNames<RideVariables>()));
		entry = new ObjectCatalogEntry("jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings(new[] { new ObjectSettingsFile("Info.Id 1100\n", "synthetic.sam") }),
			new ObjectShape(new[] { "*" }), "/model.MD2", null, "/script.RSE", new[] { "/child.RSE" }, Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem(root), null);
		mixer = new AudioMixer(null); var system = new SoundEventSystem(mixer, 7); var category = Empty<SoundCategory>(); var catalog = Empty<SoundCatalog>();
		var sample = new SoundSample(1, 65535, 0, 1, 0); var sound = new SoundEntry(100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>());
		Set(catalog, "Events", new[] { new SoundEvent(43, 0, 0, 0, new[] { sound }), new SoundEvent(44, 0, 8, 0, new[] { sound }) }); Set(category, "Catalog", catalog);
		((Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof(SoundEventSystem).GetField("decoded", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(system)!)[(category, 1, 1)] = new Mp2Audio(AudioMixer.OutputRate, 1, new short[] { 1000, 1000, 1000 }, 1, 0);
		Audio("events", system); foreach (var property in new[] { "LevelRides", "LevelAmbient", "Rides", "Kids", "Staff", "Ambient", "Ui" }) Audio("<" + property + ">k__BackingField", category);
	}
	private void Audio(string name, object value) { var field = typeof(GameAudio).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!; savedAudio.Add(field, field.GetValue(null)); field.SetValue(null, value); }
	[TestCleanup]
	public void Cleanup() { foreach (var entity in Entity.All.ToArray()) entity.Delete(); Entity.All = previousEntities; mixer.Dispose(); foreach (var pair in savedAudio) pair.Key.SetValue(null, pair.Value); savedAudio.Clear(); Directory.Delete(root, true); }
	private OriginalObject Placed(int rotation = 0, RideScriptWorld? world = null) => new(entry, new ObjectPlacement(3, 4, rotation, new N.Vector2(-7, 11), 9), world);
	private void Emit(OriginalObjectRuntime runtime, int type, int key = 7, int effect = 43)
	{
		using var stream = new MemoryStream(new RideVMTests.Asm().I(Opcode.EVENT, type, key, effect).I(Opcode.ENDSLICE).Build(Array.Empty<string>()));
		var vm = new RideVM(stream, new RideVMOptions { Effects = runtime.Script!.Effects, World = runtime.Script.World }); vm.Flags = RideVM.VMFlags.Sign;
		vm.Advance(FixedStepClock.TickDuration); Assert.AreEqual(RideVM.VMFlags.Sign, vm.Flags); Assert.AreEqual(0, vm.UnimplementedEffects.GetValueOrDefault(Opcode.EVENT), "every EVENT type is served (type 10 from the object type's own sound list, ObjectTypeSoundsTests)"); Assert.AreNotEqual(RideVMState.Faulted, vm.State); vm.Stop();
	}
	[DataTestMethod]
	[DataRow(0, -0.8f, 20.6f, 0f, 1f)][DataRow(90, 0.6f, 20.8f, 1f, 0f)][DataRow(180, 0.8f, 19.4f, 0f, -1f)][DataRow(270, -0.6f, 19.2f, -1f, 0f)]
	public void ActualVmPreparesOnlyFrozenManagedPoseAndKeepsTheFlags(int rotation, float x, float y, float dx, float dy)
	{
		var placed = Placed(rotation); Assert.AreEqual(0, placed.PartCount); Emit(placed.Runtime, 1);
		var first = placed.Runtime.LastParticleEventAttempt!.Value; Assert.AreEqual(ParticleEventPreparationStatus.Prepared, first.Status);
		var request = first.Request!.Value; Assert.AreEqual(placed.Visitors.AttractionId, request.OwnerAttractionId); Assert.AreEqual((1, 7, 43, 1000, 0, 1), (request.Type, request.Key, request.EffectId, request.Control, request.AttributeIndex, request.NodeIndex));
		Assert.IsTrue(N.Vector3.Distance(new N.Vector3(x, y, 9.4f), request.EnginePosition) < .0001f); Assert.IsNull(request.EngineUnitDirection);
		Emit(placed.Runtime, 2); Assert.IsTrue(N.Vector3.Distance(new N.Vector3(dx, dy, 0), placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EngineUnitDirection!.Value) < .0001f);
		Assert.AreEqual(0, mixer.Voices.Count); Assert.IsTrue(mixer.Mix(4).All(value => value == 0));
	}
	[TestMethod]
	public void AKeyReadsTheKeyTableAndTheSceneRefreshesItOnlyForADrawnFrame()
	{
		// [BIN:TPW-EXE:0x556b90, 0x44ab30, 0x463030] the key resolver reads the instance's key table, rebuilt at creation and by the render callback of a frame the scene drew the instance in
		var placed = Placed();
		Emit(placed.Runtime, 1); var atCreation = placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition;
		placed.Runtime.Animator.Play(0, new ModelFile(new MemoryStream(Md2AnimationTests.CreateAnimation())).Clip!, "rigid", loop: true);
		placed.Simulate(0.4);
		Assert.AreNotEqual(placed.Runtime.Animator.RestTransforms[1], placed.Runtime.Animator.NodeTransforms[1], "the clip moved the key node");
		Emit(placed.Runtime, 1); Assert.AreEqual(atCreation, placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition, "not drawn since creation: the table still holds the creation pose");
		placed.PresentDrawn();
		Emit(placed.Runtime, 1); var drawn = placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition;
		Assert.AreNotEqual(atCreation, drawn, "the callback rebuilt the table from the pose of the drawn frame");
		placed.Simulate(0.4);
		Emit(placed.Runtime, 1); Assert.AreEqual(drawn, placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition, "the table is as stale as the last drawn frame");
	}
	[DataTestMethod]
	[DataRow(0)][DataRow(3)][DataRow(4)][DataRow(5)][DataRow(6)][DataRow(7)][DataRow(8)][DataRow(9)][DataRow(10)]
	public void OtherEventTypesDoNotMutateParticleDiagnostic(int type)
	{
		var placed = Placed(); Emit(placed.Runtime, 1); var previous = placed.Runtime.LastParticleEventAttempt;
		placed.Runtime.SetEventAnchors(() => AudioListener.Position, _ => AudioListener.Position); Emit(placed.Runtime, type);
		Assert.AreEqual(previous, placed.Runtime.LastParticleEventAttempt); if (type is >= 3 and <= 9) Assert.IsTrue(mixer.Mix(1).Any(value => value != 0), "real sound positive control remains connected");
	}
	[TestMethod]
	public void ASoundEventKeyPlaysAtItsModelNodeAndAnUnknownKeyPlaysNothing()
	{
		// [BIN:TPW-EXE:0x556b90] the key is resolved with selector 0x200 to the node's position; an unknown key logs "RSSE: Invalid Node ID" and nothing is submitted
		var placed = Placed(); Emit(placed.Runtime, 2); var node = placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition;
		Emit(placed.Runtime, 3, 7); var voice = mixer.Voices.Single();
		Assert.IsNotNull(voice.Position);
		// [BIN:TPW-EXE:0x5573d0] the node position is submitted truncated to whole units
		var expected = OriginalObjectRuntime.WholeUnits(GameAudio.SoundPosition(node))!.Value;
		Assert.AreEqual(expected, voice.Position!.Value, $"{voice.Position} vs {expected}");
		Emit(placed.Runtime, 4, 99); Assert.AreEqual(1, mixer.Voices.Count, "an unknown key submits nothing");
		Emit(placed.Runtime, 9, 99); Assert.AreEqual(1, mixer.Voices.Count, "for every sound type 3-9");
	}
	[TestMethod]
	public void RefusalsHaveNoPoseAndReplacePreviousAttempt()
	{
		var placed = Placed(); Emit(placed.Runtime, 1);
		foreach (var key in new[] { -1, 99 }) { Emit(placed.Runtime, 2, key); var refused = placed.Runtime.LastParticleEventAttempt!.Value; Assert.AreEqual(ParticleEventPreparationStatus.UnresolvedKey, refused.Status); Assert.IsNull(refused.Request); }
		Emit(placed.Runtime, 1, 7, 0x8001); Assert.AreEqual(ParticleEventPreparationStatus.UnsupportedScopedEffect, placed.Runtime.LastParticleEventAttempt!.Value.Status); Assert.IsNull(placed.Runtime.LastParticleEventAttempt.Value.Request);
		Emit(placed.Runtime, 1, 7, 105); Assert.AreEqual(ParticleEventPreparationStatus.InvalidEffectId, placed.Runtime.LastParticleEventAttempt!.Value.Status);
		var headless = new OriginalObjectRuntime(entry); Emit(headless, 1); Assert.AreEqual(ParticleEventPreparationStatus.NoPlacement, headless.LastParticleEventAttempt!.Value.Status); Assert.IsNull(headless.LastParticleEventAttempt.Value.Request); headless.Stop();
		var invalid = new OriginalObject(entry, new ObjectPlacement(3, 4, 0, new N.Vector2(float.NaN, 0), 0)); Emit(invalid.Runtime, 2);
		Assert.AreEqual(ParticleKeyStatus.InvalidTransform, invalid.Runtime.LastParticleEventAttempt!.Value.KeyStatus); Assert.IsNull(invalid.Runtime.LastParticleEventAttempt.Value.Request);
	}
	[TestMethod]
	public void PreparedRequestsAreAnnouncedToTheParticleSystemButRefusalsAreNot()
	{
		var received = new List<ParticleEventRequest>(); Action<ParticleEventRequest> handler = received.Add;
		OriginalObjectRuntime.ParticleRequested += handler;
		try
		{
			var placed = Placed(); Emit(placed.Runtime, 1); Emit(placed.Runtime, 2, 99); Emit(placed.Runtime, 2, 7, 105); Emit(placed.Runtime, 1, 7, 0x8001); Emit(placed.Runtime, 2);
			Assert.AreEqual(2, received.Count, "one request per prepared EVENT 1/2; refusals start nothing");
			Assert.AreEqual((1, 43), (received[0].Type, received[0].EffectId)); Assert.IsNull(received[0].EngineUnitDirection);
			Assert.AreEqual(2, received[1].Type); Assert.IsNotNull(received[1].EngineUnitDirection);
			Assert.AreEqual(placed.Runtime.LastParticleEventAttempt!.Value.Request, received[1]);
			Emit(placed.Runtime, 4); Assert.AreEqual(2, received.Count, "other EVENT types are not particle requests");
		}
		finally { OriginalObjectRuntime.ParticleRequested -= handler; }
	}
	[DataTestMethod]
	[DataRow(0)][DataRow(104)]
	public void DirectEffectBoundariesOnlyPrepareMetadata(int effect)
	{
		var placed = Placed(); Emit(placed.Runtime, 2, 7, effect); var request = placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value;
		Assert.AreEqual(effect, request.EffectId); Assert.AreEqual(1000, request.Control); Assert.AreEqual(0, mixer.Voices.Count);
	}
	[TestMethod]
	public void ChildOwnerIsolationCurrentAnimationAndTeardownUseRealRuntime()
	{
		var world = new RideScriptWorld(); var first = Placed(world: world); var second = Placed(90, world); Emit(first.Runtime, 2); var old = first.Runtime.LastParticleEventAttempt!.Value;
		var clip = new ModelFile(new MemoryStream(Md2AnimationTests.CreateAnimation())).Clip!; first.Runtime.Animator.Play(0, clip, "synthetic", false); first.Runtime.Animator.Advance(.6); first.PresentDrawn(); Emit(first.Runtime, 2);
		Assert.AreNotEqual(old.Request!.Value.EnginePosition, first.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition); Assert.IsTrue(N.Vector3.Distance(new N.Vector3(-.8f, 20.6f, 9.4f), old.Request.Value.EnginePosition) < .0001f, "retained value does not follow later animation");
		Assert.IsNull(second.Runtime.LastParticleEventAttempt); second.Script!.SpawnChild("child.RSE", false); second.Runtime.Simulate(FixedStepClock.TickDuration);
		Assert.AreEqual(second.Visitors.AttractionId, second.Runtime.LastParticleEventAttempt!.Value.Request!.Value.OwnerAttractionId); Assert.AreEqual(second.Script.Child!.ScriptId, second.Runtime.LastParticleEventAttempt.Value.ScriptId);
		first.Delete(); Assert.IsNull(first.Runtime.LastParticleEventAttempt); Assert.IsNotNull(second.Runtime.LastParticleEventAttempt); Emit(first.Runtime, 1); Assert.IsNull(first.Runtime.LastParticleEventAttempt);
		foreach (var entity in Entity.All.ToArray()) entity.Delete(); Assert.IsNull(second.Runtime.LastParticleEventAttempt);
	}
	[TestMethod]
	public void VariableOperandsAreCopiedAndDiagnosticsDoNotEnterSavedOrCanonicalEffects()
	{
		var placed = Placed(); var before = System.Text.Json.JsonSerializer.Serialize(placed.Runtime.CaptureSnapshot());
		var initialHash = new StateHasher(); ((IRideCanonicalEffects)placed.Script!.Effects).AddCanonicalState(initialHash);
		using var stream = new MemoryStream(new RideVMTests.Asm()
			.I(Opcode.COPY, RideVMTests.V(0), 2).I(Opcode.COPY, RideVMTests.V(1), 7).I(Opcode.COPY, RideVMTests.V(2), 104)
			.I(Opcode.EVENT, RideVMTests.V(0), RideVMTests.V(1), RideVMTests.V(2)).I(Opcode.ENDSLICE)
			.I(Opcode.COPY, RideVMTests.V(0), 0).I(Opcode.COPY, RideVMTests.V(1), -1).I(Opcode.COPY, RideVMTests.V(2), 0)
			.I(Opcode.ENDSLICE).Build(new[] { "V0", "V1", "V2" }));
		var vm = new RideVM(stream, new RideVMOptions { Effects = placed.Script.Effects, World = placed.Script.World });
		try
		{
			vm.Advance(FixedStepClock.TickDuration); var retained = placed.Runtime.LastParticleEventAttempt!.Value;
			Assert.AreEqual((vm.ScriptId, 2, 7, 104), (retained.ScriptId, retained.Type, retained.Key, retained.EffectId));
			vm.Advance(FixedStepClock.TickDuration); Assert.AreEqual(retained, placed.Runtime.LastParticleEventAttempt);
			Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(placed.Runtime.CaptureSnapshot()));
			var finalHash = new StateHasher(); ((IRideCanonicalEffects)placed.Script.Effects).AddCanonicalState(finalHash); Assert.AreEqual(initialHash.Value, finalHash.Value);
		}
		finally { vm.Stop(); }
	}

	[TestMethod]
	public void SoundChildUsesOwnerAndStopReleasesPlacementResolverAndLatestSlot()
	{
		var world = new RideScriptWorld(); var placed = Placed(world: world); var other = Placed(90, world);
		placed.Script!.SpawnChild("child.RSE", true); placed.Runtime.Simulate(FixedStepClock.TickDuration);
		var retained = placed.Runtime.LastParticleEventAttempt!.Value;
		Assert.AreEqual(placed.Script.SoundChild!.ScriptId, retained.ScriptId); Assert.AreEqual(placed.Visitors.AttractionId, retained.OwnerAttractionId);
		Emit(other.Runtime, 1); var otherAttempt = other.Runtime.LastParticleEventAttempt;
		placed.Runtime.Stop(); placed.Runtime.Stop(); Assert.IsNull(placed.Runtime.LastParticleEventAttempt);
		foreach (var name in new[] { "particlePlacement", "particleKeys" }) Assert.IsNull(typeof(OriginalObjectRuntime).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(placed.Runtime), name);
		Emit(placed.Runtime, 2); Assert.IsNull(placed.Runtime.LastParticleEventAttempt); Assert.AreEqual(otherAttempt, other.Runtime.LastParticleEventAttempt);
		Assert.AreEqual(ParticleEventPreparationStatus.Prepared, retained.Status); Assert.IsNotNull(retained.Request);
		foreach (var effect in new[] { -1, -32768, 105, 32767 }) { Emit(other.Runtime, 2, 7, effect); Assert.IsNull(other.Runtime.LastParticleEventAttempt!.Value.Request); }
	}

	[TestMethod]
	public void ActualRootFaultClearsItsDiagnosticAndLoopButKeepsOtherOwnerAndCapturePolicy()
	{
		var world = new RideScriptWorld(); var first = Placed(world: world); var other = Placed(90, world);
		Emit(first.Runtime, 2); Emit(other.Runtime, 1); var retained = first.Runtime.LastParticleEventAttempt!.Value;
		var otherAttempt = other.Runtime.LastParticleEventAttempt;
		AudioVoice Loop(OriginalObjectRuntime owner)
		{
			var listener = AudioListener.Position; owner.SetEventAnchors(() => listener, _ => listener); Emit(owner, 3, -1, 44);
			return ((List<AudioVoice>)typeof(OriginalObjectEffects).GetField("ownedEventLoops", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner.Script!.Effects)!).Single();
		}
		var ownLoop = Loop(first.Runtime); var otherLoop = Loop(other.Runtime);
		Assert.AreEqual(retained, first.Runtime.LastParticleEventAttempt); Assert.IsTrue(ownLoop.Loops); Assert.IsTrue(otherLoop.Loops);
		Assert.IsTrue(mixer.Mix(4).Any(value => value != 0));
		StringAssert.Contains(Assert.ThrowsException<NotSupportedException>(() => first.Runtime.CaptureSnapshot()).Message, "looping EVENT");
		first.Script!.Abort("integration particle-owner fault"); first.Runtime.Simulate(FixedStepClock.TickDuration);
		Assert.IsNull(first.Runtime.LastParticleEventAttempt); Assert.IsTrue(ownLoop.IsFinished); Assert.IsFalse(otherLoop.IsFinished);
		Assert.AreEqual(otherAttempt, other.Runtime.LastParticleEventAttempt); Assert.IsTrue(mixer.Mix(4).Any(value => value != 0));
		foreach (var name in new[] { "particlePlacement", "particleKeys", "eventOrigin", "footprintMiddle", "eventSoundSpace" }) Assert.IsNull(typeof(OriginalObjectRuntime).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(first.Runtime), name);
		var stopped = first.Runtime.CaptureSnapshot(); Assert.AreEqual("integration particle-owner fault", stopped.StoppedScriptFault);
		StringAssert.Contains(Assert.ThrowsException<NotSupportedException>(() => other.Runtime.CaptureSnapshot()).Message, "looping EVENT");
		first.Runtime.Simulate(FixedStepClock.TickDuration); Emit(first.Runtime, 2); Assert.IsNull(first.Runtime.LastParticleEventAttempt); Assert.IsFalse(otherLoop.IsFinished);
		Assert.AreEqual(ParticleEventPreparationStatus.Prepared, retained.Status); Assert.IsNotNull(retained.Request);
	}

	private static void Run(OriginalObjectRuntime runtime, Func<RideVMTests.Asm, RideVMTests.Asm> script)
	{
		using var stream = new MemoryStream(script(new RideVMTests.Asm()).I(Opcode.ENDSLICE).Build(Array.Empty<string>()));
		var vm = new RideVM(stream, new RideVMOptions { Effects = runtime.Script!.Effects, World = runtime.Script.World });
		vm.Advance(FixedStepClock.TickDuration); Assert.AreNotEqual(RideVMState.Faulted, vm.State); vm.Stop();
	}
	[TestMethod]
	public void AddObjKeepsTheParticleHandleUnderItsSlotAndKillObjEndsEveryRecordOfTheSlot()
	{
		var started = new List<ParticleEventRequest>(); var killed = new List<int>(); var next = 0x10000;
		var previousStarter = OriginalObjectRuntime.ParticleStarter; var previousKiller = OriginalObjectRuntime.ParticleKiller;
		OriginalObjectRuntime.ParticleStarter = request => { started.Add(request); return next++; }; OriginalObjectRuntime.ParticleKiller = killed.Add;
		try
		{
			var placed = Placed();
			// [BIN:TPW-EXE:0x551F5F] ADDOBJ type key effect slot; types 1 and 2 start particle effects through the EVENT dispatcher with control 1000
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 7, 43, 5).I(Opcode.ADDOBJ, 2, 7, 44, 5).I(Opcode.ADDOBJ, 1, 7, 45, 6).I(Opcode.ADDOBJ, 3, 7, 46, 5).I(Opcode.ADDOBJ, 1, 99, 47, 5));
			CollectionAssert.AreEqual(new[] { (1, 43), (2, 44), (1, 45) }, started.Select(r => (r.Type, r.EffectId)).ToArray(), "type 3 and an unresolved key start nothing");
			Assert.IsTrue(started.All(r => r.Control == 1000 && !r.AtWorldOrigin));
			Assert.IsNotNull(started[1].EngineUnitDirection, "type 2 carries the key's direction");
			Assert.IsNull(placed.Runtime.LastParticleEventAttempt, "ADDOBJ does not touch the EVENT diagnostic");
			Assert.AreEqual(ParticleEventPreparationStatus.UnresolvedKey, placed.Runtime.LastParticleObjectAttempt!.Value.Status);
			Run(placed.Runtime, a => a.I(Opcode.KILLOBJ, 5));
			CollectionAssert.AreEquivalent(new[] { 0x10000, 0x10001 }, killed, "both records of slot 5, none of slot 6");
			Run(placed.Runtime, a => a.I(Opcode.KILLOBJ, 5));
			Assert.AreEqual(2, killed.Count, "the records are gone");
			Run(placed.Runtime, a => a.I(Opcode.KILLOBJ, 6));
			CollectionAssert.AreEquivalent(new[] { 0x10000, 0x10001, 0x10002 }, killed);
			// a start that returned no handle keeps no record; the scoped effect is refused before the dispatcher
			OriginalObjectRuntime.ParticleStarter = request => 0;
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 7, 43, 9).I(Opcode.ADDOBJ, 1, 7, 0x8001, 9).I(Opcode.KILLOBJ, 9));
			Assert.AreEqual(3, killed.Count);
			// [BIN:TPW-EXE:0x558703] destroying the object ends the effects its ADDOBJ records hold
			OriginalObjectRuntime.ParticleStarter = request => next++;
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 7, 43, 8));
			placed.Runtime.Stop(); Assert.IsNull(placed.Runtime.LastParticleObjectAttempt);
			Assert.AreEqual(4, killed.Count, "Stop kills the record's effect");
			placed.Runtime.KillParticleObjects(8); Assert.AreEqual(4, killed.Count, "and forgets it");
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleKiller = previousKiller; }
	}
	[TestMethod]
	public void AddObjExtStartsWithItsControlOperandAndIsKeptUnderItsSlot()
	{
		var started = new List<ParticleEventRequest>(); var killed = new List<int>(); var next = 0x10000;
		var previousStarter = OriginalObjectRuntime.ParticleStarter; var previousKiller = OriginalObjectRuntime.ParticleKiller;
		OriginalObjectRuntime.ParticleStarter = request => { started.Add(request); return next++; }; OriginalObjectRuntime.ParticleKiller = killed.Add;
		try
		{
			var placed = Placed();
			// [BIN:TPW-EXE:0x5520E8] ADDOBJ_EXT type key effect control slot: the control operand replaces ADDOBJ's 1000
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ_EXT, 1, 7, 43, 500, 5).I(Opcode.ADDOBJ_EXT, 2, 7, 44, 1000, 6).I(Opcode.ADDOBJ_EXT, 3, 7, 46, 500, 5));
			CollectionAssert.AreEqual(new[] { (1, 43, 500), (2, 44, 1000) }, started.Select(r => (r.Type, r.EffectId, r.Control)).ToArray(), "type 3 is a sound record");
			Run(placed.Runtime, a => a.I(Opcode.KILLOBJ, 5));
			CollectionAssert.AreEqual(new[] { 0x10000 }, killed);
			Assert.AreEqual(2, placed.Runtime.CaptureSnapshot().ParticleObjects!.Single().Type, "the slot-6 record keeps its type for the follow");
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleKiller = previousKiller; }
	}

	[TestMethod]
	public void ANegativeKeyStartsTheEffectAtTheFootprintMiddleAndATypeTwoOneStartsNothing()
	{
		var placed = Placed();
		var received = new List<ParticleEventRequest>(); Action<ParticleEventRequest> handler = received.Add; OriginalObjectRuntime.ParticleRequested += handler;
		try
		{
			// [BIN:TPW-EXE:0x556B90] key < 0: the box middle at the base height (0x466B70)
			Emit(placed.Runtime, 1, key: -1);
			var attempt = placed.Runtime.LastParticleEventAttempt!.Value;
			Assert.AreEqual(ParticleEventPreparationStatus.Prepared, attempt.Status);
			Assert.AreEqual(placed.FootprintMiddle(), attempt.Request!.Value.EnginePosition);
			Assert.IsFalse(attempt.Request.Value.AtWorldOrigin);
			// the direction output stays unset for a negative key, so a directed effect is refused
			Emit(placed.Runtime, 2, key: -1);
			Assert.AreEqual(ParticleEventPreparationStatus.UnresolvedKey, placed.Runtime.LastParticleEventAttempt!.Value.Status);
			Assert.AreEqual(1, received.Count);
		}
		finally { OriginalObjectRuntime.ParticleRequested -= handler; }
	}

	[TestMethod]
	public void EverySimulationPassMovesAKeptEffectToItsKeyAndTurnsADirectedOne()
	{
		var followed = new List<(int Handle, ParticleFollow Follow)>(); var next = 0x10000;
		var previousStarter = OriginalObjectRuntime.ParticleStarter; var previousFollower = OriginalObjectRuntime.ParticleFollower; var previousAlive = OriginalObjectRuntime.ParticleAlive;
		OriginalObjectRuntime.ParticleStarter = request => next++; OriginalObjectRuntime.ParticleFollower = (handle, follow) => followed.Add((handle, follow)); OriginalObjectRuntime.ParticleAlive = _ => true;
		try
		{
			var placed = Placed();
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 7, 43, 5).I(Opcode.ADDOBJ, 2, 7, 44, 6));
			Emit(placed.Runtime, 1);
			var pose = placed.Runtime.LastParticleEventAttempt!.Value.Request!.Value.EnginePosition;
			// [BIN:TPW-EXE:0x55189D] every pass of the object loop moves the type 1 and 2 records with a key (0x51FE30), and turns type 2 (0x51FD90)
			placed.Runtime.Simulate(FixedStepClock.TickDuration);
			Assert.AreEqual(2, followed.Count);
			Assert.AreEqual((0x10000, pose), (followed[0].Handle, followed[0].Follow.EnginePosition));
			Assert.IsNull(followed[0].Follow.EngineUnitDirection);
			Assert.AreEqual(0x10001, followed[1].Handle);
			Assert.IsNotNull(followed[1].Follow.EngineUnitDirection, "type 2 follows the key's direction too");
			// a negative key's record does not follow (the walk tests the key +0x10 >= 0)
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 0xFFFF, 43, 7));
			followed.Clear();
			placed.Runtime.Simulate(FixedStepClock.TickDuration);
			Assert.AreEqual(2, followed.Count);
			Assert.AreEqual(1, placed.Runtime.CaptureSnapshot().ParticleObjects!.Count(record => record.Key < 0));
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleFollower = previousFollower; OriginalObjectRuntime.ParticleAlive = previousAlive; }
	}

	[TestMethod]
	public void AParticleRecordFromAnOlderSaveLoadsWithoutAFollowAndABadTypeIsRefused()
	{
		var placed = Placed();
		var saved = placed.Runtime.CaptureSnapshot() with { ParticleObjects = new[] { new ParticleObjectRecord { Slot = 1, Handle = 0x10000 } } };
		saved.ValidateSnapshot();
		Assert.AreEqual(0, saved.ParticleObjects![0].Type, "a record without a type is not moved");
		Assert.ThrowsException<InvalidDataException>(() => (saved with { ParticleObjects = new[] { new ParticleObjectRecord { Slot = 1, Handle = 0x10000, Type = 3 } } }).ValidateSnapshot());
	}

	[TestMethod]
	public void AParticleRecordKeepsItsTypeAndKeyThroughJsonAndAnOldRecordWithoutThemLoads()
	{
		var placed = Placed();
		var saved = placed.Runtime.CaptureSnapshot() with { ParticleObjects = new[] { new ParticleObjectRecord { Slot = 5, Handle = 0x10001, Type = 2, Key = 7 } } };
		var back = System.Text.Json.JsonSerializer.Deserialize<OriginalObjectRuntimeSnapshot>( System.Text.Json.JsonSerializer.Serialize( saved ) )!;
		back.ValidateSnapshot();
		Assert.AreEqual( (5, 0x10001, 2, 7), (back.ParticleObjects![0].Slot, back.ParticleObjects[0].Handle, back.ParticleObjects[0].Type, back.ParticleObjects[0].Key) );
		var old = System.Text.Json.JsonSerializer.Deserialize<ParticleObjectRecord>( "{\"Slot\":5,\"Handle\":65537}" )!;
		Assert.AreEqual( (0, 0), (old.Type, old.Key), "a record written before the follow keeps neither" );
	}

	[TestMethod]
	public void AKeyEntryWithoutAMatrixStartsTheEffectAtTheWorldOrigin()
	{
		var bytes = File.ReadAllBytes(Path.Combine(root, "model.MD2")); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1b0), 0x100); File.WriteAllBytes(Path.Combine(root, "origin.MD2"), bytes);
		var origin = new ObjectCatalogEntry("jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings(new[] { new ObjectSettingsFile("Info.Id 1100\n", "synthetic.sam") }),
			new ObjectShape(new[] { "*" }), "/origin.MD2", null, "/script.RSE", new[] { "/child.RSE" }, Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem(root), null);
		var placed = new OriginalObject(origin, new ObjectPlacement(3, 4, 0, new N.Vector2(-7, 11), 9));
		var received = new List<ParticleEventRequest>(); Action<ParticleEventRequest> handler = received.Add; OriginalObjectRuntime.ParticleRequested += handler;
		try
		{
			Emit(placed.Runtime, 2); var attempt = placed.Runtime.LastParticleEventAttempt!.Value;
			Assert.AreEqual((ParticleEventPreparationStatus.Prepared, ParticleKeyStatus.ResolvedAtWorldOrigin), (attempt.Status, attempt.KeyStatus));
			var request = attempt.Request!.Value; Assert.IsTrue(request.AtWorldOrigin); Assert.AreEqual(N.Vector3.Zero, request.EnginePosition); Assert.IsNull(request.EngineUnitDirection);
			Assert.AreEqual(1, received.Count, "the request is announced like any prepared EVENT");
		}
		finally { OriginalObjectRuntime.ParticleRequested -= handler; }
	}

	[TestMethod]
	public void NegativeHandlesAreKeptRecordsOfEndedEffectsArePrunedAndRecordsSurviveTheSnapshot()
	{
		var killed = new List<int>(); var alive = new HashSet<int> { unchecked((int)0x80010003), 0x10004 }; var handles = new Queue<int>(new[] { unchecked((int)0x80010003), 0x10004, -1, 0 });
		var previousStarter = OriginalObjectRuntime.ParticleStarter; var previousKiller = OriginalObjectRuntime.ParticleKiller; var previousAlive = OriginalObjectRuntime.ParticleAlive;
		OriginalObjectRuntime.ParticleStarter = request => handles.Dequeue(); OriginalObjectRuntime.ParticleKiller = killed.Add; OriginalObjectRuntime.ParticleAlive = alive.Contains;
		try
		{
			var placed = Placed();
			Run(placed.Runtime, a => a.I(Opcode.ADDOBJ, 1, 7, 43, 5).I(Opcode.ADDOBJ, 1, 7, 43, 6).I(Opcode.ADDOBJ, 1, 7, 43, 7).I(Opcode.ADDOBJ, 1, 7, 43, 8));
			var saved = placed.Runtime.CaptureSnapshot();
			CollectionAssert.AreEqual(new[] { (5, unchecked((int)0x80010003)), (6, 0x10004) }, saved.ParticleObjects!.Select(r => (r.Slot, r.Handle)).ToArray(), "a negative generation is a handle; 0 and -1 are refusals");
			alive.Remove(0x10004);
			Assert.AreEqual(1, placed.Runtime.CaptureSnapshot().ParticleObjects!.Count, "the record of an effect that ended is pruned");
			var copy = saved.DeepCopy(); saved.ValidateSnapshot();
			Assert.AreEqual(2, copy.ParticleObjects!.Count);
			Assert.ThrowsException<InvalidDataException>(() => (saved with { ParticleObjects = new[] { new ParticleObjectRecord { Slot = 1, Handle = 0 } } }).ValidateSnapshot());
			// a restored runtime holds the records, so its KILLOBJ still ends the effect
			var restored = OriginalObjectRuntime.CreateForRestore(entry, new RideScriptWorld(), seed: 2, open: true, attractionId: saved.AttractionId);
			Assert.AreEqual(0, restored.RestoreSnapshot(saved).Count);
			Run(restored, a => a.I(Opcode.KILLOBJ, 5));
			CollectionAssert.AreEqual(new[] { unchecked((int)0x80010003) }, killed);
		}
		finally { OriginalObjectRuntime.ParticleStarter = previousStarter; OriginalObjectRuntime.ParticleKiller = previousKiller; OriginalObjectRuntime.ParticleAlive = previousAlive; }
	}

}
