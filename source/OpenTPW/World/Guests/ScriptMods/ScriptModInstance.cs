using OpenTPW.Mods;
using OpenTPW.Scripting;

namespace OpenTPW;

/// <summary>Why a script call failed (docs/SCRIPT-MODS.md, "Failure counters and the disabled flag").</summary>
internal enum ScriptFailureKind
{
	RuntimeError,
	Budget,
	InvalidResult
}

/// <summary>What one script mod did in a run, for the guest report and the tests. Measurement only: none of it is saved or hashed.</summary>
/// <param name="Calls">Hook calls that ran the interpreter.</param>
/// <param name="TotalSteps">Interpreter steps over all of them.</param>
/// <param name="MaxCallSteps">The most steps one call took.</param>
/// <param name="MaxTickSteps">The most steps the mod took in one tick.</param>
/// <param name="MaxTickCalls">The most calls the mod ran in one tick.</param>
/// <param name="Ticks">Ticks in which the mod ran at least one call.</param>
internal sealed record ScriptModStats( string Id, long Calls, long TotalSteps, long MaxCallSteps, long MaxTickSteps, long MaxTickCalls, long Ticks, long RuntimeErrors, long BudgetFailures, long InvalidResults, bool Disabled, long TicksOverBudget = 0 );

/// <summary>
/// One script mod inside one guest simulation (docs/SCRIPT-MODS.md): its compiled programs, its <c>state</c>, its step meters, its failure counters and its disabled flag. The state, the counters and the
/// flag are simulation state (<see cref="ScriptModsSave"/> keeps them, <see cref="AddCanonicalState"/> hashes them); the programs come from the bytes the mod was resolved with and are never read again.
/// </summary>
// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
internal sealed class ScriptModInstance
{
	private readonly ScriptHost[] hosts;
	private readonly IReadOnlyDictionary<string, (int File, string Function)> hooks;
	private readonly ScriptHostOptions options;
	private long tickRemaining;
	private bool tickOverCounted;
	private bool inDecision, decisionSkipped;

	// measurement
	private long calls, totalSteps, maxCallSteps, maxTickSteps, maxTickCalls, tickCount, tickSteps, tickCalls;

	public ScriptModInstance( ResolvedMod mod, ScriptHostOptions? options = null )
	{
		var scripts = mod.Scripts ?? throw new ArgumentException( $"The mod {mod.Id} has no scripts.", nameof( mod ) );
		Id = mod.Id;
		this.options = options ?? new ScriptHostOptions();
		hooks = scripts.Hooks;
		hosts = scripts.Files.Select( file => new ScriptHost( file.Program, mod.Id, this.options ) ).ToArray();
		State = ScriptState.Empty();
		tickRemaining = this.options.StepsPerTick;
	}

	public string Id { get; }

	/// <summary>The committed <c>state</c>; never modified in place (a call works on a copy), so a captured reference is a snapshot.</summary>
	public ScriptTable State { get; private set; }

	public long RuntimeErrors { get; private set; }
	public long BudgetFailures { get; private set; }
	public long InvalidResults { get; private set; }

	/// <summary>
	/// Ticks in which the mod used up its per-tick step budget. Not a failure and not counted toward disabling (a busy park raises the calls of a tick without the mod being wrong); saved and hashed
	/// for diagnostics, and logged.
	/// </summary>
	public long TicksOverBudget { get; private set; }

	/// <summary>All failures together: the number <see cref="ScriptLimits.DisableAfterFailures"/> is compared with.</summary>
	public long Failures => RuntimeErrors + BudgetFailures + InvalidResults;

	/// <summary>The mod's hooks no longer run: it failed <see cref="ScriptLimits.DisableAfterFailures"/> times. Its state is kept, and saved.</summary>
	public bool Disabled { get; private set; }

	/// <summary>The latest failure, for the log and the tests (not saved).</summary>
	public string? LastFailure { get; private set; }

	public bool HasHook( string hook ) => hooks.ContainsKey( hook );

	/// <summary>The step budget of a new tick is full again.</summary>
	public void BeginTick()
	{
		if ( tickSteps > 0 )
		{
			tickCount++;
			maxTickSteps = Math.Max( maxTickSteps, tickSteps );
			maxTickCalls = Math.Max( maxTickCalls, tickCalls );
		}
		tickSteps = 0;
		tickCalls = 0;
		tickRemaining = options.StepsPerTick;
		tickOverCounted = false;
	}

	public ScriptModStats Stats() => new( Id, calls, totalSteps, maxCallSteps, Math.Max( maxTickSteps, tickSteps ), Math.Max( maxTickCalls, tickCalls ), tickCount + (tickSteps > 0 ? 1 : 0), RuntimeErrors, BudgetFailures, InvalidResults, Disabled, TicksOverBudget );

	/// <summary>
	/// A candidate loop starts (<see cref="IGuestDecisions.BeginDecision"/>). The tick budget gates the whole decision: with none left the mod is skipped for every candidate (one overrun for the tick),
	/// otherwise every candidate runs against its per-call budget alone, so the decision's scores all come from this mod or none do. The tick meter is still charged and may go below zero.
	/// </summary>
	public void BeginDecision( string hook )
	{
		inDecision = false;
		decisionSkipped = false;
		if ( Disabled || !hooks.ContainsKey( hook ) )
			return;
		inDecision = true;
		if ( tickRemaining <= 0 )
		{
			decisionSkipped = true;
			NoteOverrun();
		}
	}

	public void EndDecision() => (inDecision, decisionSkipped) = (false, false);

	private void NoteOverrun()
	{
		if ( tickOverCounted )
			return;
		tickOverCounted = true;
		TicksOverBudget++;
		if ( TicksOverBudget == 1 || TicksOverBudget % 600 == 0 )
			Log?.Warning( $"Script mod {Id} used up its step budget for the tick ({options.StepsPerTick} steps); its remaining calls that tick answer prev. Ticks over budget so far: {TicksOverBudget}." );
	}

	/// <summary>
	/// Runs <paramref name="hook"/> of this mod and returns its result, or <paramref name="previous"/> when the mod is disabled, has no such hook or the call fails in any way. A failure rolls the
	/// state back, adds to its counter and, at the limit, disables the mod. <paramref name="accept"/> is the host's own check of a result the hook's type and range cannot express (a goal inside the
	/// map); a result it refuses is an invalid result.
	/// </summary>
	public ScriptValue Call( string hook, ReadOnlySpan<ScriptValue> arguments, ScriptValue previous, ScriptResultSpec expected, ScriptCallIdentity identity, Func<long, bool>? accept = null )
	{
		if ( Disabled || !hooks.TryGetValue( hook, out var target ) )
			return previous;
		// outside a decision a call needs the tick's budget; inside one the decision was admitted (or skipped) as a whole at its start
		if ( inDecision ? decisionSkipped : tickRemaining <= 0 )
		{
			if ( !inDecision )
				NoteOverrun();
			return previous;
		}
		var host = hosts[target.File];
		var before = State;
		var tickLimited = !inDecision && tickRemaining < options.StepsPerCall;
		host.AdoptState( before );
		host.SetTickRemaining( inDecision ? long.MaxValue / 4 : tickRemaining );
		var result = host.Call( target.Function, arguments, previous, expected, identity, hook );
		tickRemaining -= result.Steps;
		tickSteps += result.Steps;
		totalSteps += result.Steps;
		maxCallSteps = Math.Max( maxCallSteps, result.Steps );
		calls++;
		tickCalls++;
		if ( result.Succeeded && accept != null && !accept( result.Value.AsInt ) )
		{
			State = before;
			Fail( ScriptFailureKind.InvalidResult, hook, $"the result {result.Value.AsInt} is not acceptable here" );
			return previous;
		}
		if ( result.Succeeded )
		{
			State = host.State;
			return result.Value;
		}
		State = before;
		// a call stopped by what was left of the tick, not by its own budget, is a tick overrun and no failure of the mod
		if ( result.Outcome == ScriptOutcome.BudgetExhausted && tickLimited )
		{
			NoteOverrun();
			return previous;
		}
		var kind = result.Outcome switch
		{
			ScriptOutcome.BudgetExhausted => ScriptFailureKind.Budget,
			ScriptOutcome.InvalidResult => ScriptFailureKind.InvalidResult,
			_ => ScriptFailureKind.RuntimeError
		};
		Fail( kind, hook, result.Line > 0 ? $"line {result.Line}: {result.Message}" : result.Message ?? "failed" );
		return previous;
	}

	private void Fail( ScriptFailureKind kind, string hook, string message )
	{
		switch ( kind )
		{
			case ScriptFailureKind.RuntimeError: RuntimeErrors++; break;
			case ScriptFailureKind.Budget: BudgetFailures++; break;
			default: InvalidResults++; break;
		}
		LastFailure = $"{hook}: {kind}: {message}";
		if ( !Disabled && Failures >= ScriptLimits.DisableAfterFailures )
		{
			Disabled = true;
			Log?.Warning( $"Script mod {Id} is disabled for this park after {Failures} failed calls ({RuntimeErrors} runtime errors, {BudgetFailures} budget, {InvalidResults} invalid results); the last was {LastFailure}" );
		}
	}

	/// <summary>Puts a saved state, counters and flag back (<see cref="ScriptModsSave.Restore"/>, after its validation).</summary>
	internal void Restore( ScriptTable state, long runtimeErrors, long budgetFailures, long invalidResults, bool disabled, long ticksOverBudget = 0 )
	{
		State = state;
		RuntimeErrors = runtimeErrors;
		BudgetFailures = budgetFailures;
		InvalidResults = invalidResults;
		Disabled = disabled;
		TicksOverBudget = ticksOverBudget;
		LastFailure = null;
		tickRemaining = options.StepsPerTick;
		tickOverCounted = false;
		(inDecision, decisionSkipped) = (false, false);
	}

	/// <summary>This mod's part of <see cref="WorldStateHash"/>: the id, the versions, the canonical state, the counters (failures and ticks over budget) and the flag.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( Id );
		hash.Add( ScriptLimits.BehaviourVersion );
		hash.Add( ScriptLimits.ScriptApiVersion );
		hash.Add( ScriptState.Serialize( State ) );
		hash.Add( RuntimeErrors );
		hash.Add( BudgetFailures );
		hash.Add( InvalidResults );
		hash.Add( Disabled );
		hash.Add( TicksOverBudget );
	}
}
