using Xunit;

namespace Junction.Tests;

/// <summary>
/// A test on a reftable repository (<see cref="TempRepo.CreateWithInitialCommit"/> with
/// <c>reftable: true</c>). Git makes them from 2.45 on (<c>git init --ref-format=reftable</c>);
/// an older git skips the test, naming what it needs.
/// </summary>
public sealed class ReftableFactAttribute : FactAttribute
{
    public ReftableFactAttribute()
    {
        if (!TempRepo.SupportsReftable)
            Skip = "This git cannot make a reftable repository (git init --ref-format=reftable needs git 2.45 or later).";
    }
}
