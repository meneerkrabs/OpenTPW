namespace OpenTPW.Hud;

public partial class ParkHud
{
	private (ParkEconomy Economy, ParkCommands Commands)? detachedEdits;

	/// <summary>
	/// [EXT:online-coop-commands] The funnel the HUD's edits of <paramref name="economy"/> go through (docs/COOP.md): the level's own when the economy is its park's, as in every game;
	/// a funnel over the economy alone when a HUD shows an economy that is not its level's park (a test's status, a park the level has not taken yet).
	/// </summary>
	internal ParkCommands EditsOf( ParkEconomy economy )
	{
		ArgumentNullException.ThrowIfNull( economy );
		if ( level is { } live && ReferenceEquals( live.Park?.Economy, economy ) )
			return live.Commands;
		if ( detachedEdits is not { } detached || !ReferenceEquals( detached.Economy, economy ) )
			detachedEdits = detached = (economy, ParkEconomyCommands.ForEconomy( economy ));
		return detached.Commands;
	}
}
