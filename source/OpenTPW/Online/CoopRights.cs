namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-permissions] What the host lets a guest's seat do in a shared park (docs/COOP.md, "Money and rights (C4)"; the user's decision Q7: the host sets each player's rights): one
/// switch a right, each the host's to give or take back. Building is C3's grant (<see cref="SetBuildGrantCommand"/>); the others cover the park's management, the host's alone until the
/// part that makes their windows' requests wait for the host offers them (<see cref="CoopRightsOffer"/>). Pause and speed, the cheats and the session's own commands are no seat's right.
/// </summary>
[Flags]
public enum CoopRights : byte
{
	None = 0,
	/// <summary>The guests' building (C3): paths, queues, objects, land, the area clear, pools, Move, the rides' claims, tracks and coasters.</summary>
	Build = 1,
	/// <summary>Any attraction's open switch, settings, prices, prizes and goods, level upgrades and the staff it calls (C4b).</summary>
	Attractions = 2,
	/// <summary>Hiring, placing, moving, firing and renaming employees, their patrol areas and the training budgets (C4c).</summary>
	Staff = 4,
	/// <summary>The research efforts, the lab's workload and the projects' order (C4b).</summary>
	Research = 8,
	/// <summary>The park's door and entrance fee, loans and their offers, and the park's name (C4b).</summary>
	Finance = 16
}

/// <summary>[EXT:online-coop-permissions] The rights this build lets the host give a seat: a right whose actions do not yet wait for the host's answer cannot be given, not even by a hand-made command.</summary>
internal static class CoopRightsOffer
{
	/// <summary>The rights offered (C4a: building alone; C4b-1 research and finance, C4b-2 attractions, C4c-2 staff): all five.</summary>
	public const CoopRights Offered = CoopRights.Build | CoopRights.Attractions | CoopRights.Staff | CoopRights.Research | CoopRights.Finance;

	/// <summary>Each offered right, in the order the rights dialog lists them.</summary>
	public static IReadOnlyList<CoopRights> Each { get; } = Enum.GetValues<CoopRights>().Where( right => right != CoopRights.None && Offered.HasFlag( right ) ).ToArray();

	/// <summary>Whether <paramref name="rights"/> holds only offered rights.</summary>
	public static bool IsOffered( CoopRights rights ) => (rights & ~Offered) == 0;
}
