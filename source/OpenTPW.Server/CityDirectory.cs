using System.Text.Json;
using OpenTPW.Online.Api;

namespace OpenTPW.Server;

/// <summary>
/// The cities of the online world: the server's list that players publish parks into and that the globe shows as flags
/// (docs/ONLINE.md). The operator may replace it with <c>cities.json</c> in the data folder (an array of
/// <see cref="CityDefinition"/>); it is re-read when that file changes.
/// </summary>
// [EXT:ONLINE-059] the original city list came from EA's city server and is lost: only "North Pole" (the Bullfrog
// park pin, in the binary), Hong Kong, Ulaanbaatar, Timbuktu and San Francisco (manuals, reviews) are known; the rest
// of the default list is OpenTPW's choice
public sealed class CityDirectory
{
	/// <summary>The original stores a city name in 16 and a description in 128 UTF-16 characters (CFSOnlineCity).</summary>
	public const int MaximumNameLength = 16;
	public const int MaximumDescriptionLength = 128;
	/// <summary>The city whose flag is the Bullfrog pin (the binary compares the city name with it).</summary>
	public const string BullfrogCity = "North Pole";

	public sealed record CityDefinition( int Id, string Name, string Description, float Latitude, float Longitude );

	public static readonly IReadOnlyList<CityDefinition> Defaults = new CityDefinition[]
	{
		new( 1, BullfrogCity, "Bullfrog Approved Parks", 90f, 0f ),
		new( 2, "Hong Kong", "Parks by the harbour", 22.3f, 114.2f ),
		new( 3, "Ulaanbaatar", "Parks on the steppe", 47.9f, 106.9f ),
		new( 4, "Timbuktu", "Parks at the edge of the Sahara", 16.8f, -3.0f ),
		new( 5, "San Francisco", "Parks by the Golden Gate", 37.8f, -122.4f ),
		new( 6, "London", "Parks on the Thames", 51.5f, -0.1f ),
		new( 7, "Amsterdam", "Parks along the canals", 52.4f, 4.9f ),
		new( 8, "Paris", "Parks on the Seine", 48.9f, 2.4f ),
		new( 9, "Berlin", "Parks on the Spree", 52.5f, 13.4f ),
		new( 10, "Copenhagen", "Parks on the Sound", 55.7f, 12.6f ),
		new( 11, "Stockholm", "Parks among the islands", 59.3f, 18.1f ),
		new( 12, "New York", "Parks in the big city", 40.7f, -74.0f ),
		new( 13, "Rio de Janeiro", "Parks under the sugarloaf", -22.9f, -43.2f ),
		new( 14, "Cape Town", "Parks below Table Mountain", -33.9f, 18.4f ),
		new( 15, "Sydney", "Parks down under", -33.9f, 151.2f ),
		new( 16, "Tokyo", "Parks in the east", 35.7f, 139.7f ),
	};

	private readonly string path;
	private readonly object gate = new();
	private DateTime stamp = DateTime.MinValue;
	private IReadOnlyList<CityDefinition> current = Defaults;

	public CityDirectory( string dataDirectory ) => path = Path.Combine( dataDirectory, "cities.json" );

	public IReadOnlyList<CityDefinition> Read()
	{
		var next = File.Exists( path ) ? File.GetLastWriteTimeUtc( path ) : DateTime.MinValue;
		lock ( gate )
		{
			if ( next != stamp )
			{
				current = next == DateTime.MinValue ? Defaults : Load();
				stamp = next;
			}
			return current;
		}
	}

	public CityDefinition? Find( int id ) => Read().FirstOrDefault( city => city.Id == id );

	private IReadOnlyList<CityDefinition> Load()
	{
		try
		{
			var cities = JsonSerializer.Deserialize<List<CityDefinition>>( File.ReadAllText( path ), new JsonSerializerOptions( JsonSerializerDefaults.Web ) ) ?? new();
			var valid = cities.Where( city => city.Id > 0 && !string.IsNullOrWhiteSpace( city.Name )
					&& city.Latitude is >= -90 and <= 90 && city.Longitude is >= -180 and <= 180 )
				.GroupBy( city => city.Id ).Select( group => group.First() )
				.Select( city => city with
				{
					Name = city.Name.Trim()[..Math.Min( city.Name.Trim().Length, MaximumNameLength )],
					Description = (city.Description ?? "")[..Math.Min( (city.Description ?? "").Length, MaximumDescriptionLength )],
				} ).ToList();
			return valid.Count > 0 ? valid : Defaults;
		}
		catch ( Exception exception ) when ( exception is IOException or JsonException )
		{
			return Defaults;
		}
	}
}
