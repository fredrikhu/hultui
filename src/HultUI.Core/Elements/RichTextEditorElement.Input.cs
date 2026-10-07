using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;

namespace HultUI.Core.Elements;

public partial class RichTextEditorElement
{
    // The x Up and Down hold to, from the content box's left edge; -1 until a vertical move.
    private float _preferredX = -1;

    private bool _dragging;
    private SelectionUnit _dragUnit;
    private (RichPosition Start, RichPosition End) _dragAnchor;
    private string? _pressedLink;
    private Point _pressPoint;

    /// <summary>Runs on Ctrl+K: an app shows its link editor (a popover with the address) and
    /// calls <see cref="RichEditState.SetLink"/> or <see cref="RichEditState.InsertLink"/>.</summary>
    public Action? LinkRequested { get; set; }

    /// <summary>Runs when a person asks to add a picture, or to change the one selected
    /// (<see cref="RichEditState.CurrentImage"/>): the toolbar's Picture button, the context menu.
    /// An app shows its own picker and calls <see cref="RichEditState.InsertImage"/> or
    /// <see cref="RichEditState.SetImage"/>. Without it, nothing offers pictures.</summary>
    public Action? ImageRequested { get; set; }

    /// <summary>Whether a pointer drag is selecting.</summary>
    public bool IsDragging => _dragging;

    // Layout for the document as it is now — an edit since the last frame has not been laid out
    // — or false before the first frame, when nothing has been measured to move through.
    private bool EnsureFreshLayout()
    {
        if (_renderCtx is null) return false;
        if (!ReferenceEquals(_laidOutDocument, _state.Document))
            EnsureLayout(_renderCtx, _laidOutWidth >= 0 ? _laidOutWidth : Layout.AbsoluteContentBox.Width);
        return _blocks.Length > 0;
    }

    private Point ToDocument(Point screen)
    {
        var content = Layout.AbsoluteContentBox;
        return new Point(screen.X - content.X, screen.Y - content.Y + _scrollY);
    }

    // ── Text and the input method ──

    private void OnTextInput(TextInputEvent evt)
    {
        if (!_focused || _readOnly) return;
        _composition.Clear();
        _state.InsertText(evt.Text);
    }

    private void OnTextEditing(TextEditingEvent evt)
    {
        if (!_focused || _readOnly) return;
        if (!_composition.IsActive && evt.Text.Length > 0 && !_state.Selection.IsCollapsed)
            _state.DeleteSelection();
        _composition.Set(evt.Text, evt.SegmentStart, evt.SegmentLength);
        MarkPaintDirty();
    }

    // ── Keyboard ──

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;
        bool shift = evt.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = evt.Modifiers.HasFlag(KeyModifiers.Ctrl);
        bool alt = evt.Modifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && !alt && TryCtrlShortcut(evt, shift)) { evt.StopPropagation = true; return; }
        if (ctrl && alt && !_readOnly && evt.Digit is int level and <= 6)
        {
            if (level == 0) _state.SetBlockKind(BlockKind.Paragraph);
            else _state.ToggleBlockKind(BlockKind.Heading, level);
            AnnounceBlock();
            evt.StopPropagation = true;
            return;
        }

        switch (evt.Key)
        {
            case KeyCode.Left: MoveHorizontal(-1, ctrl, shift); break;
            case KeyCode.Right: MoveHorizontal(+1, ctrl, shift); break;

            // A viewer has no caret to chase: plain navigation scrolls, as in a read-only text area.
            case KeyCode.Up:
                if (_readOnly && !shift) { ScrollBy(0, -LineScroll()); break; }
                MoveVertical(-1, shift);
                break;
            case KeyCode.Down:
                if (_readOnly && !shift) { ScrollBy(0, LineScroll()); break; }
                MoveVertical(+1, shift);
                break;
            case KeyCode.PageUp:
                if (_readOnly && !shift) { ScrollBy(0, -PageScroll()); break; }
                MovePage(-1, shift);
                break;
            case KeyCode.PageDown:
                if (_readOnly && !shift) { ScrollBy(0, PageScroll()); break; }
                MovePage(+1, shift);
                break;
            case KeyCode.Home:
                if (_readOnly && !shift) { ScrollOffsetY = 0; break; }
                MoveToLineEdge(start: true, document: ctrl, shift);
                break;
            case KeyCode.End:
                if (_readOnly && !shift) { ScrollOffsetY = MaxScrollY(); break; }
                MoveToLineEdge(start: false, document: ctrl, shift);
                break;

            // Ctrl+Enter and Alt+Enter are the form's — submit, most often — not a new paragraph.
            case KeyCode.Enter:
                if (_readOnly || ctrl || alt) return;
                if (shift) _state.InsertLineBreak();
                else _state.SplitBlock();
                break;
            case KeyCode.Backspace:
                if (_readOnly) return;
                _state.DeleteBackward(word: ctrl);
                break;
            case KeyCode.Delete:
                if (_readOnly) return;
                _state.DeleteForward(word: ctrl);
                break;

            // Tab nests a list item and indents code; anywhere else it moves focus on, so a form
            // with an editor in it can still be left from the keyboard.
            case KeyCode.Tab:
                if (_readOnly || ctrl || alt || !TabEdits(shift)) return;
                break;

            default:
                return;
        }
        evt.StopPropagation = true;
        MarkPaintDirty();
    }

    private bool TabEdits(bool shift)
    {
        if (_state.CurrentTable is not null) return _state.NextCell(backward: shift);
        var block = _state.CurrentBlock;
        if (block.IsListItem || (_state.Selection.Start.Block != _state.Selection.End.Block && block.Indent > 0))
        {
            if (shift) _state.Outdent(); else _state.Indent();
            return true;
        }
        if (block.Kind == BlockKind.CodeBlock && !shift)
            return _state.InsertText("    ");
        return false;
    }

    /// <summary>Ctrl plus a letter or digit, by the letter printed on the key.</summary>
    private bool TryCtrlShortcut(KeyDownEvent evt, bool shift)
    {
        switch (evt.ShortcutLetter)
        {
            case 'a': _state.SelectAll(); return true;
            case 'c': Copy(); return true;
            case 'x' when shift: if (!_readOnly && _state.ToggleStyle(InlineStyle.Strike)) AnnounceStyle(InlineStyle.Strike, "Strikethrough"); return true;
            case 'x': if (!_readOnly) Cut(); return true;
            case 'v': if (!_readOnly) Paste(plainText: shift); return true;
            case 'z': if (!_readOnly) { if (shift) _state.Redo(); else _state.Undo(); } return true;
            case 'y': if (!_readOnly) _state.Redo(); return true;
            case 'b': if (!_readOnly && _state.ToggleStyle(InlineStyle.Bold)) AnnounceStyle(InlineStyle.Bold, "Bold"); return true;
            case 'i': if (!_readOnly && _state.ToggleStyle(InlineStyle.Italic)) AnnounceStyle(InlineStyle.Italic, "Italic"); return true;
            case 'u': if (!_readOnly && _state.ToggleStyle(InlineStyle.Underline)) AnnounceStyle(InlineStyle.Underline, "Underline"); return true;
            case 'e': if (!_readOnly && _state.ToggleStyle(InlineStyle.Code)) AnnounceStyle(InlineStyle.Code, "Code"); return true;
            case 'k': if (!_readOnly) LinkRequested?.Invoke(); return true;
        }
        if (shift && !_readOnly && evt.Digit is int d)
        {
            switch (d)
            {
                case 7: _state.ToggleBlockKind(BlockKind.NumberedItem); AnnounceBlock(); return true;
                case 8: _state.ToggleBlockKind(BlockKind.BulletItem); AnnounceBlock(); return true;
                case 9: _state.ToggleBlockKind(BlockKind.TaskItem); AnnounceBlock(); return true;
            }
        }
        return false;
    }

    private float LineScroll() => ResolvedInherited.FontSize * 1.5f;
    private float PageScroll() => Math.Max(LineScroll(), Layout.AbsoluteContentBox.Height - LineScroll());

    private void MoveHorizontal(int direction, bool word, bool extend)
    {
        var sel = _state.Selection;
        _preferredX = -1;
        _upstream = false;
        if (!extend && !sel.IsCollapsed && !word)
        {
            _state.MoveTo(direction < 0 ? sel.Start : sel.End, extend: false);
            return;
        }

        var from = sel.Focus;
        RichPosition to;
        if (!word && VisualStep(from, direction > 0) is { } visual) to = visual;
        else to = direction < 0 ? _state.StepLeft(from, word) : _state.StepRight(from, word);
        _state.MoveTo(to, extend);
    }

    // Across a line that mixes directions an arrow moves by what is seen, not by reading order.
    private RichPosition? VisualStep(RichPosition from, bool right)
    {
        if (!EnsureFreshLayout() || _blocks[from.Block].Paragraph is not { } para) return null;
        int line = para.LineOf(from.Offset, _upstream);
        return para.TryMoveCaretVisually(line, from.Offset, right, out int result) ? new RichPosition(from.Block, result) : null;
    }

    private void MoveVertical(int direction, bool extend)
    {
        if (!EnsureFreshLayout())
        {
            _state.MoveTo(direction < 0 ? _state.StepLeft(_state.Selection.Focus) : _state.StepRight(_state.Selection.Focus), extend);
            return;
        }
        var focus = _state.Selection.Focus;
        if (_preferredX < 0) _preferredX = CaretRect(focus, _upstream).X;
        var step = LineStep(focus, _upstream, direction, _preferredX);
        var (target, upstream) = step ?? (direction < 0 ? _state.Document.Start : _state.Document.End, false);
        _upstream = upstream;
        _state.MoveTo(target, extend);
    }

    private void MovePage(int direction, bool extend)
    {
        if (!EnsureFreshLayout()) return;
        var caret = CaretRect(_state.Selection.Focus, _upstream);
        if (_preferredX < 0) _preferredX = caret.X;
        float page = PageScroll();
        var (target, upstream) = PositionAt(_preferredX, caret.Y + caret.Height / 2 + direction * page);
        ScrollBy(0, direction * page);
        _upstream = upstream;
        _state.MoveTo(target, extend);
    }

    private void MoveToLineEdge(bool start, bool document, bool extend)
    {
        _preferredX = -1;
        if (document || !EnsureFreshLayout())
        {
            _upstream = false;
            _state.MoveTo(start ? _state.Document.Start : _state.Document.End, extend);
            return;
        }
        var (lineStart, lineEnd, endUpstream) = LineBounds(_state.Selection.Focus, _upstream);
        _upstream = !start && endUpstream;
        _state.MoveTo(start ? lineStart : lineEnd, extend);
    }

    // ── Pointer ──

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button == PointerEvent.RightButton && EnsureFreshLayout())
        {
            // The context menu that opens next is about what is under the pointer — a table's
            // rows, a picture — so a press outside the selection moves the caret there first.
            var (p, up) = PositionAt(ToDocument(evt.Position).X, ToDocument(evt.Position).Y);
            var s = _state.Selection;
            if (s.IsCollapsed || p < s.Start || p > s.End)
            {
                _upstream = up;
                _preferredX = -1;
                _state.MoveTo(p, extend: false);
            }
            return;
        }
        if (evt.Button != 0 || !EnsureFreshLayout()) return;
        var at = ToDocument(evt.Position);

        if (!_readOnly && TaskBoxAt(at) is int task)
        {
            _state.ToggleChecked(task);
            return;
        }

        var (pos, upstream) = PositionAt(at.X, at.Y);
        bool follows = _readOnly || evt.Modifiers.HasFlag(KeyModifiers.Ctrl);
        _pressedLink = follows ? LinkUnder(at, pos) : null;
        _pressPoint = evt.Position;

        _dragUnit = DragSelection.UnitFor(evt.ClickCount);
        if (evt.Modifiers.HasFlag(KeyModifiers.Shift) && _dragUnit == SelectionUnit.Character)
            _state.MoveTo(pos, extend: true);
        else
        {
            _dragAnchor = UnitRange(pos);
            _state.Select(_dragAnchor.Start, _dragAnchor.End);
        }
        _upstream = upstream;
        _preferredX = -1;
        _dragging = true;
        evt.CapturePointer();
        MarkPaintDirty();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!_dragging || !EnsureFreshLayout()) return;
        if (_pressedLink is not null && Distance(evt.Position, _pressPoint) > 4f) _pressedLink = null;
        var at = ToDocument(evt.Position);
        var (pos, upstream) = PositionAt(at.X, at.Y);
        _upstream = upstream;
        if (_dragUnit == SelectionUnit.Character)
            _state.MoveTo(pos, extend: true);
        else
        {
            var hit = UnitRange(pos);
            if (hit.Start < _dragAnchor.Start) _state.Select(_dragAnchor.End, hit.Start);
            else _state.Select(_dragAnchor.Start, hit.End);
        }
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_pressedLink is { } link && _state.Selection.IsCollapsed)
            LinkActivated?.Invoke(link);
        _pressedLink = null;
    }

    private static float Distance(Point a, Point b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The range one press covers at <paramref name="pos"/>: the caret, the word, or
    /// on a triple click the block.</summary>
    private (RichPosition Start, RichPosition End) UnitRange(RichPosition pos)
    {
        var text = _state.Document[pos.Block].Text;
        switch (_dragUnit)
        {
            case SelectionUnit.Word:
                var (s, e) = WordBoundary.RangeAt(text, pos.Offset);
                return (pos with { Offset = s }, pos with { Offset = e });
            case SelectionUnit.Line:
                return (pos with { Offset = 0 }, pos with { Offset = text.Length });
            default:
                return (pos, pos);
        }
    }

    // The link of the character under the point: the one after the caret position, or the one
    // before it when the point is left of the caret.
    private string? LinkUnder(Point at, RichPosition pos)
    {
        var block = _state.Document[pos.Block];
        if (block.Image is { Link: { } pictureLink })
        {
            var bl = _blocks[pos.Block];
            var r = bl.PictureRect;
            return new Rect(r.X, bl.Top, r.Width, r.Height).Contains(at) ? pictureLink : null;
        }
        if (block.Text.Length == 0) return null;
        int offset = at.X >= CaretRect(pos, false).X ? pos.Offset : pos.Offset - 1;
        if (offset < 0 || offset >= block.Text.Length) return null;
        return block.MarksAt(offset).Link;
    }

    /// <summary>The task whose box is under document point <paramref name="at"/>.</summary>
    private int? TaskBoxAt(Point at)
    {
        int b = BlockAt(at.Y);
        var bl = _blocks[b];
        if (bl.Block.Kind != BlockKind.TaskItem || bl.Paragraph is not { } para) return null;
        var line = para.Lines[0];
        float size = Math.Max(8f, bl.Style.FontSize * _rt.CheckboxScale);
        var box = CheckboxRect(bl.Block.Indent * _rt.IndentWidth, _rt.IndentWidth, bl.Top + bl.TextY + line.OffsetY,
            line.Height, size);
        // A little larger than drawn: a box this small is easy to miss.
        var hit = new Rect(box.X - 3, box.Y - 3, box.Width + 6, box.Height + 6);
        return hit.Contains(at) ? b : null;
    }

    /// <summary>Called by the window while the pointer is captured outside the editor.</summary>
    public void UpdateDragPosition(Point position)
    {
        if (!_dragging) return;
        OnPointerMove(new PointerMoveEvent { Position = position });
    }

    public void EndDrag() => _dragging = false;

    private void OnScroll(ScrollEvent evt)
    {
        if (!evt.IsFor(this)) return;
        float dy = evt.RemainingDeltaY;
        if (dy == 0) return;
        float before = _scrollY;
        _scrollY = Math.Clamp(before - dy * ScrollSpeed, 0, MaxScrollY());
        bool moved = _scrollY != before;
        if (moved) MarkPaintDirty();
        evt.Consume(this, movedX: false, movedY: moved);
    }

    // ── Clipboard ──

    /// <summary>Copies the selection as rich content, HTML and plain text.</summary>
    public void Copy()
    {
        if (_state.Selection.IsCollapsed || Clipboard is null) return;
        Clipboard.SetData(RichClipboard.Formats(_state.SelectedFragment));
    }

    public void Cut()
    {
        if (_readOnly || _state.Selection.IsCollapsed || Clipboard is null) return;
        Copy();
        _state.DeleteSelection();
    }

    /// <summary>Pastes what another rich editor copied as it was; anything else as text, in the
    /// marks typing would take. <paramref name="plainText"/> pastes text even when there is more.</summary>
    public void Paste(bool plainText = false)
    {
        if (_readOnly || Clipboard is null) return;
        if (!plainText && Clipboard.GetData(RichClipboard.MimeType) is { } data && RichClipboard.Read(data) is { } fragment)
            _state.InsertFragment(fragment);
        else if (Clipboard.GetText() is { Length: > 0 } text)
            _state.InsertText(text);
    }

    // ── Context menu ──

    private IReadOnlyList<ContextMenuItem> BuildContextMenuItems()
    {
        bool hasSelection = !_state.Selection.IsCollapsed;
        var link = _state.LinkRangeAt(_state.Selection.Focus);
        var items = new List<ContextMenuItem>();

        if (link is { } l && LinkActivated is not null)
        {
            items.Add(new ContextMenuItem { Label = "Open link", Action = () => LinkActivated?.Invoke(l.Target) });
            if (!_readOnly && _state.Features.Allows(RichTextFeatures.Links))
                items.Add(new ContextMenuItem { Label = "Remove link", Action = () => _state.SetLink(null) });
            items.Add(ContextMenuItem.Separator());
        }

        if (!_readOnly && _state.CurrentTable is not null)
        {
            bool shape = _state.Features.Allows(RichTextFeatures.Tables);
            items.Add(new ContextMenuItem { Label = "Insert row above", IsDisabled = !shape, Action = () => _state.InsertRow(below: false) });
            items.Add(new ContextMenuItem { Label = "Insert row below", IsDisabled = !shape, Action = () => _state.InsertRow() });
            items.Add(new ContextMenuItem { Label = "Insert column left", IsDisabled = !shape, Action = () => _state.InsertColumn(after: false) });
            items.Add(new ContextMenuItem { Label = "Insert column right", IsDisabled = !shape, Action = () => _state.InsertColumn() });
            if (shape)
                items.Add(new ContextMenuItem
                {
                    Label = "Align column",
                    Children =
                    [
                        new ContextMenuItem { Label = "Left", IsChecked = _state.CurrentBlock.Align == BlockAlign.Start, Action = () => _state.SetAlign(BlockAlign.Start) },
                        new ContextMenuItem { Label = "Centre", IsChecked = _state.CurrentBlock.Align == BlockAlign.Center, Action = () => _state.SetAlign(BlockAlign.Center) },
                        new ContextMenuItem { Label = "Right", IsChecked = _state.CurrentBlock.Align == BlockAlign.End, Action = () => _state.SetAlign(BlockAlign.End) },
                    ],
                });
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem { Label = "Delete row", Action = () => _state.DeleteRow() });
            items.Add(new ContextMenuItem { Label = "Delete column", Action = () => _state.DeleteColumn() });
            items.Add(new ContextMenuItem { Label = "Delete table", IsDanger = true, Action = () => _state.DeleteTable() });
            items.Add(ContextMenuItem.Separator());
        }

        if (!_readOnly && _state.CurrentImage is not null && ImageRequested is not null)
        {
            items.Add(new ContextMenuItem { Label = "Change picture…", Action = () => ImageRequested?.Invoke() });
            items.Add(ContextMenuItem.Separator());
        }

        if (!_readOnly)
        {
            items.Add(new ContextMenuItem { Label = "Undo", ShortcutHint = "Ctrl+Z", IsDisabled = !_state.CanUndo, Action = () => _state.Undo() });
            items.Add(new ContextMenuItem { Label = "Redo", ShortcutHint = "Ctrl+Y", IsDisabled = !_state.CanRedo, Action = () => _state.Redo() });
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem { Label = "Cut", ShortcutHint = "Ctrl+X", IsDisabled = !hasSelection, Action = Cut });
        }
        items.Add(new ContextMenuItem { Label = "Copy", ShortcutHint = "Ctrl+C", IsDisabled = !hasSelection, Action = Copy });
        if (!_readOnly)
            items.Add(new ContextMenuItem
            {
                Label = "Paste", ShortcutHint = "Ctrl+V",
                IsDisabled = Clipboard?.GetText() is null && Clipboard?.HasFormat(RichClipboard.MimeType) != true,
                Action = () => Paste(),
            });
        items.Add(ContextMenuItem.Separator());
        items.Add(new ContextMenuItem { Label = "Select All", ShortcutHint = "Ctrl+A", Action = _state.SelectAll });
        return items;
    }
}
