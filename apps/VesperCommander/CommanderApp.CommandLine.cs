using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Command line (Left arrow) ────────────────────────────────────────
    // A shell prompt at the bottom bar: type a command and Enter runs it in a terminal
    // at the active pane's folder; an empty command opens the configured terminal there.
    private bool _cmdActive;
    private string _cmdText = "";
    private bool _pendingCmdFocus;
    private readonly Ref<TextInputElement> _cmdRef = new();

    private void OpenCommandLine(char side)
    {
        _active = side;
        _cmdActive = true;
        _cmdText = "";
        _pendingCmdFocus = true;
        MarkDirty();
    }

    private void CloseCommandLine()
    {
        _cmdActive = false;
        _cmdText = "";
        PaneRef(_active).Current?.Focus();
        MarkDirty();
    }

    private void RunCommandLine()
    {
        var cmd = _cmdText.Trim();
        var dir = Dir(_active);
        _cmdActive = false;
        _cmdText = "";
        // "cd <path>" navigates the pane instead of spawning a shell (bare "cd" goes
        // home) — same resolution as the Go-to-path prompt; errors post a notice.
        if (cmd.Equals("cd", StringComparison.OrdinalIgnoreCase)
            || cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            var target = cmd.Length > 2 ? cmd[3..].Trim() : "~";
            if (GotoPath(_active, target.Length > 0 ? target : "~") is { } err)
            {
                _notice = err;
                MarkDirty();
            }
        }
        else if (cmd.Length > 0) FileOps.RunInTerminal(cmd, dir);
        else FileOps.OpenTerminal(_settings.Terminal, dir);
        PaneRef(_active).Current?.Focus();
        MarkDirty();
    }

    /// <summary>Open the configured terminal at the active folder (palette / no-command).</summary>
    private void OpenTerminalHere() => FileOps.OpenTerminal(_settings.Terminal, Dir(_active));

    /// <summary>Open the configured git client at the active folder (palette). No-op if unset.</summary>
    private void OpenGitClientHere() => FileOps.OpenGitClient(_settings.GitClient, Dir(_active));

    private void HandleCommandKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: CloseCommandLine(); e.StopPropagation = true; break;
            case KeyCode.Enter: RunCommandLine(); e.StopPropagation = true; break;
            // Other keys fall through to the focused command input.
        }
    }
}
