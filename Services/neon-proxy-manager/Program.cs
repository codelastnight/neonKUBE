﻿//-----------------------------------------------------------------------------
// FILE:	    Program.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.Docker;
using Neon.Hive;
using Neon.HiveMQ;
using Neon.Time;

// $todo(jeff.lill):
//
// We don't currently implement any support for:
//
//      tcp-check send ...
//      tcp-check expect ...
//
// These are used for sending and validating arbitrary verification
// data against a TCP backend and would be useful for verifying the
// health of SMTP backend servers, etc.

namespace NeonProxyManager
{
    /// <summary>
    /// Implements the <b>neon-proxy-manager</b> service which is responsible for dynamically generating the HAProxy 
    /// configurations for the <c>neon-proxy-public</c>, <c>neon-proxy-private</c>, <c>neon-proxy-public-bridge</c>,
    /// and <c>neon-proxy-private-bridge</c> services from the load balancer rules persisted in Consul and the TLS certificates
    /// persisted in Vault.  See <a href="https://hub.docker.com/r/nhive/neon-proxy-manager/">nhive/neon-proxy-manager</a>  
    /// and <a href="https://hub.docker.com/r/nhive/neon-proxy/">nhive/neon-proxy</a> for more information.
    /// </summary>
    public static partial class Program
    {
        private static readonly string serviceName = $"neon-proxy-manager:{GitVersion}";

        private const string consulPrefix          = "neon/service/neon-proxy-manager";
        private const string certWarnDaysKey       = consulPrefix + "/cert-warn-days";
        private const string cacheRemoveSecondsKey = consulPrefix + "/cache-remove-seconds";
        private const string proxyConfKey          = consulPrefix + "/conf";
        private const string proxyStatusKey        = consulPrefix + "/status";
        private const string proxyReloadKey        = proxyConfKey + "/reload";
        private const string failsafeSecondsKey    = proxyConfKey + "/failsafe-seconds";
        private const string vaultCertPrefix       = "neon-secret/cert";
        private const string allPrefix             = "~all~";   // Special path prefix indicating that all paths should be matched.

        private static ProcessTerminator        terminator;
        private static INeonLogger              log;
        private static HiveProxy                hive;
        private static VaultClient              vault;
        private static ConsulClient             consul;
        private static DockerClient             docker;
        private static BroadcastChannel         proxyNotifyChannel;
        private static TimeSpan                 certWarnTime;
        private static TimeSpan                 cacheRemoveDelay;
        private static TimeSpan                 failsafeInterval;
        private static HiveDefinition           hiveDefinition;
        private static bool                     hiveDefinitionChanged;
        private static List<DockerNode>         swarmNodes;

        private static CancellationTokenSource  cts               = new CancellationTokenSource();
        private static bool                     processingConfigs = false;
        private static bool                     exit              = false;

        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static async Task Main(string[] args)
        {
            LogManager.Default.SetLogLevel(Environment.GetEnvironmentVariable("LOG_LEVEL"));
            log = LogManager.Default.GetLogger(typeof(Program));
            log.LogInfo(() => $"Starting [{serviceName}]");
            log.LogInfo(() => $"LOG_LEVEL={LogManager.Default.LogLevel.ToString().ToUpper()}");

            // Create process terminator to handle process termination signals.

            terminator = new ProcessTerminator(log);

            terminator.AddHandler(
                () =>
                {
                    // Cancel any operations in progress.

                    exit = true;

                    cts.Cancel();

                    // This gracefully closes the [proxyNotifyChannel].

                    if (proxyNotifyChannel != null)
                    {
                        proxyNotifyChannel.Dispose();
                        proxyNotifyChannel = null;
                    }

                    try
                    {
                        NeonHelper.WaitFor(() => !processingConfigs, terminator.Timeout);
                        log.LogInfo(() => "Tasks stopped gracefully.");
                    }
                    catch (TimeoutException)
                    {
                        log.LogWarn(() => $"Tasks did not stop within [{terminator.Timeout}].");
                    }
                });

            // Establish the hive connections.

            if (NeonHelper.IsDevWorkstation)
            {
                var vaultCredentialsSecret = "neon-proxy-manager-credentials";

                Environment.SetEnvironmentVariable("VAULT_CREDENTIALS", vaultCredentialsSecret);

                hive = HiveHelper.OpenHiveRemote(new DebugSecrets().VaultAppRole(vaultCredentialsSecret, "neon-proxy-manager"));
            }
            else
            {
                hive = HiveHelper.OpenHive();
            }

            // [neon-proxy-manager] requires access to the [IHostingManager] implementation for the
            // current environment, so we'll need to initialize the hosting loader.

            HostingLoader.Initialize();

            try
            {
                // Log into Vault using a Docker secret.

                var vaultCredentialsSecret = Environment.GetEnvironmentVariable("VAULT_CREDENTIALS");

                if (string.IsNullOrEmpty(vaultCredentialsSecret))
                {
                    log.LogCritical("[VAULT_CREDENTIALS] environment variable does not exist.");
                    Program.Exit(1);
                }

                var vaultSecret = HiveHelper.GetSecret(vaultCredentialsSecret);

                if (string.IsNullOrEmpty(vaultSecret))
                {
                    log.LogCritical($"Cannot read Docker secret [{vaultCredentialsSecret}].");
                    Program.Exit(1);
                }

                var vaultCredentials = HiveCredentials.ParseJson(vaultSecret);

                if (vaultCredentials == null)
                {
                    log.LogCritical($"Cannot parse Docker secret [{vaultCredentialsSecret}].");
                    Program.Exit(1);
                }

                // Open the hive data services and then start the main service task.

                log.LogInfo(() => $"Connecting: Vault");

                using (vault = HiveHelper.OpenVault(vaultCredentials))
                {
                    log.LogInfo(() => $"Connecting: Consul");

                    using (consul = HiveHelper.OpenConsul())
                    {
                        log.LogInfo(() => $"Connecting: Docker");

                        using (docker = HiveHelper.OpenDocker())
                        {
                            log.LogInfo(() => $"Connecting: {HiveMQChannels.ProxyNotify} channel");

                            // NOTE:
                            //
                            // We're passing [useBootstrap=true] here so that the HiveMQ client will
                            // connect directly to the HiveMQ cluster nodes as opposed to routing
                            // traffic through the private load balancer.  This is necessary because
                            // the load balancers rely on HiveMQ to broadcast update notifications.
                            //
                            // One consequence of this is that this service will need to be restarted
                            // whenever HiveMQ instances are relocated to different hive hosts.

                            // $todo(jeff.lill):
                            //
                            // This service will need to be restarted whenever future code provides
                            // for relocating HiveMQ instances or when hive nodes hosting HiveMQ
                            // are added or removed.
                            //
                            //      https://github.com/jefflill/NeonForge/issues/337

                            using (proxyNotifyChannel = hive.HiveMQ.Internal.GetProxyNotifyChannel(useBootstrap: true).Open())
                            {
                                // Read the service settings, initializing their default values
                                // if they don't already exist.

                                if (!await consul.KV.Exists(certWarnDaysKey))
                                {
                                    log.LogInfo($"Persisting setting [{certWarnDaysKey}=30.0]");
                                    await consul.KV.PutDouble(certWarnDaysKey, 30.0);
                                }

                                if (!await consul.KV.Exists(cacheRemoveSecondsKey))
                                {
                                    log.LogInfo($"Persisting setting [{cacheRemoveSecondsKey}=300.0]");
                                    await consul.KV.PutDouble(cacheRemoveSecondsKey, 300.0);
                                }

                                if (!await consul.KV.Exists(failsafeSecondsKey))
                                {
                                    log.LogInfo($"Persisting setting [{failsafeSecondsKey}=120.0]");
                                    await consul.KV.PutDouble(failsafeSecondsKey, 120);
                                }

                                certWarnTime     = TimeSpan.FromDays(await consul.KV.GetDouble(certWarnDaysKey));
                                cacheRemoveDelay = TimeSpan.FromDays(await consul.KV.GetDouble(cacheRemoveSecondsKey));
                                failsafeInterval = TimeSpan.FromSeconds(await consul.KV.GetDouble(failsafeSecondsKey));

                                log.LogInfo(() => $"Using setting [{certWarnDaysKey}={certWarnTime.TotalSeconds}]");
                                log.LogInfo(() => $"Using setting [{cacheRemoveSecondsKey}={cacheRemoveDelay.TotalSeconds}]");
                                log.LogInfo(() => $"Using setting [{failsafeSecondsKey}={failsafeInterval.TotalSeconds}]");

                                // Run the service tasks.

                                var tasks = new List<Task>();

                                tasks.Add(ConfigGeneratorAsync());
                                tasks.Add(FailsafeBroadcasterAsync());
                                await NeonHelper.WaitAllAsync(tasks);

                                terminator.ReadyToExit();
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.LogCritical(e);
                Program.Exit(1);
            }
            finally
            {
                HiveHelper.CloseHive();
                terminator.ReadyToExit();
            }

            Program.Exit(0);
        }

        /// <summary>
        /// Returns the program version as the Git branch and commit and an optional
        /// indication of whether the program was build from a dirty branch.
        /// </summary>
        public static string GitVersion
        {
            get
            {
                var version = $"{ThisAssembly.Git.Branch}-{ThisAssembly.Git.Commit}";

#pragma warning disable 162 // Unreachable code

                //if (ThisAssembly.Git.IsDirty)
                //{
                //    version += "-DIRTY";
                //}

#pragma warning restore 162 // Unreachable code

                return version;
            }
        }

        /// <summary>
        /// Exits the service with an exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void Exit(int exitCode)
        {
            log.LogInfo(() => $"Exiting: [{serviceName}]");
            terminator.ReadyToExit();
            Environment.Exit(exitCode);
        }
    }
}
