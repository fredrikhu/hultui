# WebView2 capture spike — W0

**Result: the composited design works on Windows.** RTX 4080, WebView2 runtime
154.0.4258.37, 120 Hz display at 150 %, 2026-09-30. Raw output in
[`results/windows.txt`](results/windows.txt).

This is spike W0 of [`docs/design/WEBVIEW.md`](../../docs/design/WEBVIEW.md): before any
package code, does WebView2's composition controller render into a visual we can
capture offscreen, and what does that cost?

## What it does

A HultUI window with a toolbar (a `Dropdown`, to put an overlay over the page) and an
`ImageElement` over a `PixelFrameSink`. WebView2 is created with
`CreateCoreWebView2CompositionController` parented to the SDL window; its
`RootVisualTarget` is a `Windows.UI.Composition` `ContainerVisual` attached to **no**
window target. `GraphicsCaptureItem.CreateFromVisual` captures that visual into a
free-threaded frame pool; each frame is copied through a staging texture into the sink.
Pointer events on the element go to `SendMouseInput`; `CursorChanged` sets the element's
cursor.

It uses Microsoft's managed wrapper and the CsWinRT projection for speed. Neither is
NativeAOT-clean; the package will not use them. Two members the wrapper does not expose
(`ICoreWebView2CompositionController2::AutomationProvider`,
`ICoreWebView2Environment4::GetAutomationProviderForWindow`) are called through their
vtables — and the first guess at the second's slot was wrong (12, not 11), which is the
design's "copy vtable order from the IDL, never recall it" rule proving itself.

## Running it

```bash
dotnet run --project spikes/WebView2Capture -- measure
dotnet run --project spikes/WebView2Capture -- interactive
```

`measure` runs the probes below and closes; it injects no OS input, only
`SendMouseInput` into WebView2. `interactive` opens a random Wikipedia article to click,
scroll and type in by hand — the way to check IME and the overlay over the page.

## Answers

| Question | Answer |
|---|---|
| Does a visual attached to no window render and capture? | **Yes.** First frame with the page 104 ms after `NavigateToString` (navigation completed at 17 ms); one dark frame (`#121212`) before it. |
| Where does the page land inside the root visual? | **At its origin, whatever `Bounds.X/Y` is.** No cropping, and `Bounds` can carry the element's true position in the window. |
| Latency, page change → captured frame | **Median 6.7 ms, p90 11.0, max 17.2** over 40 colour changes (script round trip 0.3 ms). Drawing it adds at most one display interval. |
| Cost of a continuously animating page, 1650×1072 | Captured at **60 fps**; staging copy 1.7 ms avg, 3.0 max. **Our process 291–334 ms/s CPU composited vs 3–6 ms/s windowed**; the browser's processes cost the same either way (141–200 ms/s). A still page costs nothing: frames arrive only on change. |
| Keyboard focus | WebView2 keeps one 0×0 child, `Chrome_WidgetWin_0`, under our window. `MoveFocus` puts Win32 focus there (page `hasFocus()` true); `SetFocus(ours)` takes it back (false). **`Window.Activated` fires on neither** — SDL sees no window focus change. |
| `<select>` popup | Opens as the browser's own top-level window at screen (1430, 876); the element's bottom-left is at (1430, 870). **Correct**, via `Bounds`. Mouse points are **bounds-relative**. |
| Accessibility | The provider is an **override provider** (`ProviderOptions` 42 = ServerSide + OverrideProvider + UseComThreading), with a host provider, no parent, and a client-relative bounding rectangle. `GetAutomationProviderForWindow(Chrome_WidgetWin_0)` returns the same object. The intended route is `IRawElementProviderHwndOverride` on our window for that child. Its children were empty with no UIA client attached (Chromium builds its tree lazily). **Step 4 found more** (`-- a11y` runs only this probe): the children stay empty with a client attached, `ElementProviderFromPoint` and `GetFocus` answer null in screen and in client coordinates, and `NotifyParentWindowPositionChanged` does not move the rectangle. The provider carries nothing; it only identifies the window. |

## What it changes in the design

- The CPU cost is real for animated content: about a third of a core for a large
  animating page, from the staging copy plus Skia re-uploading 7 MB per drawn frame.
  Zero-copy (phase 3) is worth doing, not just nice; for static and mostly static pages the
  pixel path is free.
- Accessibility on Windows is an HWND override, not a fragment splice (seam W8) — but with
  **our** node as the override provider, not WebView2's, which turned out to be empty. See
  *Accessibility* in the design doc for what step 4 built.
- The focus hand-off is exactly as designed, and the `Window.Activated` worry is gone.

## Not answered here (by hand, in `interactive`)

- IME composition and candidate-window position in a page `<input>`.
- How it feels: scrolling, text selection by drag, the toolbar dropdown opening over the page.
- Why captured frames are 60 fps on a 120 Hz display (Chromium's own rate in visual
  hosting, or the capture's) — matters only for smoothness, not for correctness.
