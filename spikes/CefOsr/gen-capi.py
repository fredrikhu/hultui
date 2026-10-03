#!/usr/bin/env python3
"""Generates Capi.g.cs: CEF's C API as C# structs, enums and exports, from the headers.

Spike L0's answer to "copy struct layouts from the headers, never from memory". clang parses
every capi header at one CEF_API_VERSION (the headers drop and add members by version); a C
program compiled from the same headers prints every struct's size and every field's offset,
so the C# layouts are explicit offsets the compiler measured, not a guess at padding.
Function-pointer members become typed `delegate* unmanaged<...>` fields, so a call with the
wrong arguments, or a callback with the wrong signature, does not compile.

    python3 spikes/CefOsr/gen-capi.py [--include /usr/include/cef] [--api 14600]
"""
import argparse, json, os, re, subprocess, sys, tempfile

ap = argparse.ArgumentParser()
ap.add_argument("--include", default="/usr/include/cef")
ap.add_argument("--api", default="14600")
ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "Capi.g.cs"))
args = ap.parse_args()

capi = os.path.join(args.include, "include", "capi")
headers = ["include/cef_api_hash.h"] + sorted(
    f"include/capi/{f}" for f in os.listdir(capi) if f.endswith(".h"))
work = tempfile.mkdtemp(prefix="cefgen")
with open(os.path.join(work, "all.c"), "w") as f:
    f.write("".join(f'#include "{h}"\n' for h in headers))
cflags = [f"-DCEF_API_VERSION={args.api}", f"-I{args.include}"]
ast = json.loads(subprocess.check_output(
    ["clang", "-Xclang", "-ast-dump=json", "-fsyntax-only", *cflags, os.path.join(work, "all.c")]))

typedefs, records, enums, functions = {}, {}, {}, {}
pending_enum = None   # CEF's enums are anonymous: `typedef enum { ... } name;` names the one before it
for n in ast["inner"]:
    k, name = n["kind"], n.get("name")
    if k == "TypedefDecl":
        typedefs[name] = n["type"]["qualType"]
        if pending_enum is not None and name.startswith("cef_"):
            enums[name] = pending_enum
        pending_enum = None
        continue
    if k == "EnumDecl" and not name:
        pending_enum = [c["name"] for c in n.get("inner", []) if c["kind"] == "EnumConstantDecl"]
        continue
    pending_enum = None
    if k == "RecordDecl" and n.get("completeDefinition") and name and name.startswith("_cef_"):
        fields = [(f["name"], f["type"]["qualType"]) for f in n.get("inner", []) if f["kind"] == "FieldDecl"]
        if any(not fn for fn, _ in fields):
            sys.exit(f"{name}: anonymous member, not handled")
        records[name] = fields
    elif k == "FunctionDecl" and name and name.startswith("cef_") and n.get("storageClass") != "static":
        functions[name] = n["type"]["qualType"]

# Offsets, sizes and enum values from the compiler, not from arithmetic.
probe = ['#include <stdio.h>', '#include <stddef.h>', '#include "all.c"', 'int main(void) {']
for r, fields in records.items():
    probe.append(f'printf("R {r} %zu\\n", sizeof(struct {r}));')
    for fn, _ in fields:
        probe.append(f'printf("F {r} {fn} %zu\\n", offsetof(struct {r}, {fn}));')
for e, values in enums.items():
    probe.append(f'printf("E {e} %zu\\n", sizeof({e}));')
    for v in values:
        probe.append(f'printf("V {e} {v} %lld\\n", (long long){v});')
probe.append("return 0; }")
with open(os.path.join(work, "probe.c"), "w") as f:
    f.write("\n".join(probe))
subprocess.check_call(["clang", "-w", *cflags, f"-I{work}", "-o", os.path.join(work, "probe"), os.path.join(work, "probe.c")])
size, offset, esize, evalue = {}, {}, {}, {}
for line in subprocess.check_output([os.path.join(work, "probe")], text=True).splitlines():
    p = line.split()
    if p[0] == "R": size[p[1]] = int(p[2])
    elif p[0] == "F": offset[(p[1], p[2])] = int(p[3])
    elif p[0] == "E": esize[p[1]] = int(p[2])
    elif p[0] == "V": evalue[(p[1], p[2])] = int(p[3])

PRIM = {"int": "int", "unsigned int": "uint", "long": "long", "unsigned long": "ulong",
        "long long": "long", "unsigned long long": "ulong", "short": "short",
        "unsigned short": "ushort", "char": "byte", "signed char": "sbyte", "unsigned char": "byte",
        "char16_t": "char", "double": "double", "float": "float", "void": "void", "_Bool": "byte",
        "bool": "byte", "wchar_t": "int"}
ENUM_TYPES = {}  # C enum name -> C# underlying type
for e, values in enums.items():
    big = any(evalue[(e, v)] > 0x7FFFFFFF for v in values)
    ENUM_TYPES[e] = ("uint" if big else "int") if esize[e] == 4 else "long"

def split_params(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch == "," and depth == 0:
            out.append(cur); cur = ""
            continue
        depth += ch in "(["
        depth -= ch in ")]"
        cur += ch
    if cur.strip(): out.append(cur)
    return [p.strip() for p in out]

def cs(t, depth=0):
    """C type string -> C# type."""
    t = re.sub(r"\b(const|volatile)\b", "", t).strip()
    t = re.sub(r"\s+", " ", t)
    if "(*)" in t:
        if depth > 0:
            return "void*"   # a function pointer as a parameter: never called from C#
        ret, rest = t.split("(*)", 1)
        params = split_params(rest.strip()[1:-1])
        if params == ["void"]: params = []
        types = [cs(p, depth + 1) for p in params] + [cs(ret, depth + 1)]
        return "delegate* unmanaged<" + ", ".join(types) + ">"
    stars = 0
    while t.endswith("*"):
        stars += 1; t = t[:-1].strip()
    if t.startswith("struct "):
        name = t[len("struct "):]
        base = name[1:] if name.startswith("_cef_") else "void"
        if base != "void" and name not in records: base = "void"   # opaque
    elif t.startswith("union "):
        base = "void"
    elif t.startswith("enum "):
        base = t[len("enum "):]
    elif t in PRIM:
        base = PRIM[t]
    elif t in typedefs:
        return cs(typedefs[t] + "*" * stars, depth)
    else:
        sys.exit(f"unmapped type: {t!r}")
    if base == "void" and stars == 0 and depth == 0:
        return "void"
    return base + "*" * stars

def ident(n):
    return "@" + n if n in ("base", "string", "object", "params", "event", "delegate", "checked",
                             "fixed", "lock", "ref", "out", "in", "is", "as", "operator", "char",
                             "default", "internal", "this", "new", "type") else n

lines = [
    "// <auto-generated> by spikes/CefOsr/gen-capi.py from the CEF capi headers at",
    f"// CEF_API_VERSION={args.api}. Offsets and sizes are the C compiler's. Do not edit. </auto-generated>",
    "#pragma warning disable CS0169, CS0649, IDE1006",
    "using System.Runtime.InteropServices;",
    "",
    "namespace CefOsr.Capi;",
    "",
    "public static class CefApi",
    "{",
    f"    public const int Version = {args.api};",
    "}",
    "",
]
for e, values in sorted(enums.items()):
    lines.append(f"public enum {e} : {ENUM_TYPES[e]}")
    lines.append("{")
    for v in values:
        lines.append(f"    {v} = {evalue[(e, v)]},")
    lines.append("}")
    lines.append("")

for r, fields in sorted(records.items()):
    name = r[1:]
    lines.append(f"[StructLayout(LayoutKind.Explicit, Size = {size[r]})]")
    lines.append(f"public unsafe struct {name}")
    lines.append("{")
    for fn, ft in fields:
        off = offset[(r, fn)]
        m = re.match(r"^(.*)\[(\d+)\]$", ft)
        if m:
            elem, count = cs(m.group(1)), int(m.group(2))
            if elem in ("byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "char", "float", "double"):
                lines.append(f"    [FieldOffset({off})] public fixed {elem} {fn}[{count}];")
            else:
                stride = size["_" + elem] if ("_" + elem) in size else None
                if stride is None: sys.exit(f"{r}.{fn}: array of {elem}")
                for i in range(count):
                    lines.append(f"    [FieldOffset({off + i * stride})] public {elem} {fn}_{i};")
            continue
        lines.append(f"    [FieldOffset({off})] public {cs(ft)} {ident(fn)};")
    lines.append("}")
    lines.append("")

def export_type(ft):
    ret, rest = ft.split("(", 1)
    return cs(ret.strip() + " (*)(" + rest)

lines.append("/// <summary>Every export the headers declare, resolved from a loaded libcef.so; null where absent.</summary>")
lines.append("public static unsafe class Cef")
lines.append("{")
for fn, ft in sorted(functions.items()):
    lines.append(f"    public static {export_type(ft)} {fn};")
lines.append("")
lines.append("    public static void Load(nint library)")
lines.append("    {")
lines.append("        nint p;")
for fn, ft in sorted(functions.items()):
    lines.append(f"        {fn} = NativeLibrary.TryGetExport(library, \"{fn}\", out p) ? ({export_type(ft)})p : null;")
lines.append("    }")
lines.append("}")

text = "\n".join(lines) + "\n"
with open(args.out, "w") as f:
    f.write(text)
print(f"{len(records)} structs, {len(enums)} enums, {len(functions)} exports -> {args.out}")
