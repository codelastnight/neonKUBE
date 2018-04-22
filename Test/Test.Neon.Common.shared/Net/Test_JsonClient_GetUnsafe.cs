﻿//-----------------------------------------------------------------------------
// FILE:	    Test_JsonClient_GetUnsafe.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Owin;
using Microsoft.Owin;
using Microsoft.Owin.Hosting;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Net;
using Neon.Retry;

using Xunit;
using Xunit.Neon;

namespace TestCommon
{
    public partial class Test_JsonClient
    {
        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync()
        {
            // Ensure that GET returning an explict type works.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var request  = context.Request;
                            var response = context.Response;

                            if (request.Method != "GET")
                            {
                                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                return Task.Delay(0);
                            }

                            if (request.Path.ToString() != "/info")
                            {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                return Task.Delay(0);
                            }

                            var output = new ReplyDoc()
                                {
                                    Value1 = "Hello World!"
                                };

                            response.ContentType = "application/json";

                            return response.WriteAsync(NeonHelper.JsonSerialize(output));
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var reply = (await jsonClient.GetUnsafeAsync(baseUri + "info")).As<ReplyDoc>();

                    Assert.Equal("Hello World!", reply.Value1);
                }
            };
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_NotJson()
        {
            // Ensure that GET returning a non-JSON content type returns a NULL document.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var request  = context.Request;
                            var response = context.Response;

                            if (request.Method != "GET")
                            {
                                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                return Task.Delay(0);
                            }

                            if (request.Path.ToString() != "/info")
                            {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                return Task.Delay(0);
                            }

                            var output = new ReplyDoc()
                                {
                                    Value1 = "Hello World!"
                                };

                            response.ContentType = "application/not-json";

                            return response.WriteAsync(NeonHelper.JsonSerialize(output));
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var reply = (await jsonClient.GetUnsafeAsync(baseUri + "info")).As<ReplyDoc>();

                    Assert.Null(reply);
                }
            };
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_Args()
        {
            // Ensure that GET with query arguments work.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var request  = context.Request;
                            var response = context.Response;

                            if (request.Method != "GET")
                            {
                                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                return Task.Delay(0);
                            }

                            if (request.Path.ToString() != "/info")
                            {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                return Task.Delay(0);
                            }

                            var output = new ReplyDoc()
                                {
                                    Value1 = request.Query.Get("arg1"),
                                    Value2 = request.Query.Get("arg2")
                                };

                            response.ContentType = "application/json";

                            return response.WriteAsync(NeonHelper.JsonSerialize(output));
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var reply = (await jsonClient.GetUnsafeAsync(baseUri + "info?arg1=test1&arg2=test2")).As<ReplyDoc>();

                    Assert.Equal("test1", reply.Value1);
                    Assert.Equal("test2", reply.Value2);
                }
            };
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_Dyanmic()
        {
            // Ensure that GET returning a dynamic works.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var request  = context.Request;
                            var response = context.Response;

                            if (request.Method != "GET")
                            {
                                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                return Task.Delay(0);
                            }

                            if (request.Path.ToString() != "/info")
                            {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                return Task.Delay(0);
                            }

                            var output = new ReplyDoc()
                                {
                                    Value1 = "Hello World!"
                                };

                            response.ContentType = "application/json";

                            return response.WriteAsync(NeonHelper.JsonSerialize(output));
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var reply = (await jsonClient.GetUnsafeAsync(baseUri + "info")).AsDynamic();

                    Assert.Equal("Hello World!", (string)reply.Value1);
                }
            };
        }
 
        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_Dynamic_NotJson()
        {
            // Ensure that GET returning non-JSON returns a NULL dynamic document.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var request  = context.Request;
                            var response = context.Response;

                            if (request.Method != "GET")
                            {
                                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                                return Task.Delay(0);
                            }

                            if (request.Path.ToString() != "/info")
                            {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                return Task.Delay(0);
                            }

                            var output = new ReplyDoc()
                                {
                                    Value1 = "Hello World!"
                                };

                            response.ContentType = "application/not-json";

                            return response.WriteAsync(NeonHelper.JsonSerialize(output));
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var reply = (await jsonClient.GetUnsafeAsync(baseUri + "info")).AsDynamic();

                    Assert.Null(reply);
                }
            };
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_Error()
        {
            // Ensure that GET returning a hard error works.

            using (WebApp.Start(baseUri,
                app =>
                {
                    app.Run(
                        context =>
                        {
                            var response = context.Response;

                            response.StatusCode = (int)HttpStatusCode.NotFound;

                            return Task.Delay(0);
                        });
                }))
            {
                using (var jsonClient = new JsonClient())
                {
                    var response = await jsonClient.GetUnsafeAsync(baseUri + "info");

                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                    Assert.False(response.IsSuccess);
                    Assert.Throws<HttpException>(() => response.EnsureSuccess());
                }
            };
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_Retry()
        {
            // Ensure that GET will retry after soft errors.

            // $todo(jeff.lill): Simulate socket errors via HttpClient mocking.

            await Task.Delay(0);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_NoRetryNull()
        {
            // Ensure that GET won't retry if [retryPolicy=NULL]

            // $todo(jeff.lill): Simulate socket errors via HttpClient mocking.

            await Task.Delay(0);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCommon)]
        public async Task GetUnsafeAsync_NoRetryExplicit()
        {
            // Ensure that GET won't retry if [retryPolicy=NoRetryPolicy]

            // $todo(jeff.lill): Simulate socket errors via HttpClient mocking.

            await Task.Delay(0);
        }
    }
}
