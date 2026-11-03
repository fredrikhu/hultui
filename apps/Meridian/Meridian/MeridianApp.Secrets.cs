using Meridian.Core;

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

    // Change and forget live in the secret cell's menu (SecretActions), beside the step between an
    // environment's own value and the shared one. Each acts on one entry: an environment's own
    // value (envName), or the shared value (null), which every environment without its own uses.

    /// <summary>The environments that use the shared value: those marking the key with no own value.</summary>
    private List<string> SharedSecretUsers(string key) =>
        MarkedEnvironments(key).Where(env => _keyring.Get(_workspace!.Root, env, key) is null).ToList();

    /// <summary>"local and staging", "local, staging and ci".</summary>
    private static string Names(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    /// <summary>Asks for a new value and stores it over the old one, which is never shown meanwhile:
    /// the field starts empty, a reveal under way ends, and an empty answer is refused rather than
    /// stored.</summary>
    /// <param name="envName">The environment whose own value changes, or null for the shared one.</param>
    private void OpenChangeSecretDialog(string? envName, string key)
    {
        EndReveal(key);
        string scope = envName ?? "every environment";
        OpenDialog($"Change {key} for {scope}", "new value — stored in the OS keyring, never in the file", value =>
            {
                if (!StoreSecret(envName, key, value))
                    FlashNoKeyring(envName, key);
                else
                    Flash($"{key} changed on this machine for {scope}");
                MarkDirty();
            },
            value => value.Length == 0 ? $"Type the new value. To remove {key}'s value from this machine, use forget." : null);
    }

    /// <param name="envName">The environment whose own value goes, or null for the shared one.</param>
    private void ConfirmForgetSecret(string? envName, string key)
    {
        string after;
        if (envName != null)
            after = _keyring.Get(_workspace!.Root, null, key) is not null
                ? $"{envName} then uses the shared value."
                : $"The files still mark {key} secret, so {envName}'s requests that use it won't send until a value is set again.";
        else
        {
            var users = SharedSecretUsers(key);
            after = users.Count == 0 ? "No environment uses it: each has a value of its own."
                : $"{Names(users)} {(users.Count == 1 ? "uses" : "use")} it, and won't send requests that use {key} until a value is set again.";
        }
        OpenConfirm(envName != null ? $"Forget {key} for {envName} on this machine?" : $"Forget the shared {key} on this machine?",
            $"This machine's keyring drops the value, and Meridian can't bring it back. {after} Other machines keep their own.",
            "Forget", () => ForgetSecret(envName, key));
    }

    /// <summary>Drops this machine's value; the cell falls back to the shared value, or asks for
    /// one. A value that still resolves from an environment variable (the CI route) is not
    /// Meridian's to change, and the toast says so.</summary>
    /// <param name="envName">The environment whose own value goes, or null for the shared one.</param>
    internal void ForgetSecret(string? envName, string key)
    {
        ForgetStoredSecret(envName, key);
        EndReveal(key);
        foreach (var env in envName != null ? new List<string> { envName } : SharedSecretUsers(key))
            if (Secrets.Resolve(_keyring, _workspace!.Root, env, key) is { Source: SecretSource.Variable or SecretSource.SharedVariable } left)
            {
                string variable = left.Source == SecretSource.Variable ? Secrets.VariableName(env, key) : key;
                Flash($"{key} still comes from the environment variable {variable}, which Meridian can't change");
                break;
            }
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

    /// <summary>The keyring value <paramref name="envName"/> uses: its own, else the shared one. Not
    /// an environment variable's — that is no stored value of this machine's keyring.</summary>
    private string? StoredSecret(string envName, string key) =>
        _keyring.Get(_workspace!.Root, envName, key) ?? _keyring.Get(_workspace!.Root, null, key);

    /// <param name="envName">The environment, or null for the shared value.</param>
    private bool StoreSecret(string? envName, string key, string value) => _keyring.Set(_workspace!.Root, envName, key, value);

    /// <param name="envName">The environment, or null for the shared value.</param>
    private bool ForgetStoredSecret(string? envName, string key) => _keyring.Delete(_workspace!.Root, envName, key);
}
