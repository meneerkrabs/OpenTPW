using OpenTPW.Online.Api;
using OpenTPW.UI.Original;

namespace OpenTPW.UI;

/// <summary>
/// A city's lobby: its ten most-voted parks stand as islands in the lobby, one per ISLANDCAMERAPOSITION slot by rank,
/// each as its theme's island (docs/ONLINE.md). The game draws the islands behind this screen and orbits the selected one.
/// </summary>
public sealed class CityLobbyScreen : UiScreen
{
	/// <summary>Islands in a city lobby: lobby.txt has ten ISLANDCAMERAPOSITION slots.</summary>
	public const int IslandCount = 10;

	public CityLobbyScreen( CityInfo city ) : base( "cityLobby" )
	{
		City = city;
		HidesBelow = true;
	}

	public CityInfo City { get; }
	/// <summary>The city's parks by votes; the first <see cref="IslandCount"/> are islands.</summary>
	public IReadOnlyList<ParkSummary> Parks { get; set; } = Array.Empty<ParkSummary>();
	public IReadOnlyList<ParkSummary> Islands => Parks.Take( IslandCount ).ToList();
	public int Selected { get; set; }
	public ParkSummary? SelectedPark => Selected >= 0 && Selected < Math.Min( IslandCount, Parks.Count ) ? Parks[Selected] : null;

	public void Step( int direction )
	{
		var count = Math.Min( IslandCount, Parks.Count );
		if ( count > 0 )
			Selected = ((Selected + direction) % count + count) % count;
	}
}
