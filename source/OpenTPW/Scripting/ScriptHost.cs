namespace OpenTPW.Scripting;

public enum ScriptOutcome
{
	Ok,

	/// <summary>The script failed (type error, overflow, division by zero, missing key, depth, memory...). The message and line say which.</summary>
	RuntimeError,

	/// <summary>The call or the tick ran out of steps.</summary>
	BudgetExhausted,

	/// <summary>The call returned, but its result (or the new <c>state</c>) breaks the contract: wrong type, out of range or outside the state schema.</summary>
	InvalidResult
}

/// <summary>The type and range a hook's result must have (docs/SCRIPT-MODS.md: typed hook results).</summary>
public readonly record struct ScriptResultSpec( ScriptKind Kind, long Min = 0, long Max = 0 )
{
	/// <summary>An integer in [<paramref name="min"/>, <paramref name="max"/>].</summary>
	public static ScriptResultSpec Int( long min, long max ) => new( ScriptKind.Int, min, max );
	public static ScriptResultSpec Bool => new( ScriptKind.Bool );
	public static ScriptResultSpec String => new( ScriptKind.String );
}

/// <summary>The call identity that <c>rng</c> hashes (with the mod id and hook name): park seed, guest birth id and park turn.</summary>
public readonly record struct ScriptCallIdentity( long ParkSeed, long GuestBirthId, long ParkTurn );

/// <summary>
/// The result of <see cref="ScriptHost.Call"/>. On success <see cref="Value"/> is the script's result; on any failure it is the <c>prev</c> the
/// caller passed in (the previous mod's result, or the game's own), the state is unchanged and <see cref="Outcome"/> says why.
/// </summary>
public readonly record struct ScriptCallResult( ScriptValue Value, ScriptOutcome Outcome, string? Message, int Line, long Steps )
{
	public bool Succeeded => Outcome == ScriptOutcome.Ok;
}

public sealed class ScriptHostOptions
{
	public long StepsPerCall { get; init; } = ScriptLimits.DefaultStepsPerCall;
	public long StepsPerTick { get; init; } = ScriptLimits.DefaultStepsPerTick;
}

// [EXT:script-mods] OpenTPW's own scripted mods (TPW Script); the original game had no mods
/// <summary>
/// Runs the hooks of one compiled script against its transactional <c>state</c>. A call works on a copy-on-write view of the state; only a
/// successful call with a valid result commits it. Failure counters and disabling are the caller's (they are simulation state; docs/SCRIPT-MODS.md).
/// </summary>
public sealed class ScriptHost
{
	private readonly ScriptProgram program;
	private readonly ScriptInterpreter interpreter;
	private readonly ScriptHostOptions options;
	private readonly ulong modHash;
	private long tickRemaining;

	public ScriptHost( ScriptProgram program, string modId, ScriptHostOptions? options = null, ScriptTable? initialState = null )
	{
		this.program = program ?? throw new ArgumentNullException( nameof( program ) );
		ModId = modId ?? throw new ArgumentNullException( nameof( modId ) );
		this.options = options ?? new ScriptHostOptions();
		interpreter = new ScriptInterpreter( program );
		modHash = ScriptRng.HashText( modId );
		if ( initialState != null )
			ScriptState.Validate( initialState );
		State = initialState ?? ScriptState.Empty();
		tickRemaining = this.options.StepsPerTick;
	}

	public string ModId { get; }
	public ScriptProgram Program => program;

	/// <summary>The committed state. Treat as read-only; serialise it with <see cref="ScriptState.Serialize"/>.</summary>
	public ScriptTable State { get; private set; }

	/// <summary>Steps left in the current tick.</summary>
	public long TickStepsRemaining => tickRemaining;

	/// <summary>Starts a new tick: the per-tick step budget is full again.</summary>
	public void BeginTick() => tickRemaining = options.StepsPerTick;

	/// <summary>Replaces the state (a save resume). Validates it first.</summary>
	public void SetState( ScriptTable state )
	{
		ScriptState.Validate( state );
		State = state;
	}

	/// <summary>
	/// Calls a hook. <paramref name="arguments"/> are ints, bools, strings or read-only views (<see cref="ScriptTable.ArrayView"/>,
	/// <see cref="ScriptTable.RecordView"/>); a wrong argument count or type is a host bug and throws. Everything the script can do wrong is
	/// returned as an outcome with <paramref name="previous"/> as the value and the state rolled back.
	/// </summary>
	public ScriptCallResult Call( string hook, ReadOnlySpan<ScriptValue> arguments, ScriptValue previous, ScriptResultSpec expected, ScriptCallIdentity identity )
	{
		if ( expected.Kind is not (ScriptKind.Int or ScriptKind.Bool or ScriptKind.String) )
			throw new ArgumentException( "A hook result is an int, a bool or a string; tables cannot be hook results.", nameof( expected ) );
		if ( !program.TryGetFunction( hook, out var index ) )
			return new ScriptCallResult( previous, ScriptOutcome.RuntimeError, $"hook '{hook}' is not defined", 0, 0 );
		if ( arguments.Length != program.Functions[index].ParameterCount )
			throw new ArgumentException( $"Hook '{hook}' takes {program.Functions[index].ParameterCount} argument(s), not {arguments.Length}." );
		foreach ( var argument in arguments )
		{
			if ( argument.Kind == ScriptKind.Unit )
				throw new ArgumentException( "A hook argument cannot be a missing value." );
			if ( argument.Kind == ScriptKind.Table && !argument.AsTable.ReadOnly )
				throw new ArgumentException( "A table argument must be a read-only view." );
			if ( argument.Kind == ScriptKind.String && argument.AsString.Length > ScriptLimits.MaxStringLength )
				throw new ArgumentException( $"A string argument is at most {ScriptLimits.MaxStringLength} characters." );
		}

		if ( tickRemaining <= 0 )
			return new ScriptCallResult( previous, ScriptOutcome.BudgetExhausted, "the tick step budget is used up", 0, 0 );
		var budget = Math.Min( options.StepsPerCall, tickRemaining );

		var token = new object();
		var rngIdentity = new RngIdentity( identity.ParkSeed, modHash, ScriptRng.HashText( hook ), identity.GuestBirthId, identity.ParkTurn );

		ScriptValue value;
		try
		{
			value = interpreter.Execute( index, arguments, State, token, budget, rngIdentity );
		}
		catch ( ScriptBudgetException e )
		{
			return Fail( previous, ScriptOutcome.BudgetExhausted, e.Message, e.Line );
		}
		catch ( ScriptRuntimeException e )
		{
			return Fail( previous, ScriptOutcome.RuntimeError, e.Message, e.Line );
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			// A bug in the interpreter must not take the simulation down; the call counts as a failure.
			return Fail( previous, ScriptOutcome.RuntimeError, $"internal error: {e.GetType().Name}", 0 );
		}
		finally
		{
			tickRemaining -= interpreter.Steps;
		}

		var problem = CheckResult( value, expected );
		if ( problem != null )
			return Fail( previous, ScriptOutcome.InvalidResult, problem, 0 );

		// The state is committed only if the script touched it and changed a table. Validating it costs steps and memory like any other
		// work of the call, so it is charged to the same meters and can fail the call the same way.
		var root = interpreter.Root;
		if ( root != null && interpreter.Mutations > 0 )
		{
			var before = interpreter.Steps;
			try
			{
				ScriptState.Check( root, interpreter );
			}
			catch ( ScriptStateException e ) { return Fail( previous, ScriptOutcome.InvalidResult, $"state: {e.Message}", 0 ); }
			catch ( ScriptBudgetException e ) { return Fail( previous, ScriptOutcome.BudgetExhausted, e.Message, e.Line ); }
			catch ( ScriptRuntimeException e ) { return Fail( previous, ScriptOutcome.RuntimeError, e.Message, e.Line ); }
			finally
			{
				// The commit work is charged as it happens, so it is paid on every path, including a failed validation.
				tickRemaining -= interpreter.Steps - before;
			}
			State = root;
		}
		var steps = interpreter.Steps;
		interpreter.Reset();
		return new ScriptCallResult( value, ScriptOutcome.Ok, null, 0, steps );
	}

	private ScriptCallResult Fail( ScriptValue previous, ScriptOutcome outcome, string message, int line )
	{
		var steps = interpreter.Steps;
		interpreter.Reset();
		return new ScriptCallResult( previous, outcome, message, line, steps );
	}

	private static string? CheckResult( ScriptValue value, ScriptResultSpec expected )
	{
		if ( value.Kind == ScriptKind.Unit )
			return "the hook returned no value";
		if ( value.Kind != expected.Kind )
			return $"the hook must return {ScriptValue.KindName( expected.Kind )}, not {ScriptValue.KindName( value.Kind )}";
		if ( value.Kind == ScriptKind.String && value.AsString.Length > ScriptLimits.MaxStringLength )
			return $"the result is a string longer than {ScriptLimits.MaxStringLength} characters";
		if ( value.Kind == ScriptKind.Int && (value.AsInt < expected.Min || value.AsInt > expected.Max) )
			return $"the result {value.AsInt} is outside {expected.Min}..{expected.Max}";
		return null;
	}
}
