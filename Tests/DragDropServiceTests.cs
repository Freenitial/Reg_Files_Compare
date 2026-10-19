using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class DragDropServiceTests
{
    // A folder gives its .reg files at every depth; a file without extension is taken when it starts
    // like a .reg file; anything else is left out.
    [Fact]
    public void Resolve_FoldersFilesAndFilesWithoutExtension()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var a = Path.Combine(dir, "a.reg");
            File.WriteAllText(a, "Windows Registry Editor Version 5.00\r\n");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            var b = Path.Combine(dir, "sub", "B.REG");
            File.WriteAllText(b, "Windows Registry Editor Version 5.00\r\n");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "text");

            var loose = Directory.CreateTempSubdirectory().FullName;
            var noExtension = Path.Combine(loose, "export");
            File.WriteAllText(noExtension, "Windows Registry Editor Version 5.00\r\n", Encoding.Unicode);
            var plain = Path.Combine(loose, "readme");
            File.WriteAllText(plain, "hello");
            try
            {
                var found = new DragDropService().ResolveDroppedPaths([dir, noExtension, plain, Path.Combine(dir, "missing.reg")]);
                Assert.Equal(new[] { a, b, noExtension }.Order(), found.Order());
            }
            finally { Directory.Delete(loose, recursive: true); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // A dropped folder that cannot be listed at all is reported, not taken for an empty one.
    [Fact]
    public void Resolve_ReportsADroppedFolderThatCannotBeRead()
    {
        var dir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        File.WriteAllText(Path.Combine(dir.FullName, "inside.reg"), "");
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = dir.GetAccessControl();
        security.AddAccessRule(deny);
        dir.SetAccessControl(security);
        try
        {
            var unreadable = new List<string>();
            var found = new DragDropService().ResolveDroppedPaths([dir.FullName], unreadable);
            Assert.Empty(found);
            Assert.Single(unreadable);
            Assert.StartsWith(dir.FullName, unreadable[0], System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            dir.SetAccessControl(security);
            dir.Delete(recursive: true);
        }
    }

    // A subfolder that cannot be listed is skipped: the rest of the folder is still found.
    [Fact]
    public void Resolve_SkipsASubfolderThatCannotBeRead()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var locked = new DirectoryInfo(Path.Combine(dir, "locked"));
        locked.Create();
        File.WriteAllText(Path.Combine(locked.FullName, "hidden.reg"), "");
        var visible = Path.Combine(dir, "visible.reg");
        File.WriteAllText(visible, "");

        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(deny);
        locked.SetAccessControl(security);
        try
        {
            var unreadable = new List<string>();
            var found = new DragDropService().ResolveDroppedPaths([dir], unreadable);
            Assert.Equal([visible], found);
            Assert.Empty(unreadable);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            locked.SetAccessControl(security);
            Directory.Delete(dir, recursive: true);
        }
    }
}
