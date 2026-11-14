using System.Text;
using WorkshopManager.Helpers;

namespace WorkshopManager.Tests;

[TestClass]
public class DataFolderRenameTests
{
    private static string NewParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "swm-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return parent;
    }

    [TestMethod]
    public void ResolveAppFolder_MovesTheLegacyFolderWithItsContent()
    {
        var parent = NewParent();
        try
        {
            var legacy = Path.Combine(parent, AppPaths.LegacyAppName);
            Directory.CreateDirectory(Path.Combine(legacy, "sessions"));
            File.WriteAllText(Path.Combine(legacy, "sessions", "a.json"), "{}");

            var resolved = AppPaths.ResolveAppFolder(parent);

            Assert.AreEqual(Path.Combine(parent, AppPaths.AppName), resolved);
            Assert.IsFalse(Directory.Exists(legacy));
            Assert.AreEqual("{}", File.ReadAllText(Path.Combine(resolved, "sessions", "a.json")));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [TestMethod]
    public void ResolveAppFolder_KeepsBothWhenTheNewFolderAlreadyExists()
    {
        var parent = NewParent();
        try
        {
            var legacy = Path.Combine(parent, AppPaths.LegacyAppName);
            var current = Path.Combine(parent, AppPaths.AppName);
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(current);

            Assert.AreEqual(current, AppPaths.ResolveAppFolder(parent));
            Assert.IsTrue(Directory.Exists(legacy));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [TestMethod]
    public void ResolveAppFolder_FreshInstall_UsesTheNewName()
    {
        var parent = NewParent();
        try
        {
            Assert.AreEqual(Path.Combine(parent, AppPaths.AppName), AppPaths.ResolveAppFolder(parent));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [TestMethod]
    public void ProtectedFile_ReadsALegacyBlobAndRewritesItWithTheNewEntropy()
    {
        var parent = NewParent();
        try
        {
            var path = Path.Combine(parent, "credentials.bin");
            var legacy = Encoding.UTF8.GetBytes("old");
            var current = Encoding.UTF8.GetBytes("new");
            ProtectedFile.Write(path, [1, 2, 3], legacy);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ProtectedFile.Read(path, current, legacy));
            // Rewritten: the new entropy alone now reads it.
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ProtectedFile.Read(path, current));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
