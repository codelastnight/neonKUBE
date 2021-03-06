﻿//-----------------------------------------------------------------------------
// FILE:	    ScpCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;

using Neon.Common;
using Neon.Kube;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>scp</b> command.
    /// </summary>
    public class ScpCommand : CommandBase
    {
        private const string usage = @"
Opens a WinSCP connection to the named node in the current cluster
or the first master when no node is specified.

USAGE:

    neon scp [NODE]

ARGUMENTS:

    NODE        - Optionally names the target node.
                  This defaults to the first master.
";
        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "scp" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.HasHelpOption)
            {
                Console.WriteLine(usage);
                Program.Exit(0);
            }

            var cluster    = Program.GetCluster();
            var extensions = KubeHelper.CurrentContext.Extension;

            NodeDefinition node;

            if (commandLine.Arguments.Length == 0)
            {
                node = cluster.GetReachableMaster().Metadata;
            }
            else
            {
                var name = commandLine.Arguments[0];

                node = cluster.Definition.Nodes.SingleOrDefault(n => n.Name == name);

                if (node == null)
                {
                    Console.Error.WriteLine($"*** ERROR: The node [{name}] was not found.");
                    Program.Exit(1);
                }
            }

            // The host's SSH key fingerprint looks something like the example below.
            // We need to extract extract the bitcount and MD5 hash to generate a
            // WinSCP compatible host key fingerprint.
            //
            //      2048 MD5:cb:2f:f1:68:4b:aa:b3:8a:72:4d:53:f6:9f:5f:6a:fa root@manage-0 (RSA)

            const string    md5Pattern = "MD5:";
            string          fingerprint;
            int             bitCount;
            string          md5;
            int             startPos;
            int             endPos;

            endPos = extensions.SshNodeFingerprint.IndexOf(' ');

            if (!int.TryParse(extensions.SshNodeFingerprint.Substring(0, endPos), out bitCount) || bitCount <= 0)
            {
                Console.Error.WriteLine($"*** ERROR: Cannot parse host's SSH key fingerprint [{extensions.SshNodeFingerprint}].");
                Program.Exit(1);
            }

            startPos = extensions.SshNodeFingerprint.IndexOf(md5Pattern);

            if (startPos == -1)
            {
                Console.Error.WriteLine($"*** ERROR: Cannot parse host's SSH key fingerprint [{extensions.SshNodeFingerprint}].");
                Program.Exit(1);
            }

            startPos += md5Pattern.Length;

            endPos = extensions.SshNodeFingerprint.IndexOf(' ', startPos);

            if (endPos == -1)
            {
                md5 = extensions.SshNodeFingerprint.Substring(startPos).Trim();
            }
            else
            {
                md5 = extensions.SshNodeFingerprint.Substring(startPos, endPos - startPos).Trim();
            }

            fingerprint = $"ssh-rsa {bitCount} {md5}";

            // Launch WinSCP.

            if (!File.Exists(Program.WinScpPath))
            {
                Console.Error.WriteLine($"*** ERROR: WinSCP application is not installed at [{Program.WinScpPath}].");
                Program.Exit(1);
            }

            Process.Start(Program.WinScpPath, $@"scp://{extensions.SshUsername}:{extensions.SshPassword}@{node.PrivateAddress}:22 /hostkey=""{fingerprint}"" /newinstance /rawsettings Shell=""sudo%20-s"" compression=1");
        }
    }
}
