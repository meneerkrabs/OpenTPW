using System.Globalization;
using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

/// <summary>
/// The lobby's key panel (layout table Lobby, controls 0x1E0EA to 0x1E0EE): the key frame EA with its cost digit EB, which show whether the player's keys meet
/// the selected island's cost, and the counter EC with the golden key icon ED and its "%d x" text EE.
/// </summary>
// [BIN:TPW-EXE:0x4B8CA0 key panel create] the panel window exists once the lobby window does (0x4B8CA0, from 0x5E19B8); it starts with EA at frame 1 and EB at frame 5 (the grey art, 0x4B8D8B, 0x4B8DA2), EC hidden and ED at frame 3 (0x4B8D65, 0x4B8D7A), the stored key count (0x7CC4B8) and the effect handle (0x7CC4BC) zero, and refreshes at once (0x4B8DD5)
// [BIN:TPW-EXE:0x4B9340 key panel refresh] the full refresh: when the selected island has a record (0x5B09C0 by the island's name, valid when its global.sam loads, 0x5B11C0) EA, EB and EC are shown and, with the ledger's keys (0x5AF680) at or above the record's cost (0x5B11A0, global.sam Keys.CostToEnter), EA takes frame 0, EB frame cost - 1 and effect 0x61 starts (once); below it EA takes frame 1, EB frame cost + 4 and the effect ends; then the counter phase hides EC for no keys and otherwise shows EC and EA and writes "%d x" into EE; the keys are stored (0x7CC4B8) at the end
// [BIN:TPW-EXE:0x4B90F0 key panel selection] a change of the selected island (0x5E18BD, 0x5E1B31, 0x5E1F35, 0x5E1F95) redoes only the affordability part for the new island's record, with the stored count and not the ledger's, and sets the island's text (0x1E0EF)
// [BIN:TPW-EXE:0x4B8B70 key panel window] message 0x11 with a nonzero parameter (the panel shown) refreshes (0x4B8C64); hiding or destroying the panel (0x11 with 0, 0x14) forgets the window (0x7CC4B4) and ends effect 0x61 (0x4B8EE0(0, 0))
public sealed partial class FrontEndMenu
{
	private UiModelImage? keyFrameImage, keyCostImage, goldenKeyIcon;
	private UiLabel? goldenKeyCount;
	private int keyFrame = 1, keyCostFrame = 5;
	private string keyCountText = "";
	private bool keyPanelOpen, keysKnown;
	// the key count the last refresh stored (0x7CC4B8) and the handle of effect 0x61 (0x7CC4BC)
	private int storedKeys;
	private object? keyEffect;
	private IAdvisorParticlePool? keyEffects;
	private readonly Dictionary<string, int?> entryCosts = new( StringComparer.Ordinal );

	private void AddKeyPanel( UiScreen screen )
	{
		// [EXT:no-player-profiles] OpenTPW has no player profiles, so the count is the one player's saved Full Simulation ledger, read when the menu is built (returning from a park rebuilds it);
		// the original's Instant Action branch of the refresh (EA, EC and the arrows off, game type 0xFB3B7C == 2) belongs to a per-player mode that OpenTPW's lobby does not have: it asks the mode on every park entry
		// [BIN:TPW-EXE:0x757F60 key panel layout] EA (554,170)-(990,437) s_key and its child EB (1045,191)-(1405,416) s_X1 sit in the top middle, EC (1688,68)-(1963,186) with ED gkey (1853,76) and EE (1688,82) at the top right
		var table = OriginalLayoutTables.Lobby;
		keyFrameImage = screen.Add( new UiModelImage { Id = "lobbyKeyPanel", Model = table.Control( 0x1E0EA ).Model!, Frame = () => keyFrame,
			Bounds = table.Control( 0x1E0EA ).Rect, Anchor = UiAnchor.Top } );
		keyCostImage = screen.Add( new UiModelImage { Id = "lobbyKeyCost", Model = table.Control( 0x1E0EB ).Model!, Frame = () => keyCostFrame,
			Bounds = table.Control( 0x1E0EB ).Rect, Anchor = UiAnchor.Top } );
		goldenKeyIcon = screen.Add( new UiModelImage { Id = "lobbyGoldenKey", Model = table.Control( 0x1E0ED ).Model!, Frame = () => 3,
			Bounds = table.Control( 0x1E0ED ).Rect, Anchor = UiAnchor.TopRight, Visible = false } );
		goldenKeyCount = screen.Add( new UiLabel { Id = "lobbyGoldenKeys", Text = () => keyCountText,
			Font = fonts => fonts.Balance, Align = UiAlign.Right, Fit = true,
			Bounds = table.Control( 0x1E0EE ).Rect, Anchor = UiAnchor.TopRight, Visible = false } );
		keyPanelOpen = true;
		// the window procedure's message 0x14 (and 0x11 with 0): the panel is forgotten and effect 0x61 ends with it
		screen.Removed = CloseKeyPanel;
		RefreshKeyPanel( actions.PlayerKeySnapshot );
	}

	/// <summary>
	/// The panel's full refresh with the player's current key count (0x4B9340): at its creation, and where the original's advisor timer refreshes it (response 393's timer, a line OpenTPW does not say: ADVISOR-025).
	/// A null count (no usable Full Simulation progress) shows no panel and no effect.
	/// </summary>
	internal void RefreshKeyPanel( int? keys )
	{
		// [BIN:TPW-EXE:0x4B935D no panel] the refresh returns at once when the panel window does not exist
		if ( !keyPanelOpen )
			return;
		keysKnown = keys.HasValue;
		if ( keys is not { } count )
		{
			keyFrameImage!.Visible = keyCostImage!.Visible = goldenKeyIcon!.Visible = goldenKeyCount!.Visible = false;
			EndKeyEffect();
			return;
		}
		// the layout shows the frame (style 0x3, stored by 0x65BD2F); only the Instant Action branch hides it, which this lobby has no mode for
		keyFrameImage!.Visible = keyCostImage!.Visible = true;
		if ( EntryCost( Selected ) is { } cost )
			ApplyKeyAffordability( count, cost );
		// [BIN:TPW-EXE:0x4B96BE counter phase] no keys hides EC; otherwise EC and EA are shown and EE reads "%d x" (0x758194)
		var counter = count > 0;
		goldenKeyIcon!.Visible = goldenKeyCount!.Visible = counter;
		if ( counter )
			keyCountText = count.ToString( CultureInfo.InvariantCulture ) + " x";
		// [BIN:TPW-EXE:0x4B9816 stored count] the keys the next selection change compares with
		storedKeys = count;
	}

	/// <summary>The selected island changed (0x4B90F0): the affordability part again for the new island, with the stored key count.</summary>
	private void KeyPanelSelectionChanged()
	{
		if ( !keyPanelOpen || !keysKnown )
			return;
		if ( EntryCost( Selected ) is { } cost )
			ApplyKeyAffordability( storedKeys, cost );
	}

	private void ApplyKeyAffordability( int keys, int cost )
	{
		if ( keys >= cost )
		{
			// [BIN:TPW-EXE:0x4B958F affordable] EA frame 0 (gold), EB frame cost - 1 (the gold digit), effect 0x61 while it is not already running
			keyFrame = 0;
			keyCostFrame = cost - 1;
			StartKeyEffect();
		}
		else
		{
			// [BIN:TPW-EXE:0x4B963B unaffordable] EA frame 1 (grey), EB frame cost + 4 (the grey digit), the effect ended
			keyFrame = 1;
			keyCostFrame = cost + 4;
			EndKeyEffect();
		}
	}

	private int? EntryCost( LobbyIslandInfo island )
	{
		if ( !entryCosts.TryGetValue( island.Level, out var cost ) )
			entryCosts[island.Level] = cost = actions.ThemeEntryCost( island.Level );
		return cost;
	}

	private void StartKeyEffect()
	{
		if ( keyEffect != null )
			return;
		keyEffects ??= actions.KeyPanelEffects();
		keyEffect = keyEffects?.Create( LobbyKeyPanelEffect.EffectId, LobbyKeyPanelEffect.Position() );
	}

	private void EndKeyEffect()
	{
		if ( keyEffect != null && keyEffects != null )
			keyEffects.Kill( keyEffect );
		keyEffect = null;
	}

	private void CloseKeyPanel()
	{
		keyPanelOpen = false;
		EndKeyEffect();
	}
}
