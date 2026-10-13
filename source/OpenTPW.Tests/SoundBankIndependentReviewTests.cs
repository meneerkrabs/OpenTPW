using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SoundBankIndependentReviewTests
{
	private static byte[] Bank()
	{
		var bytes = new byte[72];
		void Word( int at, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( at ), value );
		Word( 0, 2 ); Word( 4, 12 ); Word( 8, 12 ); // Two slots may address the same entry.
		Word( 12, 56 ); Word( 16, 4 ); // Extended headers remain supported.
		"alias.mp2"u8.CopyTo( bytes.AsSpan( 20 ) );
		new byte[] { 3, 1, 4, 1 }.CopyTo( bytes, 68 );
		return bytes;
	}

	[TestMethod]
	public void AliasedSlotsAndExtendedHeadersKeepIndependentReturnedBuffers()
	{
		var bytes = Bank();
		foreach ( var bank in new[] { new SoundBank( bytes ), new SoundBank( () => new MemoryStream( bytes ), bytes.Length ) } )
		{
			var first = bank.GetMpeg( 1 )!;
			CollectionAssert.AreEqual( first, bank.GetMpeg( 2 ) );
			Assert.AreEqual( "alias.mp2", bank.GetName( 2 ) );
			first[0] = 255;
			CollectionAssert.AreEqual( new byte[] { 3, 1, 4, 1 }, bank.GetMpeg( 1 ) );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FailedReopenedReadDisposesAndDoesNotPoisonTheNextLookup( bool failSeek )
	{
		var bytes = Bank();
		var opened = 0;
		var disposed = 0;
		var bank = new SoundBank( () =>
		{
			var call = ++opened;
			// The fourth open is the first lookup's payload. Construction and its header succeed.
			return new FailureStream( bytes, call == 4, failSeek, () => disposed++ );
		}, bytes.Length );
		Assert.ThrowsException<IOException>( () => bank.GetMpeg( 1 ) );
		Assert.AreEqual( 4, opened );
		Assert.AreEqual( opened, disposed );
		CollectionAssert.AreEqual( new byte[] { 3, 1, 4, 1 }, bank.GetMpeg( 2 ) );
		Assert.AreEqual( "alias.mp2", bank.GetName( 1 ) );
		Assert.AreEqual( opened, disposed );
	}

	private sealed class FailureStream( byte[] bytes, bool fail, bool failSeek, Action disposed ) : MemoryStream( bytes, writable: false )
	{
		public override long Length => fail && !failSeek ? bytes.Length + 1 : base.Length;
		public override long Seek( long offset, SeekOrigin origin ) => fail && failSeek
			? throw new IOException( "Synthetic seek failure." ) : base.Seek( offset, origin );
		protected override void Dispose( bool disposing )
		{
			if ( disposing ) disposed();
			base.Dispose( disposing );
		}
	}
}
