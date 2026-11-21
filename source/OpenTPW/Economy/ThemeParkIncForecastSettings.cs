using System.Text;

namespace OpenTPW;

/// <summary>
/// The original advisor's forecast controls from <c>Advisor/Advisor.sam</c>. All four weather messages share
/// descriptor word 8, which is the message group the native repeat lookup and the variant switch read, so one
/// group's keys serve them all. The remaining repeat keys are read from that group, and the file's other keys
/// (scan messages, crowds, staff) are not interpreted here.
/// </summary>
// [BIN:TPI-EXE:0x71B7E0 descriptor scan] word 8 of each forecast row is 0: the group index.
// [BIN:TPI-EXE:0x718B40 repeat lookup] the interval comes from that group's MinTimeSameMessage keys, which the
// original reads into the advisor object at +0x2C; the retail group 0 has 120/300/600/1800.
internal sealed record ThemeParkIncForecastSettings( int MinimumScore, ThemeParkIncForecastTiming Timing )
{
	public const string Path = "/Advisor/Advisor.sam";
	public const int Group = 0;

	/// <summary>The file's controls, or null when no game data is mounted or the file is absent (the native constructor has no advisor then either).</summary>
	public static ThemeParkIncForecastSettings? Load()
	{
		if ( FileSystem?.FileExists( Path ) != true )
			return null;
		var bytes = FileSystem.ReadAllBytes( Path );
		if ( bytes.Length > AdvisorBalance.MaximumBytes )
			throw new InvalidDataException( $"{Path} is {bytes.Length} bytes; advisor settings are limited to {AdvisorBalance.MaximumBytes}." );
		return Parse( Encoding.Latin1.GetString( bytes ), Path );
	}

	internal static ThemeParkIncForecastSettings Parse( string text, string source )
	{
		var settings = new SamSettings( new[] { SamDocument.Parse( text, source ) } );
		var prefix = $"MessageGroups[{Group}].";
		uint Interval( string key ) => checked((uint)settings.GetInt( prefix + key ));
		return new ThemeParkIncForecastSettings(
			settings.GetInt( "GeneralAdvisor.MinScoreForConsideration" ),
			new ThemeParkIncForecastTiming( settings.GetInt( "GeneralAdvisor.MinScoreForConsideration" ),
				Interval( "MinTimeSameMessage1" ), Interval( "MinTimeSameMessage2" ),
				Interval( "MinTimeSameMessage3" ), Interval( "MinTimeSameMessageX" ) ) );
	}
}
