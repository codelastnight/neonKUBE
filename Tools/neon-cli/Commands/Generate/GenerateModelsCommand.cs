﻿//-----------------------------------------------------------------------------
// FILE:	    GenerateModelsCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;

using Neon.CodeGen;
using Neon.Common;
using Neon.Kube;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>generate models</b> command.
    /// </summary>
    public class GenerateModelsCommand : CommandBase
    {
        private const string usage = @"
Generates C# source code for data and service models defined as interfaces
within a compiled assembly.

USAGE:

    neon generate models [OPTIONS] ASSEMBLY-PATH [OUTPUT-PATH]

ARGUMENTS:

    ASSEMBLY-PATH       - Path to the assembly being scanned.

    OUTPUT-PATH         - Optional path to the output file, otherwise
                          the generated code will be writtent to STDOUT.

OPTIONS:

    --source-namespace=VALUE    - Specifies the namespace to be used when
                                  scanning for models.  By default, all
                                  classes within the assembly wll be scanned.

    --target-namespace=VALUE    - Specifies the namespace to be used when
                                  generating the models.  This overrides 
                                  the original type namespaces as scanned
                                  from the source assembly.

    --no-services               - Don't generate any service clients.

    --targets=LIST              - Specifies the comma separated list of target 
                                  names.  Any input models that are not tagged
                                  with these target will not be generated.

REMARKS:

This command is used to generate enhanced JSON based data models and
REST API clients suitable for applications based on flexible noSQL
style design conventions.  See this GitHub issue for more information:

    https://github.com/nforgeio/neonKUBE/issues/463

";

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "generate", "models" }; }
        }

        /// <inheritdoc/>
        public override string[] ExtendedOptions
        {
            get { return new string[] { "--source-namespace", "--target-namespace", "--no-services", "--targets" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.Arguments.Length == 0)
            {
                Help();
                Program.Exit(1);
            }

            var assemblyPath = commandLine.Arguments.ElementAtOrDefault(0);
            var outputPath   = commandLine.Arguments.ElementAtOrDefault(1);
            var targets      = new List<string>();

            var targetOption = commandLine.GetOption("--targets");

            if (!string.IsNullOrEmpty(targetOption))
            {
                foreach (var target in targetOption.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    targets.Add(target);
                }
            }

            var settings = new CodeGeneratorSettings(targets.ToArray())
            {
                SourceNamespace  = commandLine.GetOption("--source-namespace"),
                TargetNamespace  = commandLine.GetOption("--target-namespace"),
                NoServiceClients = commandLine.HasOption("--no-services")
            };

            var assembly      = Assembly.LoadFile(assemblyPath);
            var codeGenerator = new CodeGenerator(settings);
            var output        = codeGenerator.Generate(assembly);

            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, output.SourceCode);
            }
            else
            {
                Console.Write(output.SourceCode);
            }
        }
    }
}