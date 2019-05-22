﻿//-----------------------------------------------------------------------------
// FILE:	    WorkflowListOpenExecutionsRequest.cs
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;

using Newtonsoft.Json;
using YamlDotNet.Serialization;

using Neon.Cadence;
using Neon.Common;

namespace Neon.Cadence.Internal
{
    /// <summary>
    /// <b>proxy --> client:</b> Lists open workflows.
    /// </summary>
    [ProxyMessage(MessageTypes.WorkflowListOpenExecutionsRequest)]
    internal class WorkflowListOpenExecutionsRequest : WorkflowRequest
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public WorkflowListOpenExecutionsRequest()
        {
            Type = MessageTypes.WorkflowListOpenExecutionsRequest;
        }

        /// <inheritdoc/>
        public override MessageTypes ReplyType => MessageTypes.WorkflowListOpenExecutionsReply;

        /// <summary>
        /// Optionally specifies the target domain.  Workflows from all
        /// domains will be listed when this is omitted.
        /// </summary>
        public string Domain
        {
            get => GetStringProperty("Domain");
            set => SetStringProperty("Domain", value);
        }

        public int MaximumPageSize
        {
            get => GetIntProperty("MaximumPageSize");
            set => SetIntProperty("MaximumPageSize", value);
        }

        /// <inheritdoc/>
        internal override ProxyMessage Clone()
        {
            var clone = new WorkflowListOpenExecutionsRequest();

            CopyTo(clone);

            return clone;
        }

        /// <inheritdoc/>
        protected override void CopyTo(ProxyMessage target)
        {
            base.CopyTo(target);

            var typedTarget = (WorkflowListOpenExecutionsRequest)target;

            typedTarget.Domain = this.Domain;
        }
    }
}