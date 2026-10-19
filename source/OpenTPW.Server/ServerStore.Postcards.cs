using System.Text;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Packages;

namespace OpenTPW.Server;

public sealed partial class ServerStore
{
	// [EXT:postcard-player-names] Delivery receipts belong to the authenticated account and the immutable card content,
	// not its client-chosen id alone. Recipient lists are excluded so a partially sent local outbox can keep only failures.
	// The ledger stores hashes, never destination addresses or message text; confirmed receipts expire after 30 days.
	public sealed class PostcardDeliveryRecord
	{
		public string Sender { get; set; } = "";
		public DateTimeOffset StartedUtc { get; set; }
		public bool Complete { get; set; }
	}
	public sealed record PostcardAttempt( string Sender, string Recipient, DateTimeOffset Utc );
	private readonly Dictionary<string, PostcardDeliveryRecord> postcardDeliveries;
	private readonly List<PostcardAttempt> postcardAttempts;
	private readonly HashSet<string> activePostcardSenders = new( StringComparer.Ordinal );
	public const int MaximumUnconfirmedPostcardsPerSender = 256;
	public static readonly TimeSpan PostcardReceiptLifetime = TimeSpan.FromDays( 30 );
	// Resend keeps idempotency keys for 24 hours. Leave a safety margin and never automatically repeat an ambiguous older send.
	public static readonly TimeSpan PostcardMailRetryWindow = TimeSpan.FromHours( 23 );

	public async Task<PostcardSent> SendPostcardAsync( AccountRecord sender, Postcard card, IEmailSender? emailSender, CancellationToken cancel = default )
	{
		card.Validate();
		var principal = HashDeliveryKey( sender.Key + "\n" + sender.CreatedUtc.ToString( "O" ) );
		var content = card.Manifest with { From = sender.Name, To = Array.Empty<string>() };
		var fingerprint = ParkPackage.Hash( StrictJson.Serialize( content ) );
		lock ( gate )
		{
			if ( !activePostcardSenders.Add( principal ) )
				throw new StoreException( 409, "Another postcard is being sent; try again when it finishes." );
			foreach ( var key in postcardDeliveries.Where( pair => pair.Value.Complete && pair.Value.StartedUtc < clock() - PostcardReceiptLifetime ).Select( pair => pair.Key ).ToArray() )
				postcardDeliveries.Remove( key );
		}
		var delivered = new List<string>();
		StoreException? failure = null;
		try
		{
			foreach ( var recipient in card.Manifest.To )
			{
				var target = HashDeliveryKey( PostcardRecipient.Key( recipient ) );
				var key = HashDeliveryKey( principal + "\n" + fingerprint + "\n" + target );
				try
				{
					if ( PostcardRecipient.IsEmail( recipient ) )
						await SendPostcardEmailAsync( sender, card, recipient, principal, target, key, emailSender, cancel );
					else
						DeliverPlayerPostcard( sender, card, recipient, principal, target, key );
					delivered.Add( recipient );
				}
				catch ( StoreException exception ) { failure ??= exception; }
			}
			if ( delivered.Count == 0 && failure != null ) throw failure;
			return new PostcardSent( card.Id.ToString( "N" ), delivered, failure?.Message );
		}
		finally { lock ( gate ) activePostcardSenders.Remove( principal ); }
	}

	private static string HashDeliveryKey( string text ) => ParkPackage.Hash( Encoding.UTF8.GetBytes( text ) );

	/// <summary>Call inside the lock, before any delivery attempt. Failed attempts count too, across server restarts.</summary>
	private void CountPostcardAttempt( string principal, string target, bool email )
	{
		var now = clock();
		postcardAttempts.RemoveAll( attempt => attempt.Utc <= now - TimeSpan.FromHours( 1 ) );
		if ( postcardAttempts.Count( attempt => attempt.Sender == principal ) >= options.PostcardRecipientsPerHour
			|| (email && postcardAttempts.Count( attempt => attempt.Recipient == target ) >= options.PostcardEmailsPerAddressPerHour) )
			throw new StoreException( 429, "Too many postcard recipients; try again later." );
		postcardAttempts.Add( new PostcardAttempt( principal, target, now ) );
		Save( "postcard-attempts.json", postcardAttempts );
	}

	private void DeliverPlayerPostcard( AccountRecord sender, Postcard card, string name, string principal, string target, string key )
	{
		lock ( gate )
		{
			if ( postcardDeliveries.TryGetValue( key, out var receipt ) && receipt.Complete ) return;
			var recipient = accounts.GetValueOrDefault( OnlineText.NormalizeName( name ) )
				?? throw new StoreException( 404, "Player recipient unknown." );
			// Deterministic server ids also recover a delivery whose inbox index was saved just before a receipt write failed.
			var id = key[..32];
			if ( !postcards.ContainsKey( id ) )
			{
				if ( postcards.Values.Count( item => item.RecipientKey == recipient.Key ) >= options.MaximumInboxPostcards )
					throw new StoreException( 409, "The player's inbox is full." );
				CountPostcardAttempt( principal, target, email: false );
				// Each inbox copy lists only its recipient: mixed cards never disclose somebody else's e-mail address.
				var privateCard = card with { Manifest = card.Manifest with { From = sender.Name, To = new[] { recipient.Name } } };
				AtomicFile.Write( PostcardFile( id ), privateCard.ToBytes() );
				postcards.Add( id, new PostcardRecord
				{
					Id = id, RecipientKey = recipient.Key, From = sender.Name, Title = card.Manifest.Title, SentUtc = clock(),
					HasImage = card.Image != null, ParkName = card.Manifest.Park?.ParkName,
				} );
			}
			// A prior index write can fail after the in-memory add; retry must make it durable before confirming.
			Save( "postcards.json", postcards.Values.ToList() );
			postcardDeliveries[key] = new PostcardDeliveryRecord { Sender = principal, StartedUtc = clock(), Complete = true };
			Save( "postcard-deliveries.json", postcardDeliveries );
		}
	}

	private async Task SendPostcardEmailAsync( AccountRecord sender, Postcard card, string recipient, string principal, string target, string key,
		IEmailSender? emailSender, CancellationToken cancel )
	{
		lock ( gate )
		{
			if ( postcardDeliveries.TryGetValue( key, out var receipt ) )
			{
				if ( receipt.Complete ) return;
				if ( clock() - receipt.StartedUtc >= PostcardMailRetryWindow )
					throw new StoreException( 409, "An earlier e-mail attempt has an uncertain result. Check with its recipient before composing a new postcard." );
			}
			if ( !postcardDeliveries.ContainsKey( key ) && postcardDeliveries.Values.Count( item => item.Sender == principal && !item.Complete ) >= MaximumUnconfirmedPostcardsPerSender )
				throw new StoreException( 429, "Too many unconfirmed postcard e-mails; contact the server operator before sending more." );
			if ( emailSender == null ) throw new StoreException( 503, "This server has no e-mail delivery configured." );
			CountPostcardAttempt( principal, target, email: true );
			postcardDeliveries.TryAdd( key, new PostcardDeliveryRecord { Sender = principal, StartedUtc = clock() } );
			Save( "postcard-deliveries.json", postcardDeliveries );
		}
		try
		{
			await emailSender.SendAsync( new EmailMessage( recipient.ToLowerInvariant(), card.Manifest.Title,
				$"Postcard from {sender.Name} on {options.ServerName}\n\n{card.Manifest.Text}", card.Image, "postcard/" + key ), cancel );
		}
		catch ( Exception exception ) when ( exception is EmailException or OperationCanceledException )
		{
			throw new StoreException( 503, "The postcard mail service could not confirm delivery. Unsent recipients remain in the outbox." );
		}
		lock ( gate )
		{
			postcardDeliveries[key].Complete = true;
			Save( "postcard-deliveries.json", postcardDeliveries );
		}
	}
}
