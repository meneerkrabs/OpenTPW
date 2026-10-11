using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	public void CycleCameraView()
	{
		if ( Camera.Mode is ParkCameraMode camera )
			camera.CycleView();
	}

	/// <summary>A live park summary using existing simulation values; a read-only visit shows the published statistics.</summary>
	// [EXT:park-summary] functional host summary; original information-screen layout remains unqualified
	public void OpenParkInfoWindow()
	{
		if ( Stack.Screens.Any( screen => screen.Name == "parkInfo" ) )
			return;
		var screen = new UiScreen( "parkInfo" );
		var window = UiDialogs.CenteredWindow( 1400, 1100 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings[UIStrings.ParkInformation] );
		// A read-only visit never runs the owner's economy: the window shows the statistics the owner published
		// (the original's visitor saw the park as saved; docs/reverse/ONLINE-VISIT.md), never simulated values.
		// [EXT:ONLINE-074] the published park's figures are shown in the park information window of a visit.
		var published = level.PublishedParkStats;
		var rows = published is { } stats
			? new (UIStrings Label, Func<string> Value)[]
			{
				(UIStrings.PeopleInPark, () => stats.Visitors.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.BankBalance, () => FormatMoney( stats.Balance )),
				(UIStrings.ParkValue, () => stats.Value.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.ParkRating, () => stats.Rating.ToString( System.Globalization.CultureInfo.InvariantCulture )),
				(UIStrings.AllStaff, () => strings.Extra( OpenTpwText.NotAvailable )),
			}
			: new (UIStrings Label, Func<string> Value)[]
			{
				(UIStrings.PeopleInPark, () => (level.Guests?.GetStatistics().InPark ?? 0).ToString()),
				(UIStrings.BankBalance, () => MoneyText),
				(UIStrings.ParkValue, () => (Status.EconomySource as ParkEconomy)?.ParkValue.ToString() ?? strings.Extra( OpenTpwText.NotAvailable )),
				(UIStrings.ParkRating, () => (Status.EconomySource as ParkEconomy)?.ParkRating.ToString() ?? strings.Extra( OpenTpwText.NotAvailable )),
				(UIStrings.AllStaff, () => (Status.EconomySource as ParkEconomy)?.Staff.Members.Count.ToString() ?? "0"),
			};
		for ( var index = 0; index < rows.Length; index++ )
		{
			var row = rows[index];
			screen.Add( new UiLabel { Id = $"label{index}", Text = () => strings[row.Label],
				Bounds = new UiRect( window.X + 100, window.Y + 230 + index * 120, 650, 80 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"value{index}", Text = row.Value,
				Bounds = new UiRect( window.X + 850, window.Y + 230 + index * 120, 300, 80 ), Anchor = UiAnchor.Center } );
		}
		if ( published is { } publishedStats )
		{
			// The published in-game date and the read-only note close the visit's window.
			screen.Add( new UiLabel { Id = "published", Text = () => OnlineStrings.Get( OnlineLabel.PublishedOn ) + publishedStats.Date,
				Font = fonts => fonts.Label, Bounds = new UiRect( window.X + 100, window.Y + 850, 900, 80 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = "note", Text = () => OnlineStrings.Get( OnlineLabel.PublishedStatsNote ),
				Font = fonts => fonts.Label, Bounds = new UiRect( window.X + 100, window.Y + 940, 1100, 80 ), Anchor = UiAnchor.Center } );
		}
		screen.Add( new UiButton { Id = "close", Model = "b_okay", Clicked = Stack.Pop,
			Bounds = new UiRect( window.Right - 250, window.Bottom - 200, 120, 120 ), Anchor = UiAnchor.Center } );
		screen.Back = Stack.Pop;
		Stack.Push( screen );
	}
}
