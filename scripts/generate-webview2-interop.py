#!/usr/bin/env python3
"""Generates src/HultUI.WebView.WebView2/Interop/ComSlots.g.cs: the vtable slot of every
method, and the IID, of the COM and WinRT interfaces the WebView2 engine calls by hand.

Slots are read from the C vtable structs in the headers, which list every method in order
including the inherited ones -- so a slot here is exactly the index into the vtable, IUnknown
(and IInspectable, for WinRT) included. Nothing is typed from memory: the WebView2 spike
found two recalled slots wrong in one afternoon.

Usage (Windows, with the Windows SDK and the Microsoft.Web.WebView2 package restored):

    python scripts/generate-webview2-interop.py

The header locations can be overridden with WEBVIEW2_INCLUDE and WINDOWS_SDK_INCLUDE.
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "HultUI.WebView.WebView2", "Interop", "ComSlots.g.cs")


def newest(pattern):
    found = sorted(glob.glob(pattern))
    if not found:
        sys.exit(f"not found: {pattern}")
    return found[-1]


WEBVIEW2 = os.environ.get("WEBVIEW2_INCLUDE") or newest(
    os.path.join(os.path.expanduser("~"), ".nuget", "packages", "microsoft.web.webview2", "*", "build", "native", "include"))
SDK = os.environ.get("WINDOWS_SDK_INCLUDE") or newest(r"C:\Program Files (x86)\Windows Kits\10\Include\10.*")

# (header, interface) — the interface's own name as the header spells it.
WANTED = {
    os.path.join(WEBVIEW2, "WebView2.h"): [
        "ICoreWebView2Environment",
        "ICoreWebView2Environment3",
        "ICoreWebView2Environment4",
        "ICoreWebView2Controller",
        "ICoreWebView2Controller2",
        "ICoreWebView2Controller3",
        "ICoreWebView2CompositionController",
        "ICoreWebView2CompositionController2",
        "ICoreWebView2",
        "ICoreWebView2Settings",
        "ICoreWebView2Settings2",
        "ICoreWebView2Deferral",
        "ICoreWebView2NavigationStartingEventArgs",
        "ICoreWebView2NavigationCompletedEventArgs",
        "ICoreWebView2NavigationCompletedEventArgs2",
        "ICoreWebView2WebMessageReceivedEventArgs",
        "ICoreWebView2NewWindowRequestedEventArgs",
        "ICoreWebView2ProcessFailedEventArgs",
        "ICoreWebView2MoveFocusRequestedEventArgs",
        "ICoreWebView2AcceleratorKeyPressedEventArgs",
        "ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler",
        "ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler",
        "ICoreWebView2ExecuteScriptCompletedHandler",
        "ICoreWebView2NavigationStartingEventHandler",
        "ICoreWebView2NavigationCompletedEventHandler",
        "ICoreWebView2SourceChangedEventHandler",
        "ICoreWebView2DocumentTitleChangedEventHandler",
        "ICoreWebView2HistoryChangedEventHandler",
        "ICoreWebView2WebMessageReceivedEventHandler",
        "ICoreWebView2NewWindowRequestedEventHandler",
        "ICoreWebView2ProcessFailedEventHandler",
        "ICoreWebView2CursorChangedEventHandler",
        "ICoreWebView2MoveFocusRequestedEventHandler",
        "ICoreWebView2AcceleratorKeyPressedEventHandler",
        # Step 6: the page's own menus, dialogs, downloads and drops.
        "ICoreWebView2_4",
        "ICoreWebView2_11",
        "ICoreWebView2CompositionController3",
        "ICoreWebView2ContextMenuRequestedEventArgs",
        "ICoreWebView2ContextMenuRequestedEventHandler",
        "ICoreWebView2ContextMenuItemCollection",
        "ICoreWebView2ContextMenuItem",
        "ICoreWebView2ContextMenuTarget",
        "ICoreWebView2ScriptDialogOpeningEventArgs",
        "ICoreWebView2ScriptDialogOpeningEventHandler",
        "ICoreWebView2DownloadStartingEventArgs",
        "ICoreWebView2DownloadStartingEventHandler",
        "ICoreWebView2DownloadOperation",
        "ICoreWebView2BytesReceivedChangedEventHandler",
        "ICoreWebView2StateChangedEventHandler",
        "ICoreWebView2PermissionRequestedEventArgs",
        "ICoreWebView2PermissionRequestedEventArgs3",
        "ICoreWebView2PermissionRequestedEventHandler",
    ],
    os.path.join(SDK, "um", "d3d11.h"): [
        "ID3D11Device",
        "ID3D11DeviceContext",
        "ID3D11Texture2D",
    ],
    os.path.join(SDK, "um", "d3d11_4.h"): [
        "ID3D11Multithread",
    ],
    os.path.join(SDK, "shared", "dxgi.h"): [
        "IDXGIDevice",
    ],
    os.path.join(SDK, "winrt", "windows.graphics.capture.h"): [
        "IGraphicsCaptureItem",
        "IGraphicsCaptureItemStatics",
        "IDirect3D11CaptureFrame",
        "IDirect3D11CaptureFramePool",
        "IDirect3D11CaptureFramePoolStatics2",
        "IGraphicsCaptureSession",
    ],
    os.path.join(SDK, "winrt", "windows.ui.composition.h"): [
        "ICompositor",
        "IVisual",
    ],
    os.path.join(SDK, "winrt", "windows.foundation.h"): [
        "IClosable",
    ],
}

# Parameterized WinRT interfaces carry their computed IID in a __declspec(uuid) on the
# template specialization; the name is what the header's C section calls it.
PARAMETERIZED = {
    os.path.join(SDK, "winrt", "windows.graphics.capture.h"): {
        "FramePoolFrameArrivedHandler":
            "ITypedEventHandler<ABI::Windows::Graphics::Capture::Direct3D11CaptureFramePool*, IInspectable*>",
    },
}

METHOD = re.compile(r"\(\s*STDMETHODCALLTYPE\s*\*\s*(\w+)\s*\)")


def vtable(text, name):
    # C section: "typedef struct <name>Vtbl {" or, for WinRT, "... __x_ABI_C..._C<name>Vtbl {".
    m = re.search(r"typedef struct (?:\w+_C)?" + re.escape(name) + r"Vtbl\s*\{(.*?)\}\s*\w*Vtbl;", text, re.S)
    if not m:
        sys.exit(f"no vtable for {name}")
    methods = METHOD.findall(m.group(1))
    if methods[:3] != ["QueryInterface", "AddRef", "Release"]:
        sys.exit(f"{name}: vtable does not start with IUnknown: {methods[:3]}")
    return methods


def iid(text, name):
    m = re.search(r'MIDL_INTERFACE\("([0-9a-fA-F-]{36})"\)\s*' + re.escape(name) + r"\s*:", text)
    if not m:
        sys.exit(f"no IID for {name}")
    return m.group(1).lower()


def main():
    blocks = []
    for header, names in WANTED.items():
        with open(header, encoding="utf-8", errors="replace") as f:
            text = f.read()
        for name in names:
            methods = vtable(text, name)
            lines = [f'    /// <summary>From {os.path.basename(header)}.</summary>',
                     f"    internal static class {name}",
                     "    {",
                     f'        public static readonly Guid IID = new("{iid(text, name)}");']
            seen = set()
            for slot, method in enumerate(methods):
                if slot < 3 or method in seen:
                    continue
                seen.add(method)
                hides = "new " if method in ("GetType", "ToString", "Equals", "GetHashCode") else ""
                lines.append(f"        public {hides}const int {method} = {slot};")
            lines.append("    }")
            blocks.append("\n".join(lines))

    for header, specializations in PARAMETERIZED.items():
        with open(header, encoding="utf-8", errors="replace") as f:
            text = f.read()
        for name, template in specializations.items():
            m = re.search(r'__declspec\(uuid\("([0-9a-fA-F-]{36})"\)\)\s*' + re.escape(template), text)
            if not m:
                sys.exit(f"no IID for {template}")
            blocks.append("\n".join([
                f'    /// <summary>{template.replace("<", "&lt;").replace(">", "&gt;")}, from {os.path.basename(header)}.</summary>',
                f"    internal static class {name}",
                "    {",
                f'        public static readonly Guid IID = new("{m.group(1).lower()}");',
                "        public const int Invoke = 3;",
                "    }"]))

    version = os.path.basename(os.path.dirname(os.path.dirname(os.path.dirname(WEBVIEW2))))
    sdk = os.path.basename(SDK)
    body = "\n\n".join(blocks)
    source = f"""// <auto-generated>
// Generated by scripts/generate-webview2-interop.py from WebView2.h (Microsoft.Web.WebView2 {version})
// and the Windows SDK {sdk} headers. Do not edit; rerun the script.
// </auto-generated>
#pragma warning disable CS1591

namespace HultUI.WebView.WebView2.Interop;

// Vtable slots: the index of each method in the interface's vtable, IUnknown (and, for
// WinRT, IInspectable) included, read from the headers' C vtable structs.

{body}
"""
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(source)
    print(f"wrote {OUT}: {len(blocks)} interfaces")


if __name__ == "__main__":
    main()
