using App.Services.Storage;

namespace App.Tests;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void GetModelDirectory_SanitizesRepoId()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        var directory = paths.GetModelDirectory("Systran/faster-whisper-small");

        StringAssert.EndsWith(directory, Path.Combine("models", "Systran--faster-whisper-small"));
    }
}
