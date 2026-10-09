"""Demangler for Metrowerks CodeWarrior / MPW (cfront-style) C++ names.

Examples::

    GetRideList__4CityFPvUli            -> City::GetRideList(void*, unsigned long, int)
    __ct__Q213SamsUtilities7UDialogFv   -> SamsUtilities::UDialog::UDialog()
    FindReverse__15TbStringBase<w>CFPCwUi
        -> TbStringBase<wchar_t>::FindReverse(const wchar_t*, unsigned int) const

Returns the input unchanged when it is not a mangled name. Written from the
cfront/ARM mangling description; covers the forms that occur in the Feral
SimThemePark libraries (classes, nested Q-names, templates in angle brackets,
const methods, pointers/references/arrays/function pointers, T/N repeats,
operators, vtables).

Script use: ``python3 -I demangle.py NAME...`` or pipe names on stdin.
"""

from __future__ import annotations

import sys

BUILTIN = {
    "v": "void", "c": "char", "s": "short", "i": "int", "l": "long", "x": "long long",
    "f": "float", "d": "double", "r": "long double", "b": "bool", "w": "wchar_t", "e": "...",
}

OPERATORS = {
    "nw": "operator new", "dl": "operator delete", "nwa": "operator new[]", "dla": "operator delete[]",
    "pl": "operator+", "mi": "operator-", "ml": "operator*", "dv": "operator/", "md": "operator%",
    "er": "operator^", "ad": "operator&", "or": "operator|", "co": "operator~", "nt": "operator!",
    "as": "operator=", "lt": "operator<", "gt": "operator>", "apl": "operator+=", "ami": "operator-=",
    "amu": "operator*=", "adv": "operator/=", "amd": "operator%=", "aer": "operator^=",
    "aad": "operator&=", "aor": "operator|=", "ls": "operator<<", "rs": "operator>>",
    "ars": "operator>>=", "als": "operator<<=", "eq": "operator==", "ne": "operator!=",
    "le": "operator<=", "ge": "operator>=", "aa": "operator&&", "oo": "operator||", "pp": "operator++",
    "mm": "operator--", "cm": "operator,", "rm": "operator->*", "rf": "operator->", "cl": "operator()",
    "vc": "operator[]",
}


class _Parser:
    def __init__(self, s: str):
        self.s = s
        self.i = 0
        self.types: list[str] = []  # for T/N back references (parameters)

    def peek(self) -> str:
        return self.s[self.i] if self.i < len(self.s) else ""

    def eat(self, ch: str) -> bool:
        if self.s.startswith(ch, self.i):
            self.i += len(ch)
            return True
        return False

    def number(self) -> int:
        j = self.i
        while self.i < len(self.s) and self.s[self.i].isdigit():
            self.i += 1
        if j == self.i:
            raise ValueError("number expected")
        return int(self.s[j:self.i])

    def lname(self) -> str:
        n = self.number()
        name = self.s[self.i:self.i + n]
        if len(name) != n:
            raise ValueError("short name")
        self.i += n
        return demangle_template_args(name)

    def qualified(self) -> list[str]:
        if self.eat("Q"):
            if self.eat("_"):
                count = self.number()
                self.eat("_")
            else:
                count = int(self.s[self.i])
                self.i += 1
            return [self.lname() for _ in range(count)]
        return [self.lname()]

    def type(self) -> str:
        prefix = []
        while True:
            ch = self.peek()
            if ch == "C":
                self.i += 1
                prefix.append("const")
            elif ch == "V":
                self.i += 1
                prefix.append("volatile")
            elif ch == "U":
                self.i += 1
                prefix.append("unsigned")
            elif ch == "S":
                self.i += 1
                prefix.append("signed")
            else:
                break
        ch = self.peek()
        if ch == "P":
            self.i += 1
            inner = self.type()
            return self._ptr(inner, "*", prefix)
        if ch == "R":
            self.i += 1
            inner = self.type()
            return self._ptr(inner, "&", prefix)
        if ch == "A":
            self.i += 1
            n = self.number()
            self.eat("_")
            inner = self.type()
            return f"{inner}[{n}]"
        if ch == "F":
            self.i += 1
            params = self.params(stop="_")
            ret = "void"
            if self.eat("_"):
                ret = self.type()
            return f"{ret} (@)({params})"
        if ch == "M":
            self.i += 1
            cls = "::".join(self.qualified())
            inner = self.type()
            return f"{inner} {cls}::*"
        if ch in BUILTIN:
            self.i += 1
            return " ".join(prefix + [BUILTIN[ch]])
        if ch.isdigit() or ch == "Q":
            return " ".join(prefix + ["::".join(self.qualified())])
        raise ValueError(f"bad type at {self.i} in {self.s!r}")

    @staticmethod
    def _ptr(inner: str, op: str, prefix: list[str]) -> str:
        if "(@" in inner:  # pointer/reference to function: decorate the declarator
            text = inner.replace("(@", f"({op}@", 1)
        else:
            text = f"{inner}{op}"
        if prefix:
            text = " ".join(prefix) + " " + text
        return text

    def params(self, stop: str = "") -> str:
        out: list[str] = []
        while self.i < len(self.s) and self.peek() != stop:
            if self.eat("T"):
                n = int(self.s[self.i])
                self.i += 1
                t = self.types[n - 1] if 0 < n <= len(self.types) else "?"
                out.append(t)
                self.types.append(t)
                continue
            if self.eat("N"):
                count = int(self.s[self.i])
                n = int(self.s[self.i + 1])
                self.i += 2
                t = self.types[n - 1] if 0 < n <= len(self.types) else "?"
                for _ in range(count):
                    out.append(t)
                    self.types.append(t)
                continue
            t = self.type()
            out.append(t)
            self.types.append(t)
        if out == ["void"]:
            return ""
        return ", ".join(out)


def demangle_template_args(name: str) -> str:
    """Template arguments inside <> are themselves mangled types."""
    if "<" not in name or not name.endswith(">"):
        return name
    base, args = name.split("<", 1)
    args = args[:-1]
    parts, depth, cur = [], 0, ""
    for ch in args:
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
            continue
        depth += ch == "<"
        depth -= ch == ">"
        cur += ch
    parts.append(cur)
    out = []
    for p in parts:
        try:
            parser = _Parser(p)
            t = parser.type()
            out.append(t if parser.i == len(p) else p)
        except (ValueError, IndexError):
            out.append(p)
    return f"{base}<{', '.join(out)}>"


def _split(mangled: str) -> tuple[str, str] | None:
    start = 2 if mangled.startswith("__") else 0
    k = mangled.find("__", start)
    while k != -1:
        rest = mangled[k + 2:]
        if rest and (rest[0].isdigit() or rest[0] in "QFC"):
            return mangled[:k], rest
        k = mangled.find("__", k + 1)
    return None


def demangle(mangled: str) -> str:
    parts = _split(mangled)
    if parts is None:
        return mangled
    func, rest = parts
    try:
        p = _Parser(rest)
        cls: list[str] = []
        if p.peek().isdigit() or p.peek() == "Q":
            cls = p.qualified()
        if func == "__vt":
            return "vtable for " + "::".join(cls)
        const = p.eat("C")
        if not p.eat("F"):
            # static data member: name__Class
            if p.i == len(rest) and cls:
                return "::".join(cls + [func])
            return mangled
        params = p.params()
        if func == "__ct":
            func = cls[-1].split("<")[0] if cls else func
        elif func == "__dt":
            func = "~" + (cls[-1].split("<")[0] if cls else func)
        elif func.startswith("__op"):
            func = "operator " + _Parser(func[4:]).type()
        elif func.startswith("__") and func[2:] in OPERATORS:
            func = OPERATORS[func[2:]]
        full = "::".join(cls + [func])
        return (f"{full}({params})" + (" const" if const else "")).replace("@", "")
    except (ValueError, IndexError):
        return mangled


def class_of(mangled: str) -> str:
    """Return the class part of a mangled member name ('' for free functions)."""
    parts = _split(mangled)
    if parts is None:
        return ""
    try:
        p = _Parser(parts[1])
        if p.peek().isdigit() or p.peek() == "Q":
            return "::".join(p.qualified())
    except (ValueError, IndexError):
        pass
    return ""


if __name__ == "__main__":
    names = sys.argv[1:] or [line.strip() for line in sys.stdin if line.strip()]
    for n in names:
        print(f"{n}\t{demangle(n)}")
