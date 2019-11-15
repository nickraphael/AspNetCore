// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Microsoft.AspNetCore.Blazor.Build
{
    internal class ProjectDirectory : IDisposable
    {
        public bool PreserveWorkingDirectory { get; set; } = true;

        private static readonly string ComponentsRoot = GetTestAttribute("Testing.ComponentsRoot");
        private static readonly string GlobalJsonPath = GetTestAttribute("Testing.GlobalJsonPath");

        public static ProjectDirectory Create(string projectName, string baseDirectory = "", string[] additionalProjects = null)
        {
            var destinationPath = Path.Combine(Path.GetTempPath(), "BlazorBuild", baseDirectory, Path.GetRandomFileName());
            Directory.CreateDirectory(destinationPath);

            try
            {
                if (Directory.EnumerateFiles(destinationPath).Any())
                {
                    throw new InvalidOperationException($"{destinationPath} should be empty");
                }

                if (string.IsNullOrEmpty(ComponentsRoot))
                {
                    throw new InvalidOperationException("Could not find Components root directory.");
                }

                foreach (var project in new string[] { projectName, }.Concat(additionalProjects ?? Array.Empty<string>()))
                {
                    var testAppsRoot = Path.Combine(ComponentsRoot, "Blazor", "Build", "testassets");
                    var projectRoot = Path.Combine(testAppsRoot, project);
                    if (!Directory.Exists(projectRoot))
                    {
                        throw new InvalidOperationException($"Could not find project at '{projectRoot}'");
                    }

                    var projectDestination = Path.Combine(destinationPath, project);
                    var projectDestinationDir = Directory.CreateDirectory(projectDestination);
                    CopyDirectory(new DirectoryInfo(projectRoot), projectDestinationDir);
                    SetupDirectoryBuildFiles(ComponentsRoot, testAppsRoot, projectDestination);
                }

                var directoryPath = Path.Combine(destinationPath, projectName);
                var projectPath = Path.Combine(directoryPath, projectName + ".csproj");

                CopyRepositoryAssets(destinationPath);

                return new ProjectDirectory(
                    destinationPath,
                    directoryPath,
                    projectPath);
            }
            catch
            {
                CleanupDirectory(destinationPath);
                throw;
            }

            static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination, bool recursive = true)
            {
                foreach (var file in source.EnumerateFiles())
                {
                    file.CopyTo(Path.Combine(destination.FullName, file.Name));
                }

                if (!recursive)
                {
                    return;
                }

                foreach (var directory in source.EnumerateDirectories())
                {
                    if (directory.Name == "bin")
                    {
                        // Just in case someone has opened the project in an IDE or built it. We don't want to copy
                        // these folders.
                        continue;
                    }

                    var created = destination.CreateSubdirectory(directory.Name);
                    if (directory.Name == "obj")
                    {
                        // Copy NuGet restore assets (viz all the files at the root of the obj directory, but stop there.)
                        CopyDirectory(directory, created, recursive: false);
                    }
                    else
                    {
                        CopyDirectory(directory, created);
                    }
                }
            }

            static void SetupDirectoryBuildFiles(string componentsRoot, string testAppsRoot, string projectDestination)
            {
                var beforeDirectoryPropsContent =
$@"<Project>
  <PropertyGroup>
    <ComponentsRoot>{componentsRoot}</ComponentsRoot>
  </PropertyGroup>
</Project>";
                File.WriteAllText(Path.Combine(projectDestination, "Before.Directory.Build.props"), beforeDirectoryPropsContent);

                new List<string> { "Directory.Build.props", "Directory.Build.targets", }
                    .ForEach(file =>
                    {
                        var source = Path.Combine(testAppsRoot, file);
                        var destination = Path.Combine(projectDestination, file);
                        File.Copy(source, destination);
                    });
            }

            static void CopyRepositoryAssets(string projectRoot)
            {
                var globalJsonPath = GlobalJsonPath;

                var destinationFile = Path.Combine(projectRoot, Path.GetFileName(globalJsonPath));
                File.Copy(globalJsonPath, destinationFile);
            }
        }

        protected ProjectDirectory(string solutionPath, string directoryPath, string projectFilePath)
        {
            SolutionPath = solutionPath;
            DirectoryPath = directoryPath;
            ProjectFilePath = projectFilePath;
        }

        public string DirectoryPath { get; }

        public string ProjectFilePath { get; }

        public string SolutionPath { get; }

        public string GetBuildOutputDirectory(string targetFramework, string configuration = "Debug") => Path.Combine("bin", configuration, targetFramework);

        internal void AddProjectFileContent(string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var existing = File.ReadAllText(ProjectFilePath);
            var updated = existing.Replace("<!-- Test Placeholder -->", content);
            File.WriteAllText(ProjectFilePath, updated);
        }

        public void Dispose()
        {
            if (PreserveWorkingDirectory)
            {
                Console.WriteLine($"Skipping deletion of working directory {SolutionPath}");
            }
            else
            {
                CleanupDirectory(SolutionPath);
            }
        }

        internal static void CleanupDirectory(string filePath)
        {
            var tries = 5;
            var sleep = TimeSpan.FromSeconds(3);

            for (var i = 0; i < tries; i++)
            {
                try
                {
                    Directory.Delete(filePath, recursive: true);
                    return;
                }
                catch when (i < tries - 1)
                {
                    Console.WriteLine($"Failed to delete directory {filePath}, trying again.");
                    Thread.Sleep(sleep);
                }
            }
        }

        private static string GetTestAttribute(string key)
        {
            return typeof(ProjectDirectory).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(f => f.Key == key)
                ?.Value;
        }
    }
}
