﻿//-----------------------------------------------------------------------------
// FILE:	    HAProxyHttpFrontend.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2017 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;
using Newtonsoft;
using Newtonsoft.Json;

using Neon.Cluster;
using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;

namespace NeonProxyManager
{
    /// <summary>
    /// Holds information about an HAProxy HTTP frontend being generated.
    /// </summary>
    public class HAProxyHttpFrontend
    {
        //---------------------------------------------------------------------
        // Static members.

        /// <summary>
        /// Returns the host part from a $"{host}:{path}".
        /// </summary>
        /// <param name="hostPath"></param>
        /// <returns></returns>
        public static string GetHost(string hostPath)
        {
            var pos = hostPath.IndexOf(':');

            return hostPath.Substring(0, pos);
        }

        /// <summary>
        /// Returns the path part from a $"{host}:{path}".
        /// </summary>
        /// <param name="hostPath"></param>
        /// <returns></returns>
        public static string GetPath(string hostPath)
        {
            var pos = hostPath.IndexOf(':');

            return hostPath.Substring(pos + 1);
        }

        //---------------------------------------------------------------------
        // Instance members.

        /// <summary>
        /// Retrurns the HAProxy frontend name.
        /// </summary>
        public string Name
        {
            get
            {
                var scheme = Tls ? "https" : "http";

                return $"{scheme}:port-{Port}";
            }
        }

        /// <summary>
        /// The TCP port to be bound.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// The path prefix to be matched.
        /// </summary>
        public string PathPrefix { get; set; }

        /// <summary>
        /// A dictionary of the referenced certificates keyed by name.
        /// </summary>
        public Dictionary<string, TlsCertificate> Certificates { get; private set; } = new Dictionary<string, TlsCertificate>();

        /// <summary>
        /// A dictionary that maps host:path prefix to HAProxy backend names.
        /// </summary>
        public Dictionary<string, string> HostPathMappings { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// Returns <c>true</c> for TLS frontends.
        /// </summary>
        public bool Tls
        {
            get { return Certificates.Count > 0; }
        }

        /// <summary>
        /// Indicates that logging is enabled for the frontend.
        /// </summary>
        public bool Log { get; set; }
    }
}
