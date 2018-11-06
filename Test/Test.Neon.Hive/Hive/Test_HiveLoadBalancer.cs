﻿//-----------------------------------------------------------------------------
// FILE:	    Test_HiveLoadBalancer.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Cryptography;
using Neon.Hive;
using Neon.Xunit;
using Neon.Xunit.Hive;

using Xunit;

namespace TestHive
{
    public partial class Test_HiveLoadBalancer : IClassFixture<HiveFixture>
    {
        private const string            testHostname = "vegomatic.test";
        private static TlsCertificate   certificate;

        private HiveFixture     hiveFixture;
        private HiveProxy       hive;
        private string          vegomaticImage = $"nhive/vegomatic:{ThisAssembly.Git.Branch}-latest";

        public Test_HiveLoadBalancer(HiveFixture fixture)
        {
            if (!fixture.LoginAndInitialize())
            {
                fixture.Reset();
            }

            this.hiveFixture = fixture;
            this.hive        = fixture.Hive;

            // Generate a self-signed certificate we can reuse across tests if
            // we haven't already created one.

            if (certificate == null)
            {
                certificate = TlsCertificate.CreateSelfSigned(testHostname);
            }
        }

        /// <summary>
        /// Waits for the a remote proxy and origin to report being ready.
        /// </summary>
        /// <param name="baseUri">The base URI.</param>
        /// <param name="hostname">The target hostname.</param>
        private async Task WaitUntilReadyAsync(Uri baseUri, string hostname)
        {
            // Allow self-signed certificates for HTTPS tests.

            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using (var client = new TestHttpClient(disableConnectionReuse: true, handler: handler, disposeHandler: true))
            {
                client.BaseAddress                = baseUri;
                client.DefaultRequestHeaders.Host = hostname;

                await NeonHelper.WaitForAsync(
                    async () =>
                    {
                        var response = await client.GetAsync("/");

                        return response.IsSuccessStatusCode;
                    },
                    timeout: TimeSpan.FromMinutes(2),
                    pollTime: TimeSpan.FromMilliseconds(100));
            }
        }

        /// <summary>
        /// Determines whether a response was delivered via Varnish.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns><c>true</c> when the response was delivered via Varnish.</returns>
        private bool ViaVarnish(HttpResponseMessage response)
        {
            // The [X-Varnish] header will be present if Varnish delivered the response.

            return response.Headers.Contains("X-Varnish");
        }

        /// <summary>
        /// Determines whether a response was delivered via Varnish and was a cache hit.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns><c>true</c> for a cache hit.</returns>
        private bool CacheHit(HttpResponseMessage response)
        {
            // The [X-Varnish] header will be present and will include two
            // space separated integer IDs when the reponse was returned 
            // from the cache.

            if (response.Headers.TryGetValues("X-Varnish", out var values))
            {
                var value  = values.Single().Trim();
                var fields = value.Split(' ');

                return fields.Length == 2 && int.TryParse(fields[0], out var v1) && int.TryParse(fields[1], out var v2);
            }
            else
            {
                return false;
            }
        }
    }
}
