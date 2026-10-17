using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenTPW;

/// <summary>
/// The acceptance thresholds of docs/SMARTER-GUESTS.md in code. The noise unit of a metric is its standard deviation over the distinct seeds, floored at 1% of its mean and at an absolute
/// floor for its kind (so a metric with no spread cannot make noise significant); the difference of two means of n seeds each has a standard error of unit · √(2/n), and a change counts
/// beyond <c>k</c> of them (3 by default: with about 55 checks per comparison, 2 would flag about 2 by chance alone).
/// <c>--guest-compare baseline.json candidate.json</c> prints every metric's change in standard errors and exits 1 when a metric regresses or a named target does not improve.
/// </summary>
public static class GuestReportCompare
{
	/// <summary>The wanted direction of each headline metric: 1 up, -1 down, 0 none (reported without a verdict).</summary>
	public static int Direction( string metric ) => metric switch
	{
		"queue.boarded.rate" or "happiness.guestTime" or "happiness.survivors" or "revenue.perAdmittedGuest" => 1,
		"queue.rejected.rate" or "queue.wait.p50" or "queue.wait.p90" or "queue.wait.p99" or "queue.wait.p90.censored" or "queue.wait.p99.censored"
			or "departures.perAdmission" or "departures.happinessZero.perAdmission" or "use.giniRides" or "use.giniRidesPerCapacity" or "guests.stuck" => -1,
		_ => 0
	};

	/// <summary>The smallest noise unit a metric of this kind can have: its own scale's resolution.</summary>
	public static double AbsoluteFloor( string metric ) =>
		metric.Contains( "rate" ) || metric.Contains( "perAdmission" ) || metric.Contains( "gini" ) ? 0.002
		: metric.Contains( "wait" ) ? 1
		: metric.StartsWith( "happiness." ) ? 0.1
		: 0.5;

	public static double NoiseUnit( string metric, double mean, double standardDeviation ) =>
		Math.Max( Math.Max( double.IsFinite( standardDeviation ) ? standardDeviation : 0, 0.01 * Math.Abs( mean ) ), AbsoluteFloor( metric ) );

	/// <summary>k standard errors of the difference of two means of <paramref name="seeds"/> seeds each (NaN without seeds).</summary>
	public static double Band( string metric, double mean, double standardDeviation, int seeds, double k ) =>
		seeds <= 0 || !double.IsFinite( mean ) ? double.NaN : k * NoiseUnit( metric, mean, standardDeviation ) * Math.Sqrt( 2.0 / seeds );

	public sealed record Row( string Scenario, string Metric, double Baseline, double Candidate, double StandardErrors, string Verdict );

	public sealed record Result( IReadOnlyList<Row> Rows, bool Failed, string Notes );

	/// <summary>Compares two reports scenario by scenario; <paramref name="targets"/> are the metrics the candidate must improve (beyond k standard errors) on every scenario where they are defined.</summary>
	public static Result Compare( JsonObject baseline, JsonObject candidate, IReadOnlyCollection<string> targets, double k = 3 )
	{
		var rows = new List<Row>();
		var failed = false;
		var notes = new StringBuilder();
		// a report made with extensions on (--guest-extension) says so; naming both sides keeps an on/off comparison readable
		static string Extensions( JsonObject report ) => report["extensions"] is JsonArray { Count: > 0 } list ? string.Join( ", ", list.Select( item => (string)item! ) ) : "none";
		if ( Extensions( baseline ) != Extensions( candidate ) )
			notes.AppendLine( $"extensions: baseline {Extensions( baseline )}, candidate {Extensions( candidate )}" );
		static string Mods( JsonObject report ) => report["mods"] is JsonArray { Count: > 0 } list ? string.Join( ", ", list.Select( item => (string)item! ) ) : "none";
		if ( Mods( baseline ) != Mods( candidate ) )
			notes.AppendLine( $"mods: baseline {Mods( baseline )}, candidate {Mods( candidate )}" );
		var candidates = candidate["scenarios"]!.AsArray().ToDictionary( item => (string)item!["name"]!, item => item!.AsObject() );
		foreach ( var scenario in baseline["scenarios"]!.AsArray() )
		{
			var name = (string)scenario!["name"]!;
			if ( !candidates.TryGetValue( name, out var other ) )
			{
				notes.AppendLine( $"{name}: missing from the candidate report" );
				failed = true;
				continue;
			}
			var spread = other["spread"]!.AsArray().ToDictionary( item => (string)item!["metric"]!, item => item!.AsObject() );
			foreach ( var a in scenario["spread"]!.AsArray() )
			{
				var metric = (string)a!["metric"]!;
				if ( !spread.TryGetValue( metric, out var b ) || a["mean"] is null || b["mean"] is null )
					continue;
				double meanA = (double)a["mean"]!, meanB = (double)b["mean"]!, sdA = (double?)a["sd"] ?? 0, sdB = (double?)b["sd"] ?? 0;
				int nA = (int)a["n"]!, nB = (int)b["n"]!;
				if ( nA == 0 || nB == 0 )
					continue;
				var ua = NoiseUnit( metric, meanA, sdA );
				var ub = NoiseUnit( metric, meanB, sdB );
				var error = Math.Sqrt( ua * ua / nA + ub * ub / nB );
				var z = (meanB - meanA) / error;
				var direction = Direction( metric );
				var undefined = meanA == 0 && meanB == 0 && sdA == 0 && sdB == 0;
				var verdict = undefined ? "undefined"
					: direction == 0 ? Math.Abs( z ) > k ? "changed" : "same"
					: z * direction > k ? "improved" : z * direction < -k ? "regressed" : "same";
				if ( verdict == "regressed" )
					failed = true;
				if ( metric == "queue.superseded" && meanB > 0 )
				{
					verdict = "BROKEN";
					failed = true;
				}
				if ( targets.Contains( metric ) && !undefined && verdict != "improved" )
				{
					notes.AppendLine( $"{name}: target {metric} did not improve beyond {k} standard errors ({z:0.0})" );
					failed = true;
				}
				rows.Add( new Row( name, metric, meanA, meanB, z, verdict ) );
			}
		}
		if ( baseline["performance"] is JsonObject pa && candidate["performance"] is JsonObject pb && (double?)pa["observerOffMicrosecondsPerTick"] is { } cpuA and > 0 && (double?)pb["observerOffMicrosecondsPerTick"] is { } cpuB )
			notes.AppendLine( $"cpu per tick: {cpuB / cpuA:0.00} of the baseline (the limit is 1.10; timings are noisy, compare on one quiet machine)" );
		return new Result( rows, failed, notes.ToString() );
	}

	public static string ToTable( Result result, double k )
	{
		var text = new StringBuilder();
		text.AppendLine( $"{"scenario",-16} {"metric",-40} {"baseline",12} {"candidate",12} {"SE",8} verdict (beyond {k:0.#} standard errors)" );
		foreach ( var row in result.Rows )
			text.AppendLine( $"{row.Scenario,-16} {row.Metric,-40} {row.Baseline.ToString( "0.###", CultureInfo.InvariantCulture ),12} {row.Candidate.ToString( "0.###", CultureInfo.InvariantCulture ),12} {row.StandardErrors.ToString( "0.0", CultureInfo.InvariantCulture ),8} {row.Verdict}" );
		text.Append( result.Notes );
		text.AppendLine( result.Failed ? "FAILED: a regression, a target that did not improve, or a broken count" : "ok: no regression beyond the band, every target improved" );
		return text.ToString();
	}

	/// <summary><c>--guest-compare baseline.json candidate.json [--guest-targets metric,metric] [--guest-compare-k 3]</c>.</summary>
	public static int RunCommand( string[] args )
	{
		var index = Array.IndexOf( args, "--guest-compare" );
		if ( index + 2 >= args.Length || args[index + 1].StartsWith( "--" ) || args[index + 2].StartsWith( "--" ) )
			throw new ArgumentException( "--guest-compare requires a baseline and a candidate report path." );
		var targets = Option( args, "--guest-targets" )?.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ?? Array.Empty<string>();
		var k = Option( args, "--guest-compare-k" ) is { } text ? double.Parse( text, CultureInfo.InvariantCulture ) : 3;
		var result = Compare( JsonNode.Parse( File.ReadAllText( args[index + 1] ) )!.AsObject(), JsonNode.Parse( File.ReadAllText( args[index + 2] ) )!.AsObject(), targets, k );
		Console.Write( ToTable( result, k ) );
		return result.Failed ? 1 : 0;
	}

	private static string? Option( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith( "--" ) ? args[index + 1] : null;
	}
}
