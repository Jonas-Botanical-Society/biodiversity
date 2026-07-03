using System;
using System.Collections.Generic;
using System.Linq;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;
using Cake.Common.Tools.DotNet.Build; 
using Newtonsoft.Json.Linq;
using Cake.Common;
using Cake.Json;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class ModInfo
{
    public string Version { get; set; }
}

public class BuildContext : FrostingContext
{
    // Repo root is the parent of the CakeBuild project directory.
    public DirectoryPath RootDirectory { get; }

    // Top-level bin/ folder, used only to EXCLUDE packaged output from mod discovery,
    // regardless of which Configuration's subfolder it lives in.
    public DirectoryPath RootBinDirectory { get; }

    // Actual packaging destination: bin/<Configuration>/Mods/<modname>
    public DirectoryPath BinDirectory { get; }

    // Distributable zip output: Releases/<ModID>_<Version>.zip
    public DirectoryPath ReleasesDirectory { get; }

    public List<DirectoryPath> ModDirectories { get; set; } = new List<DirectoryPath>();
    public bool SkipJsonValidation { get; }
    public string BuildConfiguration { get; }
    public BuildContext(ICakeContext context)
        : base(context)
    {
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        BuildConfiguration = context.Argument("configuration", "Release");

        RootDirectory = context.Environment.WorkingDirectory.Combine("..").Collapse();
        RootBinDirectory = RootDirectory.Combine("bin");
        BinDirectory = RootBinDirectory.Combine(BuildConfiguration).Combine("Mods");
        ReleasesDirectory = RootDirectory.Combine("Releases");
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
 [TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation) return;

        foreach (var dir in context.ModDirectories)
        {
            var jsonFiles = context.GetFiles(dir.FullPath + "/assets/**/*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(file.FullPath);
                    Newtonsoft.Json.Linq.JToken.Parse(json);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
    }
}
[TaskName("Discover-Mods")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class DiscoverModsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var binPath = context.RootBinDirectory.FullPath.TrimEnd('/', '\\');

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
                context.Information("Building {0} -> {1}", dirName, context.BuildConfiguration, packagedDir.FullPath);

                context.DotNetBuild(csproj.FullPath, new DotNetBuildSettings
                {
                    Configuration = context.BuildConfiguration,
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

[TaskName("Copy-Content")]
[IsDependentOn(typeof(DiscoverModsTask))]
public sealed class CopyContentTask : FrostingTask<BuildContext>
{
    // Non-code files that should ride along into the packaged mod.
    // Extend this list if new metadata file types show up (e.g. .yaml).
    private static readonly string[] IncludedExtensions = { ".json", ".md", ".txt", ".png" };

    public override void Run(BuildContext context)
    {
        foreach (var dir in context.ModDirectories)
        {
            var dirName = dir.GetDirectoryName();
            var packagedDir = context.BinDirectory.Combine(dirName);
            context.EnsureDirectoryExists(packagedDir);

            // Copy the whole assets/ folder as-is, preserving its nested structure.
            var assetsDir = dir.Combine("assets");
            if (context.DirectoryExists(assetsDir.FullPath))
            {
                context.CopyDirectory(assetsDir, packagedDir.Combine("assets"));
            }

            // Copy any remaining metadata files anywhere else in the mod folder
            // (modinfo.json, modicon.png, README.md, LICENSE.txt, etc.) while
            // skipping source, build intermediates, and the already-copied assets/.
            var dirPathNormalized = dir.FullPath.TrimEnd('/', '\\');
            var candidateFiles = context.GetFiles(dir.FullPath + "/**/*.*");

            foreach (var file in candidateFiles)
            {
                var ext = file.GetExtension();
                if (ext is null || Array.IndexOf(IncludedExtensions, ext.ToLowerInvariant()) < 0)
                    continue;

                var relativePath = file.FullPath
                    .Substring(dirPathNormalized.Length)
                    .TrimStart('/', '\\');

                if (relativePath.StartsWith("src/", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destination = packagedDir.CombineWithFilePath(relativePath);
                context.EnsureDirectoryExists(destination.GetDirectory());
                context.CopyFile(file, destination);
            }
        }
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(CopyContentTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
}

[TaskName("Export")]
[IsDependentOn(typeof(PackageTask))]
public sealed class ExportTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists(context.ReleasesDirectory);

        foreach (var dir in context.ModDirectories)
        {
            var dirName = dir.GetDirectoryName();
            var packagedDir = context.BinDirectory.Combine(dirName);
            var modInfoFile = dir.CombineWithFilePath("modinfo.json");

            if (!context.FileExists(modInfoFile))
            {
                context.Warning("{0} has no modinfo.json, skipping export.", dirName);
                continue;
            }

            var modInfo = context.DeserializeJsonFromFile<ModInfo>(modInfoFile.FullPath);
            var zipFile = context.ReleasesDirectory.CombineWithFilePath($"{dirName}_{modInfo.Version}.zip");

            context.Information("Zipping {0} -> {1}", dirName, zipFile.FullPath);
            context.Zip(packagedDir, zipFile);
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(CleanTask))]
[IsDependentOn(typeof(PackageTask))]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
}