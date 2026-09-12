using HultUI;
using HultUI.Core.Platform;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// The real Windows Credential Manager, under a throw-away service name that is deleted
/// again in the same test. Skipped as a pass on other platforms — there is no store to test,
/// and <see cref="NoCredentialStore"/> is the documented answer there.
/// </summary>
public class CredentialStoreTests
{
    [Fact]
    public void ASecretRoundTripsThroughTheOsStoreAndCanBeDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore();
        var service = "junction-tests|" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(service, "token"));
            Assert.True(store.Write(service, "token", "glpat-Ünïcode-ok-1234"));
            Assert.Equal("glpat-Ünïcode-ok-1234", store.Read(service, "token"));

            Assert.True(store.Write(service, "token", "replaced"));
            Assert.Equal("replaced", store.Read(service, "token"));

            Assert.True(store.Delete(service, "token"));
            Assert.Null(store.Read(service, "token"));
            Assert.True(store.Delete(service, "token"));    // gone already: still the outcome asked for
        }
        finally
        {
            store.Delete(service, "token");
        }
    }

    [Fact]
    public void AValueTooLongForAGenericCredentialIsRefusedNotTruncated()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore();
        var service = "junction-tests|" + Guid.NewGuid().ToString("N");

        Assert.False(store.Write(service, "token", new string('x', 1281)));
        Assert.Null(store.Read(service, "token"));
    }

    [Fact]
    public void TheDefaultStoreMatchesThePlatform()
    {
        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsCredentialStore>(CredentialStore.Default);
        else Assert.IsType<NoCredentialStore>(CredentialStore.Default);
    }
}
