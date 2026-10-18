// Preserve the server's NFKC identity rule. .NET still validates Unicode and applies
// ToUpperInvariant; JavaScript casing has different expansion rules for some letters.
export function normalizeCompatibility(text) {
	return text.normalize('NFKC');
}
