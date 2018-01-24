﻿//-----------------------------------------------------------------------------
// FILE:	    NetworkOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.Net;

namespace Neon.Cluster
{
    /// <summary>
    /// Describes the network options for a neonCLUSTER.
    /// </summary>
    /// <remarks>
    /// <para>
    /// neonCLUSTERs can be deployed in two basic environments, cloud or on-premise.  Cloud providers include
    /// <see cref="HostingEnvironments.Aws"/>, <see cref="HostingEnvironments.Azure"/>, and <see cref="HostingEnvironments.Google"/>
    /// and on-premise providers include <see cref="HostingEnvironments.HyperV"/>, <see cref="HostingEnvironments.Machine"/> and
    /// <see cref="HostingEnvironments.XenServer"/>.  Cluster network options are interpreted somewhat differently
    /// depending on whether the the cluster is being provisioned to the cloud or to on-premise hardware.
    /// </para>
    /// <para>
    /// Both cloud and on-premise clusters are provisioned with two standard overlay networks: <b>neon-public</b> and <b>neon-private</b>.
    /// These networks are used to as the service backend networks for the <b>neon-proxy-public</b> and <b>neon-proxy-private</b> TCP/HTTP
    /// network proxies used to forward external in internal traffic from Docker ingress/mesh networks to services.  Both of these
    /// networks are assigned reasonable default subnets for standalone clusters, but you'll need to take care to avoid conflicts
    /// when deploying more than one cluster on a network.
    /// </para>
    /// <para>
    /// <see cref="PublicSubnet"/> is configured by default on the <b>10.249.0.0/16</b> subnet and is intended to
    /// host public facing service endpoints to be served by the <b>neon-proxy-public</b> proxy service.
    /// </para>
    /// <para>
    /// <see cref="PrivateSubnet"/> is configured by default on the <b>10.248.0.0/16</b> subnet and is intended to
    /// host internal service endpoints to be served by the <b>neon-proxy-private</b> proxy service.
    /// </para>
    /// <para><b>On-Premise Network Configuration</b></para>
    /// <para>
    /// When deploying to an on-premise environment, you'll need to define up to four cluster subnets:
    /// </para>
    /// <list type="table">
    /// <item>
    ///     <term><see cref="NodesSubnet"/></term>
    ///     <description>
    ///     This subnet describes where the neonCLUSTER Docker host node IP addresses will be allocated.  This may
    ///     be any valid subnet for on-premise deployments but will typically a <b>/24</b> or larger.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="PremiseSubnet"/></term>
    ///     <description>
    ///     This specifies the subnet for entire host network for on-premise environments like
    ///     <see cref="HostingEnvironments.Machine"/>, <see cref="HostingEnvironments.HyperV"/> and
    ///     <see cref="HostingEnvironments.XenServer"/>.  This is required for those environments.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="PublicSubnet"/></term>
    ///     <description>
    ///     <para>
    ///     This defaults to <b>10.249.0.0/16</b> as described above to allow roughly 64K 
    ///     addresses to be assigned to Docker services on the public network.
    ///     </para>
    ///     <note>
    ///     This subnet is internal to the neonCLUSTER so you don't need to worry about conflicting
    ///     with other clusters or network services.
    ///     </note>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="PrivateSubnet"/></term>
    ///     <description>
    ///     <para>
    ///     This defaults to <b>10.248.0.0/16</b> as described above to allow roughly 64K 
    ///     addresses to be assigned to Docker services on the private network.
    ///     </para>
    ///     <note>
    ///     This subnet is internal to the neonCLUSTER so you don't need to worry about conflicting
    ///     with other clusters or network services.
    ///     </note>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="VpnPoolSubnet"/></term>
    ///     <description>
    ///     This subnet is required if the cluster is deployed with an integrated VPN.
    ///     This subnet must be a <b>/22</b> at this time and defaults to <b>10.169.0.0/22</b>.
    ///     </description>
    /// </item>
    /// </list>
    /// <para>
    /// In general, we recommend that you allocate two <b>/22</b> subnets for each on-premise
    /// cluster.  One <b>/22</b> for <see cref="NodesSubnet"/> which allows for up to 1024 
    /// host nodes and the second <b>/22</b> for the <see cref="VpnPoolSubnet"/>.  This results
    /// in each cluster being allocated a <b>/21</b> overall.
    /// </para>
    /// <para><b>Cloud Network Configuration</b></para>
    /// <para>
    /// Cloud deployment options are a bit simpler: only three subnets must be specified:
    /// </para>
    /// <list type="table">
    /// <item>
    ///     <term><see cref="CloudSubnet"/></term>
    ///     <description>
    ///     <para>
    ///     This defaults to <b>10.168.0.0/21</b> and specifies where all cloud NIC and VPN client
    ///     addresses will be provisioned.  <see cref="CloudSubnet"/> will be automatically
    ///     split into <see cref="NodesSubnet"/> and <see cref="VpnPoolSubnet"/> and other internal
    ///     subnets when the neonCLUSTER is provisioned. 
    ///     </para>
    ///     <para>
    ///     This must be a <b>/21</b> subnet.
    ///     </para>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="PublicSubnet"/></term>
    ///     <description>
    ///     <para>
    ///     This defaults to <b>10.249.0.0/16</b> as described above to allow roughly 64K 
    ///     addresses to be assigned to Docker services on the public network.
    ///     </para>
    ///     <note>
    ///     This subnet is internal to the neonCLUSTER so you don't need to worry about conflicting
    ///     with other clusters or network services.
    ///     </note>
    ///     </description>
    /// </item>
    /// <item>
    ///     <term><see cref="PrivateSubnet"/></term>
    ///     <description>
    ///     <para>
    ///     This defaults to <b>10.248.0.0/16</b> as described above to allow roughly 64K 
    ///     addresses to be assigned to Docker services on the private network.
    ///     </para>
    ///     <note>
    ///     This subnet is internal to the neonCLUSTER so you don't need to worry about conflicting
    ///     with other clusters or network services.
    ///     </note>
    ///     </description>
    /// </item>
    /// </list>
    /// </remarks>
    public class NetworkOptions
    {
        private const string defaultPublicSubnet  = "10.249.0.0/16";
        private const string defaultPrivateSubnet = "10.248.0.0/16";
        private const string defaultCloudSubnet   = "10.168.0.0/21";
        private const string defaultVpnPoolSubnet = "10.169.0.0/22";

        // WARNING: [pdns-server] and the [pdns-remote-backend] packages must come from the same build.

        private const string defaultPdnsServerPackageUri          = "https://jefflill.github.io/neoncluster/binaries/ubuntu/pdns-server_4.1.0~rc1-1pdns.xenial_amd64.deb";
        private const string defaultPdnsBackendRemotePackageUri   = "https://jefflill.github.io/neoncluster/binaries/ubuntu/pdns-backend-remote_4.1.0~rc1-1pdns.xenial_amd64.deb";
        private const string defaultPdnsRecursorPackagePackageUri = "https://jefflill.github.io/neoncluster/binaries/ubuntu/pdns-recursor_4.1.0~alpha1-1pdns.xenial_amd64.deb";

        /// <summary>
        /// Default constructor.
        /// </summary>
        public NetworkOptions()
        {
        }

        /// <summary>
        /// The subnet to be assigned to the built-in <b>neon-public</b> overlay network.  This defaults to <b>10.249.0.0/16</b>.
        /// </summary>
        /// <remarks>
        /// <note>
        /// You must take care that this subnet does not conflict with any other subnets for this
        /// cluster or any other clusters that may be deployed to the same network.
        /// </note>
        /// </remarks>
        [JsonProperty(PropertyName = "PublicSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultPublicSubnet)]
        public string PublicSubnet { get; set; } = defaultPublicSubnet;

        /// <summary>
        /// Allow non-Docker swarm mode service containers to attach to the built-in <b>neon-public</b> cluster 
        /// overlay network.  This defaults to <b>true</b> for flexibility but you may consider disabling this for
        /// better security.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The advantage of enabling is is that any container will be able to connect to the default network
        /// and access swarm mode services.  The downside is that this makes it possible for a bad guy who
        /// gains root access to a single node could potentially deploy a malicious container that could also
        /// join the network.  With this disabled, the bad guy would need to gain access to one of the manager
        /// nodes to deploy a malicious service.
        /// </para>
        /// <para>
        /// Unfortunately, it's not currently possible to change this setting after a cluster is deployed.
        /// </para>
        /// </remarks>
        [JsonProperty(PropertyName = "PublicAttachable", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(true)]
        public bool PublicAttachable { get; set; } = true;

        /// <summary>
        /// The subnet to be assigned to the built-in <b>neon-public</b> overlay network.  This defaults to <b>10.248.0.0/16</b>.
        /// </summary>
        /// <remarks>
        /// <note>
        /// You must take care that this subnet does not conflict with any other subnets for this
        /// cluster or any other clusters that may be deployed to the same network.
        /// </note>
        /// </remarks>
        [JsonProperty(PropertyName = "PrivateSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultPrivateSubnet)]
        public string PrivateSubnet { get; set; } = defaultPrivateSubnet;

        /// <summary>
        /// Allow non-Docker swarm mode service containers to attach to the built-in <b>neon-private</b> cluster 
        /// overlay network.  This defaults to <b>true</b> for flexibility but you may consider disabling this for
        /// better security.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The advantage of enabling is is that any container will be able to connect to the default network
        /// and access swarm mode services.  The downside is that this makes it possible for a bad guy who
        /// gains root access to a single node could potentially deploy a malicious container that could also
        /// join the network.  With this disabled, the bad guy would need to gain access to one of the manager
        /// nodes to deploy a malicious service.
        /// </para>
        /// <para>
        /// Unfortunately, it's not currently possible to change this setting after a cluster is deployed.
        /// </para>
        /// </remarks>
        [JsonProperty(PropertyName = "PrivateAttachable", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(true)]
        public bool PrivateAttachable { get; set; } = true;

        /// <summary>
        /// The IP addresses of the upstream DNS nameservers to be used by the cluster.  This defaults to the 
        /// Google Public DNS servers: <b>[ "8.8.8.8", "8.8.4.4" ]</b> when the property is <c>null</c> or empty.
        /// </summary>
        /// <remarks>
        /// <para>
        /// neonCLUSTERs configure the Consul servers running on the manager nodes to handle the DNS requests
        /// from the cluster host nodes and containers by default.  This enables the registration of services
        /// with Consul that will be resolved to specific IP addresses.  This is used by the <b>proxy-manager</b>
        /// to support stateful services deployed as multiple containers and may also be used in other future
        /// scenarios.
        /// </para>
        /// <para>
        /// neonCLUSTER Consul DNS servers answer requests for names with the <b>cluster</b> top-level domain.
        /// Other requests will be handled recursively by forwarding the request to one of the IP addresses
        /// specified here.
        /// </para>
        /// </remarks>
        [JsonProperty(PropertyName = "Nameservers", Required = Required.Default)]
        [DefaultValue(null)]
        public string[] Nameservers { get; set; } = null;

        /// <summary>
        /// <para>
        /// URI for the <a href="https://www.powerdns.com/auth.html">PowerDNS Authoritative Server</a> package 
        /// to use for provisioning cluster dynbamic DNS services on the cluster mnanagers.  This defaults to 
        /// a known good release.
        /// </para>
        /// <note>
        /// <see cref="PdnsServerPackageUri"/> and <see cref="PdnsBackendRemotePackageUri"/> must specify packages
        /// from the same PowerDNS build.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "PdnsServerPackageUri", Required = Required.Default)]
        [DefaultValue(defaultPdnsServerPackageUri)]
        public string PdnsServerPackageUri { get; set; } = defaultPdnsServerPackageUri;

        /// <summary>
        /// <para>
        /// URI for the <a href="https://www.powerdns.com/auth.html">PowerDNS Authoritative Server Remote Backend</a>
        /// package to use for provisioning cluster dynamic DNS services on the cluster managers.  This defaults to 
        /// a known good release.
        /// </para>
        /// <note>
        /// <see cref="PdnsServerPackageUri"/> and <see cref="PdnsBackendRemotePackageUri"/> must specify packages
        /// from the same PowerDNS build.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "PdnsBackendRemotePackageUri", Required = Required.Default)]
        [DefaultValue(defaultPdnsBackendRemotePackageUri)]
        public string PdnsBackendRemotePackageUri { get; set; } = defaultPdnsBackendRemotePackageUri;

        /// <summary>
        /// URI for the <a href="https://www.powerdns.com/recursor.html">PowerDNS Recursor</a> package 
        /// to use for provisioning cluster DNS services.  This defaults to a known good release.
        /// </summary>
        [JsonProperty(PropertyName = "PdnsRecursorPackageUri", Required = Required.Default)]
        [DefaultValue(defaultPdnsRecursorPackagePackageUri)]
        public string PdnsRecursorPackageUri { get; set; } = defaultPdnsRecursorPackagePackageUri;

        /// <summary>
        /// Enables the deployment of the PowerDNS Authoritative server to the cluster manager nodes
        /// along with the <b>neon-dns</b> and <b>neon-dns-health</b> services to provide dynamic
        /// DNS capabilities to the cluster.  Worker nodes will be configured to use the managers
        /// as their upstream DNS servers.  This defaults to <c>true</c>.
        /// </summary>
        [JsonProperty(PropertyName = "DynamicDns", Required = Required.Default)]
        [DefaultValue(true)]
        public bool DynamicDns { get; set; } = true;

        /// <summary>
        /// The cluster's manager load balancer's FQDN or IP address during cluster
        /// provisioning or <c>null</c> if the cluster does not have load balancers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For cloud deployments, the <b>neon-cli</b> will set this to IP address or
        /// FQDN of the public cluster load balancer responsible for forwarding traffic
        /// to the manager nodes.
        /// </para>
        /// <para>
        /// This needs to be explicitly defined for on-premise clusters that also
        /// deploy VPN servers.  In this case, you'll need to specify the public IP
        /// address or FQDN of your cluster router that has forwarding rules for
        /// the inbound VPN traffic.
        /// </para>
        /// </remarks>
        [JsonProperty(PropertyName = "ManagerRouter", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Include)]
        [DefaultValue(null)]
        public string ManagerRouter { get; set; }

        /// <summary>
        /// The cluster's worker load balancer's FQDN or IP address during cluster
        /// provisioning or <c>null</c> if the cluster does not have load balancers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For cloud deployments, the <b>neon-cli</b> will set this to IP address or
        /// FQDN of the public cluster load balancer responsible for forwarding traffic
        /// to the worker nodes.
        /// </para>
        /// <para>
        /// This is not required for on-premise deployments.
        /// </para>
        /// </remarks>
        [JsonProperty(PropertyName = "WorkerRouterAddress", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Include)]
        [DefaultValue(null)]
        public string WorkerRouterAddress { get; set; }

        /// <summary>
        /// <para>
        /// Specifies the overall cluster address space (CIDR) for cloud environments.  This must be a <b>/21</b>
        /// subnet with 2048 IP addresses for deployment to cloud providers.  This defaults to 
        /// <b>10.168.0.0/21</b>.
        /// </para>
        /// <note>
        /// This property is ignored for the <see cref="HostingEnvironments.Machine"/> provider.
        /// </note>
        /// </summary>
        /// <remarks>
        /// <note>
        /// IMPORTANT: You should take care to ensure that this subnet does not conflict
        /// with subnets assigned to your company or home networks and if you ever intend
        /// to deploy multiple clusters and link them via VPNs, you must ensure that
        /// each cluster is assigned a unique address space.
        /// </note>
        /// <para>
        /// The <b>neon-cli</b> divides this address space into four subnets when deploying 
        /// a cluster to a cloud platform such as AWS or Azure.  The table below
        /// describes this using the the default address space <b>10.168.0.0/21</b>:
        /// </para>
        /// <list type="list">
        /// <item>
        ///     <term><b>10.168.0.0/22</b></term>
        ///     <description>
        ///     <see cref="CloudVNetSubnet"/>: The cloud address space is split in half and the 
        ///     first half will be used as the overall address space for the virtual network to
        ///     be created for the cluster.  The VNET address space will be split into to equal
        ///     sized subnets to be assigned to manager node NICs as well as NICs for all nodes.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>10.168.0.0/23</b></term>
        ///     <description>
        ///     <see cref="NodesSubnet"/>: The first half of <see cref="CloudVNetSubnet"/> will
        ///     be assigned to the NICs for all nodes.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>10.168.2.0/23</b></term>
        ///     <description>
        ///     The second half of <see cref="CloudVNetSubnet"/> will be assigned to the NICs 
        ///     for manager nodes.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>10.168.4.0/22</b></term>
        ///     <description>
        ///     <see cref="VpnPoolSubnet"/>: The second half of the cloud address space is 
        ///     reserved for the OpenVPN tunnels with the OpenVPN tunnel on each cluster manager
        ///     being assigned a <b>/25</b> subnet from this address space.
        ///     </description>
        /// </item>
        /// </list>
        /// <para>
        /// The default value <b>10.168.0.0/21</b> is a reasonable choice
        /// in many situations, but you may need to customize this to
        /// avoid subnet conflicts.
        /// </para>
        /// <para>
        /// You may specify subnets in any of the private Internet address spaces:
        /// </para>
        /// <list type="bullet">
        /// <item><b>10.0.0.0/8</b></item>
        /// <item><b>172.16.0.0/12</b></item>
        /// <item><b>192.168.0.0/16</b></item>
        /// </list>
        /// </remarks>
        [JsonProperty(PropertyName = "CloudSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultCloudSubnet)]
        public string CloudSubnet { get; set; } = defaultCloudSubnet;

        /// <summary>
        /// <para>
        /// The <b>/22</b> address space to be assigned to the cluster's cloud virtual network.
        /// </para>
        /// <note>
        /// This property is ignored for the <see cref="HostingEnvironments.Machine"/> provider and is
        /// computed automatically by the <b>neon-cli</b> when provisioning in a cloud environment.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "CloudVNetSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string CloudVNetSubnet { get; set; }

        /// <summary>
        /// <para>
        /// The cloud VPN server <b>/23</b> subnet.  Cluster managers will have a NIC attached to this subnet 
        /// in addition to one attached to <see cref="NodesSubnet"/>.  These will be reponsible for forwarding
        /// return traffic from the nodes in the <see cref="NodesSubnet"/> back to the VPN clients.
        /// </para>
        /// <note>
        /// This property is ignored for the <see cref="HostingEnvironments.Machine"/> provider and is
        /// computed automatically by the <b>neon-cli</b> when provisioning in a cloud environment.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "CloudVpnSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string CloudVpnSubnet { get; set; }

        /// <summary>
        /// Specifies the subnet for entire host network for on-premise environments like
        /// <see cref="HostingEnvironments.Machine"/>, <see cref="HostingEnvironments.HyperV"/> and
        /// <see cref="HostingEnvironments.XenServer"/>.  This is required for those environments.
        /// </summary>
        [JsonProperty(PropertyName = "PremiseSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string PremiseSubnet { get; set; }

        /// <summary>
        /// <para>
        /// The subnet where the cluster nodes reside.
        /// </para>
        /// <note>
        /// This property must be configured for the on-premise providers (<see cref="HostingEnvironments.Machine"/>, 
        /// <see cref="HyperV"/>, and <see cref="XenServer"/>) and is computed automatically by the <b>neon</b> tool when
        /// provisioning in a cloud environment.
        /// </note>
        /// <note>
        /// For on-premise clusters, the statically assigned IP addresses assigned 
        /// to the nodes must reside within the this subnet.  The network gateway
        /// will be assumed to be the second address in this subnet and the broadcast
        /// address will assumed to be the last address.
        /// </note>
        /// <note>
        /// For clusters hosted by cloud providers, the <b>neon-cli</b> will split this
        /// into three subnets: <see cref="NodesSubnet"/>, <see cref="CloudVpnSubnet"/> and 
        /// <see cref="VpnPoolSubnet"/> and will automatically assign IP addresses to the 
        /// virtual machines.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "NodesSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string NodesSubnet { get; set; }

        /// <summary>
        /// <para>
        /// The cluster VPN client return <b>/22</b> subnet.  This where OpenVPN servers will 
        /// run and also acts as the pool of addresses that will be assigned to connecting VPN clients.
        /// This will be further split into <b>/25</b> subnets assigned to each manager/OpenVPN
        /// server.
        /// </para>
        /// <note>
        /// This property must be specifically initialized for on-premise hosting providers and is 
        /// computed automatically by the <b>neon-cli</b> when provisioning in a cloud environment.
        /// </note>
        /// <note>
        /// For on-premise clusters this will default to <b>10.169.0.0/22</b> which will work
        /// for many clusters.  You may need to adjust this to avoid conflicts with your
        /// local network or if you intend to deploy multiple clusters on the same network.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "VpnPoolSubnet", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultVpnPoolSubnet)]
        public string VpnPoolSubnet { get; set; } = defaultVpnPoolSubnet;

        /// <summary>
        /// Indicates that host IP addresses are to be configured explicitly as static values.
        /// This defaults to <c>true</c>.  This is ignored for cloud hosting environments.
        /// </summary>
        [JsonProperty(PropertyName = "StaticIP", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(true)]
        public bool StaticIP { get; set; } = true;

        /// <summary>
        /// Specifies the default network gateway address to be configured for hosts when <see cref="StaticIP"/> is set to <c>true</c>.
        /// This defaults to the first usable address in the <see cref="PremiseSubnet"/>.  For example, for the
        /// <b>10.0.0.0/24</b> subnet, this will be set to <b>10.0.0.1</b>.  This is ignored for cloud hosting 
        /// environments.
        /// </summary>
        [JsonProperty(PropertyName = "Gateway", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string Gateway { get; set; } = null;

        /// <summary>
        /// Specifies the default network broadcast address to be configured for hosts when <see cref="StaticIP"/> is set to <c>true</c>.
        /// This defaults to the last address in the <see cref="PremiseSubnet"/>.  For example, for the
        /// <b>10.0.0.0/24</b> subnet, this will be set to <b>10.0.0.255</b>.  This is ignored for cloud hosting 
        /// environments.
        /// </summary>
        [JsonProperty(PropertyName = "Broadcast", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string Broadcast { get; set; } = null;

        /// <summary>
        /// Used for checking subnet conflicts below.
        /// </summary>
        private class SubnetDefinition
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="name">Subnet name.</param>
            /// <param name="cidr">Subnet CIDR.</param>
            public SubnetDefinition(string name, NetworkCidr cidr)
            {
                this.Name = $"{nameof(NetworkOptions)}.{name}";
                this.Cidr = cidr;
            }

            /// <summary>
            /// Identifies the subnet.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// The subnet CIDR.
            /// </summary>
            public NetworkCidr Cidr { get; set; }
        }

        /// <summary>
        /// Validates the options and also ensures that all <c>null</c> properties are
        /// initialized to their default values.
        /// </summary>
        /// <param name="clusterDefinition">The cluster definition.</param>
        /// <exception cref="ClusterDefinitionException">Thrown if the definition is not valid.</exception>
        [Pure]
        public void Validate(ClusterDefinition clusterDefinition)
        {
            Covenant.Requires<ArgumentNullException>(clusterDefinition != null);

            var subnets = new List<SubnetDefinition>();

            if (!NetworkCidr.TryParse(PublicSubnet, out var cidr))
            {
                throw new ClusterDefinitionException($"Invalid [{nameof(NetworkOptions)}.{nameof(PublicSubnet)}={PublicSubnet}].");
            }

            subnets.Add(new SubnetDefinition(nameof(PublicSubnet), cidr));

            if (!NetworkCidr.TryParse(PrivateSubnet, out cidr))
            {
                throw new ClusterDefinitionException($"Invalid [{nameof(NetworkOptions)}.{nameof(PrivateSubnet)}={PrivateSubnet}].");
            }

            subnets.Add(new SubnetDefinition(nameof(PrivateSubnet), cidr));

            if (PublicSubnet == PrivateSubnet)
            {
                throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(PublicSubnet)}] cannot be the same as [{nameof(PrivateSubnet)}] .");
            }

            if (Nameservers == null || Nameservers.Length == 0)
            {
                Nameservers = new string[] { "8.8.8.8", "8.8.4.4" };
            }

            foreach (var nameserver in Nameservers)
            {
                if (!IPAddress.TryParse(nameserver, out var address))
                {
                    throw new ClusterDefinitionException($"[{nameserver}] is not a valid [{nameof(NetworkOptions)}.{nameof(Nameservers)}] IP address.");
                }
            }

            PdnsServerPackageUri = PdnsServerPackageUri ?? defaultPdnsServerPackageUri;

            if (!Uri.TryCreate(PdnsServerPackageUri, UriKind.Absolute, out var uri1))
            {
                throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(PdnsServerPackageUri)}={PdnsServerPackageUri}] is not a valid URI.");
            }

            PdnsBackendRemotePackageUri = PdnsBackendRemotePackageUri ?? defaultPdnsBackendRemotePackageUri;

            if (!Uri.TryCreate(PdnsBackendRemotePackageUri, UriKind.Absolute, out var uri2))
            {
                throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(PdnsBackendRemotePackageUri)}={PdnsBackendRemotePackageUri}] is not a valid URI.");
            }

            PdnsRecursorPackageUri = PdnsRecursorPackageUri ?? defaultPdnsRecursorPackagePackageUri;

            if (!Uri.TryCreate(PdnsServerPackageUri, UriKind.Absolute, out var uri3))
            {
                throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(PdnsRecursorPackageUri)}={PdnsRecursorPackageUri}] is not a valid URI.");
            }

            if (clusterDefinition.Hosting.IsCloudProvider)
            {
                // Verify [CloudSubnet].

                if (string.IsNullOrEmpty(CloudSubnet))
                {
                    CloudSubnet = defaultCloudSubnet;
                }

                if (!NetworkCidr.TryParse(CloudSubnet, out var cloudSubnetCidr))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(CloudSubnet)}={CloudSubnet}] is not a valid IPv4 subnet.");
                }

                if (cloudSubnetCidr.PrefixLength != 21)
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(CloudSubnet)}={CloudSubnet}] prefix length is not valid.  Only [/21] subnets are currently supported.");
                }

                // Compute [NodeSubnet] by splitting [ClusterSubnet] in quarters and taking the
                // first quarter.

                NetworkCidr nodesSubnetCidr;

                nodesSubnetCidr = new NetworkCidr(cloudSubnetCidr.Address, cloudSubnetCidr.PrefixLength + 2);
                NodesSubnet     = nodesSubnetCidr.ToString();

                subnets.Add(new SubnetDefinition(nameof(NodesSubnet), nodesSubnetCidr));

                // Ensure that the node subnet is big enough to allocate an
                // IP address for each node.

                if (clusterDefinition.Nodes.Count() > nodesSubnetCidr.AddressCount - 4)
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(NodesSubnet)}={NodesSubnet}] subnet not large enough for the [{clusterDefinition.Nodes.Count()}] node addresses.");
                }

                // Verify/Compute VPN properties.

                if (clusterDefinition.Vpn.Enabled)
                {
                    // Compute [CloudVpnSubnet] by taking the second quarter of [ClusterSubnet].

                    NetworkCidr cloudVpnCidr;

                    cloudVpnCidr   = new NetworkCidr(nodesSubnetCidr.NextAddress, cloudSubnetCidr.PrefixLength + 2);
                    CloudVpnSubnet = cloudVpnCidr.ToString();

                    // Compute [CloudVNetSubnet] by taking the first half of [CloudSubnet],
                    // which includes both [NodesSubnet] and [CloudVpnSubnet].

                    NetworkCidr cloudVNetSubnet;

                    cloudVNetSubnet = new NetworkCidr(cloudSubnetCidr.Address, cloudSubnetCidr.PrefixLength + 1);
                    CloudVNetSubnet = cloudVNetSubnet.ToString();

                    // Compute [VpnPoolSubnet] by taking the upper half of [ClusterSubnet].

                    NetworkCidr vpnPoolCidr;

                    vpnPoolCidr   = new NetworkCidr(cloudVpnCidr.NextAddress, 22);
                    VpnPoolSubnet = vpnPoolCidr.ToString();

                    subnets.Add(new SubnetDefinition(nameof(VpnPoolSubnet), vpnPoolCidr));
                }
            }
            else
            {
                // Verify [PremiseSubnet].

                if (!NetworkCidr.TryParse(PremiseSubnet, out var premiseCidr))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(PremiseSubnet)}={PremiseSubnet}] is not a valid IPv4 subnet.");
                }

                // Verify [Gateway]

                if (string.IsNullOrEmpty(Gateway))
                {
                    // Default to the first valid address of the cluster nodes subnet 
                    // if this isn't already set.

                    Gateway = premiseCidr.FirstUsableAddress.ToString();
                }

                if (!IPAddress.TryParse(Gateway, out var gateway) || gateway.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(Gateway)}={Gateway}] is not a valid IPv4 address.");
                }

                if (!premiseCidr.Contains(gateway))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(Gateway)}={Gateway}] address is not within the [{nameof(NetworkOptions)}.{nameof(NetworkOptions.NodesSubnet)}={NodesSubnet}] subnet.");
                }

                // Verify [Broadcast]

                if (string.IsNullOrEmpty(Broadcast))
                {
                    // Default to the first valid address of the cluster nodes subnet 
                    // if this isn't already set.

                    Broadcast = premiseCidr.LastAddress.ToString();
                }

                if (!IPAddress.TryParse(Broadcast, out var broadcast) || broadcast.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(Broadcast)}={Broadcast}] is not a valid IPv4 address.");
                }

                if (!premiseCidr.Contains(broadcast))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(Broadcast)}={Broadcast}] address is not within the [{nameof(NetworkOptions)}.{nameof(NetworkOptions.NodesSubnet)}={NodesSubnet}] subnet.");
                }

                // Verify [NodesSubnet].

                if (!NetworkCidr.TryParse(NodesSubnet, out var nodesSubnetCidr))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(NodesSubnet)}={NodesSubnet}] is not a valid IPv4 subnet.");
                }

                if (!premiseCidr.Contains(nodesSubnetCidr))
                {
                    throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(NodesSubnet)}={NodesSubnet}] is not within [{nameof(NetworkOptions)}.{nameof(PremiseSubnet)}={PremiseSubnet}].");
                }

                // Verify VPN properties for on-premise environments.

                if (clusterDefinition.Vpn.Enabled)
                {
                    if (clusterDefinition.Hosting.Environment == HostingEnvironments.Machine)
                    {
                        if (string.IsNullOrEmpty(ManagerRouter))
                        {
                            throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(ManagerRouter)}] is required for on-premise deployments that enable VPN.  Set the public IP address or FQDN of your cluster router.");
                        }
                    }

                    // Verify [VpnPoolSubnet].

                    if (!NetworkCidr.TryParse(VpnPoolSubnet, out var vpnPoolCidr))
                    {
                        throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(VpnPoolSubnet)}={VpnPoolSubnet}] is not a valid subnet.");
                    }

                    if (vpnPoolCidr.PrefixLength > 23)
                    {
                        throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(VpnPoolSubnet)}={VpnPoolSubnet}] is too small.  The subnet prefix length cannot be longer than [23].");
                    }

                    subnets.Add(new SubnetDefinition(nameof(VpnPoolSubnet), vpnPoolCidr));

                    if (nodesSubnetCidr.Overlaps(vpnPoolCidr))
                    {
                        throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(NodesSubnet)}={NodesSubnet}] and [{nameof(VpnPoolSubnet)}={VpnPoolSubnet}] overlap.");
                    }

                    subnets.Add(new SubnetDefinition(nameof(NodesSubnet), nodesSubnetCidr));

                    if (!premiseCidr.Contains(vpnPoolCidr))
                    {
                        throw new ClusterDefinitionException($"[{nameof(NetworkOptions)}.{nameof(VpnPoolSubnet)}={VpnPoolSubnet}] is not within [{nameof(NetworkOptions)}.{nameof(PremiseSubnet)}={PremiseSubnet}].");
                    }
                }
            }

            // Verify that none of the major subnets conflict.

            foreach (var subnet in subnets)
            {
                foreach (var subnetTest in subnets)
                {
                    if (subnet == subnetTest)
                    {
                        continue;   // Don't test against self.
                    }

                    if (subnet.Cidr.Overlaps(subnetTest.Cidr))
                    {
                        throw new ClusterDefinitionException($"[{subnet.Name}={subnet.Cidr}] and [{subnetTest.Name}={subnetTest.Cidr}] overlap.");
                    }
                }
            }
        }
    }
}
