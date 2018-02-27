﻿//-----------------------------------------------------------------------------
// FILE:	    LogoutCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

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

using Neon.Cluster;
using Neon.Common;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>login</b> command.
    /// </summary>
    public class LogoutCommand : CommandBase
    {
        private const string usage = @"
Logs out of a cluster.

USAGE:

    neon logout 
";

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "logout" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            var clusterLogin = Program.ClusterLogin;

            Console.WriteLine("");

            // Close all VPN connections even if we're not officially logged in.
            //
            // We're passing NULL to close all cluster VPN connections to ensure that 
            // we're only connected to one at a time.  It's very possible for a operator
            // to have to manage multiple disconnnected clusters that share the same
            // IP address space.

            NeonClusterHelper.VpnClose(null); 

            // Actually logout.

            if (clusterLogin == null)
            {
                Console.WriteLine("*** You are not logged in.");
                Console.WriteLine("");
                return;
            }

            Console.WriteLine($"Logging out of [{clusterLogin.ClusterName}].");
            Console.WriteLine("");

            CurrentClusterLogin.Delete();
        }

        /// <inheritdoc/>
        public override DockerShimInfo Shim(DockerShim shim)
        {
            return new DockerShimInfo(isShimmed: false, ensureConnection: false);
        }
    }
}
