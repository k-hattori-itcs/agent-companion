using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void InstanceDataDirectory_IsStableForSameExecutableDirectory()
    {
        var first = AppDataPaths.GetInstanceDataDirectory("C:\\Apps\\CodexPet", "C:\\Users\\test\\AppData\\Local");
        var second = AppDataPaths.GetInstanceDataDirectory("c:\\apps\\codexpet\\", "C:\\Users\\test\\AppData\\Local");

        Assert.Equal(first, second, ignoreCase: true);
    }

    [Fact]
    public void InstanceDataDirectory_IsolatedPerExecutableDirectory()
    {
        var codex = AppDataPaths.GetInstanceDataDirectory("C:\\Apps\\CodexPet", "C:\\Users\\test\\AppData\\Local");
        var claude = AppDataPaths.GetInstanceDataDirectory("C:\\Apps\\ClaudePet", "C:\\Users\\test\\AppData\\Local");

        Assert.False(string.Equals(codex, claude, StringComparison.OrdinalIgnoreCase));
    }
}
