using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace OpenTPW.Server;

/// <summary>
/// [EXT:SERVER-WEB] Optionally serves the browser build of the game (docs/WEB.md) from the same
/// address as the API, so the page needs no cross-origin access and finds its server by itself.
/// The browser build contains only OpenTPW code; players still choose their own game folder.
/// </summary>
public static partial class WebClient
{
	[GeneratedRegex( @"\.[a-z0-9]{10}\.[a-z]+$" )]
	private static partial Regex Fingerprinted();

	/// <summary>
	/// Serves <paramref name="directory"/> (the published <c>wwwroot</c> of OpenTPW.Web) at the site
	/// root. Runs before the API's rate limiter: one page load fetches over a hundred files.
	/// </summary>
	public static void UseWebClient( this WebApplication app, string directory )
	{
		var root = Path.GetFullPath( directory );
		if ( !File.Exists( Path.Combine( root, "index.html" ) ) )
			throw new InvalidOperationException( $"OpenTPW:WebClientDirectory '{root}' has no index.html; publish OpenTPW.Web into it (docs/SERVER.md)." );
		var files = new PhysicalFileProvider( root );
		var types = new FileExtensionContentTypeProvider();
		types.Mappings[".wasm"] = "application/wasm";
		types.Mappings[".dat"] = "application/octet-stream";

		// The publish step stores a Brotli copy beside each file; send it to browsers that accept it.
		app.Use( async ( context, next ) =>
		{
			var request = context.Request;
			if ( HttpMethods.IsGet( request.Method ) || HttpMethods.IsHead( request.Method ) )
			{
				var path = request.Path.Value ?? "/";
				if ( path == "/" )
					path = "/index.html";
				if ( !path.StartsWith( "/api/", StringComparison.Ordinal ) && types.TryGetContentType( path, out _ ) && files.GetFileInfo( path ).Exists )
				{
					context.Response.Headers.Vary = "Accept-Encoding";
					var acceptsBrotli = request.Headers.AcceptEncoding.ToString().Contains( "br", StringComparison.OrdinalIgnoreCase );
					if ( acceptsBrotli && files.GetFileInfo( path + ".br" ).Exists )
					{
						context.Response.Headers.ContentEncoding = "br";
						path += ".br";
					}
					request.Path = path;
				}
			}
			await next();
		} );

		app.UseStaticFiles( new StaticFileOptions
		{
			FileProvider = files,
			ContentTypeProvider = new CompressedContentTypes( types ),
			OnPrepareResponse = context =>
			{
				var http = context.Context;
				var name = context.File.Name.EndsWith( ".br", StringComparison.Ordinal ) ? context.File.Name[..^3] : context.File.Name;
				// Fingerprinted runtime files never change; the page and its scripts are revalidated.
				http.Response.Headers.CacheControl = http.Request.Path.StartsWithSegments( "/_framework" ) && Fingerprinted().IsMatch( name )
					? "public, max-age=31536000, immutable"
					: "no-cache";
				if ( name == "index.html" )
				{
					var host = http.Request.Host.Value;
					http.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; "
						+ $"connect-src 'self' ws://{host} wss://{host}; img-src 'self' data: blob:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
				}
			}
		} );
	}

	/// <summary>Content type of <c>name.ext.br</c> is that of <c>name.ext</c>.</summary>
	private sealed class CompressedContentTypes( FileExtensionContentTypeProvider inner ) : IContentTypeProvider
	{
		public bool TryGetContentType( string subpath, out string contentType )
			=> inner.TryGetContentType( subpath.EndsWith( ".br", StringComparison.Ordinal ) ? subpath[..^3] : subpath, out contentType! );
	}
}
