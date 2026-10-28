namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Func<Vector3?>? eventOrigin;
	private Func<Vector3?>? footprintMiddle;
	private Func<System.Numerics.Vector3, Vector3>? eventSoundSpace;

	/// <summary>Presentation-only sound-space origin of the object (its model origin), sampled when a repair sound executes; headless runtimes have none.</summary>
	internal void SetEventOrigin(Func<Vector3?> origin) => eventOrigin = origin;

	/// <summary>
	/// Presentation-only anchors of the object's sounds: <paramref name="footprint"/> is the footprint middle in sound units (screams, and an EVENT with a negative key) and
	/// <paramref name="soundSpace"/> turns an engine position (an EVENT key's model node) into sound units. Headless runtimes have neither and play flat.
	/// </summary>
	internal void SetEventAnchors(Func<Vector3?> footprint, Func<System.Numerics.Vector3, Vector3> soundSpace)
	{
		footprintMiddle = footprint;
		eventSoundSpace = soundSpace;
	}

	// [BIN:TPW-EXE:0x556a80 object position] the repair sound's place is the owner object's position: the root node's matrix row 3 (+0x40/+0x44/+0x48 of [[[object+8]+4]+0x78]), sampled once at instruction time and never updated; the model origin's height here
	internal Vector3? ReadEventOrigin() => Finite(eventOrigin?.Invoke());

	/// <summary>A sound position as the original submits it: each coordinate truncated toward zero to whole units.</summary>
	// [BIN:TPW-EXE:0x5573d0 EVENT submit] every sound type converts the anchor's three floats with __ftol (0x67a830, truncation) before 0x51bfc0, which turns the ints back into floats; the REPAIREFFECT sounds do the same (0x5562f6-0x55630a, 0x55639f-0x5563b3)
	internal static Vector3? WholeUnits(Vector3? position) => position is { } point ? new Vector3(Truncate(point.X), Truncate(point.Y), Truncate(point.Z)) : null;

	// [EXT:sound-unit-round-trip] OpenTPW's anchors pass through engine units (x 0.2 and back), which leaves whole values a few ulps short (46.999996); a value within 1/1024 of a whole unit counts as that unit before the truncation, so the round trip does not cost a unit the original keeps
	private static float Truncate(float value)
	{
		var nearest = MathF.Round(value);
		return MathF.Abs(value - nearest) < 1f / 1024f ? nearest : MathF.Truncate(value);
	}

	/// <summary>The footprint middle in sound units: the original's anchor for a scream and for an EVENT whose key is negative; null for a runtime without a placement.</summary>
	// [BIN:TPW-EXE:0x556d42 footprint middle height] the EVENT anchor for a negative key takes x and z as the middles of 0x466b70's box and y from the root node's +0x44 alone, the same word 0x556a80 reads as the object's height, which is the base height here
	// [BIN:TPW-EXE:0x466b70 footprint middle] ((x0 + x1) / 2, model height, (z0 + z1) / 2) of the object's packed u16 footprint rectangle (+0xC0/+0xC4), scaled x10 with a one-cell offset; used by 0x556af0 (SINGLESCREAM), 0x551130 (STARTSCREAM) and 0x556d42 (EVENT, key < 0)
	internal Vector3? ReadFootprintMiddle() => Finite(footprintMiddle?.Invoke());

	private static Vector3? Finite(Vector3? origin) => origin is { } point && float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z) ? point : null;

	/// <summary>The outcome of resolving an EVENT sound's key to a place.</summary>
	internal enum EventAnchorStatus
	{
		/// <summary>Play at the position, or flat when the runtime has no placement (a headless host).</summary>
		Play,
		/// <summary>The key names no node: the original logs "RSSE: Invalid Node ID" and plays nothing.</summary>
		InvalidNode,
	}

	/// <summary>
	/// Resolves the key operand of an <c>EVENT</c> sound: a non-negative key is the model node of that key (selector 0x200), a negative key the footprint middle;
	/// an unknown key plays nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x556b90 EVENT anchor] called as (object, &position, 0, key, 0x200) by every sound type (3-9 from 0x5575c7 on, and 10 at 0x5576fb): key >= 0 looks the key up with selector 0x200 (0x44b220) and takes the node's position (+0x30/+0x34/+0x38), key < 0 the footprint middle at the model height (0x556d42); an unknown key logs "RSSE: Invalid Node ID" (0x556d2c), returns 0 and the dispatcher submits nothing (0x5575e5)
	internal EventAnchorStatus ResolveEventAnchor(int key, out Vector3? position)
	{
		var status = ResolveEventAnchorFloat(key, out position);
		position = WholeUnits(position);
		return status;
	}

	private EventAnchorStatus ResolveEventAnchorFloat(int key, out Vector3? position)
	{
		position = null;
		if (key < 0)
		{
			position = ReadFootprintMiddle();
			return EventAnchorStatus.Play;
		}
		if (eventSoundSpace is not { } toSound || particlePlacement is not { } placement)
			return EventAnchorStatus.Play;
		particleKeys ??= new ParticleKeyResolver(Model);
		var pose = particleKeys.Resolve(key, ParticleKeyResolver.SoundSelector, KeyPose, placement);
		switch (pose.Status)
		{
			case ParticleKeyStatus.Resolved:
				position = Finite(toSound(pose.EnginePosition!.Value));
				return position == null ? EventAnchorStatus.InvalidNode : EventAnchorStatus.Play;
			case ParticleKeyStatus.ResolvedAtWorldOrigin:
			case ParticleKeyStatus.UnsupportedNode:
				// a key whose node has no matrix: the original reads a zero default (0x556c08), the park's corner, not an invalid node; OpenTPW plays it at the footprint middle (flat without a placement)
				position = ReadFootprintMiddle();
				return EventAnchorStatus.Play;
			default:
				Log.Trace($"RSSE: Invalid Node ID (EVENT key {key}).");
				return EventAnchorStatus.InvalidNode;
		}
	}
}
