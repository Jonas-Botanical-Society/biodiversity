using System.Collections.Generic;
using System.Linq;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    // Repo root is the parent of the CakeBuild project directory.
    public DirectoryPath RootDirectory { get; }
    public DirectoryPath BinDirectory { get; }
    public List<DirectoryPath> ModDirectories { get; set; } = new List<DirectoryPath>();

    public BuildContext(ICakeContext context)
        : base(context)
    {
        // Assumes the build is invoked from the repo root, e.g.:
        //   dotnet run --project CakeBuild -- --target=Default
        RootDirectory = context.Environment.WorkingDirectory;
        BinDirectory = RootDirectory.Combine("bin");
    }
}

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.BinDirectory.FullPath);
    }
}

[TaskName("Discover-Mods")]
public sealed class DiscoverModsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var binPath = context.BinDirectory.FullPath.TrimEnd('/', '\\');

        var modInfoFiles = context.GetFiles(
            context.RootDirectory.FullPath + "/**/modinfo.json");

        context.ModDirectories = modInfoFiles
            .Select(f => f.GetDirectory())
            // Exclude anything already inside bin/ (e.g. previously packaged output)
            .Where(dir => !dir.FullPath.StartsWith(binPath))
            .ToList();

        foreach (var dir in context.ModDirectories)
        {
            context.Information("Found mod: {0}", dir.GetDirectoryName());
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(DiscoverModsTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var dir in context.ModDirectories)
        {
            var dirName = dir.GetDirectoryName();
            var csprojFiles = context.GetFiles(dir.FullPath + "/*.csproj");
            var csproj = csprojFiles.FirstOrDefault();

            var packagedDir = context.BinDirectory.Combine(dirName);
            context.EnsureDirectoryExists(packagedDir);

            if (csproj is not null)
            {
                context.Information("Building {0} -> {1}", dirName, packagedDir.FullPath);

                context.DotNetBuild(csproj.FullPath, new DotNetBuildSettings
                {
                    Configuration = "Release",
                    OutputDirectory = packagedDir.FullPath
                });
            }
            else
            {
                context.Information("{0} has no .csproj, skipping compile step.", dirName);
            }
        }
    }
}

[TaskName("Copy-Assets")]
[IsDependentOn(typeof(DiscoverModsTask))]
public sealed class CopyAssetsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var dir in context.ModDirectories)
        {
            var dirName = dir.GetDirectoryName();
            var packagedDir = context.BinDirectory.Combine(dirName);
            context.EnsureDirectoryExists(packagedDir);

            var assetsDir = dir.Combine("assets");
            if (context.DirectoryExists(assetsDir.FullPath))
            {
                context.CopyDirectory(assetsDir, packagedDir.Combine("assets"));
            }

            var modInfoFile = dir.CombineWithFilePath("modinfo.json");
            if (context.FileExists(modInfoFile))
            {
                context.CopyFile(modInfoFile, packagedDir.CombineWithFilePath("modinfo.json"));
            }

            var modIconFile = dir.CombineWithFilePath("modicon.png");
            if (context.FileExists(modIconFile))
            {
                context.CopyFile(modIconFile, packagedDir.CombineWithFilePath("modicon.png"));
            }
        }
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(CopyAssetsTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
}

[TaskName("Default")]
[IsDependentOn(typeof(CleanTask))]
[IsDependentOn(typeof(PackageTask))]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
}