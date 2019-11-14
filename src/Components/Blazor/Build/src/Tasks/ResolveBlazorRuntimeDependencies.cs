// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.AspNetCore.Blazor.Build
{
    public class ResolveBlazorRuntimeDependencies : Task
    {
        [Required]
        public string EntryPoint { get; set; }

        [Required]
        public ITaskItem[] ApplicationDependencies { get; set; }

        [Required]
        public ITaskItem[] MonoBCLAssemblies { get; set; }

        [Output]
        public ITaskItem[] Dependencies { get; set; }

        public override bool Execute()
        {
            var paths = ResolveRuntimeDependenciesCore(EntryPoint, ApplicationDependencies.Select(c => c.ItemSpec), MonoBCLAssemblies.Select(c => c.ItemSpec));
            Dependencies = paths.Select(p => new TaskItem(p)).ToArray();

            return true;
        }

        public static IEnumerable<string> ResolveRuntimeDependenciesCore(
            string entryPoint,
            IEnumerable<string> applicationDependencies,
            IEnumerable<string> monoBclDirectories)
        {
            var assembly = new AssemblyEntry(entryPoint, GetAssemblyName(entryPoint));

            var dependencies = applicationDependencies
                .Select(a => new AssemblyEntry(a, GetAssemblyName(a)))
                .ToArray();

            var bcl = monoBclDirectories
                .SelectMany(d => Directory.EnumerateFiles(d, "*.dll").Select(f => Path.Combine(d, f)))
                .Select(a => new AssemblyEntry(a, GetAssemblyName(a)))
                .ToArray();

            var assemblyResolutionContext = new AssemblyResolutionContext(
                assembly,
                dependencies,
                bcl);

            assemblyResolutionContext.ResolveAssemblies();

            var paths = assemblyResolutionContext.Results.Select(r => r.Path);
            return paths.Concat(FindPdbs(paths));
        }

        private static string GetAssemblyName(string entryPoint)
        {
            return AssemblyName.GetAssemblyName(entryPoint).Name;
        }

        private static IEnumerable<string> FindPdbs(IEnumerable<string> dllPaths)
        {
            return dllPaths
                .Select(path => Path.ChangeExtension(path, "pdb"))
                .Where(path => File.Exists(path));
        }

        public class AssemblyResolutionContext
        {
            public AssemblyResolutionContext(
                AssemblyEntry assembly,
                AssemblyEntry[] dependencies,
                AssemblyEntry[] bcl)
            {
                Assembly = assembly;
                Dependencies = dependencies;
                Bcl = bcl;
            }

            public AssemblyEntry Assembly { get; }
            public AssemblyEntry[] Dependencies { get; }
            public AssemblyEntry[] Bcl { get; }

            public IList<AssemblyEntry> Results { get; } = new List<AssemblyEntry>();

            internal void ResolveAssemblies()
            {
                var visitedAssemblies = new HashSet<string>();
                var pendingAssemblies = new Stack<string>();
                pendingAssemblies.Push(Assembly.Name);
                ResolveAssembliesCore();

                void ResolveAssembliesCore()
                {
                    while (pendingAssemblies.Count > 0)
                    {
                        var current = pendingAssemblies.Pop();
                        if (visitedAssemblies.Add(current))
                        {
                            // Not all references will be resolvable within the Mono BCL.
                            // Skipping unresolved assemblies here is equivalent to passing "--skip-unresolved true" to the Mono linker.
                            var resolved = Resolve(current);
                            if (resolved != null)
                            {
                                Results.Add(resolved);
                                var references = GetAssemblyReferences(resolved.Path);
                                foreach (var reference in references)
                                {
                                    pendingAssemblies.Push(reference);
                                }
                            }
                        }
                    }
                }

                AssemblyEntry Resolve(string assemblyName)
                {
                    if (Assembly.Name == assemblyName)
                    {
                        return Assembly;
                    }

                    // Resolution logic. For right now, we will prefer the mono BCL version of a given
                    // assembly if there is a candidate assembly and an equivalent mono assembly.
                    return Bcl.FirstOrDefault(c => c.Name == assemblyName) ??
                        Dependencies.FirstOrDefault(c => c.Name == assemblyName);
                }

                static IReadOnlyList<string> GetAssemblyReferences(string assemblyPath)
                {
                    try
                    {
                        using var peReader = new PEReader(File.OpenRead(assemblyPath));
                        if (!peReader.HasMetadata)
                        {
                            return Array.Empty<string>(); // not a managed assembly
                        }

                        var metadataReader = peReader.GetMetadataReader();

                        var references = new List<string>();
                        foreach (var handle in metadataReader.AssemblyReferences)
                        {
                            var reference = metadataReader.GetAssemblyReference(handle);
                            var referenceName = metadataReader.GetString(reference.Name);

                            references.Add(referenceName);
                        }

                        return references;
                    }
                    catch (BadImageFormatException)
                    {
                        // not a PE file, or invalid metadata
                    }

                    return Array.Empty<string>(); // not a managed assembly
                }
            }
        }

        [DebuggerDisplay("{ToString(),nq}")]
        public class AssemblyEntry
        {
            public AssemblyEntry(string path, string name)
            {
                Path = path;
                Name = name;
            }

            public string Path { get; set; }
            public string Name { get; set; }

            public override string ToString() => Name;
        }
    }
}
