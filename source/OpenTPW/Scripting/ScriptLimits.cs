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

	/// <summary>The hooks of API v1 with the number of parameters each hook function takes. Hooks are inert until phase 4 wires them in.</summary>
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
	public const long DefaultStepsPerCall = 10_000;
	public const long DefaultStepsPerTick = 200_000;

	/// <summary>Bytes of strings and tables a single call may create (accounted, not measured: see the interpreter).</summary>
	public const long MaxCallMemory = 512 * 1024;
	public const int MaxTableEntries = 4096;

	// The <c>state</c> schema.
	public const int MaxStateDepth = 16;
	public const int MaxStateNodes = 4096;
	public const int MaxStateBytes = 64 * 1024;
	public const int MaxStateKeyLength = 64;
}
