using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	public void CycleCameraView()
	{
		if ( Camera.Mode is ParkCameraMode camera )
			camera.CycleView();
	}

	/// <summary>A live park summary using existing simulation values.</summary>
	// [EXT:park-summary] functional host summary; original information-screen layout remains unqualified
	public void OpenParkInfoWindow()
	{
		if ( Stack.Screens.Any( screen => screen.Name == "parkInfo" ) )
			return;
		var screen = new UiScreen( "parkInfo" );
		var window = UiDialogs.CenteredWindow( 1400, 1100 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings[UIStrings.ParkInformation] );
		var rows = new (UIStrings Label, Func<string> Value)[]
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
		screen.Add( new UiButton { Id = "close", Model = "b_okay", Clicked = Stack.Pop,
			Bounds = new UiRect( window.Right - 250, window.Bottom - 200, 120, 120 ), Anchor = UiAnchor.Center } );
		screen.Back = Stack.Pop;
		Stack.Push( screen );
	}
}
