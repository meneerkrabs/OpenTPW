namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-undo] A copy of the bank's balance a prediction pays with (C5-2's pool strokes, <see cref="PoolBuilder.PredictionMoney"/>; C5-3's track lines,
/// <see cref="TrackBuilder.PredictionMoney"/>): the bank's own two tests on it, the preview's (<see cref="ParkEconomy.CanPreviewLine"/>) and the commit's
/// (<see cref="ParkEconomy.CanSpendCell"/>), and each charge taken off it as the bank takes it (nothing while its withdrawals are off).
/// </summary>
internal sealed class CellPredictionMoney( long balance, long cellCost, bool withdrawals )
{
	public long Balance { get; private set; } = balance;
	public long CellCost => cellCost;
	public bool CanPreviewLine( long pending ) => ParkEconomy.PreviewsLine( pending, Balance );
	public bool CanSpendCell() => ParkEconomy.Affords( Balance, cellCost );

	/// <summary>One cell's charge, as the bank posts it (<see cref="ParkEconomy.TrySpendCell"/>).</summary>
	public void SpendCell()
	{
		if ( withdrawals )
			Balance -= cellCost;
	}

	/// <summary>The bank of <paramref name="economy"/> as it stands now, for cells of <paramref name="purchase"/>.</summary>
	public static CellPredictionMoney Of( ParkEconomy economy, CellPurchase purchase ) => new( economy.Balance, economy.CellCost( purchase ), economy.WithdrawalsEnabled );
}
