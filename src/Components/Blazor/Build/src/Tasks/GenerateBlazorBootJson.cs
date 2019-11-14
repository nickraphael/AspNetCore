// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.AspNetCore.Blazor.Build
{
    public class GenerateBlazorBootJson : Task
    {
        [Required]
        public string AssemblyPath { get; set; }

        [Required]
        public ITaskItem[] References { get; set; }

        [Required]
        public bool LinkerEnabled { get; set; }

        [Required]
        public string OutputPath { get; set; }

        public override bool Execute()
        {
            var assemblyReferences = References.Select(c => Path.GetFileName(c.ItemSpec)).ToArray();

            var data = new BootJsonData(
                AssemblyName.GetAssemblyName(AssemblyPath).Name,
                assemblyReferences,
                LinkerEnabled);

            var bootJsonText = JsonSerializer.Serialize(data, JsonSerializerOptionsProvider.Options);
            File.WriteAllText(OutputPath, bootJsonText);

            return true;
        }

        /// <summary>
        /// Defines the structure of a Blazor boot JSON file
        /// </summary>
        private readonly struct BootJsonData
        {
            public string EntryAssembly { get; }
            public IEnumerable<string> AssemblyReferences { get; }
            public bool LinkerEnabled { get; }

            public BootJsonData(
                string entryAssembly,
                IEnumerable<string> assemblyReferences,
                bool linkerEnabled)
            {
                EntryAssembly = entryAssembly;
                AssemblyReferences = assemblyReferences;
                LinkerEnabled = linkerEnabled;
            }
        }
    }
}
