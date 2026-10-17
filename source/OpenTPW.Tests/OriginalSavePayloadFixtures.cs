using System;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// A synthetic, full-size original-format payload model for the writer tests that run without the game's or the private saves' bytes: every block has the shape its loader
/// reads (the World's 0x4000 cells, the 29 message lists, the RSSE header, ...) and every value is made up, so it proves the writer's structure and nothing about the original's data.
/// </summary>
internal static class OriginalSavePayloadFixtures
{
	public const uint BankId = 1;

	/// <summary>The bank thing of <paramref name="settings"/>: its loan offers, the given balance, nothing bought.</summary>
	public static SaveBank Bank( BalanceSettings settings, int balance = 50_000 )
	{
		var loans = Enumerable.Range( 0, SaveBank.LoanRecords ).Select( index =>
		{
			var offer = settings.Loans[index];
			return new SaveLoanRecord( 0, index, false, (int)offer.Amount, offer.AprPercent, offer.Months, (int)LoanMath.MonthlyRepayment( offer.Amount, offer.AprPercent, offer.Months ), false, 0, offer.LenderNameIndex );
		} ).ToArray();
		return new SaveBank( (int)BankId, new SaveThingBase( 128, 128, 0, 0 ), new SaveBankRecord( 0, 20, balance, 0, true, balance, 0, 0 ), loans );
	}

	public static OriginalSavePayload Blank( BalanceSettings settings )
	{
		var payload = new OriginalSavePayload();
		payload.ActionRecording = new SaveActionRecordingData { Published = false, Recording = new byte[] { 1, 2, 3 } };
		var world = payload.World;
		world.Variables = new SaveWorldVariables( 2, 0, 0, 0, (ushort)BankId, 0, 0, 0, 0, 1, 0, 0, 0, 0xABCD1234, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		world.Controls = new SaveObjectControlTable { Count = 2, PreviousSearchKey = 1100 };
		SetControl( world.Controls, 0, 1100 );
		SetControl( world.Controls, 1, 1101 );
		world.StaffPool = new SaveStaffPool( 0, Enumerable.Repeat( new SaveStaffCandidate( 0, 0, 0, 0, false, false, 0, 0 ), SaveStaffPool.Slots ).ToArray(),
			new uint[SaveStaffPool.Roles], new bool[SaveStaffPool.Roles], 0, false );
		world.Calendar = new SaveCalendar( 0, SaveCalendar.InitialFunnyTimeStart, 1234, 0, SaveCalendar.InitialDay, SaveCalendar.InitialRate );
		world.Arrivals = new byte[SaveWorld.ArrivalBytes];
		world.Cells = Enumerable.Range( 0, SaveBlockWidths.WorldCells ).Select( _ =>
		{
			var cell = new byte[SaveCell.RecordBytes];
			cell[0] = 3;
			cell[^2] = cell[^1] = 0xFF;
			return cell;
		} ).ToArray();
		world.Things.Add( new SaveThingEntry { Id = BankId, Next = 0, Model = SaveThingModel.Bank, Body = Bank( settings ).ToBody() } );
		payload.Scripts = new SaveScriptsData { SlotWords = new uint[100] };
		payload.MessageCentre = new SaveMessageCentreData { Word = 29, Lists = Enumerable.Range( 0, SaveBlockWidths.MessageLists ).Select( _ => Array.Empty<ushort>() ).ToList() };
		payload.Clock = new uint[] { 11, 22 };
		payload.VanillaTime = 33;
		payload.GameSystem = Enumerable.Range( 1, SaveBlockWidths.GameSystemBytes / 4 ).Select( value => (uint)value ).ToArray();
		payload.Rsse = new SaveRsseData { Padding = Enumerable.Repeat( 0x5F444150u, SaveRsseBlock.PadWords ).ToArray() };
		payload.Camera = Enumerable.Range( 0, 10 ).Select( value => (uint)value ).ToArray();
		return payload;
	}

	private static void SetControl( SaveObjectControlTable table, int index, uint id )
	{
		BitConverter.TryWriteBytes( table.Records.AsSpan( index * SaveObjectControl.Bytes ), id );
		BitConverter.TryWriteBytes( table.Records.AsSpan( index * SaveObjectControl.Bytes + 4 ), 1000u );
	}
}
