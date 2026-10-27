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

	/// <summary>The content hash the page adds to a pools sprite address (<c>?v=</c>, the first 16 hex digits of the manifest's SHA-256; wwwroot/opentpw-pool-sprites.js).</summary>
	[GeneratedRegex( @"(^|&)v=[a-f0-9]{16}(&|$)" )]
	private static partial Regex ContentHash();

	/// <summary>The folder of the pools sprite sets, published as static files (docs/POOLS.md).</summary>
	private const string PoolSpriteFolder = "/pools-sprites";

	/// <summary>
	/// True when the Accept-Encoding header lists <paramref name="coding"/> with a quality above zero (<c>br;q=0</c> refuses it, as the
	/// standard says); a wildcard is deliberately not counted, so only a coding the browser names is sent.
	/// </summary>
	public static bool AcceptsEncoding( string header, string coding )
	{
		foreach ( var item in header.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = item.Split( ';', StringSplitOptions.TrimEntries );
			if ( !parts[0].Equals( coding, StringComparison.OrdinalIgnoreCase ) )
				continue;
			var quality = parts.Skip( 1 ).FirstOrDefault( part => part.StartsWith( "q=", StringComparison.OrdinalIgnoreCase ) );
			return quality == null || !double.TryParse( quality[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight ) || weight > 0;
		}
		return false;
	}

	/// <summary>The extensions of the pools sprite files the page fetches; unknown types are not served, so each needs a mapping.</summary>
	public static readonly string[] PoolSpriteExtensions = { ".esp", ".tpc" };

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
		// The pools extension's sprite files (pools-sprites/, docs/POOLS.md): every extension of content/pools/esprites.manifest.json
		foreach ( var extension in PoolSpriteExtensions )
			types.Mappings[extension] = "application/octet-stream";

		// The publish step stores a Brotli and a gzip copy beside each compressible file; send the best one the browser accepts (the proxy in front,
		// deploy/Caddyfile, leaves a response that already has a Content-Encoding alone, and would otherwise gzip a 44 MB runtime again for every request).
		app.Use( async ( context, next ) =>
		{
			var request = context.Request;
			if ( HttpMethods.IsGet( request.Method ) || HttpMethods.IsHead( request.Method ) )
			{
				var path = request.Path.Value ?? "/";
				if ( path == "/" )
					path = "/index.html";
				// The compressed copies are chosen by the middleware below, by the request's Accept-Encoding; asking for one by its own name would send the bytes without their Content-Encoding.
				if ( !path.StartsWith( "/api/", StringComparison.Ordinal ) && (path.EndsWith( ".br", StringComparison.Ordinal ) || path.EndsWith( ".gz", StringComparison.Ordinal )) && files.GetFileInfo( path ).Exists )
				{
					context.Response.StatusCode = StatusCodes.Status404NotFound;
					return;
				}
				if ( !path.StartsWith( "/api/", StringComparison.Ordinal ) && types.TryGetContentType( path, out _ ) && files.GetFileInfo( path ).Exists )
				{
					context.Response.Headers.Vary = "Accept-Encoding";
					var accepted = request.Headers.AcceptEncoding.ToString();
					if ( AcceptsEncoding( accepted, "br" ) && files.GetFileInfo( path + ".br" ).Exists )
					{
						context.Response.Headers.ContentEncoding = "br";
						path += ".br";
					}
					else if ( AcceptsEncoding( accepted, "gzip" ) && files.GetFileInfo( path + ".gz" ).Exists )
					{
						context.Response.Headers.ContentEncoding = "gzip";
						path += ".gz";
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
				var name = context.File.Name;
				if ( name.EndsWith( ".br", StringComparison.Ordinal ) )
					name = name[..^3];
				else if ( name.EndsWith( ".gz", StringComparison.Ordinal ) )
					name = name[..^3];
				// Fingerprinted runtime files never change, and neither does a pools sprite file asked for by its content hash (?v=, opentpw-pool-sprites.js: a changed
				// file is a new address); the page and its scripts are revalidated (ETag, answered 304).
				http.Response.Headers.CacheControl = (http.Request.Path.StartsWithSegments( "/_framework" ) && Fingerprinted().IsMatch( name ))
					|| (http.Request.Path.StartsWithSegments( PoolSpriteFolder ) && MatchesContentHash( files, http.Request ))
					? "public, max-age=31536000, immutable"
					: "no-cache";
				if ( name == "index.html" )
				{
					var host = http.Request.Host.Value;
					http.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; "
						// https: so the player can load a CD image from an address of their choice (docs/WEB.md, "Game data sources"); scripts stay 'self' only.
						+ $"connect-src 'self' ws://{host} wss://{host} https:; img-src 'self' data: blob:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
				}
			}
		} );
	}

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Modified, string Hash)> fileHashes = new();

	/// <summary>
	/// True when the request's <c>?v=</c> is the first 16 hex digits of the SHA-256 of the file's uncompressed bytes (not of a Brotli or gzip copy). Only then does the address name
	/// this content, so only then may the response be cached for good: any other well-formed value (an old tab's, after a deploy) is revalidated. The hash is kept per file, length and modification time.
	/// </summary>
	private static bool MatchesContentHash( IFileProvider files, HttpRequest request )
	{
		var query = request.Query["v"].ToString();
		if ( !ContentHash().IsMatch( "v=" + query ) )
			return false;
		var path = request.Path.Value ?? "";
		if ( path.EndsWith( ".br", StringComparison.Ordinal ) || path.EndsWith( ".gz", StringComparison.Ordinal ) )
			path = path[..^3];
		var info = files.GetFileInfo( path );
		if ( !info.Exists || info.PhysicalPath == null )
			return false;
		var key = info.PhysicalPath;
		if ( !fileHashes.TryGetValue( key, out var known ) || known.Length != info.Length || known.Modified != info.LastModified.UtcDateTime )
		{
			using var stream = info.CreateReadStream();
			known = (info.Length, info.LastModified.UtcDateTime, Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ));
			fileHashes[key] = known;
		}
		return known.Hash.StartsWith( query, StringComparison.Ordinal );
	}

	/// <summary>Content type of <c>name.ext.br</c> and <c>name.ext.gz</c> is that of <c>name.ext</c>.</summary>
	private sealed class CompressedContentTypes( FileExtensionContentTypeProvider inner ) : IContentTypeProvider
	{
		public bool TryGetContentType( string subpath, out string contentType )
			=> inner.TryGetContentType( subpath.EndsWith( ".br", StringComparison.Ordinal ) || subpath.EndsWith( ".gz", StringComparison.Ordinal ) ? subpath[..^3] : subpath, out contentType! );
	}
}
