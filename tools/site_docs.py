"""Build the website's public documentation pages (standard library only).

Usage: python3 -I tools/site_docs.py [--repo CHECKOUT] [--out website/docs]

The root README and licence, docs/, and public tools/ guides become static HTML
in the site's style. Doc links resolve to HTML; existing source-code links point
at GitHub. Missing targets and stale fragments are qualified and reported. The
converter supports the repository's headings, lists, tables, fences, quotations,
inline formatting, and a safe subset of the README's HTML. It is intentionally
not a general CommonMark implementation. Deploy generates the ignored output.
"""

from __future__ import annotations

import argparse
import html
import importlib.util
import json
import os
import posixpath
import re
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit

# The progress log is assembled from docs/progress/ fragments and the frozen docs/PROGRESS.md (tools/progress.py).
_PROGRESS_SPEC = importlib.util.spec_from_file_location("progress", Path(__file__).with_name("progress.py"))
progress = importlib.util.module_from_spec(_PROGRESS_SPEC)
_PROGRESS_SPEC.loader.exec_module(progress)

GITHUB = "https://github.com/meneerkrabs/OpenTPW/blob/main/"
GITHUB_TREE = "https://github.com/meneerkrabs/OpenTPW/tree/main/"
OWN_REPOS = {"meneerkrabs/OpenTPW", "OpenTPW/OpenTPW"}
SAFE_IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif"}


def document_path(source: str) -> str:
    """Keep the old docs URLs and give public tooling guides their own subtree."""
    path = source[5:] if source.startswith("docs/") else source
    return path[:-3] + ".html"


def document_sources(repo: Path) -> list[str]:
    """Publish public guides, never local agent contracts or private work notes.

    Progress fragments are not pages of their own: they appear in the assembled progress log."""
    sources = [name for name in ("README.md", "LICENSE.md") if (repo / name).is_file()]
    for folder in ("docs", "tools"):
        sources.extend(p.relative_to(repo).as_posix() for p in (repo / folder).rglob("*.md") if p.is_file() and not p.is_symlink())
    return sorted(source for source in sources if not source.startswith(progress.FRAGMENTS + "/"))


def document_text(repo: Path, source: str) -> str:
    """A published document's Markdown; the progress log is assembled from its fragments and history."""
    if source == progress.HISTORY:
        return progress.assemble(repo)
    return (repo / source).read_text(encoding="utf-8")

# Documents a player or server host is most likely to want, in this order, under their own heading.
FEATURED = [
    ("MODS.md", "Extensions and mods"),
    ("SCRIPT-MODS.md", "Script mods (TPW Script)"),
    ("POOLS.md", "Swimming pools (on by default)"),
    ("EVENING.md", "Evening and night (on by default)"),
    ("RUNNING.md", "Running from source and command-line options"),
    ("SETUP.md", "How the game folder is found"),
    ("WEB.md", "The browser version"),
    ("ONLINE.md", "Online play"),
    ("SERVER.md", "Hosting a server"),
    ("MODDING-GUIDE.md", "Modding guide (start here)"),
    ("MODS.md", "Extensions and mods"),
    ("SCRIPT-MODS.md", "Script mods (TPW Script)"),
    ("POOLS.md", "Pools (on by default)"),
    ("LIGHTS.md", "Point lights and object lights"),
    ("TEXTURE-PACKS.md", "Texture packs"),
    ("LANGUAGES.md", "Languages"),
    ("COMPATIBILITY.md", "Compatibility"),
    ("PROGRESS.md", "Progress notes"),
    ("FEATURE-MATRIX.md", "Feature matrix"),
    ("FIDELITY-REGISTER.md", "Fidelity register (every approximation)"),
    ("RELEASES.md", "Release builds"),
]


def slug(text: str) -> str:
    """GitHub's heading anchor: lower case, markup and punctuation dropped, spaces to hyphens."""
    # Code can contain literal angle brackets, which are not HTML tags.
    text = re.sub(r"(`+)(.*?)\1", lambda match: html.escape(match.group(2)), text)
    text = html.unescape(re.sub(r"<[^>]+>", "", text))
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = text.replace("`", "").replace("*", "")
    text = re.sub(r"(?<!\w)_{1,2}(.+?)_{1,2}(?!\w)", r"\1", text)
    text = text.strip().lower()
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


class SafeHTML(HTMLParser):
    """Keep the README's small HTML header, with the same safe link handling."""
    allowed = {"a", "p", "br", "hr", "img", "strong", "b", "em", "i", "code", "kbd", "s", "del", "sup", "sub", "details", "summary", "h1", "h2", "h3", "h4", "h5", "h6"}
    void = {"br", "hr", "img"}

    def __init__(self, converter: "Converter"):
        super().__init__(convert_charrefs=True)
        self.converter = converter
        self.out: list[str] = []
        self.tags: list[tuple[str, str]] = []
        self.headings: list[tuple[str, int, list[str]]] = []
        self.suppressed = 0

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag in {"script", "style", "iframe", "object"} or self.suppressed:
            self.suppressed += 1
            return
        if tag not in self.allowed:
            return
        values = dict(attrs)
        rendered = tag
        attributes = ""
        if tag in {"a", "img"}:
            key = "href" if tag == "a" else "src"
            target = self.converter.link_target(values.get(key) or "", image=tag == "img")
            if target is None:
                rendered = "span" if tag == "a" else ""
                attributes = ' class="doc-unavailable"' if tag == "a" else ""
            else:
                attributes = f' {key}="{html.escape(target, quote=True)}"'
            if tag == "img":
                if not rendered:
                    self.out.append(html.escape(values.get("alt") or ""))
                    return
                # The site's CSP permits local images only. External figures
                # remain available without an automatic third-party request.
                if urlsplit(target).scheme or urlsplit(target).netloc:
                    label = html.escape(values.get("alt") or "image")
                    self.out.append(f'<a href="{html.escape(target, quote=True)}">View image: {label}</a>')
                    return
                attributes += f' alt="{html.escape(values.get("alt") or "", quote=True)}" loading="lazy"'
        if tag.startswith("h") and len(tag) == 2 and tag[1].isdigit():
            self.headings.append((tag, len(self.out), []))
        self.out.append(f"<{rendered}{attributes}>")
        if tag not in self.void:
            self.tags.append((tag, rendered))

    def handle_endtag(self, tag: str) -> None:
        if self.suppressed:
            self.suppressed -= 1
            return
        if tag not in self.allowed or tag in self.void:
            return
        index = next((i for i in range(len(self.tags) - 1, -1, -1) if self.tags[i][0] == tag), None)
        if index is None:
            return
        if self.headings and self.headings[-1][0] == tag:
            _, start, text_parts = self.headings.pop()
            text = "".join(text_parts).strip()
            ident = self.converter.heading_id(text)
            self.out[start] = f'<{tag} id="{ident}">'
            if tag == "h1" and not self.converter.title:
                self.converter.title = text
        for _, rendered in reversed(self.tags[index:]):
            self.out.append(f"</{rendered}>")
        del self.tags[index:]

    def handle_startendtag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self.handle_starttag(tag, attrs)
        if tag not in self.void:
            self.handle_endtag(tag)

    def handle_data(self, text: str) -> None:
        if self.suppressed:
            return
        self.out.append(html.escape(text))
        for _, _, parts in self.headings:
            parts.append(text)

    def finish(self, source: str) -> str:
        self.feed(source)
        self.close()
        for tag, _ in list(reversed(self.tags)):
            self.handle_endtag(tag)
        return "".join(self.out)


class Converter:
    def __init__(self, source_path: str, repo: str, published: set[str] | None = None):
        self.source_path = source_path  # repository-relative, e.g. docs/reverse/COAST-plan.md
        self.repo = repo
        self.used_ids: dict[str, int] = {}
        self.title = ""
        self.published = published if published is not None else set(document_sources(Path(repo)))
        self.warnings: list[str] = []
        self.references: dict[str, str] = {}

    # ------------------------------------------------------------------ inline
    def unavailable(self, target: str, reason: str) -> None:
        self.warnings.append(f"{self.source_path}: {target}: {reason}")

    def link_target(self, target: str, *, image: bool = False) -> str | None:
        """Resolve repo links without exposing local files or executable schemes."""
        if any(ord(ch) < 32 for ch in target) or "\\" in target:
            self.unavailable(target, "unsafe link")
            return None
        try:
            parsed = urlsplit(html.unescape(target))
        except ValueError:
            self.unavailable(target, "invalid URL")
            return None
        path = unquote(parsed.path)
        source_relative = True
        if parsed.scheme or parsed.netloc:
            if parsed.scheme.lower() not in {"http", "https", "mailto"} or (image and parsed.scheme.lower() not in {"http", "https"}):
                self.unavailable(target, "unsupported link scheme")
                return None
            if parsed.hostname in {"github.com", "www.github.com", "raw.githubusercontent.com"}:
                segments = path.strip("/").split("/")
                own = "/".join(segments[:2]) in OWN_REPOS
                if own and len(segments) >= 5 and segments[2] in {"blob", "tree", "raw"}:
                    path = "/".join(segments[4:])
                    source_relative = False
                elif own and parsed.hostname == "raw.githubusercontent.com" and len(segments) >= 4:
                    path = "/".join(segments[3:])
                    source_relative = False
                elif own and len(segments) == 2 and parsed.fragment == "readme":
                    path = "README.md"
                    parsed = parsed._replace(fragment="")
                    source_relative = False
                else:
                    return target
            elif parsed.hostname in {"opentpw.io", "www.opentpw.io"} and path.startswith("/docs/") and path.lower().endswith(".md"):
                path = path.lstrip("/")
                source_relative = False
            else:
                return target
        elif path.startswith("/"):
            # Repository-root doc links are public; workstation paths never are.
            if not path.startswith(("/docs/", "/tools/")) and path not in {"/README.md", "/LICENSE.md"}:
                self.unavailable(target, "absolute local path")
                return None
            path = path.lstrip("/")
            source_relative = False
        if not path:
            return "#" + parsed.fragment if parsed.fragment and not image else None
        resolved = posixpath.normpath(posixpath.join(posixpath.dirname(self.source_path), path) if source_relative else path)
        if resolved == ".." or resolved.startswith("../"):
            self.unavailable(target, "link escapes repository")
            return None
        actual = Path(self.repo, resolved)
        if not actual.resolve().is_relative_to(Path(self.repo).resolve()):
            self.unavailable(target, "link resolves outside repository")
            return None
        suffix = ("?" + parsed.query if parsed.query else "") + ("#" + parsed.fragment if parsed.fragment else "")
        output_dir = posixpath.dirname(document_path(self.source_path)) or "."
        if resolved.lower().endswith(".md"):
            if resolved not in self.published:
                self.unavailable(target, "missing public document")
                return None
            return quote(posixpath.relpath(document_path(resolved), output_dir), safe="/.") + suffix
        if resolved == "docs":
            return posixpath.relpath("index.html", output_dir) + suffix
        if image:
            if not actual.is_file() or actual.suffix.lower() not in SAFE_IMAGE_SUFFIXES:
                self.unavailable(target, "missing or unsupported image")
                return None
            if resolved.startswith("website/"):
                return quote(posixpath.relpath("../" + resolved[len("website/"):], output_dir), safe="/.") + suffix
            return "https://raw.githubusercontent.com/meneerkrabs/OpenTPW/main/" + quote(resolved, safe="/.") + suffix
        if not actual.exists():
            self.unavailable(target, "missing repository target")
            return None
        return (GITHUB_TREE if actual.is_dir() else GITHUB) + quote(resolved, safe="/.") + suffix

    def inline(self, text: str) -> str:
        return self.inline_markup(text)

    @staticmethod
    def closing(text: str, start: int, opening: str, closing: str) -> int | None:
        depth = 1
        i = start + 1
        while i < len(text):
            if text[i] == "\\":
                i += 2
                continue
            if text[i] == opening:
                depth += 1
            elif text[i] == closing:
                depth -= 1
                if not depth:
                    return i
            i += 1
        return None

    def inline_markup(self, text: str, *, links: bool = True) -> str:
        # Tokenize links before their labels' code spans; splitting on backticks
        # first turns [a `method`](file.md) into a broken literal Markdown link.
        out: list[str] = []
        i = 0
        while i < len(text):
            ch = text[i]
            if ch == "\\" and i + 1 < len(text) and text[i + 1] in r"\`*_{}[]()#+-.!|>":
                out.append(html.escape(text[i + 1]))
                i += 2
                continue
            if ch == "`":
                marker = re.match(r"`+", text[i:]).group(0)
                match = re.search(r"(?<!`)" + re.escape(marker) + r"(?!`)", text[i + len(marker):])
                if match:
                    end = i + len(marker) + match.start()
                    code = text[i + len(marker):end].replace("\n", " ")
                    if code.startswith(" ") and code.endswith(" ") and code.strip(" "):
                        code = code[1:-1]
                    out.append("<code>" + html.escape(code) + "</code>")
                    i = end + len(marker)
                    continue
            image = text.startswith("![", i)
            start = i + 1 if image else i
            if links and text[start:start + 1] == "[":
                end_label = self.closing(text, start, "[", "]")
                if end_label is not None:
                    label = text[start + 1:end_label]
                    end = end_label + 1
                    target = None
                    if text[end:end + 1] == "(":
                        end_target = self.closing(text, end, "(", ")")
                        if end_target is not None:
                            destination = text[end + 1:end_target].strip()
                            match = re.fullmatch(r'(?:<([^>]+)>|([^\s]+))(?:\s+[\"\'].*[\"\'])?', destination)
                            if match:
                                target = match.group(1) or match.group(2)
                                end = end_target + 1
                    else:
                        reference = label
                        if text[end:end + 1] == "[":
                            end_ref = self.closing(text, end, "[", "]")
                            if end_ref is not None:
                                reference = text[end + 1:end_ref] or label
                                end = end_ref + 1
                        target = self.references.get(" ".join(reference.lower().split()))
                    if target is not None:
                        destination = self.link_target(target, image=image)
                        rendered = self.inline_markup(label, links=False)
                        if destination is None:
                            out.append(f'<span class="doc-unavailable" title="Link target unavailable">{rendered}</span>')
                        elif image:
                            alt = re.sub(r"<[^>]+>", "", rendered)
                            if urlsplit(destination).scheme or urlsplit(destination).netloc:
                                out.append(f'<a href="{html.escape(destination, quote=True)}">View image: {html.escape(html.unescape(alt))}</a>')
                            else:
                                out.append(f'<img src="{html.escape(destination, quote=True)}" alt="{html.escape(html.unescape(alt), quote=True)}" loading="lazy">')
                        else:
                            out.append(f'<a href="{html.escape(destination, quote=True)}">{rendered}</a>')
                        i = end
                        continue
            if ch == "<":
                match = re.match(r"<(https?://[^>\s]+|mailto:[^>\s]+)>", text[i:])
                if match:
                    target = self.link_target(match.group(1))
                    if target:
                        out.append(f'<a href="{html.escape(target, quote=True)}">{html.escape(match.group(1))}</a>')
                    i += match.end()
                    continue
                tag = re.match(r"<([a-zA-Z][a-zA-Z0-9]*)\b[^>]*>", text[i:])
                if tag:
                    end_tag = re.search(r"</" + re.escape(tag.group(1)) + r"\s*>", text[i + tag.end():], re.I)
                    end = i + tag.end() + end_tag.end() if end_tag else i + tag.end()
                    out.append(SafeHTML(self).finish(text[i:end]))
                    i = end
                    continue
            if ch in "*_~":
                for marker, tags in (("***", ("strong", "em")), ("___", ("strong", "em")), ("**", ("strong",)), ("__", ("strong",)), ("~~", ("del",)), ("*", ("em",)), ("_", ("em",))):
                    if not text.startswith(marker, i) or (marker.startswith("_") and i and text[i - 1].isalnum()):
                        continue
                    end = text.find(marker, i + len(marker))
                    if end > i + len(marker) and not text[i + len(marker)].isspace() and not text[end - 1].isspace():
                        out.append("".join(f"<{tag}>" for tag in tags) + self.inline_markup(text[i + len(marker):end], links=links) + "".join(f"</{tag}>" for tag in reversed(tags)))
                        i = end + len(marker)
                        break
                else:
                    out.append(html.escape(ch))
                    i += 1
                continue
            out.append(html.escape(ch))
            i += 1
        return "".join(out)

    # ------------------------------------------------------------------ blocks
    def heading_id(self, text: str) -> str:
        base = slug(text) or "section"
        count = self.used_ids.get(base, 0)
        ident = base if count == 0 else f"{base}-{count}"
        while ident in self.used_ids:
            count += 1
            ident = f"{base}-{count}"
        self.used_ids[base] = count + 1
        self.used_ids[ident] = self.used_ids.get(ident, 1)
        return ident

    def convert(self, markdown: str) -> str:
        self.used_ids.clear()
        self.title = ""
        self.references.clear()
        lines = markdown.replace("\r\n", "\n").replace("\t", "    ").split("\n")
        fence = None
        for i, line in enumerate(lines):
            marker = re.match(r"^\s{0,3}(`{3,}|~{3,})", line)
            if marker:
                if fence is None:
                    fence = marker.group(1)
                elif marker.group(1)[0] == fence[0] and len(marker.group(1)) >= len(fence):
                    fence = None
                continue
            reference = re.match(r'^\s{0,3}\[([^\]]+)\]:\s*(?:<([^>]+)>|(\S+))(?:\s+[\"\'].*[\"\'])?\s*$', line)
            if fence is None and reference:
                self.references[" ".join(reference.group(1).lower().split())] = reference.group(2) or reference.group(3)
                lines[i] = ""
        return self.blocks(lines)

    def blocks(self, lines: list[str]) -> str:
        out: list[str] = []
        i = 0
        n = len(lines)
        while i < n:
            line = lines[i]
            stripped = line.strip()
            if not stripped:
                i += 1
                continue
            if stripped.startswith("<!--"):
                while i < n and "-->" not in lines[i]:
                    i += 1
                i += 1
                continue
            if re.match(r"^<(?:p|h[1-6]|details|summary|script|style|iframe|object)\b", stripped, re.I):
                raw = []
                while i < n and lines[i].strip():
                    raw.append(lines[i])
                    i += 1
                out.append(SafeHTML(self).finish("\n".join(raw)))
                continue
            fence = re.match(r"^(\s{0,3})(`{3,}|~{3,})\s*([\w+-]*)", line)
            if fence:
                marker, indent = fence.group(2), len(fence.group(1))
                body = []
                i += 1
                closing = re.compile(r"^\s{0,3}" + re.escape(marker[0]) + "{" + str(len(marker)) + r",}\s*$")
                while i < n and not closing.match(lines[i]):
                    body.append(lines[i][indent:] if lines[i][:indent].strip() == "" else lines[i])
                    i += 1
                i += 1
                lang = fence.group(3)
                cls = f' class="language-{html.escape(lang)}"' if lang else ""
                out.append(f"<pre><code{cls}>{html.escape(chr(10).join(body))}</code></pre>")
                continue
            if line.startswith("    "):
                body = []
                while i < n and (lines[i].startswith("    ") or not lines[i].strip()):
                    body.append(lines[i][4:] if lines[i].startswith("    ") else "")
                    i += 1
                out.append("<pre><code>" + html.escape("\n".join(body).rstrip("\n")) + "</code></pre>")
                continue
            heading = re.match(r"^(#{1,6})\s+(.*?)\s*#*\s*$", stripped)
            if heading:
                level = len(heading.group(1))
                text = heading.group(2)
                if level == 1 and not self.title:
                    self.title = re.sub(r"[`*]", "", text)
                ident = self.heading_id(text)
                out.append(f'<h{level} id="{ident}">{self.inline(text)}<a class="anchor" href="#{ident}" aria-label="Link to this section">#</a></h{level}>')
                i += 1
                continue
            if i + 1 < n and re.fullmatch(r"\s{0,3}(=+|-+)\s*", lines[i + 1]):
                level = 1 if lines[i + 1].strip().startswith("=") else 2
                if level == 1 and not self.title:
                    self.title = stripped
                ident = self.heading_id(stripped)
                out.append(f'<h{level} id="{ident}">{self.inline(stripped)}<a class="anchor" href="#{ident}" aria-label="Link to this section">#</a></h{level}>')
                i += 2
                continue
            if re.match(r"^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$", stripped):
                out.append("<hr>")
                i += 1
                continue
            if stripped.startswith(">"):
                quote = []
                while i < n and lines[i].strip().startswith(">"):
                    quote.append(re.sub(r"^\s*>\s?", "", lines[i]))
                    i += 1
                out.append("<blockquote>" + self.blocks(quote) + "</blockquote>")
                continue
            if "|" in stripped and i + 1 < n and self.table_separator(lines[i + 1]):
                rows = []
                while i < n and lines[i].strip() and "|" in lines[i]:
                    rows.append(lines[i].strip())
                    i += 1
                out.append(self.table(rows))
                continue
            if re.match(r"^\s*([-*+]|\d+[.)])\s+", line):
                rendered, i = self.list_block(lines, i)
                out.append(rendered)
                continue
            para = []
            while i < n and lines[i].strip() and not re.match(r"^(#{1,6}\s|```|~~~|>|\|)", lines[i].strip()) and not re.match(r"^\s*([-*+]|\d+[.)])\s+", lines[i]):
                hard_break = lines[i].endswith(("  ", "\\"))
                text = lines[i].strip()
                if lines[i].endswith("\\"):
                    text = text[:-1]
                para.append(text + ("\n" if hard_break else ""))
                i += 1
            if not para:  # a line no rule took (e.g. a table without separator): keep it as text
                para.append(lines[i].strip())
                i += 1
            text = self.inline("\n".join(para)).replace("\n\n", "<br>").replace("\n", " ")
            out.append(f"<p>{text}</p>")
        return "\n".join(out)

    def list_block(self, lines: list[str], start_at: int) -> tuple[str, int]:
        item_pattern = re.compile(r"^( *)([-*+]|\d+[.)])( +)(.*)$")
        first = item_pattern.match(lines[start_at])
        base_indent = len(first.group(1))
        ordered = first.group(2)[0].isdigit()
        items: list[list[str]] = []
        i = start_at
        while i < len(lines):
            match = item_pattern.match(lines[i])
            if not match or len(match.group(1)) != base_indent or match.group(2)[0].isdigit() != ordered:
                break
            indent = match.start(4)
            item = [match.group(4)]
            i += 1
            while i < len(lines):
                if not lines[i].strip():
                    ahead = i + 1
                    while ahead < len(lines) and not lines[ahead].strip():
                        ahead += 1
                    if ahead == len(lines):
                        i = ahead
                        break
                    next_item = item_pattern.match(lines[ahead])
                    if next_item and len(next_item.group(1)) == base_indent:
                        i = ahead
                        break
                    if len(lines[ahead]) - len(lines[ahead].lstrip(" ")) < indent:
                        break
                    item.append("")
                    i += 1
                    continue
                next_item = item_pattern.match(lines[i])
                if next_item and len(next_item.group(1)) <= base_indent:
                    break
                leading = len(lines[i]) - len(lines[i].lstrip(" "))
                if leading >= indent:
                    item.append(lines[i][indent:])
                elif not next_item and not re.match(r"^\s*(#{1,6}\s|>|`{3,}|~{3,}|<)", lines[i]):
                    # CommonMark lazy paragraph continuation in a tight list.
                    item.append(lines[i].strip())
                else:
                    break
                i += 1
            items.append(item)
        tag = "ol" if ordered else "ul"
        start = ""
        if ordered:
            number = int(re.match(r"\d+", first.group(2)).group(0))
            if number != 1:
                start = f' start="{number}"'
        body = []
        for item in items:
            task = re.match(r"^\[([ xX])\]\s+(.*)", item[0])
            if task:
                item[0] = task.group(2)
            content = self.blocks(item)
            if task:
                checkbox = '<input type="checkbox" disabled' + (" checked" if task.group(1).lower() == "x" else "") + ' aria-label="Completed"' + ">"
                content = content.replace("<p>", "<p>" + checkbox + " ", 1)
            body.append(f"<li>{content}</li>")
        return f"<{tag}{start}>" + "".join(body) + f"</{tag}>", i

    @staticmethod
    def table_separator(line: str) -> bool:
        cells = line.strip().strip("|").split("|")
        return bool(cells) and all(re.fullmatch(r"\s*:?-{3,}:?\s*", cell) for cell in cells)

    def table(self, rows: list[str]) -> str:
        def cells(row: str) -> list[str]:
            row = row.strip()
            if row.startswith("|"):
                row = row[1:]
            if row.endswith("|") and not row.endswith("\\|"):
                row = row[:-1]
            parts, current, code_marker = [], "", ""
            i = 0
            while i < len(row):
                ch = row[i]
                if ch == "\\" and i + 1 < len(row):
                    current += row[i:i + 2]
                    i += 2
                    continue
                if ch == "`":
                    marker = re.match(r"`+", row[i:]).group(0)
                    if not code_marker:
                        code_marker = marker
                    elif marker == code_marker:
                        code_marker = ""
                    current += marker
                    i += len(marker)
                    continue
                if ch == "|" and not code_marker:
                    parts.append(current)
                    current = ""
                else:
                    current += ch
                i += 1
            parts.append(current)
            return [p.strip().replace("\\|", "|") for p in parts]

        header = cells(rows[0])
        aligns = []
        for spec in cells(rows[1]):
            left, right = spec.startswith(":"), spec.endswith(":")
            aligns.append("center" if left and right else "right" if right else "")
        out = ['<div class="table-wrap"><table><thead><tr>']
        for k, cell in enumerate(header):
            style = f' class="align-{aligns[k]}"' if k < len(aligns) and aligns[k] else ""
            out.append(f"<th{style}>{self.inline(cell)}</th>")
        out.append("</tr></thead><tbody>")
        for row in rows[2:]:
            out.append("<tr>")
            for k, cell in enumerate(cells(row)):
                style = f' class="align-{aligns[k]}"' if k < len(aligns) and aligns[k] else ""
                out.append(f"<td{style}>{self.inline(cell)}</td>")
            out.append("</tr>")
        out.append("</tbody></table></div>")
        return "".join(out)


def page(title: str, body: str, depth: int, source: str | None) -> str:
    up = "../" * depth
    edit = f'<p class="doc-source">Source: <code>{html.escape(source)}</code> in the <a href="https://github.com/meneerkrabs/OpenTPW">project repository</a>.</p>' if source else ""
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(title)} · OpenTPW</title>
<meta name="theme-color" content="#2b1a72">
<link rel="icon" href="{up}../favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="{up}../style.css">
<link rel="stylesheet" href="{up}../docs.css">
<script src="{up}../a11y.js"></script>
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<header class="top"><div class="wrap"><a class="brand" href="{up}../"><img src="{up}../logo.svg" alt="" width="40" height="40"><span>Open<b>TPW</b></span></a>
<nav aria-label="Main"><a href="{up}../install.html">Install</a><a href="{up}index.html">Docs</a><a href="{up}MODS.html">Mods</a><a href="{up}../#download">Download</a><a href="https://github.com/meneerkrabs/OpenTPW">GitHub</a></nav></div></header>
<main id="main" class="wrap page doc">
<!--email_off-->
{body}
{edit}
<!--/email_off-->
</main>
<footer><div class="wrap"><nav aria-label="Footer"><a href="https://github.com/meneerkrabs/OpenTPW">GitHub</a><a href="{up}../privacy.html">Privacy</a><a href="{up}../rules.html">Community rules</a></nav>
<p>OpenTPW is free software under the MIT licence. Theme Park World is a trademark of Electronic Arts Inc. OpenTPW is an independent project and is not affiliated with or endorsed by Electronic Arts or Bullfrog Productions.</p></div></footer>
</body>
</html>
"""


class PageLinks(HTMLParser):
    def __init__(self, text: str):
        super().__init__()
        self.ids: set[str] = set()
        self.feed(text)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        ident = dict(attrs).get("id")
        if ident:
            self.ids.add(ident)


def qualify_stale_anchors(pages: dict[str, str], sources: dict[str, str]) -> list[str]:
    """Keep document navigation working when an old source section was renamed."""
    ids = {path: PageLinks(text).ids for path, text in pages.items()}
    warnings: list[str] = []
    for path, text in list(pages.items()):
        def replace(match: re.Match) -> str:
            href = html.unescape(match.group(1))
            url = urlsplit(href)
            if url.scheme or url.netloc or not url.fragment:
                return match.group(0)
            target = posixpath.normpath(posixpath.join(posixpath.dirname(path), unquote(url.path))) if url.path else path
            if target not in ids or unquote(url.fragment) in ids[target]:
                return match.group(0)
            warnings.append(f"{sources.get(path, path)}: {href}: source section missing; link opens document")
            replacement = href.split("#", 1)[0] or "#main"
            return f'href="{html.escape(replacement, quote=True)}" title="This source section has moved; opens the document instead"'
        pages[path] = re.sub(r'href="([^"]*)"', replace, text)
    return warnings


def build(repo: str, out: str) -> int:
    repo_root = Path(repo).resolve()
    output = Path(out)
    output.mkdir(parents=True, exist_ok=True)
    published = set(document_sources(repo_root))
    entries: list[tuple[str, str, str]] = []
    pages: dict[str, str] = {}
    sources: dict[str, str] = {}
    warnings: list[str] = []
    for rel in sorted(published):
        converter = Converter(rel, str(repo_root), published)
        body = converter.convert(document_text(repo_root, rel))
        title = converter.title or Path(rel).stem
        inner = document_path(rel)
        pages[inner] = page(title, body, inner.count("/"), rel)
        sources[inner] = rel
        entries.append((rel[5:] if rel.startswith("docs/") else rel, inner, title))
        warnings.extend(converter.warnings)
    by_name = {rel: (inner, title) for rel, inner, title in entries}
    featured = [f'<li><a href="{by_name[name][0]}">{html.escape(label)}</a></li>' for name, label in FEATURED if name in by_name]
    shown = {name for name, _ in FEATURED}
    main_docs = sorted((e for e in entries if e[0] not in shown and not e[0].startswith(("reverse/", "tools/"))), key=lambda e: e[2].lower())
    reverse = sorted((e for e in entries if e[0].startswith("reverse/")), key=lambda e: e[2].lower())
    tooling = sorted((e for e in entries if e[0].startswith("tools/")), key=lambda e: (e[2].lower(), e[0]))
    def listing(items):
        return "".join(f'<li><a href="{quote(inner, safe="/.")}">{html.escape(title)}</a> <span class="doc-file">{html.escape(rel[:-3])}</span></li>' for rel, inner, title in items)
    index_body = f"""<h1>Documentation</h1>
<p class="lead">Installation, gameplay, hosting and technical reference for OpenTPW. Players start with <a href="../install.html">Installing OpenTPW</a>.</p>
<h2>Start here</h2><ul class="doc-list">{''.join(featured)}</ul>
<h2>Formats, systems and plans</h2><ul class="doc-list">{listing(main_docs)}</ul>
<h2>Research notes</h2><p>Evidence from the original game's binaries and data, lane by lane.</p><ul class="doc-list">{listing(reverse)}</ul>
<h2>Developer tools</h2><ul class="doc-list">{listing(tooling)}</ul>"""
    pages["index.html"] = page("Documentation", index_body, 0, None)
    warnings.extend(qualify_stale_anchors(pages, sources))
    # Delete only previously generated pages, preserving unrelated manual HTML.
    manifest = output / ".site-docs-manifest.json"
    if manifest.is_file():
        previous = json.loads(manifest.read_text(encoding="utf-8"))
        for inner in previous:
            existing = output / inner
            if inner not in pages and existing.suffix == ".html" and existing.resolve().is_relative_to(output.resolve()) and existing.is_file():
                existing.unlink()
    for inner, text in pages.items():
        target = output / inner
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")
    manifest.write_text(json.dumps(sorted(pages)) + "\n", encoding="utf-8")
    for warning in sorted(set(warnings)):
        print("site_docs warning: " + warning, file=sys.stderr)
    print(f"site_docs: wrote {len(entries)} pages and an index to {os.path.relpath(out, repo)}; {len(set(warnings))} source-link warning(s)")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", default=os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")))
    parser.add_argument("--out", default=None)
    args = parser.parse_args()
    out = args.out or os.path.join(args.repo, "website", "docs")
    os.makedirs(out, exist_ok=True)
    return build(args.repo, out)


if __name__ == "__main__":
    sys.exit(main())
