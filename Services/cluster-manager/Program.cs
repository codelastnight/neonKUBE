﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Marcus Bowyer
// COPYRIGHT:   Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Kube;
using Neon.Service;

using k8s;
using k8s.Models;

namespace ClusterManager
{
    /// <summary>
    /// The Neon cluster initialization operator.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The program entrypoint.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        public static void Main(string[] args)
        {
            new ClusterManager(NeonServiceMap.Production, NeonServices.ClusterManager).RunAsync().Wait();
        }
    }
}
