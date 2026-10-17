namespace OpenTPW.Scripting;

/// <summary>
/// Every static and dynamic limit of TPW Script (docs/SCRIPT-MODS.md). Changing a value changes what scripts may do, so it bumps
/// <see cref="BehaviourVersion"/>: saves record the version and are refused under different rules.
/// </summary>
public static class ScriptLimits
{
	/// <summary>Version of the interpreter semantics and of every limit below. Bump on any change to either.</summary>
	public const int BehaviourVersion = 1;

	/// <summary>
	/// Version of the hook API a mod's <c>scriptApi</c> range is checked against (docs/SCRIPT-MODS.md): the hook names, their parameters and
	/// the views they receive. Mods target the API, not the interpreter; bump the major version on any incompatible change to a hook.
	/// </summary>
	public const string ScriptApiVersion = "1.0.0";

	/// <summary>The hooks of API v1 with the number of parameters each hook function takes (docs/SCRIPT-MODS.md, "Hook API v1"; the game wires them in <c>ScriptGuestDecisions</c>).</summary>
	public static readonly IReadOnlyDictionary<string, int> HookParameters = new Dictionary<string, int>( StringComparer.Ordinal )
	{
		["scoreAttraction"] = 3,
		["joinQueue"] = 3,
		["leaveQueue"] = 3,
		["wanderGoal"] = 2,
	};

	/// <summary>The hooks of API v1 that only change what the player sees or reads, never the simulation. None exist in v1.</summary>
	public static readonly IReadOnlySet<string> UiOnlyHooks = new HashSet<string>( StringComparer.Ordinal );

	// Static limits, checked while parsing.
	public const int MaxSourceBytes = 64 * 1024;
	public const int MaxTokens = 20_000;
	public const int MaxNesting = 32;
	public const int MaxFunctions = 64;
	public const int MaxParameters = 8;
	public const int MaxLocalsPerFunction = 64;
	public const int MaxStringLength = 1024;
	public const int MaxNameLength = 64;

	// Dynamic limits, checked while running.
	public const int MaxCallDepth = 32;
	public const int StackSize = 4096;
	/// <summary>
	/// Steps one call may take. Measured on the guest workloads of docs/SCRIPT-MODS.md ("Budgets"): the heaviest call of the measured mods took 124 steps, so this leaves a factor of 40, and 5 000 steps are
	/// about 65 microseconds of interpretation on a desktop.
	/// </summary>
	public const long DefaultStepsPerCall = 5_000;

	/// <summary>
	/// Steps one mod may take in one simulation tick, over all its calls. The busiest tick of the measured mods took 3 188 steps (64 calls), so this leaves a factor of 15; it is about 0.7 ms on a desktop and
	/// bounds a mod that burns it all every tick to a few milliseconds even in the browser build, against a 16.7 ms tick.
	/// </summary>
	public const long DefaultStepsPerTick = 50_000;

	/// <summary>
	/// A mod is disabled for the rest of the park once this many of its calls have failed (runtime error, budget or invalid result, counted together; docs/SCRIPT-MODS.md, "Failure counters").
	/// Not an interpreter semantic: it belongs to the game wiring, which saves and hashes the counters.
	/// </summary>
	public const int DisableAfterFailures = 16;

	/// <summary>The largest score a <c>scoreAttraction</c> hook may return: the host's score is an unsigned 32-bit word.</summary>
	public const long MaxScore = uint.MaxValue;

	/// <summary>A wander goal is one int, <c>y * GoalStride + x</c> of the goal cell (the largest park map is 1024 cells wide).</summary>
	public const int GoalStride = 1024;

	/// <summary>Bytes of strings and tables a single call may create (accounted, not measured: see the interpreter).</summary>
	public const long MaxCallMemory = 512 * 1024;
	public const int MaxTableEntries = 4096;

	// The <c>state</c> schema.
	public const int MaxStateDepth = 16;
	public const int MaxStateNodes = 4096;
	public const int MaxStateBytes = 64 * 1024;
	public const int MaxStateKeyLength = 64;
}
