using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── A secret's stored value: change it, forget it, or un-flag the key ──
    //
    // The keyring holds a secret's value for this machine alone; these manage it from the
    // Environments table, never from the OS's credential manager. Change and forget act on the
    // keyring at once, as setting a value does: they touch no file, so Ctrl+S has nothing to write
    // for them. Un-flagging edits the files, so what it does to the keyring waits for the Ctrl+S
    // that writes them (ForgetRemovedSecrets).

    /// <summary>Change and forget, at the end of a secret's masked cell. Icons, 22px each, so the
    /// cell keeps room for the mask and the reveal with three environments side by side.</summary>
    private Element SecretValueActions(string envName, string key) =>
        Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center).Children(
            CellAction(Icons.ChangeValue, $"change {key}'s value on this machine", "change value",
                $"change-secret-{key}", () => OpenChangeSecretDialog(envName, key)),
            CellAction(Icons.ForgetValue, $"forget {key}'s value on this machine", "forget on this machine",
                $"forget-secret-{key}", () => ConfirmForgetSecret(envName, key)));

    private static Element CellAction(string glyph, string name, string tooltip, string automationId, Action onClick) =>
        Ui.IconButton(glyph, 13f, Azimuth.TextMuted, name, onClick, automationId, Ui.TreeAction).Tooltip(tooltip);

    /// <summary>Asks for a new value and stores it over the old one, which is never shown meanwhile:
    /// the field starts empty, a reveal under way ends, and an empty answer is refused rather than
    /// stored.</summary>
    private void OpenChangeSecretDialog(string envName, string key)
    {
        EndReveal(key);
        OpenDialog($"Change {key}", "new value — stored in the OS keyring, never in the file", value =>
            {
                if (!StoreSecret(envName, key, value))
                    Flash("No keyring on this platform — set it as an environment variable instead");
                else
                    Flash($"{key} changed on this machine");
                MarkDirty();
            },
            value => value.Length == 0 ? $"Type the new value. To remove {key}'s value from this machine, use forget." : null);
    }

    private void ConfirmForgetSecret(string envName, string key) =>
        OpenConfirm($"Forget {key} on this machine?",
            $"This machine's keyring drops the value of {key}, and Meridian can't bring it back. The files "
            + $"still mark {key} secret, so requests that use it won't send until a value is set again. "
            + "Other machines keep their own.",
            "Forget", () => ForgetSecret(envName, key));

    /// <summary>Drops this machine's value; the cell asks for one again. A value that still
    /// resolves after the delete comes from an environment variable of the key's name (the CI
    /// route), which is not Meridian's to change, and the toast says so.</summary>
    internal void ForgetSecret(string envName, string key)
    {
        ForgetStoredSecret(envName, key);
        EndReveal(key);
        if (StoredSecret(envName, key) is not null)
            Flash($"{key} still comes from an environment variable of that name, which Meridian can't change");
        MarkDirty();
    }

    /// <summary>The SECRET toggle, off. When this machine holds a value for the key, it asks what
    /// becomes of it: moved into the files that mark the key, in plain text, or forgotten. Either
    /// edits the table now and reaches the keyring on the Ctrl+S that writes the files, so nothing
    /// is lost before the save and re-marking the key undoes it. A key with no stored value loses
    /// its flag without asking.</summary>
    private void AskUnmarkSecret(string key)
    {
        var stored = MarkedEnvironments(key).Where(env => StoredSecret(env, key) is not null).ToList();
        if (stored.Count == 0)
        {
            SetSecret(key, false);
            return;
        }

        bool emptyInFile = MarkedEnvironments(key).All(env => _envDocs[env].Values.GetValueOrDefault(key, "") == "");
        string forget = $"Forget it, and {key} takes its value from the file{(emptyInFile ? ", which is empty" : "")}.";
        string when = $"Nothing changes until {Shortcuts.Keys("save")} saves the files.";
        string title = $"Stop keeping {key} secret?";

        if (stored.Any(env => StoredSecret(env, key) is { } value && HasLineBreak(value)))
        {
            OpenConfirm(title,
                $"This machine's keyring holds a value for {key} with a line break in it, which a line of "
                + $"a .env file can't hold. {forget} {when}",
                "Forget value", () => SetSecret(key, false));
            return;
        }

        string files = stored.Count == 1 ? stored[0] + ".env"
            : string.Join(", ", stored.Take(stored.Count - 1).Select(env => env + ".env")) + " and " + stored[^1] + ".env";
        OpenConfirm(title,
            $"This machine's keyring holds a value for {key}. Move it into {files}, and it is saved there "
            + "as plain text: shown in this table, and readable by anyone who can read the file or the "
            + $"repository it is committed to. {forget} {when}",
            stored.Count == 1 ? $"Move into {files}" : "Move into the files", () => MoveSecretIntoFiles(key),
            alternative: ("Forget value", () => SetSecret(key, false)));
    }

    /// <summary>Un-flags the key and writes each marking file's stored value into it: the value the
    /// key resolved to there, so a request sends what it sent before. The keyring forgets it on the
    /// save, as for <see cref="SetSecret"/>, leaving the file the one copy. Every document is edited
    /// before the one build: <see cref="SetSecret"/> builds the table at once, and an edit after it
    /// would reach the file but not the cell.</summary>
    internal void MoveSecretIntoFiles(string key)
    {
        _removedSecrets.Add(key);
        foreach (var env in MarkedEnvironments(key))
        {
            var doc = _envDocs[env];
            string? value = StoredSecret(env, key);
            doc.UnmarkSecret(key);   // first: a flag-only key's `secret` line becomes the `key=` line set here
            if (value is not null && !HasLineBreak(value)) doc.SetValue(key, value);
        }
        MarkDirty();
    }

    private List<string> MarkedEnvironments(string key) =>
        _envFiles.Select(e => e.Name).Where(name => _envDocs.GetValueOrDefault(name)?.IsSecret(key) == true).ToList();

    private static bool HasLineBreak(string value) => value.AsSpan().IndexOfAny('\r', '\n') >= 0;

    /// <summary>Masks the key again, so a changed or forgotten value is never the one on show.</summary>
    private void EndReveal(string key)
    {
        if (_reveal is { } r && r.Key == key) _reveal = null;
    }

    // ── This file's keyring calls, in one place ──────────────────────────
    // The keyring is scoped by workspace today: every environment shares a key's one value, so the
    // environment these take is not part of the entry yet.

    private string? StoredSecret(string envName, string key) => _keyring.Get(_workspace!.Root, key);

    private bool StoreSecret(string envName, string key, string value) => _keyring.Set(_workspace!.Root, key, value);

    private bool ForgetStoredSecret(string envName, string key) => _keyring.Delete(_workspace!.Root, key);
}
