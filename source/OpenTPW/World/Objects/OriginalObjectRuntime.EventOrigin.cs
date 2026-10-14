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

	// [BIN:TPW-EXE:0x556a80 object position] the repair sound's place is the owner object's position; [APPROX:AUDIO-908] the position is sampled once at instruction time (as the original, which passes ints and never updates it), but OpenTPW keeps floats and the model origin's height
	internal Vector3? ReadEventOrigin() => Finite(eventOrigin?.Invoke());

	/// <summary>The footprint middle in sound units: the original's anchor for a scream and for an EVENT whose key is negative; null for a runtime without a placement.</summary>
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
	// [BIN:TPW-EXE:0x556b90 EVENT anchor] called as (object, &position, 0, key, 0x200) by every sound type (3-9, from 0x5575c7 on): key >= 0 looks the key up with selector 0x200 (0x44b220) and takes the node's position (+0x30/+0x34/+0x38), key < 0 the footprint middle at the model height (0x556d42); an unknown key logs "RSSE: Invalid Node ID" (0x556d2c), returns 0 and the dispatcher submits nothing (0x5575e5)
	internal EventAnchorStatus ResolveEventAnchor(int key, out Vector3? position)
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
		var pose = particleKeys.Resolve(key, ParticleKeyResolver.SoundSelector, Animator.NodeTransforms, placement);
		switch (pose.Status)
		{
			case ParticleKeyStatus.Resolved:
				position = Finite(toSound(pose.EnginePosition!.Value));
				return position == null ? EventAnchorStatus.InvalidNode : EventAnchorStatus.Play;
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
