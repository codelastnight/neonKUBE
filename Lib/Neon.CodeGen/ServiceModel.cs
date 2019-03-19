﻿//-----------------------------------------------------------------------------
// FILE:	    DataAttributes.cs
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
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Text;

namespace Neon.CodeGen
{
    /// <summary>
    /// Holds information about a service model extracted from a source assembly.
    /// </summary>
    internal class ServiceModel
    {
        private string clientGroup;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="sourceType">The source service type.</param>
        /// <param name="codeGenerator">The code generator instance.</param>
        public ServiceModel(Type sourceType, CodeGenerator codeGenerator)
        {
            Covenant.Requires<ArgumentNullException>(sourceType != null);

            this.SourceType = sourceType;

            // Determine the name we'll use for the generated service class.

            var serviceAttribute = sourceType.GetCustomAttribute<ServiceModelAttribute>();

            if (serviceAttribute != null && !string.IsNullOrEmpty(serviceAttribute.Name))
            {
                this.ClientTypeName = serviceAttribute.Name;
            }
            else
            {
                this.ClientTypeName = sourceType.Name;

                if (this.ClientTypeName.EndsWith("Controller"))
                {
                    this.ClientTypeName = this.ClientTypeName.Substring(0, this.ClientTypeName.Length - "Controller".Length);
                }
            }

            // Determine the service route template.

            var routeAttribute = sourceType.GetCustomAttribute<RouteAttribute>();

            if (routeAttribute != null && !string.IsNullOrEmpty(routeAttribute.Template))
            {
                if (!routeAttribute.Template.StartsWith("/"))
                {
                    codeGenerator.Output.Errors.Add($"ERROR: Service [{sourceType.FullName}] has a relative [Route] attribute.  Only absolute service routes are allowed.");
                }
            }
            else
            {
                this.RouteTemplate = "/";
            }
        }

        /// <summary>
        /// Returns the source type.
        /// </summary>
        public Type SourceType { get; private set; }

        /// <summary>
        /// Returns the target groups for the type.
        /// </summary>
        public HashSet<string> TargetGroups { get; private set; } = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// Specifies the class name to use for the generated service client.
        /// </summary>
        public string ClientTypeName { get; set; }

        /// <summary>
        /// Optionally used group multiple services into a single generated service client.
        /// </summary>
        public string ClientGroup
        {
            get => clientGroup;

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    clientGroup = null;
                }
                else
                {
                    clientGroup = value.Trim();
                }
            }
        }

        /// <summary>
        /// Optionally specifies the route template prefix for the service.
        /// </summary>
        public string RouteTemplate { get; set; }

        /// <summary>
        /// Lists the service.
        /// </summary>
        public List<ServiceMethod> Methods { get; private set; } = new List<ServiceMethod>();
    }
}