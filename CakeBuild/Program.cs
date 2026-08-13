using System;
using System.IO;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

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
    public const string ProjectName = "AnanLaQuenchingPrediction";
    public const string CsprojName = "AnanLaQuenchingPrediction";
    public string BuildConfiguration { get; set; }
    public string Version { get; }
    public string Name { get; }
    public bool SkipJsonValidation { get; set; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{BuildContext.ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
    }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }
        var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json");
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file.FullPath);
                JToken.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.CsprojName}.csproj",
            new DotNetCleanSettings
            {
                Configuration = context.BuildConfiguration
            });


        context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.CsprojName}.csproj",
            new DotNetPublishSettings
            {
                Configuration = context.BuildConfiguration
            });
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // 按构建配置分目录（Debug/Release 产物互不覆盖）
        var releaseDir = $"../Releases/{context.BuildConfiguration}";
        var outputDir = $"{releaseDir}/{context.Name}";
        context.EnsureDirectoryExists(releaseDir);
        context.EnsureDirectoryExists(outputDir);
        context.CleanDirectory(outputDir);
        context.CopyFiles($"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*", outputDir);
        context.CopyDirectory($"../{BuildContext.ProjectName}/assets", $"{outputDir}/assets");
        context.CopyFile($"../{BuildContext.ProjectName}/modinfo.json", $"{outputDir}/modinfo.json");
        if (context.FileExists($"../{BuildContext.ProjectName}/modicon.png"))
        {
            context.CopyFile($"../{BuildContext.ProjectName}/modicon.png", $"{outputDir}/modicon.png");
        }
        context.Zip(outputDir, $"{releaseDir}/{context.Name}_{context.Version}.zip");
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}