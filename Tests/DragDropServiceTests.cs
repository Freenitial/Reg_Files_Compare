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

    // A junction that points back to a parent folder is not followed: each file comes once, at once.
    [Fact]
    public void Resolve_JunctionLoop_GivesEachFileOnce()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var a = Path.Combine(dir, "a.reg");
            File.WriteAllText(a, "Windows Registry Editor Version 5.00\r\n");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            // Two loops: following them would list a.reg about 2^32 times.
            foreach (var link in new[] { Path.Combine(dir, "sub", "up"), Path.Combine(dir, "back") })
            {
                using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    System.IO.Path.Combine(System.Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{dir}\"")
                { CreateNoWindow = true, UseShellExecute = false })!;
                mklink.WaitForExit();
                Assert.True(Directory.Exists(link), "the junction could not be made");
            }

            var found = new DragDropService().ResolveDroppedPaths([dir, a]);
            Assert.Equal([a], found);
        }
        finally
        {
            // The junctions first: deleting them does not touch their target.
            foreach (var link in new[] { Path.Combine(dir, "sub", "up"), Path.Combine(dir, "back") })
                if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(dir, recursive: true);
        }
    }

    // A path given that does not exist is reported, the others still resolved.
    [Fact]
    public void Resolve_ReportsAMissingPath()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var a = Path.Combine(dir, "a.reg");
            File.WriteAllText(a, "Windows Registry Editor Version 5.00\r\n");
            var missing = Path.Combine(dir, "typo.reg");
            var unreadable = new List<string>();
            Assert.Equal([a], new DragDropService().ResolveDroppedPaths([a, missing], unreadable));
            Assert.Equal([$"{missing} (not found)"], unreadable);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
