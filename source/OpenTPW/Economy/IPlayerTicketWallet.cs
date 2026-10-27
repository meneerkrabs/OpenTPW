namespace OpenTPW;

/// <summary>
/// The player-wide ticket account a park spends from while it is part of its player's progress (the original's ticket object, tp.exe <c>0x409410</c>): the earned count less the spent count
/// (<c>0x5AF5D0</c>), the set of items already bought with tickets (<c>0x5AF780</c>) and the purchase that adds the cost to the spent count (<c>0x5AF740</c>). A park without a wallet
/// (the sandbox, Instant Action, a shared co-op park, a visit) counts its own tickets, as before.
/// </summary>
internal interface IPlayerTicketWallet
{
	/// <summary>Whether the park spends from the player's account now; false while the park is outside its player's progress (a co-op session).</summary>
	bool Active { get; }
	/// <summary>The player's earned tickets less the spent count.</summary>
	int Available { get; }
	/// <summary>Whether the player has bought the item with tickets already; later copies are paid in cash.</summary>
	bool Owns( int infoId );
	/// <summary>Adds <paramref name="cost"/> to the player's spent count and records the item as bought; false (nothing changes) when it is bought already or the cost exceeds <see cref="Available"/>.</summary>
	bool TrySpend( int infoId, int cost );
}
