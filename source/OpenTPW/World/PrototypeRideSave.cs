using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The developer prototype Totem of one saved park (the park-resume envelope's optional prototype part): where it
/// stands, its open flag, the seed its script drew, its runtime state with its script-world root, and its economy
/// association. The entrance/exit cells guests queue at are re-derived from the restored grid on load (the same
/// derivation the placement ran — it is deterministic in the grid and the position), so they are never saved. The
/// part describes exactly the Inca Totem (<see cref="PrototypeRide"/>, <see cref="PrototypeRide.InfoId"/>); any other
/// entry is refused, not blanket-accepted as opaque state.
/// </summary>
/// <param name="RootScriptId">
/// The script world's root id of the Totem's Totem.RSE script while its runtime is live, or 0 when the runtime was
/// stopped (its VM has already unregistered itself, so the world does not carry it).
/// </param>
/// <param name="EconomyInstanceId">
/// The economy instance the live park bound the prototype to, or null when it has none (no reachable entrance cell
/// at placement, or the theme's economy catalogue does not list the Totem).
/// </param>
/// <param name="EconomyBought">Whether the live park paid for the Totem (<c>TryBuild</c>); a removal sells it only when bought.</param>
internal sealed record PrototypeRideSaveData
{
	public required int AttractionId { get; init; }
	public required float PositionX { get; init; }
	public required float PositionY { get; init; }
	public required float PositionZ { get; init; }
	public required bool Open { get; init; }
	/// <summary>The seed the Totem's script was created with (drawn from the world's seed stream when placed); the restore constructs its VM with it, so the stream is never drawn twice.</summary>
	public required int ScriptSeed { get; init; }
	public required int RootScriptId { get; init; }
	public required OriginalObjectRuntimeSnapshot Runtime { get; init; }
	public int? EconomyInstanceId { get; init; }
	public bool EconomyBought { get; init; }
}

/// <summary>
/// Capture and restore of the prototype part of a park-resume envelope (docs/PARK-SAVES.md): the developer prototype
/// Totem joined by its attraction id — the same allocator the placed objects draw from — and a park without one
/// writes no part at all, so saves from before the prototype slice stay exactly what they were. The capture reads the
/// runtime directly (the headless rigs hold an <see cref="OriginalObjectRuntime"/> without the GPU entity); the level
/// joins its live Totem, its position and its economy association the same way.
/// </summary>
internal static class PrototypeRideSave
{
	/// <summary>Any coordinate a save may carry is finite and within this magnitude (a placement click's cell centre is map-scale).</summary>
	public const float MaximumCoordinate = 1_000_000f;

	/// <summary>
	/// The deep copy of one live Totem: its identity and position, its open flag, the seed and world-root of its script,
	/// its runtime state (animator, head slots, counters) and the economy association the live park holds.
	/// </summary>
	public static PrototypeRideSaveData Capture( OriginalObjectRuntime runtime, Vector3 position, bool open, int? economyInstance, bool economyBought )
	{
		ArgumentNullException.ThrowIfNull( runtime );
		var state = runtime.CaptureSnapshot();
		return new PrototypeRideSaveData
		{
			AttractionId = runtime.AttractionId,
			PositionX = position.X,
			PositionY = position.Y,
			PositionZ = position.Z,
			Open = open,
			ScriptSeed = runtime.Script?.Seed ?? 0,
			RootScriptId = state.HasScript && !state.Stopped ? runtime.Script!.ScriptId : 0,
			Runtime = state,
			EconomyInstanceId = economyInstance,
			EconomyBought = economyBought
		};
	}
}
