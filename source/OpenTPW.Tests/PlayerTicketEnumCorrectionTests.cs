using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class PlayerTicketEnumCorrectionTests
{
	private static byte[] File( string mode, string kind, string? theme ) => Encoding.UTF8.GetBytes(
		$"{{\"Format\":\"opentpw-player-progress\",\"Version\":1,\"Mode\":\"{mode}\",\"ExtraKeys\":0,\"SpentTickets\":0,\"Earned\":[{{\"Theme\":{(theme == null ? "null" : "\"" + theme + "\"")},\"Kind\":\"{kind}\"}}]}}" );

	[TestMethod]
	public void EveryCombinedPairThatWouldBecomeAKnownValueIsRefused()
	{
		var modes = Enum.GetValues<ParkGameMode>();
		foreach ( var left in modes ) foreach ( var right in modes )
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( File( $"{left},{right}", "Visitors", "jungle" ) ) );
		var kinds = Enum.GetValues<GoldenTicketKind>();
		var checkedPairs = 0;
		foreach ( var left in kinds ) foreach ( var right in kinds )
		{
			// The old generic converter ORs names; supply a valid scope for that result,
			// so invalid scope cannot conceal acceptance of the forged combined token.
			var combined = (GoldenTicketKind)((int)left | (int)right);
			if ( !Enum.IsDefined( combined ) ) continue;
			var theme = (int)combined < 6 ? "jungle" : null;
			Assert.ThrowsException<InvalidDataException>( () => PlayerTicketFile.Deserialize( File( "FullSimulation", $"{left}, {right}", theme ) ) );
			checkedPairs++;
		}
		Assert.AreEqual( 112, checkedPairs );
	}

	[TestMethod]
	public void ExactDecodedNamesAcceptJsonEscapesAndWriteCanonicalTokensForBothModes()
	{
		foreach ( var mode in Enum.GetValues<ParkGameMode>() ) foreach ( var kind in Enum.GetValues<GoldenTicketKind>() )
		{
			var theme = (int)kind < 6 ? "jungle" : null;
			var escapedMode = $"\\u{(int)mode.ToString()[0]:x4}" + mode.ToString()[1..];
			var escapedKind = $"\\u{(int)kind.ToString()[0]:x4}" + kind.ToString()[1..];
			var decoded = PlayerTicketFile.Deserialize( File( escapedMode, escapedKind, theme ) );
			Assert.AreEqual( mode, decoded.Mode );
			Assert.AreEqual( kind, decoded.Capture().Earned.Single().Kind );
			CollectionAssert.AreEqual( File( mode.ToString(), kind.ToString(), theme ), PlayerTicketFile.Serialize( decoded.Capture() ) );
		}
	}
}
