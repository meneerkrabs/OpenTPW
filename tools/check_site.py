"""Check local HTML links, assets and section anchors before publishing website/."""

from __future__ import annotations

import argparse
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


class Page(HTMLParser):
    def __init__(self, source: str):
        super().__init__()
        self.ids: set[str] = set()
        self.links: list[tuple[str, bool]] = []
        self.policy_errors: list[str] = []
        self.feed(source)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if "style" in values:
            self.policy_errors.append("Inline style blocked by the site's content policy")
        if tag == "img" and values.get("src"):
            source = urlsplit(values["src"])
            if source.netloc and (source.hostname != "play.opentpw.io" or source.scheme not in {"", "https"}):
                self.policy_errors.append("Embedded image blocked by the site's content policy: " + values["src"])
        if values.get("id"):
            self.ids.add(values["id"])
        if values.get("href"):
            self.links.append((values["href"], tag == "a"))
        if values.get("src"):
            self.links.append((values["src"], False))


def check(root: Path) -> list[str]:
    root = root.resolve()
    pages = {p.resolve(): Page(p.read_text(encoding="utf-8")) for p in root.rglob("*.html")}
    errors = []
    for path, page in sorted(pages.items()):
        errors.extend(f"{path.relative_to(root)}: {error}" for error in page.policy_errors)
        for target, anchor_link in page.links:
            url = urlsplit(target)
            label = f"{path.relative_to(root)}: {target}"
            if url.scheme or url.netloc:
                if url.scheme and url.scheme not in {"http", "https", "mailto"}:
                    errors.append(f"Unsupported link scheme: {label}")
                if url.netloc == "github.com" and url.path.startswith("/meneerkrabs/OpenTPW/") and url.path.lower().endswith(".md"):
                    errors.append(f"Documentation links to Markdown: {label}")
                continue
            local = unquote(url.path)
            destination = (root / local.lstrip("/") if local.startswith("/") else path.parent / local).resolve() if local else path
            if not destination.is_relative_to(root):
                errors.append(f"Link escapes website: {label}")
                continue
            if destination.is_dir():
                destination /= "index.html"
            # Pages serves extensionless URLs for HTML documents.
            if not destination.exists() and destination.with_suffix(".html").is_file():
                destination = destination.with_suffix(".html")
            if not destination.is_file():
                errors.append(f"Missing local target: {label}")
                continue
            if destination.suffix.lower() == ".md":
                errors.append(f"Documentation links to Markdown: {label}")
            if anchor_link and url.fragment and destination in pages and unquote(url.fragment) not in pages[destination].ids:
                errors.append(f"Missing section anchor: {label}")
    if not pages:
        errors.append("No HTML pages found")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).resolve().parents[1] / "website")
    args = parser.parse_args()
    errors = check(args.root)
    if errors:
        print("\n".join(errors))
        print(f"site check: {len(errors)} error(s)")
        return 1
    print(f"site check: all links, assets and anchors in {len(list(args.root.rglob('*.html')))} HTML pages resolve")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
