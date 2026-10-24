using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:winter-tuning] The winter shops' prices in a real park (docs/WINTER.md, "Price"): the level gate's park of a theme built from the original level and its balance files (needs <c>OPENTPW_GAME_PATH</c>), with the
/// extension's shipped kiosk and glühwein stall placed beside the gate's own shop, so the price test reads the shops' real terms (the shipped .sam merged over the theme's <c>Shops.sam</c>, the economy's live price) and the
/// guests carry the level's own starting money and moods. Counts sales and price refusals of the three shops; nothing here is pinned.
/// </summary>
[TestClass]
[DoNotParallelize]
public class WinterPriceMeasureTests
{
	private const string ModId = "opentpw.winter";
	private BaseFileSystem? previousFileSystem;
	private IReadOnlyList<CatalogContentRoot>? previousRoots;
	private IDisposable? extensions;

	[TestInitialize]
	public void Initialize()
	{
		previousFileSystem = FileSystem;
		Log ??= new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original level objects and balance files." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		previousRoots = ObjectCatalog.ContentRoots;
		var folder = WinterContentTests.Folder;
		var objects = ModObjectsFile.Load( Path.Combine( folder, "objects.json" ), Array.Empty<string>() );
		ObjectCatalog.SetContentRoots( new[] { new CatalogContentRoot( ContentRoot.Mod( ModId ), folder, objects.Objects ) } );
		extensions = OpenTpwExtensions.Use( ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { [ModId] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		extensions?.Dispose();
		if ( previousRoots != null )
			ObjectCatalog.SetContentRoots( previousRoots );
		FileSystem = previousFileSystem!;
	}

	internal const string Kiosk = "winter kiosk", Stall = "gluehwein stall", Shop = "shop";

	/// <summary>Sales, price refusals and the price test's words of the gate's shop, the kiosk and the stall over one run.</summary>
	internal sealed record PriceRun( string Level, ulong Seed, int Month, int Weather, long Admitted, IReadOnlyDictionary<string, int> Sales, IReadOnlyDictionary<string, int> Refusals, IReadOnlyDictionary<string, PriceTerms> Terms );

	/// <summary>One level park of <paramref name="level"/> with the winter shops placed, for <paramref name="days"/> park days in a fixed month and weather.</summary>
	internal static PriceRun Measure( string level, ulong seed, int month, int weather, int days )
	{
		var refusals = new WinterTuningMeasureTests.RefusalCounter();
		var spent = new Dictionary<int, int>();
		long admitted = 0;
		var terms = new Dictionary<int, PriceTerms>();
		var gate = new M3GateOptions
		{
			Level = level, Seed = new WorldSeed( seed ).GuestStream, WorldSeed = new WorldSeed( seed ), DeterminismProbe = false, SweptTurns = true, ArrivalTimeBetween = 20, GuestObserver = refusals,
			ExtraObjects = new (string, Func<ObjectCatalogEntry, bool>)[]
			{
				(Kiosk, entry => entry.ModId == ModId && entry.Settings.GetInt( "UsageInfo.AppearanceEffect" ) == WinterRules.KioskAppearanceEffect),
				(Stall, entry => entry.ModId == ModId && entry.Settings.GetInt( "UsageInfo.SpecialIngredient" ) == 3)
			},
			GuestSetup = guests =>
			{
				guests.WinterMonth = () => month;
				guests.WinterWeatherState = () => weather;
				guests.UmbrellaWeatherState = () => weather;
				guests.MoneySpent += ( _, _, attraction ) => spent[attraction] = spent.GetValueOrDefault( attraction ) + 1;
			},
			BeforeTeardown = guests =>
			{
				admitted = guests.Admissions;
				foreach ( var attraction in guests.Attractions )
					terms[attraction.AttractionId] = GuestSimulation.PriceTermsOf( attraction );
			}
		};
		var run = M3GateRun.Execute( gate, GuestReport.TicksForDays( days ) );
		var roles = new[] { Shop, Kiosk, Stall };
		var ids = roles.ToDictionary( role => role, role => run.AttractionIdOf( role ) ?? throw new AssertFailedException( $"{level}: no {role} placed" ) );
		return new PriceRun( level, seed, month, weather, admitted, roles.ToDictionary( role => role, role => spent.GetValueOrDefault( ids[role] ) ),
			roles.ToDictionary( role => role, role => refusals.Price.GetValueOrDefault( ids[role] ) ), roles.ToDictionary( role => role, role => terms[ids[role]] ) );
	}

	[TestMethod]
	public void EveryThemesWinterShopsInheritTheShopsSamPerTypeWordTheMeasurementHarnessAssumes()
	{
		var shipped = WinterTuningMeasureTests.ShippedPrices();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			var defaults = ObjectSettingsFile.Load( FileSystem, $"/levels/{theme}/shops/Shops.sam" );
			Assert.AreEqual( WinterTuningMeasureTests.ShopsSamRipOffOk.ToString( CultureInfo.InvariantCulture ), defaults.Values["UsageInfo.RipOffOK"], $"{theme}: the game's Shops.sam" );
			var winter = catalog.Entries.Where( entry => entry.ModId == ModId ).OrderBy( entry => entry.InfoId ).ToArray();
			Assert.AreEqual( 2, winter.Length, theme );
			for ( var index = 0; index < 2; index++ )
			{
				var bridge = OriginalObjectRuntime.CreateVisitorBridge( winter[index], 1 );
				var terms = GuestSimulation.PriceTermsOf( bridge );
				var expected = shipped[index];
				Assert.AreEqual( (expected.Price, expected.InitCostOfGoods, expected.ThirstEffect, expected.HungerEffect, expected.VomitEffect, expected.HappinessEffect, expected.RipOffOk),
					(terms.Price, terms.InitCostOfGoods, terms.ThirstEffect, terms.HungerEffect, terms.VomitEffect, terms.HappinessEffect, terms.TypeWord), $"{theme} {winter[index].DisplayName}: the harness's terms are the game's" );
			}
		}
	}

	private static readonly ulong[] Seeds = { 1, 2, 3 };

	[TestMethod]
	public void InTheRealLevelParksTheWinterShopsSellAtTheirShippedPricesAsTheGamesOwnShopDoes()
	{
		var report = new StringBuilder();
		report.AppendLine( "Level parks with the winter shops, 17 park days, seeds 1 to 3 summed: month, weather, admitted, then sales / price refusals of the gate's shop, the kiosk and the stall (price in brackets)" );
		var sums = new Dictionary<(string Level, int Month, int Weather), (long Admitted, Dictionary<string, int> Sales, Dictionary<string, int> Refusals)>();
		foreach ( var (level, month, weather) in new[] { ("jungle", 1, UmbrellaRules.WeatherSnow), ("jungle", 1, UmbrellaRules.WeatherNone), ("jungle", 4, UmbrellaRules.WeatherNone), ("hallow", 1, UmbrellaRules.WeatherSnow), ("space", 1, UmbrellaRules.WeatherSnow), ("fantasy", 1, UmbrellaRules.WeatherSnow) } )
		{
			var runs = Seeds.Select( seed => Measure( level, seed, month, weather, 17 ) ).ToList();
			var sales = new[] { Shop, Kiosk, Stall }.ToDictionary( role => role, role => runs.Sum( run => run.Sales[role] ) );
			var refused = new[] { Shop, Kiosk, Stall }.ToDictionary( role => role, role => runs.Sum( run => run.Refusals[role] ) );
			sums[(level, month, weather)] = (runs.Sum( run => run.Admitted ), sales, refused);
			report.AppendLine( FormattableString.Invariant( $"{level,-7} month {month,2} weather {weather}: admitted {runs.Sum( run => run.Admitted ),4}, " ) + string.Join( ", ",
				new[] { Shop, Kiosk, Stall }.Select( role => FormattableString.Invariant( $"{role} ({runs[0].Terms[role].Price}) {sales[role],4} / {refused[role],4}" ) ) ) );
		}
		Write( report.ToString() );
		foreach ( var level in ObjectCatalog.Themes )
		{
			var snowy = sums[(level, 1, UmbrellaRules.WeatherSnow)];
			Assert.IsTrue( snowy.Admitted > 0, $"{level}: guests arrive" );
			foreach ( var role in new[] { Kiosk, Stall } )
			{
				var (sold, refused) = (snowy.Sales[role], snowy.Refusals[role]);
				Assert.IsTrue( sold > 0, $"{level}: the {role} sells in a snowy January ({sold} sold, {refused} refused on price)" );
				// nearly every guest who reaches the counter pays, as at the game's own shop at its default price: at most one refusal in ten sales
				Assert.IsTrue( refused * 10 <= sold, $"{level}: the {role}: {sold} sold against {refused} refused on price" );
			}
		}
	}

	private static void Write( string text )
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_WINTER_MEASURE" ) is { Length: > 0 } path )
			File.AppendAllText( path, text + "\n" );
		Console.WriteLine( text );
	}
}
