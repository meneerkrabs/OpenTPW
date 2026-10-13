using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The developer prototype Totem of one saved park (the park-resume envelope's optional prototype part): where it
/// stands, its open flag, the seed its script drew, its runtime state with its script-world root, its economy
/// association and the guest cells its bridge queues at. The entrance/exit cells are saved and restored verbatim:
/// they are placement-time derivations the live park never re-runs (a later grid edit leaves the queue's front at
/// the cell the placement picked), so re-deriving them from the saved grid would resume a different park than the
/// one that was saved — or refuse its own save at the queue-geometry check. Null cells mean the live park never
/// registered the Totem with the guests (no walkable cell at placement). The part describes exactly the Inca Totem
/// (<see cref="PrototypeRide"/>, <see cref="PrototypeRide.InfoId"/>); any other entry is refused, not
/// blanket-accepted as opaque state.
/// </summary>
/// <param name="RootScriptId">
/// The script world's root id of the Totem's Totem.RSE script while its runtime is live, or 0 when the runtime was
/// stopped (its VM has already unregistered itself, so the world does not carry it).
/// </param>
/// <param name="EntranceCell">
/// The bridge's entrance cell as the live park held it, or null when the Totem was never registered with the guests.
/// </param>
/// <param name="ExitCell">The bridge's exit cell (the placement sets it to the entrance), with <paramref name="EntranceCell"/>'s nullability.</param>
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
	public (int X, int Y)? EntranceCell { get; init; }
	public (int X, int Y)? ExitCell { get; init; }
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
	/// its runtime state (animator, head slots, counters), the guest cells its bridge queues at and the economy
	/// association the live park holds.
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
			EntranceCell = runtime.Visitors.HasCells ? runtime.Visitors.EntranceCell : null,
			ExitCell = runtime.Visitors.HasCells ? runtime.Visitors.ExitCell : null,
			EconomyInstanceId = economyInstance,
			EconomyBought = economyBought
		};
	}

	/// <summary>
	/// Restores the saved guest cells onto a fresh candidate's bridge, verbatim, and registers it with the guests —
	/// the registration the placement ran, minus the purchase. Returns false when the live park never registered the
	/// Totem (null cells): the candidate stays unregistered, exactly like the ride it is resuming. The cells are never
	/// re-derived: the placement's derivation is history the saved grid cannot reproduce (the live park keeps its
	/// entrance after later path edits), and the restored queue must be the saved one or the resumed park is not the
	/// park that was saved.
	/// </summary>
	public static bool RestoreGuestCells( PrototypeRideSaveData saved, RideVisitorBridge visitors, GuestSimulation guests )
	{
		ArgumentNullException.ThrowIfNull( saved );
		ArgumentNullException.ThrowIfNull( visitors );
		ArgumentNullException.ThrowIfNull( guests );
		if ( saved.EntranceCell.HasValue != saved.ExitCell.HasValue )
			throw new InvalidDataException( "The saved prototype holds one guest cell but not the other." );
		if ( saved.EntranceCell is not { } entrance )
			return false;
		visitors.EntranceCell = entrance;
		visitors.ExitCell = saved.ExitCell!.Value;
		visitors.HasCells = true;
		guests.Register( visitors );
		return true;
	}
}
