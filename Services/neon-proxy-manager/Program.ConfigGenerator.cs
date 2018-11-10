﻿//-----------------------------------------------------------------------------
// FILE:	    Program.ConfigGenerator.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserve

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
using Neon.Tasks;
using Neon.Time;

namespace NeonProxyManager
{
    public static partial class Program
    {
        private static AsyncMutex asyncLock = new AsyncMutex();

        /// <summary>
        /// Generates the HAProxy and Vault configurations.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private static async Task ConfigGeneratorAsync()
        {
            // The implementation is pretty straightforward:
            //
            // We're simply going to listen for [ProxyRegenerateMessage] messages
            // broadcast on the [proxy-notify] channel when changes are made to
            // the proxy configuration or periodicaly broadcast by [neon-hive-manager] 
            // signalling that the proxy configurations should be proactively
            // regenerated to recover from a potential loss of update notification
            // messages as well as to check for configuration problems like expired
            // or expiring TLS certificates.
            //
            // Note that we're going to broadcast a single [ProxyRegenerateMessage] 
            // to ourself so we'll perform an immediate regeneration when the 
            // service starts.

            proxyNotifyChannel.Publish(new ProxyRegenerateMessage() { Reason="[neon-proxy-manager]: Startup" });

            await Task.Run(
                () =>
                {
                    processingConfigs = false;

                    try
                    {
                        log.LogInfo(() => "MONITOR: Starting");
                        log.LogInfo(() => $"MONITOR: Listening for [{nameof(ProxyRegenerateMessage)}] messages on [{proxyNotifyChannel.Name}].");

                        proxyNotifyChannel.Consume<ProxyRegenerateMessage>(
                            async notifyMessage =>
                            {
                                // We cannot process updates in parallel so we'll use an 
                                // AsyncMutex to prevent this.

                                using (await asyncLock.AcquireAsync())
                                {
                                    try
                                    {
                                        log.LogInfo(() => $"MONITOR: Received [{nameof(ProxyRegenerateMessage)}({notifyMessage.Reason})]");

                                        if (processingConfigs)
                                        {
                                            log.LogInfo(() => $"MONITOR: Ignorning [{nameof(ProxyRegenerateMessage)}] because we're already generating proxy configurations.");
                                            return;
                                        }

                                        processingConfigs = true;

                                        // Load and check the hive certificates.

                                        var hiveCerts = new HiveCerts();
                                        var utcNow    = DateTime.UtcNow;

                                        log.LogInfo(() => "Reading hive certificates.");

                                        try
                                        {
                                            foreach (var certName in await vault.ListAsync(vaultCertPrefix, terminator.CancellationToken))
                                            {
                                                var certificate = await vault.ReadJsonAsync<TlsCertificate>($"{vaultCertPrefix}/{certName}", terminator.CancellationToken);
                                                var certInfo    = new CertInfo(certName, certificate);

                                                if (!certInfo.Certificate.IsValidDate(utcNow))
                                                {
                                                    log.LogError(() => $"Certificate [{certInfo.Name}] for [hosts={certInfo.Certificate.HostNames}] expired at [{certInfo.Certificate.ValidUntil.Value}].");
                                                }
                                                else if (!certInfo.Certificate.IsValidDate(utcNow + certWarnTime))
                                                {
                                                    log.LogWarn(() => $"Certificate [{certInfo.Name}] for [hosts={certInfo.Certificate.HostNames}] will expire in [{(certInfo.Certificate.ValidUntil.Value - utcNow).TotalDays}] days at [{certInfo.Certificate.ValidUntil}].");
                                                }

                                                hiveCerts.Add(certInfo);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            log.LogError("Unable to load certificates from Vault.", e);
                                            log.LogError(() => "Aborting load balancer configuration.");
                                        }

                                        // Fetch the hive definition and detect whether it changed since the
                                        // previous run.

                                        var currentHiveDefinition = await HiveHelper.GetDefinitionAsync(hiveDefinition, terminator.CancellationToken);

                                        hiveDefinitionChanged = hiveDefinition == null || !NeonHelper.JsonEquals(hiveDefinition, currentHiveDefinition);
                                        hiveDefinition        = currentHiveDefinition;

                                        // Fetch the list of active Docker Swarm nodes.  We'll need this to generate the
                                        // proxy bridge configurations.

                                        swarmNodes = await docker.NodeListAsync(terminator.CancellationToken);

                                        // Rebuild the proxy configurations and write the captured status to
                                        // Consul to make it available for the [neon proxy public|private status]
                                        // command.  Note that we're going to build the [neon-proxy-public-bridge]
                                        // and [neon-proxy-private-bridge] configurations as well for use by any 
                                        // hive pet nodes.

                                        var publicBuildStatus = await BuildProxyConfigAsync("public", hiveCerts);
                                        var publicProxyStatus = new LoadBalancerStatus() { Status = publicBuildStatus.Status };

                                        await consul.KV.PutString($"{proxyStatusKey}/public", NeonHelper.JsonSerialize(publicProxyStatus), terminator.CancellationToken);

                                        var privateBuildStatus = await BuildProxyConfigAsync("private", hiveCerts);
                                        var privateProxyStatus = new LoadBalancerStatus() { Status = privateBuildStatus.Status };

                                        await consul.KV.PutString($"{proxyStatusKey}/private", NeonHelper.JsonSerialize(privateProxyStatus), terminator.CancellationToken);

                                        // We need to ensure that the deployment's load balancer and security
                                        // rules are updated to match changes to the public load balancer rules.
                                        // Note that we're going to call this even if the PUBLIC load balancer
                                        // hasn't changed to ensure that the load balancer doesn't get
                                        // out-of-sync.

                                        await UpdateHiveNetwork(publicBuildStatus.Rules);
                                    }
                                    finally
                                    {
                                        processingConfigs = false;
                                    }
                                }
                            });
                    }
                    catch (OperationCanceledException)
                    {
                        log.LogInfo(() => "MONITOR: Terminating.");
                        return;
                    }
                    catch (Exception e)
                    {
                        log.LogError(e);
                    }

                    if (exit)
                    {
                        return;
                    }
                });
        }

        /// <summary>
        /// Normalizes a path prefix by converting <c>null</c> or empty prefixes to <see cref="allPrefix"/>.
        /// </summary>
        /// <param name="pathPrefix">The input prefix.</param>
        /// <returns>The prefix or <see cref="allPrefix"/>.</returns>
        private static string NormalizePathPrefix(string pathPrefix)
        {
            if (string.IsNullOrEmpty(pathPrefix))
            {
                return allPrefix;
            }
            else
            {
                return pathPrefix;
            }
        }

        /// <returns>The option argument string.</returns>
        private static string GetHttpCheckOptionArgs(LoadBalancerRule rule)
        {
            Covenant.Requires<ArgumentNullException>(rule != null);

            if (rule.UseHttpCheckMode)
            {
                // Generate the request headers.  We're going to append the [CheckHost] if present first, 
                // followed by any custom check headers.

                var headers = string.Empty;

                if (!string.IsNullOrWhiteSpace(rule.CheckHost))
                {
                    headers += $@"Host: {rule.CheckHost}\r\n";
                }

                foreach (var header in rule.CheckHeaders)
                {
                    headers += $@"{header.Name}: {header.Value}\r\n";
                }

                var optionArgs = $"{rule.CheckMethod.ToUpperInvariant()} {rule.CheckUri} HTTP/{rule.CheckVersion}";

                if (!string.IsNullOrEmpty(headers))
                {
                    optionArgs += $@"\r\n{headers}";
                }

                return optionArgs;
            }
            else
            {
                throw new InvalidOperationException($"Rule [{rule.Name}] does not perform HTTP health checks.");
            }
        }

        /// <summary>
        /// Rebuilds the configurations for a public or private load balancers and 
        /// persists them to Consul if they differ from the previous version.
        /// </summary>
        /// <param name="loadBalancerName">The load balancer name: <b>public</b> or <b>private</b>.</param>
        /// <param name="hiveCerts">The hive certificate information.</param>
        /// <returns>
        /// A tuple including the load balancer's rule dictionary and publication status details.
        /// </returns>
        private static async Task<(Dictionary<string, LoadBalancerRule> Rules, string Status)> 
            BuildProxyConfigAsync(string loadBalancerName, HiveCerts hiveCerts)
        {
            var proxyDisplayName       = loadBalancerName.ToUpperInvariant();
            var proxyBridgeName        = $"{loadBalancerName}-bridge";
            var proxyBridgeDisplayName = proxyBridgeName.ToUpperInvariant();
            var isPublic               = loadBalancerName.Equals("public", StringComparison.InvariantCultureIgnoreCase);
            var configError            = false;
            var log                    = new LogRecorder(Program.log);

            log.LogInfo(() => $"Rebuilding load balancer [{proxyDisplayName}].");

            // We need to track which certificates are actually referenced by load balancer rules.

            hiveCerts.ClearReferences();

            // Load the load balancer's settings and rules.

            string                  proxyPrefix = $"{proxyConfKey}/{loadBalancerName}";
            var                     rules       = new Dictionary<string, LoadBalancerRule>();
            var                     hostGroups  = hiveDefinition.GetHostGroups(excludeAllGroup: false);
            LoadBalancerSettings    settings;

            try
            {
                settings = await consul.KV.GetObjectOrDefault<LoadBalancerSettings>($"{proxyPrefix}/settings", terminator.CancellationToken);

                if (settings == null)
                {
                    // Initialize default settings for the load balancer if they aren't written to Consul yet.

                    HiveProxyPorts proxyPorts;

                    switch (loadBalancerName)
                    {
                        case "public":

                            proxyPorts = HiveConst.PublicProxyPorts;
                            break;

                        case "private":

                            proxyPorts = HiveConst.PrivateProxyPorts;
                            break;

                        default:

                            throw new NotImplementedException();
                    }

                    settings = new LoadBalancerSettings()
                    {
                        ProxyPorts = proxyPorts
                    };

                    log.LogInfo(() => $"Updating proxy [{proxyDisplayName}] settings.");
                    await consul.KV.PutString($"{proxyPrefix}/settings", NeonHelper.JsonSerialize(settings, Formatting.None), terminator.CancellationToken);
                }

                log.LogInfo(() => $"Reading [{proxyDisplayName}] rules.");

                var result = await consul.KV.List($"{proxyPrefix}/rules/", terminator.CancellationToken);

                if (result.Response != null)
                {
                    foreach (var ruleKey in result.Response)
                    {
                        var rule = LoadBalancerRule.ParseJson(Encoding.UTF8.GetString(ruleKey.Value));

                        rules.Add(rule.Name, rule);
                    }
                }
            }
            catch (Exception e)
            {
                // Warn and exit for (presumably transient) Consul errors.

                log.LogWarn($"Consul request failure for load balancer [{proxyDisplayName}].", e);
                return (Rules: rules, Status: log.ToString());
            }

            log.Record();

            // Ensure that all of the HTTP rules have a non-NULL cache property.

            foreach (LoadBalancerHttpRule httpRule in rules.Values.Where(r => r.Mode == LoadBalancerMode.Http))
            {
                httpRule.Cache = httpRule.Cache ?? new LoadBalancerHttpCache();
            }

            // Record some details about the rules.

            var httpRuleCount = rules.Values.Count(r => r.Mode == LoadBalancerMode.Http);
            var tcpRuleCount  = rules.Values.Count(r => r.Mode == LoadBalancerMode.Tcp);

            // Record HTTP rule summaries.

            if (httpRuleCount == 0 && tcpRuleCount == 0)
            {
                log.Record("*** No load balancer rules defined.");
            }

            if (httpRuleCount > 0)
            {
                log.Record($"HTTP Rules [count={httpRuleCount}]");
                log.Record("------------------------------");

                foreach (LoadBalancerHttpRule rule in rules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Http)
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                {
                    log.Record();
                    RecordRule(log, rule);
                }
            }

            log.Record();

            // Record TCP rule summaries.

            if (tcpRuleCount > 0)
            {
                log.Record($"TCP Rules [count={tcpRuleCount}]");
                log.Record("------------------------------");

                foreach (LoadBalancerTcpRule rule in rules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Tcp)
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                {
                    log.Record();
                    RecordRule(log, rule);
                }
            }

            log.Record();

            // Verify the configuration.

            var loadBalancerDefinition = new LoadBalancerDefinition()
            {
                Name     = loadBalancerName,
                Settings = settings,
                Rules    = rules
            };

            var validationContext = loadBalancerDefinition.Validate(hiveCerts.ToTlsCertificateDictionary());

            if (validationContext.HasErrors)
            {
                log.LogError(validationContext.GetErrors());
                return (Rules: rules, log.ToString());
            }

            // Generate the contents of the [haproxy.cfg] file.
            //
            // Note that the [neon-log-collector] depends on the format of the proxy frontend
            // and backend names, so don't change these.

            var sbHaProxy = new StringBuilder();

            sbHaProxy.Append(
$@"#------------------------------------------------------------------------------
# {proxyDisplayName} PROXY: HAProxy configuration file.
#
# Generated by:     {serviceName}
# Documentation:    http://cbonte.github.io/haproxy-dconv/1.8/configuration.html

global
    daemon

# Specify the maximum number of connections allowed for a proxy instance.

    maxconn             {settings.MaxConnections}

# Enable logging to syslog on the local Docker host under the
# [HiveSysLogFacility_ProxyPublic] facility.

    log                 ""${{NEON_NODE_IP}}:{HiveHostPorts.LogHostSysLog}"" len 65535 {HiveSysLogFacility.ProxyName}

# Certificate Authority and Certificate file locations:

    ca-base             ""${{HAPROXY_CONFIG_FOLDER}}""
    crt-base            ""${{HAPROXY_CONFIG_FOLDER}}""

# Disable TLS certificate verification for backend health checks:

    ssl-server-verify   none

# Other settings

    tune.ssl.default-dh-param   {settings.MaxDHParamBits}

defaults
    balance             roundrobin
    retries             2
    timeout connect     {ToHaProxyTime(settings.Timeouts.ConnectSeconds)}
    timeout client      {ToHaProxyTime(settings.Timeouts.ClientSeconds)}
    timeout server      {ToHaProxyTime(settings.Timeouts.ServerSeconds)}
    timeout check       {ToHaProxyTime(settings.Timeouts.CheckSeconds)}
");

            if (settings.Resolvers.Count > 0)
            {
                foreach (var resolver in settings.Resolvers)
                {
                    sbHaProxy.Append(
$@"
resolvers {resolver.Name}
    resolve_retries     {resolver.ResolveRetries}
    timeout retry       {ToHaProxyTime(resolver.RetrySeconds)}
    hold valid          {ToHaProxyTime(resolver.HoldSeconds)}
");
                    foreach (var nameserver in resolver.NameServers)
                    {
                        sbHaProxy.AppendLine($@"    nameserver          {nameserver.Name} {nameserver.Endpoint}");
                    }
                }
            }

            // Enable the HAProxy statistics pages.  These will be available on the 
            // [HiveConst.HAProxyStatsPort] port on the [neon-public] or
            // [neon-private] network the proxy serves.
            //
            // HAProxy statistics pages are not intended to be viewed directly by
            // by hive operators.  Instead, the statistics from multiple HAProxy
            // instances will be aggregated by the hive Dashboard.

            sbHaProxy.AppendLine($@"
#------------------------------------------------------------------------------
# Enable HAProxy statistics pages.

frontend haproxy_stats
    bind                *:{HiveConst.HAProxyStatsPort}
    mode                http
    log                 global
    option              httplog
    option              http-keep-alive
    use_backend         haproxy_stats

backend haproxy_stats
    mode                http
    stats               enable
    stats               scope .
    stats               uri {HiveConst.HaProxyStatsUri}
    stats               refresh 5s
");
            //-----------------------------------------------------------------
            // Verify that the rules don't conflict.

            // Verify that TCP rules don't have conflicting publically facing ports.

            var publicTcpPortToRule = new Dictionary<int, LoadBalancerRule>();

            foreach (LoadBalancerTcpRule rule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Tcp))
            {
                foreach (var frontend in rule.Frontends)
                {
                    if (frontend.PublicPort <= 0)
                    {
                        continue;
                    }

                    if (publicTcpPortToRule.TryGetValue(frontend.PublicPort, out LoadBalancerRule conflictRule))
                    {
                        log.LogError(() => $"TCP rule [{rule.Name}] has a public Internet facing port [{frontend.PublicPort}] conflict with TCP rule [{conflictRule.Name}].");
                        configError = true;
                    }
                    else
                    {
                        publicTcpPortToRule.Add(frontend.PublicPort, rule);
                    }
                }
            }

            // Verify that HTTP rules don't have conflicting publically facing ports and prefix
            // combinations.  To pass, an HTTP frontend public port can't already be assigned
            // to a TCP rule and the hostname/port/prefix combination can't already be assigned 
            // to another frontend.

            var publicHttpHostPortPrefixToRule = new Dictionary<string, LoadBalancerRule>();

            // Verify that Internet facing ports are assigned a single TCP rule.  HTTP
            // rules can share a port since HAProxy can use the hostname in requests for
            // routing.  We do though need to ensure that the combination of host/port/prefix
            // is unique for every HTTP rule thnough.

            foreach (LoadBalancerHttpRule rule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Http))
            {
                foreach (var frontend in rule.Frontends)
                {
                    if (frontend.PublicPort <= 0)
                    {
                        continue;
                    }

                    if (publicTcpPortToRule.TryGetValue(frontend.PublicPort, out LoadBalancerRule conflictRule))
                    {
                        var conflictRuleType = conflictRule.Mode.ToString().ToUpperInvariant();

                        log.LogError(() => $"HTTP rule [{rule.Name}] has a public Internet facing port [{frontend.PublicPort}] that conflicts with {conflictRuleType} rule [{conflictRule.Name}].");
                        configError = true;
                        continue;
                    }

                    if (rule.Mode == LoadBalancerMode.Http)
                    {
                        var hostPortPrefix = $"{frontend.Host}:{frontend.ProxyPort}:{frontend.PathPrefix ?? string.Empty}";

                        if (publicHttpHostPortPrefixToRule.TryGetValue(hostPortPrefix, out conflictRule) && rule != conflictRule)
                        {
                            log.LogError(() => $"HTTP rule [{rule.Name}] has a public Internet facing hostname/port [{hostPortPrefix}] that conflicts with HTTP rule [{conflictRule.Name}].");
                            configError = true;
                            continue;
                        }
                        else
                        {
                            publicHttpHostPortPrefixToRule.Add(hostPortPrefix, rule);
                        }
                    }
                }
            }

            // Verify that TCP rules don't have conflicting HAProxy frontends.  For
            // TCP, this means that a port can have only one assigned frontend.

            var haTcpProxyPortToRule = new Dictionary<int, LoadBalancerRule>();

            foreach (LoadBalancerTcpRule rule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Tcp))
            {
                foreach (var frontend in rule.Frontends)
                {
                    if (haTcpProxyPortToRule.TryGetValue(frontend.PublicPort, out LoadBalancerRule conflictRule))
                    {
                        log.LogError(() => $"TCP rule [{rule.Name}] has an HAProxy frontend port [{frontend.ProxyPort}] conflict with TCP rule [{conflictRule.Name}].");
                        configError = true;
                    }
                    else
                    {
                        haTcpProxyPortToRule.Add(frontend.ProxyPort, rule);
                    }
                }
            }

            // Verify that HTTP rules don't have conflicting HAProxy frontend ports.  For
            // HTTP, we need to make sure that there isn't already a TCP frontend on the
            // port and then ensure that only one HTTP frontend maps to a hostname/port/prefix
            // combination.

            var haHttpProxyHostPortPrefixToRule = new Dictionary<string, LoadBalancerRule>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadBalancerHttpRule rule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Http))
            {
                foreach (var frontend in rule.Frontends)
                {
                    if (!string.IsNullOrEmpty(frontend.PathPrefix))
                    {
                        continue;
                    }

                    if (haTcpProxyPortToRule.TryGetValue(frontend.PublicPort, out LoadBalancerRule conflictRule))
                    {
                        log.LogError(() => $"HTTP rule [{rule.Name}] has an HAProxy frontend port [{frontend.ProxyPort}] conflict with TCP rule [{conflictRule.Name}].");
                        configError = true;
                        continue;
                    }

                    var hostPortPrefix = $"{frontend.Host}:{frontend.ProxyPort}:{frontend.PathPrefix ?? string.Empty}";

                    if (haHttpProxyHostPortPrefixToRule.TryGetValue(hostPortPrefix, out conflictRule))
                    {
                        log.LogError(() => $"HTTP rule [{rule.Name}] has an HAProxy frontend hostname/port/path [{hostPortPrefix}] conflict with HTTP rule [{conflictRule.Name}].");
                        configError = true;
                    }
                    else
                    {
                        haHttpProxyHostPortPrefixToRule.Add(hostPortPrefix, rule);
                    }
                }
            }

            //-----------------------------------------------------------------
            // Generate the TCP rules.

            var hasTcpRules = false;

            if (rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Tcp)
                .Count() > 0)
            {
                hasTcpRules = true;

                sbHaProxy.AppendLine("#------------------------------------------------------------------------------");
                sbHaProxy.AppendLine("# TCP Rules");
            }

            foreach (LoadBalancerTcpRule tcpRule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Tcp))
            {
                // Generate the resolvers argument to be used to locate the
                // backend servers.

                var initAddrArg  = " init-addr last,libc,none";
                var resolversArg = string.Empty;

                if (!string.IsNullOrEmpty(tcpRule.Resolver))
                {
                    resolversArg = $" resolvers {tcpRule.Resolver}";
                }

                // Generate the frontend and it's associated backend servers.

                foreach (var frontend in tcpRule.Frontends)
                {
                    sbHaProxy.Append(
$@"
listen tcp:{tcpRule.Name}-port-{frontend.ProxyPort}
    mode                tcp
    bind                *:{frontend.ProxyPort}
");

                    if (tcpRule.MaxConnections > 0)
                    {
                        sbHaProxy.AppendLine($"    maxconn             {tcpRule.MaxConnections}");
                    }

                    if (tcpRule.Log)
                    {
                        sbHaProxy.AppendLine($"    log                 global");
                        sbHaProxy.AppendLine($"    log-format          {HiveHelper.GetProxyLogFormat("neon-proxy-" + loadBalancerName, tcp: true)}");
                    }

                    if (tcpRule.CheckMode != LoadBalancerCheckMode.Disabled && tcpRule.LogChecks)
                    {
                        sbHaProxy.AppendLine($"    option              log-health-checks");
                    }

                    if (tcpRule.UseHttpCheckMode)
                    {
                        sbHaProxy.AppendLine($"    option              httpchk {GetHttpCheckOptionArgs(tcpRule)}");

                        if (!string.IsNullOrEmpty(tcpRule.CheckExpect))
                        {
                            sbHaProxy.AppendLine($"    http-check          expect {tcpRule.CheckExpect.Trim()}");
                        }
                    }

                    // Explicitly set any rule timeouts that don't match the default settings.

                    if (tcpRule.Timeouts.ConnectSeconds != settings.Timeouts.ConnectSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout connect     {ToHaProxyTime(tcpRule.Timeouts.ConnectSeconds)}");
                    }

                    if (tcpRule.Timeouts.ClientSeconds != settings.Timeouts.ClientSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout client      {ToHaProxyTime(tcpRule.Timeouts.ClientSeconds)}");
                    }

                    if (tcpRule.Timeouts.ServerSeconds != settings.Timeouts.ServerSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout server      {ToHaProxyTime(tcpRule.Timeouts.ServerSeconds)}");
                    }

                    if (tcpRule.Timeouts.CheckSeconds != settings.Timeouts.CheckSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout check       {ToHaProxyTime(tcpRule.Timeouts.CheckSeconds)}");
                    }

                    sbHaProxy.AppendLine($"    default-server      inter {ToHaProxyTime(tcpRule.CheckSeconds)}");
                }

                var checkArg    = tcpRule.CheckMode != LoadBalancerCheckMode.Disabled ? " check" : " no-check";
                var checkSslArg = tcpRule.CheckTls ? " check-ssl" : string.Empty;
                var serverIndex = 0;

                if (tcpRule.CheckMode == LoadBalancerCheckMode.Disabled)
                {
                    checkSslArg = string.Empty;
                }

                foreach (var backend in tcpRule.SelectBackends(hostGroups))
                {
                    var backendName = $"server-{serverIndex++}";

                    if (!string.IsNullOrEmpty(backend.Name))
                    {
                        backendName = backend.Name;
                    }

                    sbHaProxy.AppendLine($"    server              {backendName} {backend.Server}:{backend.Port}{checkArg}{checkSslArg}{initAddrArg}{resolversArg}");
                }
            }

            //-----------------------------------------------------------------
            // HTTP rules are tricker:
            //
            //      1. We need to generate an HAProxy frontend for each IP/port combination 
            //         and then use HOST header or SNI rules in addition to an optional path
            //         prefix to map the correct backend.   This means that neonHIVE proxy
            //         frontends don't map directly to HAProxy frontends.
            //
            //      2. We need to generate an HAProxy backend for each neonHIVE proxy backend.
            //
            //      3. For TLS frontends, we're going to persist all of the referenced certificates 
            //         into frontend specific folders and then reference the folder in the bind
            //         statement.  HAProxy will use SNI to present the correct certificate to clients.

            var haProxyFrontends = new Dictionary<int, HAProxyHttpFrontend>();

            if (rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Http)
                .Count() > 0)
            {
                if (hasTcpRules)
                {
                    sbHaProxy.AppendLine();
                }

                sbHaProxy.AppendLine("#------------------------------------------------------------------------------");
                sbHaProxy.AppendLine("# HTTP Rules");

                // Enumerate all of the rules and build a dictionary with information about
                // the HAProxy frontends we'll need to generate.  This dictionary will be
                // keyed by the host/path.

                foreach (LoadBalancerHttpRule httpRule in rules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Http)
                    .OrderBy(r => r.Name))
                {
                    foreach (var frontend in httpRule.Frontends)
                    {
                        if (!haProxyFrontends.TryGetValue(frontend.ProxyPort, out HAProxyHttpFrontend haProxyFrontend))
                        {
                            haProxyFrontend = new HAProxyHttpFrontend(frontend)
                            {
                                Port       = frontend.ProxyPort,
                                PathPrefix = NormalizePathPrefix(frontend.PathPrefix)
                            };

                            haProxyFrontends.Add(frontend.ProxyPort, haProxyFrontend);
                        }

                        var hostPath = $"{frontend.Host ?? string.Empty}:{NormalizePathPrefix(frontend.PathPrefix)}";

                        if (haProxyFrontend.HostPathMappings.ContainsKey(hostPath))
                        {
                            // It's possible to incorrectly define multiple HTTP rules with the same 
                            // host/path mapping to the same HAProxy frontend port.  This code will
                            // simply choose a winner and log a warning.

                            // $todo(jeff.lill): 
                            //
                            // I'm not entirely sure that this check is really necessary.

                            LoadBalancerHttpRule conflictRule = null;

                            foreach (LoadBalancerHttpRule checkRule in rules.Values
                                .Where(r => r.Mode == LoadBalancerMode.Http && r != httpRule))
                            {
                                if (checkRule.Frontends.Count(fe => fe.ProxyPort == frontend.ProxyPort && fe.Host.Equals(frontend.Host, StringComparison.CurrentCultureIgnoreCase)) > 0)
                                {
                                    conflictRule = checkRule;
                                }
                            }

                            if (conflictRule != null)
                            {
                                log.LogWarn(() => $"HTTP rule [{httpRule.Name}] defines a frontend for host/port [{frontend.Host}/{frontend.ProxyPort}] which conflicts with rule [{conflictRule.Name}].  This frontend will be ignored.");
                            }
                        }
                        else
                        {
                            haProxyFrontend.HostPathMappings[hostPath] = new HostPathMapping(httpRule, frontend, $"http:{httpRule.Name}");
                        }

                        if (httpRule.Log)
                        {
                            // If any of the rules on this port require logging we'll have to
                            // enable logging for all of the rules, since they'll end up sharing
                            // the same proxy frontend.

                            haProxyFrontend.Log = true;
                        }

                        if (frontend.Tls)
                        {
                            if (!hiveCerts.TryGetValue(frontend.CertName, out CertInfo certInfo))
                            {
                                log.LogError(() => $"Rule [{httpRule.Name}] references certificate [{frontend.CertName}] which does not exist or could not be loaded.");
                                configError = true;
                                continue;
                            }

                            if (!certInfo.Certificate.IsValidHost(frontend.Host))
                            {
                                log.LogError(() => $"Certificate [{certInfo.Name}] for [hosts={certInfo.Certificate.HostNames}] does not cover host [{frontend.Host}] for a rule [{httpRule.Name}] frontend.");
                            }

                            certInfo.WasReferenced = true;
                            haProxyFrontend.Certificates[certInfo.Name] = certInfo.Certificate;
                        }

                        if (frontend.Tls != haProxyFrontend.Tls)
                        {
                            if (frontend.Tls)
                            {
                                log.LogError(() => $"Rule [{httpRule.Name}] specifies a TLS frontend on port [{frontend.ProxyPort}] that conflict with non-TLS frontends on the same port.");
                                configError = true;
                            }
                            else
                            {
                                log.LogError(() => $"Rule [{httpRule.Name}] specifies a non-TLS frontend on port [{frontend.ProxyPort}] that conflict with TLS frontends on the same port.");
                                configError = true;
                            }
                        }
                    }
                }

                // We can generate the HAProxy HTTP frontends now.

                foreach (var haProxyFrontend in haProxyFrontends.Values.OrderBy(f => f.Port))
                {
                    var certArg = string.Empty;

                    if (haProxyFrontend.Tls)
                    {
                        certArg = $" ssl strict-sni crt certs-{haProxyFrontend.Name.Replace(':', '-')}";
                    }

                    var scheme = haProxyFrontend.Tls ? "https" : "http";

                    sbHaProxy.Append(
$@"
frontend {haProxyFrontend.Name}
    mode                http
    bind                *:{haProxyFrontend.Port}{certArg}
    unique-id-header    {LogActivity.HttpHeader}
    unique-id-format    {HiveConst.HAProxyUidFormat}
    option              forwardfor
    option              http-keep-alive
    http-request        set-header X-Forwarded-Proto https if {{ ssl_fc }}
");

                    if (haProxyFrontend.Log)
                    {
                        sbHaProxy.AppendLine($"    capture             request header Host len 255");
                        sbHaProxy.AppendLine($"    capture             request header User-Agent len 2048");
                        sbHaProxy.AppendLine($"    log                 global");
                        sbHaProxy.AppendLine($"    log-format          {HiveHelper.GetProxyLogFormat("neon-proxy-" + loadBalancerName, tcp: false)}");
                    }

                    // We need to keep track of the ACLs we've defined for each hostname
                    // referenced by this frontend so we won't generate multiple ACLs
                    // for the same host.

                    var aclHosts = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

                    // Generate the backend mappings for frontends with path prefixes
                    // first, before we fall back to matching just the hostname.
                    // Note that we're going to generate mappings for the longest 
                    // paths first and we're also going to sort by path so the
                    // HAProxy config file will be a bit easier to read.

                    var pathAclCount = 0;

                    foreach (var hostPathMapping in haProxyFrontend.HostPathMappings
                        .Where(m => HAProxyHttpFrontend.GetPath(m.Key) != allPrefix)
                        .OrderByDescending(m => HAProxyHttpFrontend.GetPath(m.Key).Length)
                        .ThenBy(m => HAProxyHttpFrontend.GetPath(m.Key)))
                    {
                        var host        = HAProxyHttpFrontend.GetHost(hostPathMapping.Key);
                        var path        = HAProxyHttpFrontend.GetPath(hostPathMapping.Key);
                        var hostAclName = $"is-{host.Replace('.', '-')}";
                        var pathAclName = $"is-path-{pathAclCount++}";

                        sbHaProxy.AppendLine();

                        if (hostPathMapping.Value.Frontend.RedirectTo != null)
                        {
                            continue;
                        }
                        else if (haProxyFrontend.Tls)
                        {
                            if (!aclHosts.Contains(host))
                            {
                                aclHosts.Add(host);
                                sbHaProxy.AppendLine($"    acl                 {hostAclName} ssl_fc_sni {host}");
                            }

                            sbHaProxy.AppendLine($"    acl                 {pathAclName} path_beg '{path}'");
                            SetCacheFrontendHeader(sbHaProxy, haProxyFrontend, path, hostPathMapping.Value, host, pathAclName, hostAclName);
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName} if {hostAclName} {pathAclName}");
                        }
                        else if (!string.IsNullOrEmpty(host))
                        {
                            if (!aclHosts.Contains(host))
                            {
                                aclHosts.Add(host);
                                sbHaProxy.AppendLine($"    acl                 {hostAclName} hdr_reg(host) -i {host}(:\\d+)?");
                            }

                            sbHaProxy.AppendLine($"    acl                 {pathAclName} path_beg '{path}'");
                            SetCacheFrontendHeader(sbHaProxy, haProxyFrontend, path, hostPathMapping.Value, host, pathAclName, hostAclName);
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName} if {hostAclName} {pathAclName}");
                        }
                        else
                        {
                            // The frontend does not specify a host so we'll use the backend
                            // if only the path matches.

                            sbHaProxy.AppendLine($"    acl                 {pathAclName} path_beg '{path}'");
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName} if {pathAclName}");
                        }
                    }

                    // Now generate the backend mappings for frontends without path prefixes.
                    // This code is a bit of a hack.  It depends on the host/path mapping key
                    // being formatted as $"{host}:{path}" with [path] being normalized.

                    foreach (var hostPathMapping in haProxyFrontend.HostPathMappings
                        .Where(m => HAProxyHttpFrontend.GetPath(m.Key) == allPrefix)
                        .OrderBy(m => HAProxyHttpFrontend.GetHost(m.Key)))
                    {
                        var host        = HAProxyHttpFrontend.GetHost(hostPathMapping.Key);
                        var hostAclName = $"is-{host.Replace('.', '-')}";

                        sbHaProxy.AppendLine();

                        if (hostPathMapping.Value.Frontend.RedirectTo != null)
                        {
                            if (haProxyFrontend.Tls)
                            {
                                if (!aclHosts.Contains(host))
                                {
                                    aclHosts.Add(host);
                                    sbHaProxy.AppendLine($"    acl                 {hostAclName} ssl_fc_sni {host}");
                                }
                            }
                            else
                            {
                                if (!aclHosts.Contains(host))
                                {
                                    aclHosts.Add(host);
                                    sbHaProxy.AppendLine($"    acl                 {hostAclName} hdr_reg(host) -i {host}(:\\d+)?");
                                }
                            }

                            sbHaProxy.AppendLine($"    redirect            location {hostPathMapping.Value.Frontend.RedirectTo} if {hostAclName}");
                        }
                        else if (haProxyFrontend.Tls)
                        {
                            if (!aclHosts.Contains(host))
                            {
                                aclHosts.Add(host);
                                sbHaProxy.AppendLine($"    acl                 {hostAclName} ssl_fc_sni {host}");
                            }

                            SetCacheFrontendHeader(sbHaProxy, haProxyFrontend, "/", hostPathMapping.Value, host, hostAclName);
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName} if {hostAclName}");
                        }
                        else if (!string.IsNullOrEmpty(host))
                        {
                            if (!aclHosts.Contains(host))
                            {
                                aclHosts.Add(host);
                                sbHaProxy.AppendLine($"    acl                 {hostAclName} hdr_reg(host) -i {host}(:\\d+)?");
                            }

                            SetCacheFrontendHeader(sbHaProxy, haProxyFrontend, "/", hostPathMapping.Value, host, hostAclName);
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName} if {hostAclName}");
                        }
                        else
                        {
                            // The frontend does not specify a host so we'll always use the backend.

                            SetCacheFrontendHeader(sbHaProxy, haProxyFrontend, "/", hostPathMapping.Value, host);
                            sbHaProxy.AppendLine($"    use_backend         {hostPathMapping.Value.BackendName}");
                        }
                    }
                }

                // Generate the HTTP backends

                foreach (LoadBalancerHttpRule httpRule in rules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Http)
                    .Where(r => ((LoadBalancerHttpRule)r).Backends.Count > 0)
                    .OrderBy(r => r.Name))
                {
                    // Generate the resolvers argument to be used to locate the
                    // backend servers.

                    var resolversArg = string.Empty;

                    if (!string.IsNullOrEmpty(httpRule.Resolver))
                    {
                        resolversArg = $" resolvers {httpRule.Resolver}";
                    }

                    var checkArg    = httpRule.CheckMode != LoadBalancerCheckMode.Disabled ? " check" : " no-check";
                    var checkSslArg = httpRule.CheckTls ? " check-ssl" : string.Empty;
                    var initAddrArg = " init-addr last,libc,none";
                    var backends    = httpRule.SelectBackends(hostGroups);

                    var checkMode = httpRule.CheckMode;

                    if (httpRule.CheckMode == LoadBalancerCheckMode.Disabled)
                    {
                        checkSslArg = string.Empty;
                    }

                    sbHaProxy.Append(
$@"
backend http:{httpRule.Name}
    mode                http
");

                    if (httpRule.Log)
                    {
                        sbHaProxy.AppendLine($"    log                 global");
                    }

                    if (checkMode != LoadBalancerCheckMode.Disabled)
                    {
                        if (httpRule.UseHttpCheckMode)
                        {
                            sbHaProxy.AppendLine($"    option              httpchk {GetHttpCheckOptionArgs(httpRule)}");

                            if (!string.IsNullOrEmpty(httpRule.CheckExpect))
                            {
                                sbHaProxy.AppendLine($"    http-check          expect {httpRule.CheckExpect.Trim()}");
                            }
                        }
                        else if (httpRule.CheckMode == LoadBalancerCheckMode.Tcp)
                        {
                            sbHaProxy.AppendLine($"    option              tcp-check");
                        }

                        if (httpRule.CheckMode != LoadBalancerCheckMode.Disabled && httpRule.LogChecks)
                        {
                            sbHaProxy.AppendLine($"    option              log-health-checks");
                        }

                        if (httpRule.Timeouts.CheckSeconds != settings.Timeouts.CheckSeconds)
                        {
                            sbHaProxy.AppendLine($"    timeout check       {ToHaProxyTime(httpRule.Timeouts.CheckSeconds)}");
                        }

                        sbHaProxy.AppendLine($"    default-server      inter {ToHaProxyTime(httpRule.CheckSeconds)}");
                    }

                    // Explicitly set any rule timeouts that don't match the default settings.

                    if (httpRule.Timeouts.ConnectSeconds != settings.Timeouts.ConnectSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout connect     {ToHaProxyTime(httpRule.Timeouts.ConnectSeconds)}");
                    }

                    if (httpRule.Timeouts.ClientSeconds != settings.Timeouts.ClientSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout client      {ToHaProxyTime(httpRule.Timeouts.ClientSeconds)}");
                    }

                    if (httpRule.Timeouts.ServerSeconds != settings.Timeouts.ServerSeconds)
                    {
                        sbHaProxy.AppendLine($"    timeout server      {ToHaProxyTime(httpRule.Timeouts.ServerSeconds)}");
                    }

                    if (httpRule.Cache.Enabled)
                    {
                        // Caching is enabled so we need to add add a single server backend
                        // that forwards traffic to Varnish.

                        // NOTE:
                        //
                        // We're going to assume that the Varnish backend is always healthy because
                        // there's only one server specifyed here and we're relying on Docker to
                        // manage failover between multiple proxy instances.  So we're not going
                        // enable health checks.

                        var varnishBackend = isPublic ? "neon-proxy-public-cache" : "neon-proxy-private-cache";

                        sbHaProxy.AppendLine($"    server              {varnishBackend} {varnishBackend}:80{initAddrArg}{resolversArg}");
                    }
                    else
                    {
                        // Caching is not enabled so we'll just target the actual origin servers.

                        var serverIndex = 0;

                        foreach (var backend in backends)
                        {
                            var serverName = $"server-{serverIndex++}";

                            if (!string.IsNullOrEmpty(backend.Name))
                            {
                                serverName = backend.Name;
                            }

                            var sslArg = string.Empty;

                            if (backend.Tls)
                            {
                                sslArg = $" ssl verify required";
                            }

                            if (checkMode == LoadBalancerCheckMode.Disabled)
                            {
                                sbHaProxy.AppendLine($"    server              {serverName} {backend.Server}:{backend.Port}{sslArg}{initAddrArg}{resolversArg}");
                            }
                            else
                            {
                                sbHaProxy.AppendLine($"    server              {serverName} {backend.Server}:{backend.Port}{sslArg}{checkArg}{checkSslArg}{initAddrArg}{resolversArg}");
                            }
                        }
                    }
                }
            }

            if (configError)
            {
                log.LogError("Proxy configuration aborted due to one or more errors.");
                return (Rules: rules, log.ToString());
            }

            // Generate the contents of the [certs.list] file.

            var sbCerts = new StringBuilder();

            foreach (var haProxyFrontend in haProxyFrontends.Values.OrderBy(f => f.Port))
            {
                var certFolder = $"certs-{haProxyFrontend.Name.Replace(':', '-')}";

                foreach (var item in haProxyFrontend.Certificates.OrderBy(certItem => certItem.Key))
                {
                    sbCerts.AppendLine($"{vaultCertPrefix}/{item.Key} {certFolder} {item.Key}.pem");
                }
            }

            // Generate the Varnish VCL file.  This will be deployed as the 
            // [varnish.vcl] file within the ZIP archive.

            var sbVarnishVcl = new StringBuilder();

            var stubVcl =
$@"vcl 4.0;
#------------------------------------------------------------------------------
# {proxyDisplayName} PROXY: Varnish configuration file.
#
# Generated by:     {serviceName}
# Documentation:    http://varnish-cache.org

# The proxy configuration has no rules that enable caching so 
# we'll use this stub VCL file that doesn't do anything.

backend stub {{
    .host = ""localhost"";
    .port = ""8080"";
}}
";
            var httpRules    = rules.Where(r => r.Value.Mode == LoadBalancerMode.Http).Select(r => (LoadBalancerHttpRule)r.Value);
            var cachingRules = httpRules
                .Where(r => r.Cache.Enabled && r.Backends.Count() > 0)
                .OrderBy(r => r.Name.ToUpperInvariant())
                .ToList();

            if (cachingRules.IsEmpty())
            {
                sbVarnishVcl.Append(stubVcl);
            }
            else
            {
                var vclHeader =
$@"vcl 4.0;
#------------------------------------------------------------------------------
# {proxyDisplayName} PROXY: Varnish configuration file.
#
# Generated by:     {serviceName}
# Documentation:    http://varnish-cache.org

import directors;
import dynamic;

# Varnish requires at least one backend but we may be using only dynamic backends 
# so we'll define a stub backend here.

backend stub {{
    .host = ""localhost"";
    .port = ""8080"";
}}
";
                sbVarnishVcl.Append(vclHeader);

                // Generate the [vcl_backend_error] subroutine to return a synthetic
                // response with a more responable error message than Varnish generates
                // (which is "Guru Meditation").

                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
                sbVarnishVcl.AppendLine($"# Override the default Varnish response error message for 503 backend errors.");
                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"sub vcl_backend_error {{");
                sbVarnishVcl.AppendLine($"    if (beresp.status == 503) {{");
                sbVarnishVcl.AppendLine($"        set beresp.http.status = 503;");
                sbVarnishVcl.AppendLine($"        set beresp.http.reason = \"Service Unavailable\";");
                sbVarnishVcl.AppendLine($"        set beresp.http.Cache-Control = \"no-cache\";");
                sbVarnishVcl.AppendLine($"        set beresp.http.Content-Type = \"text/html; charset=utf-8\";");
                sbVarnishVcl.AppendLine($"synthetic({{\"<html>");
                sbVarnishVcl.AppendLine($"<head><title>neonHIVE Proxy Cache Error</title></head>");
                sbVarnishVcl.AppendLine($"<body>");
                sbVarnishVcl.AppendLine($"<h1>503: Service Unavailable</h1>");
                sbVarnishVcl.AppendLine($"No origin servers are healthy.");
                sbVarnishVcl.AppendLine($"</body>");
                sbVarnishVcl.AppendLine($"</html>");
                sbVarnishVcl.AppendLine($"\"}} );");
                sbVarnishVcl.AppendLine($"        return (deliver);");
                sbVarnishVcl.AppendLine($"    }}");
                sbVarnishVcl.AppendLine($"}}");

                // Generate a custom [vcl_synth] subroutine too.

                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
                sbVarnishVcl.AppendLine($"# Override the default Varnish [vcl_synth] subroutine too.");
                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"sub vcl_synth {{");
                sbVarnishVcl.AppendLine($"    set resp.http.Content-Type = \"text/html; charset=utf-8\";");
                sbVarnishVcl.AppendLine($"synthetic({{\"<html>");
                sbVarnishVcl.AppendLine($"<head><title>neonHIVE Proxy Cache</title></head>");
                sbVarnishVcl.AppendLine($"<body>");
                sbVarnishVcl.AppendLine($"<h1>\"}} + resp.status + \": \" + resp.reason + {{\"</h1>");
                sbVarnishVcl.AppendLine($"Unable to handle this request.");
                sbVarnishVcl.AppendLine($"</body>");
                sbVarnishVcl.AppendLine($"</html>");
                sbVarnishVcl.AppendLine($"\"}} );");
                sbVarnishVcl.AppendLine($"    return (deliver);");
                sbVarnishVcl.AppendLine($"}}");

                // Generate the backends for each HTTP rule that enables caching
                // and whose origin servers are reached only via IP addresses.
                // We're going to name each of these backends like:
                //
                //      rule_RULE#_backend_BACKEND#
                //
                // where RULE# is the index of the rule and BACKEND# is the index of 
                // the specific backend within the rule.  We're going to process the
                // rules in sorted order by name so the order of the generated code
                // elements will tend to be stable.
                //
                // We're going to use dynamic backends for rules whose single origin
                // server is reached via a hostname lookup.  The load balancer model 
                // code should insist that there be only a single backend in this 
                // case, but for resilience, we'll use the first origin server 
                // when there are more than one, ignorning the others.

                var ruleIndex = 0;

                foreach (var rule in cachingRules)
                {
                    // Determine whether to enable HTTP health probes.  We're not going to
                    // do these if they were explicitly disabled for the rule or if there
                    // is only one origin server (in which case we might as well forward
                    // the request because there's no better place to send it).

                    var probeEnabled = rule.CheckMode != LoadBalancerCheckMode.Disabled &&
                                       rule.CheckMode != LoadBalancerCheckMode.Tcp &&
                                       rule.Backends.Count > 1;

                    sbVarnishVcl.AppendLine();
                    sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
                    sbVarnishVcl.AppendLine($"# Rule[{ruleIndex}]: {rule.Name}");

                    var backendIndex = 0;

                    foreach (var backend in rule.Backends)
                    {
                        var sbCheckHeaders = new StringBuilder();
                        var checkStatus    = "200";

                        // We need to verify that [CheckExpect] is set to [string STATUS] where STATUS
                        // is a valid integer status code.  Varnish health probes don't have a way to 
                        // specify a regex like HAProxy can do.  We should never see anything other than
                        // this because the rule should varify this but we're going to fall back to checking 
                        // for 200 responses just in case as a fail-safe.

                        var statusFields = rule.CheckExpect.Split(' ');

                        if (statusFields.Length == 2 && statusFields[0] == "string" &&
                            int.TryParse(statusFields[1], out var statusCode) && 0 < statusCode && statusCode < 600)
                        {
                            checkStatus = statusCode.ToString();
                        }

                        // Generate the health probes.

                        foreach (var header in rule.CheckHeaders)
                        {
                            sbCheckHeaders.Append($"            {header.Name}: {header.Value}");
                        }

                        var probeVcl = string.Empty;

                        if (probeEnabled)
                        {
                            // $todo(jeff.lill: Health check parameters (hardcoded for now).
                            //
                            // We're setting [initial=0] which means that backends will be 
                            // considered healthy immediately after the service starts.  This
                            // will help avoid 503 errors when the proxy cache is started or
                            // updated.

                            const int window    = 3;
                            const int initial   = 0;
                            const int threshold = 2;

                            if (sbCheckHeaders.Length == 0)
                            {
                                probeVcl =
$@"
probe rule_{ruleIndex}_probe_{backendIndex} {{
    .request =
        ""{rule.CheckMethod} / HTTP/{rule.CheckVersion ?? "1.1"}""
        ""Host: {rule.CheckHost ?? backend.Server}""
        ""Accept: */*""
        ""Connection: close"";
    .window            = {window};
    .threshold         = {threshold};
    .initial           = {initial};
    .interval          = {RoundUp(rule.CheckSeconds)}s;
    .timeout           = {RoundUp(rule.Timeouts.CheckSeconds)}s;
    .expected_response = {checkStatus};
}}
";
                            }
                            else
                            {
                                probeVcl =
$@"
probe rule_{ruleIndex}_probe_{backendIndex} {{
    .request =
        ""{rule.CheckMethod} / HTTP/{rule.CheckVersion ?? "1.1"}""
        ""Host: {rule.CheckHost ?? backend.Server}""
        {sbCheckHeaders}
        ""Connection: close"";
    .window            = {window};
    .threshold         = {threshold};
    .initial           = {initial};
    .interval          = {RoundUp(rule.CheckSeconds)}s;
    .timeout           = {RoundUp(rule.Timeouts.CheckSeconds)}s;
    .expected_response = {checkStatus};
}}
";
                            }

                            sbVarnishVcl.Append(probeVcl);
                        }

                        // Generate the backend for origin servers reachable via IP address.
                        // We'll handle hostname resolved backends below.

                        var backendVcl = string.Empty;

                        if (!rule.HasSingleHostnameBackend)
                        {
                            if (probeEnabled)
                            {
                                backendVcl =
$@"
backend rule_{ruleIndex}_backend_{backendIndex} {{
    .host  = ""{backend.Server}"";
    .port  = ""{backend.Port}"";
    .probe = ""rule_{ruleIndex}_probe_{backendIndex}"";
}}
";
                            }
                            else
                            {
                                backendVcl =
$@"
backend rule_{ruleIndex}_backend_{backendIndex} {{
    .host  = ""{backend.Server}"";
    .port  = ""{backend.Port}"";
}}
";
                            }
                        }

                        sbVarnishVcl.Append(backendVcl);
                        backendIndex++;
                    }

                    // Generate the rule's initialization subroutine.  We're going to create
                    // a single round-robin director and then add a dynamic director for each
                    // rule backend.

                    sbVarnishVcl.AppendLine();
                    sbVarnishVcl.AppendLine($"sub init_rule_{ruleIndex} {{");

                    if (rule.HasSingleHostnameBackend)
                    {
                        var backend = rule.Backends.First();

                        if (probeEnabled)
                        {
                            sbVarnishVcl.AppendLine($"    new rule_{ruleIndex}_director = dynamic.director(port = \"{backend.Port}\", probe = rule_{ruleIndex}_probe_{backendIndex}), ttl = {rule.Cache.DnsTTL}s);");
                        }
                        else
                        {
                            sbVarnishVcl.AppendLine($"    new rule_{ruleIndex}_director = dynamic.director(port = \"{backend.Port}\", ttl = {rule.Cache.DnsTTL}s);");
                        }
                    }
                    else
                    {
                        backendIndex = 0;

                        foreach (var backend in rule.Backends)
                        {
                            sbVarnishVcl.AppendLine($"    new rule_{ruleIndex}_director = directors.round_robin();");

                            for (int i = 0; i < rule.Backends.Count(); i++)
                            {
                                sbVarnishVcl.AppendLine($"    rule_{ruleIndex}_director.add_backend(rule_{ruleIndex}_backend_{i});");
                            }

                            backendIndex++;
                        }
                    }

                    sbVarnishVcl.AppendLine($"}}");

                    ruleIndex++;
                }

                // Generate the [vcl_init] subroutine to call all of the rule
                // initialization subroutines.

                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
                sbVarnishVcl.AppendLine($"# Initialize the directors and backends for each rule.");
                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"sub vcl_init {{");

                for (int ruleNum = 0; ruleNum < cachingRules.Count; ruleNum++)
                {
                    var separator = new string(' ', 6 - ruleNum.ToString().Length);

                    sbVarnishVcl.AppendLine($"    call init_rule_{ruleNum};{separator}# Rule[{ruleNum}]: {cachingRules[ruleNum].Name}");
                }

                sbVarnishVcl.AppendLine($"}}");

                // Generate the [vcl_recv] subroutine that tries to map the request to 
                // the correct rule director or dynamic backend using the [X-Neon-Frontend]
                // header which must be present to be able to identify the correct director.
                //
                // The [X-Neon-Frontend] header is added by the HAProxy configuration
                // for rules with caching enabled.  This will look like one of the following
                // depending on whether the frontend defines a hostname:
                //
                //      X-Neon-Frontend: PORT PREFIX HOSTNAME
                //      X-Neon-Frontend: PORT PREFIX
                //
                // Where PORT is the proxy frontend port that received the request, PREFIX
                // is the path prefix (defaults to "/") and HOSTNAME identifies the request 
                // hosts (without any ":PORT" part).

                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
                sbVarnishVcl.AppendLine($"# Use the [X-Neon-Frontend] header to identify the target director.");
                sbVarnishVcl.AppendLine();
                sbVarnishVcl.AppendLine($"sub vcl_recv {{");

                sbVarnishVcl.AppendLine($"    if (!req.http.X-Neon-Frontend) {{");
                sbVarnishVcl.AppendLine($"        if (req.method == \"OPTIONS\" || req.method == \"HEAD\") {{");
                sbVarnishVcl.AppendLine($"            return (synth(200)); # Treat this as an HAProxy health probe.");
                sbVarnishVcl.AppendLine($"        }}");
                sbVarnishVcl.AppendLine($"        return (synth(400, \"[X-Neon-Frontend] header is required.\"));");
                sbVarnishVcl.AppendLine($"    }} else if (req.http.X-Neon-Frontend == \"\") {{");
                sbVarnishVcl.AppendLine($"        return (synth(400, \"[X-Neon-Frontend] header cannot be empty.\"));");

                for (ruleIndex = 0; ruleIndex < cachingRules.Count; ruleIndex++)
                {
                    var rule = cachingRules[ruleIndex];

                    foreach (var frontend in rule.Frontends)
                    {
                        sbVarnishVcl.AppendLine($"    }} else if (req.http.X-Neon-Frontend == \"{frontend.GetProxyFrontendHeader()}\") {{");

                        if (rule.HasSingleHostnameBackend)
                        {
                            var hostname = rule.Backends.First().Server;

                            sbVarnishVcl.AppendLine($"        set req.backend_hint = rule_{ruleIndex}_director.backend(\"{hostname}\"); # Rule[{ruleIndex}]: {rule.Name}");
                        }
                        else
                        {
                            sbVarnishVcl.AppendLine($"        set req.backend_hint = rule_{ruleIndex}_director.backend(); # Rule[{ruleIndex}]: {rule.Name}");
                        }

                        if (rule.Cache.Debug)
                        {
                            // Add the [X-Neon-Cache-Debug: true] header here so that the generated
                            // [vcl_deliver] subroutine will know to add the [X-Neon-Cache] DEBUG header.

                            sbVarnishVcl.AppendLine($"    set req.http.X-Neon-Cache-Debug = \"true\";");
                        }
                    }
                }

                sbVarnishVcl.AppendLine($"    }}");
                sbVarnishVcl.AppendLine($"    else {{");
                sbVarnishVcl.AppendLine($"        return (synth(503, \"[\" + req.http.X-Neon-Frontend + \"] does not map to a load balancer rule.\"));");
                sbVarnishVcl.AppendLine($"    }}");

                sbVarnishVcl.AppendLine($"}}");
            }

            // Generate the [vcl_deliver] subroutine that performs any last rites on the response
            // (like adding the [X-Neon-Cache] header for DEBUG mode.

            sbVarnishVcl.AppendLine();
            sbVarnishVcl.AppendLine($"#------------------------------------------------------------------------------");
            sbVarnishVcl.AppendLine($"# Make any final modifications before delivering the response.");
            sbVarnishVcl.AppendLine();
            sbVarnishVcl.AppendLine($"sub vcl_deliver {{");
            sbVarnishVcl.AppendLine($"    if (req.http.X-Neon-Cache-Debug) {{");
            sbVarnishVcl.AppendLine($"        set resp.http.X-Neon-Proxy-Cache = \"hits=\" + obj.hits;");
            sbVarnishVcl.AppendLine($"    }}");
            sbVarnishVcl.AppendLine($"    return(deliver);");
            sbVarnishVcl.AppendLine($"}}");

            // Generate the cache settings which will be deployed as the
            // [cache-settings.json] file within the ZIP archive.

            var cacheSettings = new LoadBalancerCacheSettings();

            foreach (var rule in httpRules.Where(r => r.Mode == LoadBalancerMode.Http && r.Cache.Enabled))
            {
                foreach (var warmTarget in rule.Cache.WarmTargets)
                {
                    cacheSettings.WarmTargets.Add(warmTarget);
                }
            }

            // Generate the [neon-proxy] and [neon-proxy-cache] service compatible configuration ZIP archive.

            byte[] zipBytes;

            using (var ms = new MemoryStream())
            {
                using (var zip = ZipFile.Create(ms))
                {
                    // We need all archive entries to have fixed dates so we'll be able
                    // to compare configuration archives for changes using MD5 hashes.

                    zip.EntryFactory = new ZipEntryFactory(new DateTime(2000, 1, 1));

                    // NOTE: We're converting text to Linux style line endings.

                    zip.BeginUpdate();
                    zip.Add(new StaticBytesDataSource(NeonHelper.ToLinuxLineEndings(sbHaProxy.ToString())), "haproxy.cfg");
                    zip.Add(new StaticBytesDataSource(NeonHelper.ToLinuxLineEndings(sbCerts.ToString())), "certs.list");
                    zip.Add(new StaticBytesDataSource(NeonHelper.ToLinuxLineEndings(sbVarnishVcl.ToString())), "varnish.vcl");
                    zip.Add(new StaticBytesDataSource(NeonHelper.ToLinuxLineEndings(NeonHelper.JsonSerialize(cacheSettings, Formatting.Indented))), "cache-settings.json");
                    zip.CommitUpdate();
                }

                zipBytes = ms.ToArray();
            }

            // Compute the MD5 hash for the combined configuration ZIP and the referenced certificates.

            var     hasher = MD5.Create();
            string  combinedHash;

            if (hiveCerts.HasReferences)
            {
                using (var ms = new MemoryStream())
                {
                    ms.Write(hasher.ComputeHash(zipBytes));
                    ms.Write(hiveCerts.HashReferenced());

                    ms.Position  = 0;
                    combinedHash = Convert.ToBase64String(hasher.ComputeHash(ms));
                }
            }
            else
            {
                combinedHash = Convert.ToBase64String(hasher.ComputeHash(zipBytes));
            }

            // Compare the combined hash against what's already published to Consul
            // for the load balancer and update these keys if the hashes differ.

            var publish = false;

            try
            {
                if (!await consul.KV.Exists($"{consulPrefix}/proxies/{loadBalancerName}/proxy-hash", terminator.CancellationToken) || 
                    !await consul.KV.Exists($"{consulPrefix}/proxies/{loadBalancerName}/proxy-conf", terminator.CancellationToken))
                {
                    publish = true; // Nothing published yet.
                }
                else
                {
                    publish = combinedHash != await consul.KV.GetString($"{consulPrefix}/proxies/{loadBalancerName}/proxy-hash", terminator.CancellationToken);
                }

                if (publish)
                {
                    log.LogInfo(() => $"Updating load balancer [{proxyDisplayName}] configuration: [rules={rules.Count}] [hash={combinedHash}]");

                    // Write the hash and configuration out as a transaction so we'll 
                    // be sure they match (don't get out of sync).  We don't need to
                    // do CAS here because only one proxy manager will be running
                    // most of the time and even if multiple instances happened to
                    // update this with different values for some reason, the most 
                    // recent updates would be applied the next time a proxy manager
                    // polled the config and then the instances will remain in sync
                    // until the next routing change is detected.

                    var operations = new List<KVTxnOp>()
                    {
                        new KVTxnOp($"{consulPrefix}/proxies/{loadBalancerName}/proxy-hash", KVTxnVerb.Set) { Value = Encoding.UTF8.GetBytes(combinedHash) },
                        new KVTxnOp($"{consulPrefix}/proxies/{loadBalancerName}/proxy-conf", KVTxnVerb.Set) { Value = zipBytes }
                    };

                    await consul.KV.Txn(operations, terminator.CancellationToken);

                    // Notify the proxy instances that they should reconfigure.

                    await proxyNotifyChannel.PublishAsync(
                        new ProxyUpdateMessage()
                        {
                            Reason       = "proxy update",
                            PublicProxy  = isPublic,
                            PrivateProxy = !isPublic
                        });
                }
                else
                {
                    log.LogInfo(() => $"No changes detected for proxy [{proxyDisplayName}].");
                }
            }
            catch (Exception e)
            {
                // Warn and exit for Consul errors.

                log.LogWarn("Consul request failure.", e);
                return (Rules: rules, log.ToString());
            }

            //-----------------------------------------------------------------
            // Generate the HAProxy bridge configuration.  This configuration is  pretty simple.  
            // All we need to do is forward all endpoints as TCP connections to the proxy we just
            // generated above.  We won't treat HTTP/S specially and we don't need to worry 
            // about TLS termination or generate fancy health checks.
            //
            // The bridge proxy is typically deployed as a standalone Docker container on hive
            // pet nodes and will expose internal hive services on the pets using the same
            // ports where they are deployed on the internal Swarm nodes.  This means that containersl
            // running on pet nodes can consume hive services the same way as they do on the manager
            // and worker nodes.
            //
            // This was initially used as a way to route pet node logging traffic from the
            // [neon-log-host] containers to the [neon-log-collector] service to handle
            // upstream log processing and persistance to the Elasticsearch cluster but
            // the bridges could also be used in the future to access any hive service
            // with a public or private load balancer rule defined.
            //
            // The code below generally assumes that the bridge target proxy is exposed 
            // on all Swarm manager or worker nodes (via the Docker ingress/mesh network 
            // or because the proxy is running in global mode).  Exactly which nodes will
            // be configured to handle forwarded traffic is determined by the proxy
            // settings.
            //
            // The code below starts by examining the list of active Docker Swarm nodes captured
            // before we started generating proxy configs.  If there are 5 (the default) or fewer 
            // worker nodes, the configuration will use all hive nodes (including managers) as 
            // target endpoints.  If there are more than 5 worker nodes, the code will randomly 
            // select 5 of them as endpoints.
            //
            // This approach balances the need for simplicity and reliability in the face of 
            // node failure while trying to avoid an explosion of health check traffic.
            //
            // This may become a problem for hives with a large number of pet nodes
            // banging away with health checks.  One way to mitigate this is to target
            // specific Swarm nodes in the [ProxySettings] by IP address.

            // Determine which hive Swarm nodes will be targeted by the bridge.  The
            // target nodes may have been specified explicitly by IP address in the 
            // proxy settings or we may need to select them here.
            // 
            // Start out by querying Docker for the current Swarm nodes.

            Dictionary<string, DockerNode>  addressToSwarmNode = new Dictionary<string, DockerNode>();

            foreach (var node in swarmNodes)
            {
                addressToSwarmNode.Add(node.Addr, node);
            }

            var bridgeTargets = new List<string>();

            if (settings.BridgeTargetAddresses.Count > 0)
            {
                // Specific Swarm nodes have been targeted.

                foreach (var targetAddress in settings.BridgeTargetAddresses)
                {
                    bridgeTargets.Add(targetAddress.ToString());

                    if (!addressToSwarmNode.ContainsKey(targetAddress.ToString()))
                    {
                        log.LogWarn(() => $"Proxy bridge target [{targetAddress}] does not reference a known hive Swarm node.");
                    }
                }
            }
            else
            {
                // We're going to automatically select the target nodes.
                //
                // We're going to try to favor worker nodes that report being
                // reachable and we're going to sort candidates by the hash of
                // their node names, to try to select a consistent list of target
                // swarm nodes that will be used to relay the traffic bridged
                // from the pets.  This will avoid unnecessary bridge HAProxy
                // configuration changes, as decribed here:
                //
                //      https://github.com/jefflill/NeonForge/issues/349
                //
                // The sort-by-hash is a way to consistently "randomize" the targets
                // without sorting by name which may tend to concentrate traffic to 
                // nodes having the same function or network subnets (depending on the
                // hive node naming conventions).  Sorting by hash will tend to spread 
                // traffic around better.
                //
                // We'll fall back to selecting random swarm nodes if none are 
                // reachable (which should never happen because the [neon-proxy-manager]
                // has to be running somewhere.

                var targetCandidates = new List<DockerNode>();

                swarmNodes = swarmNodes.Where(n => n.State == "ready").ToList();    // We want only READY Swarm nodes.

                var workers = swarmNodes.Where(n => n.Role == "worker").ToList();

                if (workers.Count >= settings.BridgeTargetCount)
                {
                    // There are enough workers to select targets from, so we'll just do that.
                    // The idea here is to try to keep the managers from doing as much routing 
                    // work as possible because they may be busy handling global hive activities,
                    // especially for large hives.

                    foreach (var worker in workers)
                    {
                        targetCandidates.Add(worker);
                    }
                }
                else
                {
                    // Otherwise for small hives, we'll select targets from both
                    // managers and workers.

                    foreach (var node in swarmNodes)
                    {
                        targetCandidates.Add(node);
                    }
                }

                if (targetCandidates.Count == 0)
                {
                    // No swarm nodes appear to be ready (which should never happen because this
                    // program needs to be running somewhere) but we'll select all swarm nodes
                    // as target candidates just to be safe.

                    foreach (var node in swarmNodes)
                    {
                        targetCandidates.Add(node);
                    }
                }

                // Semi-randomly select the swarm target nodes by sorting by the hash of the
                // node name and then taking nodes from the front of the sorted list.

                foreach (var node in targetCandidates.OrderBy(n => NeonHelper.ComputeMD5(n.Hostname)).Take(Math.Min(targetCandidates.Count, settings.BridgeTargetCount)))
                {
                    bridgeTargets.Add(node.Addr);
                }
            }

            if (bridgeTargets.Count == 0)
            {
                log.LogWarn(() => $"No bridge targets were specified or are ready.");
            }

            // Generate the contents of the [haproxy.cfg] file.

            sbHaProxy = new StringBuilder();

            sbHaProxy.Append(
$@"#------------------------------------------------------------------------------
# {proxyBridgeDisplayName} HAProxy configuration file.
#
# Generated by:     {serviceName}
# Documentation:    http://cbonte.github.io/haproxy-dconv/1.7/configuration.html#7.1.4

global
    daemon

# Specify the maximum number of connections allowed for a proxy instance.

    maxconn             {settings.MaxConnections}

# We're going to disable bridge proxy logging for now because I'm not entirely 
# sure that this will be useful.  If we decide to enable this in the future, we
# should probably specify a different SYSLOG facility so we can distinguish 
# between problems with bridges and normal proxies. 

# log                 ""${{NEON_NODE_IP}}:{HiveHostPorts.LogHostSysLog}"" len 65535 {HiveSysLogFacility.ProxyName}

# Certificate Authority and Certificate file locations:

    ca-base             ""${{HAPROXY_CONFIG_FOLDER}}""
    crt-base            ""${{HAPROXY_CONFIG_FOLDER}}""

# Other settings

    tune.ssl.default-dh-param   {settings.MaxDHParamBits}

defaults
    balance             roundrobin
    retries             2
    timeout connect     {ToHaProxyTime(settings.Timeouts.ConnectSeconds)}
    timeout client      {ToHaProxyTime(settings.Timeouts.ClientSeconds)}
    timeout server      {ToHaProxyTime(settings.Timeouts.ServerSeconds)}
    timeout check       {ToHaProxyTime(settings.Timeouts.CheckSeconds)}
");
            // Enable the HAProxy statistics pages.  These will be available on the 
            // [HiverConst.HAProxyStatsPort] port on the [neon-public] or
            // [neon-private] network the proxy serves.
            //
            // HAProxy statistics pages are not intended to be viewed directly by
            // by hive operators.  Instead, the statistics from multiple HAProxy
            // instances will be aggregated by the hive Dashboard.

            sbHaProxy.AppendLine($@"
#------------------------------------------------------------------------------
# Enable HAProxy statistics pages.

frontend haproxy_stats
    bind                *:{HiveConst.HAProxyStatsPort}
    mode                http
    option              http-keep-alive
    use_backend         haproxy_stats

backend haproxy_stats
    mode                http
    stats               enable
    stats               scope .
    stats               uri {HiveConst.HaProxyStatsUri}
    stats               refresh 5s
");
            // Generate the TCP bridge rules.

            sbHaProxy.AppendLine("#------------------------------------------------------------------------------");
            sbHaProxy.AppendLine("# TCP Rules");

            var bridgePorts = new HashSet<int>();

            foreach (LoadBalancerTcpRule tcpRule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Tcp))
            {
                foreach (var frontEnd in tcpRule.Frontends)
                {
                    if (!bridgePorts.Contains(frontEnd.ProxyPort))
                    {
                        bridgePorts.Add(frontEnd.ProxyPort);
                    }
                }
            }

            foreach (LoadBalancerHttpRule httpRule in rules.Values
                .Where(r => r.Mode == LoadBalancerMode.Http))
            {
                foreach (var frontEnd in httpRule.Frontends)
                {
                    if (!bridgePorts.Contains(frontEnd.ProxyPort))
                    {
                        bridgePorts.Add(frontEnd.ProxyPort);
                    }
                }
            }

            foreach (var port in bridgePorts)
            {
                    sbHaProxy.Append(
$@"
listen tcp:port-{port}
    mode                tcp
    bind                *:{port}
");

                // Bridge logging is disabled for now.

                // $todo(jeff.lill):
                //
                // I wonder if it's possible to have HAProxy log ONLY health checks.  It seems like this
                // is the main thing we'd really want to log for bridge proxies.

                //sbHaProxy.AppendLine($"    log                 global");
                //sbHaProxy.AppendLine($"    log-format          {HiveHelper.GetProxyLogFormat("neon-proxy-" + proxyName, tcp: true)}");
                //sbHaProxy.AppendLine($"    option              log-health-checks");

                var checkArg    = " check";
                var initAddrArg = " init-addr last,libc,none";
                var serverIndex = 0;

                foreach (var targetAddress in bridgeTargets)
                {
                    var backendName = $"server-{serverIndex++}";

                    sbHaProxy.AppendLine($"    server              {backendName} {targetAddress}:{port}{checkArg}{initAddrArg}");
                }
            }

            // Generate the [neon-proxy] service compatible configuration ZIP archive for the bridge.
            // Note that the bridge forwards only TCP traffic so there are no TLS certificates.

            using (var ms = new MemoryStream())
            {
                using (var zip = ZipFile.Create(ms))
                {
                    // We need all archive entries to have fixed dates so we'll be able
                    // to compare configuration archives for changes using MD5 hashes.

                    zip.EntryFactory = new ZipEntryFactory(new DateTime(2000, 1, 1));

                    // NOTE: We're converting text to Linux style line endings.

                    zip.BeginUpdate();
                    zip.Add(new StaticBytesDataSource(NeonHelper.ToLinuxLineEndings(sbHaProxy.ToString())), "haproxy.cfg");
                    zip.CommitUpdate();
                }

                zipBytes = ms.ToArray();
            }

            // Compute the MD5 hash for the configuration ZIP.

            hasher       = MD5.Create();
            combinedHash = Convert.ToBase64String(hasher.ComputeHash(zipBytes));

            // Compare the hash against what's already published to Consul
            // for the proxy and update these keys if the hashes differ.

            publish = false;

            try
            {
                if (!await consul.KV.Exists($"{consulPrefix}/proxies/{proxyBridgeName}/proxy-hash", terminator.CancellationToken) ||
                    !await consul.KV.Exists($"{consulPrefix}/proxies/{proxyBridgeName}/proxy-conf", terminator.CancellationToken))
                {
                    publish = true; // Nothing published yet.
                }
                else
                {
                    publish = combinedHash != await consul.KV.GetString($"{consulPrefix}/proxies/{proxyBridgeName}/proxy-hash", terminator.CancellationToken);
                }

                if (publish)
                {
                    log.LogInfo(() => $"Updating proxy [{proxyBridgeDisplayName}] configuration: [rules={bridgePorts.Count}] [hash={combinedHash}]");

                    // Write the hash and configuration out as a transaction so we'll 
                    // be sure they match (don't get out of sync).  We don't need to
                    // do CAS here because only one proxy manager will be running
                    // most of the time and even if multiple instances happened to
                    // update this with different values for some reason, the most 
                    // recent updates would be applied the next time a proxy manager
                    // polled the config and then the instances will remain in sync
                    // until the next routing change is detected.

                    var operations = new List<KVTxnOp>()
                    {
                        new KVTxnOp($"{consulPrefix}/proxies/{proxyBridgeName}/proxy-hash", KVTxnVerb.Set) { Value = Encoding.UTF8.GetBytes(combinedHash) },
                        new KVTxnOp($"{consulPrefix}/proxies/{proxyBridgeName}/proxy-conf", KVTxnVerb.Set) { Value = zipBytes }
                    };

                    await consul.KV.Txn(operations, terminator.CancellationToken);

                    // Notify the proxy-bridge instances that they should reconfigure.

                    await proxyNotifyChannel.PublishAsync(
                        new ProxyUpdateMessage()
                        {
                            Reason        = "bridge update",
                            PublicBridge  = isPublic,
                            PrivateBridge = !isPublic
                        });
                }
                else
                {
                    log.LogInfo(() => $"No changes detected for proxy [{proxyBridgeDisplayName}].");
                }
            }
            catch (Exception e)
            {
                // Warn and exit for Consul/Docker errors.

                log.LogWarn("Consul or Docker request failure.", e);
                return (Rules: rules, log.ToString());
            }
            
            //-----------------------------------------------------------------
            // We're done

            return (Rules: rules, Status: log.ToString());
        }

        /// <summary>
        /// Updates the hive's public load balancer and network security rules so they
        /// are consistent with the public load balancer rules passed.
        /// </summary>
        /// <param name="publicRules">The public proxy rules.</param>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private static async Task UpdateHiveNetwork(Dictionary<string, LoadBalancerRule> publicRules)
        {
            try
            {
                // Clone the cached hive definition and add the hosting options
                // acquired from Vault to create a hive proxy.

                var clonedHiveDefinition = NeonHelper.JsonClone<HiveDefinition>(hiveDefinition);

                clonedHiveDefinition.Hosting = await vault.ReadJsonAsync<HostingOptions>("neon-secret/hosting/options", cancellationToken: terminator.CancellationToken);

                var hive = new HiveProxy(clonedHiveDefinition);

                // Retrieve the current public load balancer rules and then compare
                // these with the public rules defined for the hive to determine
                // whether we need to update the load balancer and network security
                // rules.

                // $note(jeff.lill):
                //
                // It's possible for the load balancer and network security rules
                // to be out of sync if the operator modifies the security rules
                // manually (e.g. via the cloud portal).  This code won't detect
                // this situation and the security rules won't be brought back
                // into sync until the public rules changes enough to actually 
                // change the external load balanced port set.
                //
                // This could be considered a feature.  For example, this allows
                // the operator temporarily block a port manually.

                var hostingManager = new HostingManagerFactory().GetManager(hive);

                if (hostingManager == null)
                {
                    Console.Error.WriteLine($"*** ERROR: No hosting manager for the [{hive.Definition.Hosting.Environment}] hosting environment could be located.");
                    Program.Exit(1, immediate: true);
                }

                if (!hostingManager.CanUpdatePublicEndpoints)
                {
                    return; // Operators need to maintain the load balancer manually for this environment.
                }

                var publicEndpoints = hostingManager.GetPublicEndpoints();
                var latestEndpoints = new List<HostedEndpoint>();

                // Build a dictionary mapping the load balancer frontend ports to 
                // internal HAProxy frontend ports for the latest rules.

                foreach (LoadBalancerTcpRule rule in publicRules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Tcp))
                {
                    foreach (var frontend in rule.Frontends)
                    {
                        if (frontend.PublicPort > 0)
                        {
                            latestEndpoints.Add(new HostedEndpoint(HostedEndpointProtocol.Tcp, frontend.PublicPort, frontend.ProxyPort));
                        }
                    }
                }

                foreach (LoadBalancerHttpRule rule in publicRules.Values
                    .Where(r => r.Mode == LoadBalancerMode.Http))
                {
                    foreach (var frontend in rule.Frontends)
                    {
                        if (frontend.PublicPort > 0)
                        {
                            latestEndpoints.Add(new HostedEndpoint(HostedEndpointProtocol.Tcp, frontend.PublicPort, frontend.ProxyPort));
                        }
                    }
                }

                // Determine if the latest rule port mappings differ from the current
                // hive load balancer rules.

                var changed = false;

                if (latestEndpoints.Count != publicEndpoints.Count)
                {
                    changed = true;
                }
                else
                {
                    // We're going to compare the endpoints in sorted order (by public load balancer port).

                    publicEndpoints = publicEndpoints.OrderBy(ep => ep.FrontendPort).ToList();
                    latestEndpoints = latestEndpoints.OrderBy(ep => ep.FrontendPort).ToList();

                    for (int i = 0; i < publicEndpoints.Count; i++)
                    {
                        if (publicEndpoints[i].FrontendPort != latestEndpoints[i].FrontendPort ||
                            publicEndpoints[i].BackendPort != latestEndpoints[i].BackendPort)
                        {
                            changed = true;
                            break;
                        }
                    }
                }

                if (!changed)
                {
                    log.LogInfo(() => $"Public hive load balancer configuration matches current rules. [endpoint-count={publicEndpoints.Count}]");
                    return;
                }

                // The endpoints have changed so update the hive.

                log.LogInfo(() => $"Updating: public hive load balancer and security. [endpoint-count={publicEndpoints.Count}]");
                hostingManager.UpdatePublicEndpoints(latestEndpoints);
                log.LogInfo(() => $"Update Completed: public hive load balancer and security. [endpoint-count={publicEndpoints.Count}]");
            }
            catch (Exception e)
            {
                log.LogError($"Unable to update hive load balancer and/or network security configuration.", e);
            }
        }

        /// <summary>
        /// Converts a <c>double</c> number of seconds into the corresponding HAProxy time representation
        /// consisting of an integer followed by a unit.  See <a href="http://cbonte.github.io/haproxy-dconv/1.8/configuration.html#2.4">HAProxy Time Format</a>
        /// </summary>
        /// <param name="seconds">The seconds.</param>
        /// <returns>The formatted time value.</returns>
        private static string ToHaProxyTime(double seconds)
        {
            if (seconds < 0.0)
            {
                seconds = 0.0;
            }

            if (seconds == Math.Truncate(seconds))
            {
                // There's no fractional part.

                return $"{seconds}s";
            }

            // There's a fractional part so we're going to render this as
            // milliseconds, rounding up to the nearest millisecond.

            var milliseconds = seconds * 1000.0;

            if (milliseconds != Math.Truncate(milliseconds))
            {
                milliseconds = Math.Truncate(milliseconds) + 1;
            }

            return $"{milliseconds}ms";
        }

        /// <summary>
        /// Rounds a <c>double</c> up to the next highest integer value.
        /// </summary>
        /// <param name="value">The input.</param>
        /// <returns>The rounded value.</returns>
        private static double RoundUp(double value)
        {
            var truncated = Math.Truncate(value);

            if (value == truncated)
            {
                return value;
            }
            else
            {
                return truncated + 1.0;
            }
        }

        /// <summary>
        /// Records a load balancer rule to a log rercorder to be part of the proxy status.
        /// </summary>
        /// <param name="log">The log recorder.</param>
        /// <param name="rule">The rule.</param>
        private static void RecordRule(LogRecorder log, LoadBalancerRule rule)
        {
            //  We're going to write a line with then rule name and then output
            // the rule it self as YAML, indented by 2 characters for readability.

            log.Record($"{rule.Name}:");

            using (var reader = new StringReader(rule.ToYaml()))
            {
                foreach (var line in reader.Lines())
                {
                    log.Record($"    {line}");
                }
            }
        }

        /// <summary>
        /// Appends an <b>http-request set-header X-Neon-Frontend VALUE [if acl]</b> to the <see cref="StringBuilder"/>
        /// for an HTTP frontend when the associated rule enables caching.
        /// </summary>
        /// <param name="sb">The target string builder.</param>
        /// <param name="haProxyFrontend">The HTTP frontend.</param>
        /// <param name="pathPrefix">The frontend path prefix.</param>
        /// <param name="hostPathMapping">The specific host/path mapping.</param>
        /// <param name="hostname">The target proxy frontend hostname or <c>null</c>.</param>
        /// <param name="aclNames">
        /// The optional names of the ACLs that used to select the backend.
        /// The header will be set unconditionally when this is empty.
        /// </param>
        private static void SetCacheFrontendHeader(StringBuilder sb, HAProxyHttpFrontend haProxyFrontend, string pathPrefix, HostPathMapping hostPathMapping, string hostname, params string[] aclNames)
        {
            Covenant.Requires<ArgumentNullException>(sb != null);
            Covenant.Requires<ArgumentNullException>(haProxyFrontend != null);
            Covenant.Requires<ArgumentNullException>(hostPathMapping != null);
            Covenant.Requires<ArgumentException>(hostPathMapping.Rule.Mode == LoadBalancerMode.Http);

            string proxyFrontendHeader;

            if (string.IsNullOrEmpty(pathPrefix))
            {
                pathPrefix = "/";
            }
            else if (!pathPrefix.EndsWith("/"))
            {
                pathPrefix += "/";
            }

            if (!string.IsNullOrEmpty(hostname))
            {
                proxyFrontendHeader = $"{haProxyFrontend.Port} {pathPrefix} {hostname}";
            }
            else
            {
                proxyFrontendHeader = $"{haProxyFrontend.Port} {pathPrefix}";
            }

            if (!hostPathMapping.Rule.Cache.Enabled)
            {
                return;
            }

            // IMPLEMENTATION NOTE:
            // --------------------
            //
            // HAProxy doesn't work as I expected.  I thought that HAProxy would walk down a frontend's
            // rules until it hit a [use-backend] and then stop processing frontend rules and start
            // processing the backend rules.
            //
            // What really happens is that HAProxy will continue processing frontend rules after the
            // first [use_backend] whose conditions are satisfied but it will ignore any subsequent
            // [use-backend] rules.  Only after processing all of the frontend rules will it jump
            // to process the backend rules.
            //
            // This makes this code generation a bit more challenging.  The issue centers around
            // the generation of the [http-request set-header X-Neon-Frontend ...] rules.  We need
            // to have only the first prefix match actually set this header and for subsequent
            // prefix matches to be ignored.  If we can't express this then subsequent matching
            // rules will set the wrong header.
            //
            // For example, say we have frontends on the same port for the "/foo/bar/" and "/foo/"
            // prefixes.  We generate rules such that the longest prefix will match first, so
            // we want a request for "/foo/bar/test.jpg" to match the "/foo/bar/" prefix, set the
            // header and be directed to the corresponding backend:
            //
            // frontend: test
            //      mode                http
            //      bind                *:80
            //
            //      acl                 is-test-com hdr_reg(host) -i test.com(:\d+)?
            //      acl                 is-foo-bar path_beg '/foo/bar/'
            //      http-request        set-header X-Neon-Frontend '80 /foo/bar/ vegomatic.test' if is-test-com is-foo-bar
            //      use_backend         foo-bar if is-test-com is-foo-bar
            //
            //      acl                 is-test-com hdr_reg(host) -i test.com(:\d+)?
            //      acl                 is-foo path_beg '/foo/'
            //      http-request        set-header X-Neon-Frontend '80 /foo/ vegomatic.test' if is-test-com is-foo
            //      use_backend         foo-bar if is-test-com is-foo
            //
            // I originally thought that for a http://test.com/foo/bar/test.jpg request that HAProxy
            // would match the first two ACLs, execute the first [http-request set-header], and the
            // first [use_backend] and then stop processing frontend rules.  HAProxy does issue a
            // warning about this behavior when it it's loading the configuration, but we won't
            // see that in the logs unless there's an actual error in the configuration.
            //
            // Instead, it remembers the first backend but continues processing the next two ACLs
            // which also match, so the second [http-request set-header] will also be executed,
            // setting the wrong header.
            //
            // I'm going to handle this by setting the [x-neon-frontend-found] ACL which will indicate
            // whether the the [X-Neon-Frontend] header has already been added to the request and
            // use this to ensure that only the first prefix match actually sets the header. 
            //
            // This will look something like:
            //
            // frontend: test
            //      mode                http
            //      bind                *:80
            //
            //      acl                 is-test-com hdr_reg(host) -i test.com(:\d+)?
            //      acl                 is-foo-bar path_beg '/foo/bar/'
            //      acl                 x-neon-frontend-found req.hdr(X-Neon-Frontend) -m found
            //      http-request        set-header X-Neon-Frontend '80 /foo/bar/ vegomatic.test' if is-test-com is-foo-bar !x-neon-frontend-found-0
            //      use_backend         foo-bar if is-test-com is-foo-bar
            //
            //      acl                 is-test-comhdr_reg(host) -i test.com(:\d+)?
            //      acl                 is-foo path_beg '/foo/'
            //      acl                 x-neon-frontend-found req.hdr(X-Neon-Frontend) -m found
            //      http-request        set-header X-Neon-Frontend '80 /foo/ vegomatic.test' if is-test-com is-foo !x-neon-frontend-found-1
            //      use_backend         foo-bar if is-test-com is-foo

            sb.AppendLine($"    acl                 x-neon-frontend-found req.hdr(X-Neon-Frontend) -m found");

            if (aclNames.Length == 0)
            {
                sb.AppendLine($"    http-request        set-header X-Neon-Frontend '{proxyFrontendHeader}' if !x-neon-frontend-found");
            }
            else
            {
                var conditions = string.Empty;

                foreach (var aclName in aclNames.Union(new string[] { $"!x-neon-frontend-found" }))
                {
                    if (conditions.Length > 0)
                    {
                        conditions += " ";
                    }

                    conditions += aclName;
                }

                sb.AppendLine($"    http-request        set-header X-Neon-Frontend '{proxyFrontendHeader}' if {conditions}");
            }
        }
    }
}
