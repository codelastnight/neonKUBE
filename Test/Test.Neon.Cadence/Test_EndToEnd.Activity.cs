﻿//-----------------------------------------------------------------------------
// FILE:        Test_EndToEnd.Activity.cs
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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Cryptography;
using Neon.Data;
using Neon.IO;
using Neon.Xunit;
using Neon.Xunit.Cadence;

using Newtonsoft.Json;
using Xunit;

namespace TestCadence
{
    public partial class Test_EndToEnd
    {
        //---------------------------------------------------------------------

        private static bool activityTests_ActivityWithNoResultCalled;
        private static bool activityTests_WorkflowWithNoResultCalled;

        public interface IActivityWithNoResult : IActivity
        {
            [ActivityMethod]
            Task RunAsync();
        }

        [Activity(AutoRegister = true)]
        public class ActivityWithNoResult : ActivityBase, IActivityWithNoResult
        {
            public async Task RunAsync()
            {
                activityTests_ActivityWithNoResultCalled = true;

                await Task.CompletedTask;
            }
        }

        public interface IActivityWorkflowWithNoResult : IWorkflow
        {
            [WorkflowMethod]
            Task RunAsync();
        }

        [Workflow(AutoRegister = true)]
        public class ActivityWorkflowWithNoResult : WorkflowBase, IActivityWorkflowWithNoResult
        {
            public async Task RunAsync()
            {
                activityTests_WorkflowWithNoResultCalled = true;

                var stub = Workflow.NewActivityStub<IActivityWithNoResult>();

                await stub.RunAsync();
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCadence)]
        public async Task Activity_WithNoResult()
        {
            // Verify that we can call a simple workflow that accepts a
            // parameter, calls a similarly simple activity and results
            // a result.

            activityTests_ActivityWithNoResultCalled = false;
            activityTests_WorkflowWithNoResultCalled = false;

            var stub = client.NewWorkflowStub<IActivityWorkflowWithNoResult>();

            await stub.RunAsync();

            Assert.True(activityTests_WorkflowWithNoResultCalled);
            Assert.True(activityTests_ActivityWithNoResultCalled);
        }

        //---------------------------------------------------------------------

        public interface IActivityWithResult : IActivity
        {
            [ActivityMethod]
            Task<string> HelloAsync(string name);
        }

        [Activity(AutoRegister = true)]
        public class ActivityWithResult : ActivityBase, IActivityWithResult
        {
            public async Task<string> HelloAsync(string name)
            {
                return await Task.FromResult($"Hello {name}!");
            }
        }

        public interface IActivityWorkflowWithResult : IWorkflow
        {
            [WorkflowMethod]
            Task<string> HelloAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class ActivityWorkflowWithResult : WorkflowBase, IActivityWorkflowWithResult
        {
            public async Task<string> HelloAsync(string name)
            {
                var stub = Workflow.NewActivityStub<IActivityWithResult>();

                return await stub.HelloAsync(name);
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCadence)]
        public async Task Activity_WithResult()
        {
            // Verify that we can call a simple workflow that accepts a
            // parameter, calls a similarly simple activity that returns
            // a result.

            var stub = client.NewWorkflowStub<IActivityWorkflowWithResult>();

            Assert.Equal("Hello Jeff!", await stub.HelloAsync("Jeff"));
        }

        //---------------------------------------------------------------------

        public interface ILocalActivityWithResult : IActivity
        {
            [ActivityMethod]
            Task<string> HelloAsync(string name);
        }

        public class LocalActivityWithResult : ActivityBase, ILocalActivityWithResult
        {
            [ActivityMethod]
            public async Task<string> HelloAsync(string name)
            {
                return await Task.FromResult($"Hello {name}!");
            }
        }

        public interface ILocalActivityWorkflowWithResult : IWorkflow
        {
            [WorkflowMethod]
            Task<string> HelloAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class LocalActivityWorkflowWithResult : WorkflowBase, ILocalActivityWorkflowWithResult
        {
            public async Task<string> HelloAsync(string name)
            {
                var stub = Workflow.NewLocalActivityStub<ILocalActivityWithResult, LocalActivityWithResult>();

                return await stub.HelloAsync(name);
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCadence)]
        public async Task LocalActivity_WithResult()
        {
            // Verify that we can call a simple workflow that accepts a
            // parameter, calls a similarly simple local activity that
            // returns a result.

            var stub = client.NewWorkflowStub<ILocalActivityWorkflowWithResult>();

            Assert.Equal("Hello Jeff!", await stub.HelloAsync("Jeff"));
        }

        //---------------------------------------------------------------------

        public interface ILocalActivityWithoutResult : IActivity
        {
            [ActivityMethod]
            Task HelloAsync(string name);
        }

        public class LocalActivityWithouthResult : ActivityBase, ILocalActivityWithoutResult
        {
            public static string Name { get; private set; } = null;

            public static void Reset()
            {
                Name = null;
            }

            [ActivityMethod]
            public async Task HelloAsync(string name)
            {
                LocalActivityWithouthResult.Name = name;

                await Task.CompletedTask;
            }
        }

        public interface ILocalActivityWorkflowWithoutResult : IWorkflow
        {
            [WorkflowMethod]
            Task HelloAsync(string name);
        }

        [Workflow(AutoRegister = true)]
        public class LocalActivityWorkflowWithoutResult : WorkflowBase, ILocalActivityWorkflowWithoutResult
        {
            public async Task HelloAsync(string name)
            {
                var stub = Workflow.NewLocalActivityStub<ILocalActivityWithoutResult, LocalActivityWithouthResult>();

                await stub.HelloAsync(name);
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCadence)]
        public async Task LocalActivity_WithoutResult()
        {
            LocalActivityWithouthResult.Reset();

            // Verify that we can call a simple workflow that accepts a
            // parameter, calls a similarly simple local activity that
            // doesn't return a result.

            var stub = client.NewWorkflowStub<ILocalActivityWorkflowWithoutResult>();

            await stub.HelloAsync("Jeff");
            Assert.Equal("Jeff", LocalActivityWithouthResult.Name);
        }

        //---------------------------------------------------------------------

        public interface IActivityMultipleStubCalls : IActivity
        {
            [ActivityMethod]
            Task<string> HelloAsync(string name);
        }

        [Activity(AutoRegister = true)]
        public class ActivityMultipleStubCalls : ActivityBase, IActivityMultipleStubCalls
        {
            [ActivityMethod]
            public async Task<string> HelloAsync(string name)
            {
                return await Task.FromResult($"Hello {name}!");
            }
        }

        public interface IActivityWorkflowMultipleStubCalls : IWorkflow
        {
            [WorkflowMethod]
            Task<List<string>> RunAsync();
        }

        [Workflow(AutoRegister = true)]
        public class ActivityWorkflowMultipleStubCalls : WorkflowBase, IActivityWorkflowMultipleStubCalls
        {
            public async Task<List<string>> RunAsync()
            {
                var stub = Workflow.NewActivityStub<IActivityMultipleStubCalls>();
                var list = new List<string>();

                list.Add(await stub.HelloAsync("Jack"));
                list.Add(await stub.HelloAsync("Jill"));

                return list;
            }
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCadence)]
        public async Task Activity_MultipleStubCalls()
        {
            // Verify that we can reuse an activity stub to make multiple calls.

            var stub = client.NewWorkflowStub<IActivityWorkflowMultipleStubCalls>();
            var list = await stub.RunAsync();

            Assert.Equal(new List<string>() { "Hello Jack!", "Hello Jill!" }, list);
        }
    }
}