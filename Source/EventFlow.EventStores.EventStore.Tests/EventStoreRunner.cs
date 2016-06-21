﻿// The MIT License (MIT)
//
// Copyright (c) 2015-2016 Rasmus Mikkelsen
// Copyright (c) 2015-2016 eBay Software Foundation
// https://github.com/rasmus/EventFlow
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EventFlow.Extensions;
using EventFlow.TestHelpers;
using EventStore.ClientAPI;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;

namespace EventFlow.EventStores.EventStore.Tests
{
    [Category(Categories.Integration)]
    public class EventStoreRunner : Runner
    {
        protected override IEnumerable<SoftwareDescription> SoftwareDescriptions { get; } = new[]
            {
                new SoftwareDescription(new Version(3, 3, 1), new Uri("http://download.geteventstore.com/binaries/EventStore-OSS-Win-v3.3.1.zip", UriKind.Absolute)),
                new SoftwareDescription(new Version(3, 4, 0), new Uri("http://download.geteventstore.com/binaries/EventStore-OSS-Win-v3.4.0.zip", UriKind.Absolute)),
            };

        protected override string SoftwareName { get; } = "EventStore";

        public class EventStoreInstance : IDisposable
        {
            private readonly IDisposable _processDisposable;
            public Uri ConnectionStringUri { get; }

            public EventStoreInstance(
                Uri connectionStringUri,
                IDisposable processDisposable)
            {
                _processDisposable = processDisposable;
                ConnectionStringUri = connectionStringUri;
            }

            public void Dispose()
            {
                _processDisposable.Dispose();
            }
        }

        [Test, Explicit("Used to test the EventStore runner")]
        [Timeout(60000)]
        public async Task TestRunner()
        {
            using (await StartAsync().ConfigureAwait(false))
            {
                // Put EventStore usage here...
                Thread.Sleep(TimeSpan.FromSeconds(0.5));
            }
        }

        public static Task<EventStoreInstance> StartAsync()
        {
            return new EventStoreRunner().InternalStartAsync();
        }

        private async Task<EventStoreInstance> InternalStartAsync()
        {
            var eventStoreVersion = SoftwareDescriptions.OrderByDescending(kv => kv.Version).First();
            var eventStorePath = await InstallAsync(eventStoreVersion.Version).ConfigureAwait(false);

            var tcpPort = TcpHelper.GetFreePort();
            var httpPort = TcpHelper.GetFreePort();
            var connectionStringUri = new Uri($"tcp://admin:changeit@{IPAddress.Loopback}:{tcpPort}");
            var exePath = Path.Combine(eventStorePath, "EventStore.ClusterNode.exe");

            IDisposable processDisposable = null;
            try
            {
                processDisposable = StartExe(
                    exePath,
                    "'admin' user added to $users",
                    "--mem-db=True",
                    "--cluster-size=1",
                    $"--ext-tcp-port={tcpPort}",
                    $"--ext-http-port={httpPort}");

                var connectionSettings = ConnectionSettings.Create()
                    .EnableVerboseLogging()
                    .KeepReconnecting()
                    .SetDefaultUserCredentials(new UserCredentials("admin", "changeit"))
                    .Build();
                using (var eventStoreConnection = EventStoreConnection.Create(connectionSettings, connectionStringUri))
                {
                    var start = DateTimeOffset.Now;
                    while (true)
                    {
                        if (start + TimeSpan.FromSeconds(10) < DateTimeOffset.Now)
                        {
                            throw new Exception("Failed to connect to EventStore");
                        }

                        try
                        {
                            await eventStoreConnection.ConnectAsync().ConfigureAwait(false);
                            break;
                        }
                        catch (Exception)
                        {
                            Console.WriteLine("Failed to connect, retrying");
                        }
                    }
                }
            }
            catch (Exception)
            {
                processDisposable.DisposeSafe("Failed to dispose EventStore process");
                throw;
            }

            return new EventStoreInstance(
                connectionStringUri,
                processDisposable);
        }
    }
}