using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenTPW.Server;

/// <summary>One plain-text e-mail with an optional postcard PNG attachment.</summary>
public sealed record EmailMessage( string To, string Subject, string Text, byte[]? Image = null, string? IdempotencyKey = null );

/// <summary>Sends account e-mails and postcards. <see cref="ResendEmailSender"/> on the official server.</summary>
public interface IEmailSender
{
	Task SendAsync( EmailMessage message, CancellationToken cancel = default );
}

public sealed class EmailException( string message ) : Exception( message );

/// <summary>
/// [EXT:online-email] Sends through the Resend API (<c>POST https://api.resend.com/emails</c>). The API key comes from the
/// settings only and is never logged or returned; a failed send throws <see cref="EmailException"/> without the key.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
	public static readonly Uri Endpoint = new( "https://api.resend.com/emails" );
	private readonly HttpClient http;
	private readonly string apiKey, from;
	private readonly string? replyTo;

	public ResendEmailSender( string apiKey, string from, string? replyTo, HttpMessageHandler? handler = null )
	{
		if ( string.IsNullOrWhiteSpace( apiKey ) || string.IsNullOrWhiteSpace( from ) )
			throw new ArgumentException( "Resend needs an API key and a sender address." );
		this.apiKey = apiKey.Trim();
		this.from = from.Trim();
		this.replyTo = string.IsNullOrWhiteSpace( replyTo ) ? null : replyTo.Trim();
		http = new HttpClient( handler ?? new HttpClientHandler { AllowAutoRedirect = false } ) { Timeout = TimeSpan.FromSeconds( 10 ) };
	}

	public async Task SendAsync( EmailMessage message, CancellationToken cancel = default )
	{
		var body = new Dictionary<string, object> { ["from"] = from, ["to"] = new[] { message.To }, ["subject"] = message.Subject, ["text"] = message.Text };
		if ( replyTo != null )
			body["reply_to"] = replyTo;
		if ( message.Image != null )
			body["attachments"] = new[] { new { filename = "postcard.png", content = Convert.ToBase64String( message.Image ) } };
		using var request = new HttpRequestMessage( HttpMethod.Post, Endpoint )
		{
			Content = new StringContent( JsonSerializer.Serialize( body ), Encoding.UTF8, "application/json" ),
		};
		if ( message.IdempotencyKey != null ) request.Headers.Add( "Idempotency-Key", message.IdempotencyKey );
		request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", apiKey );
		HttpResponseMessage response;
		try { response = await http.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancel ); }
		catch ( Exception exception ) when ( exception is HttpRequestException or TaskCanceledException )
		{
			throw new EmailException( $"The mail service could not be reached ({exception.GetType().Name})." );
		}
		using ( response )
		{
			if ( !response.IsSuccessStatusCode )
				throw new EmailException( $"The mail service refused the message ({(int)response.StatusCode})." );
		}
	}
}
