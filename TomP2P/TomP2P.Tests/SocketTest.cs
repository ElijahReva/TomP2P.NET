﻿
// this is an older Windows Socket test, I'll leave it here for potential documentary reasons

/*using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TomP2P.Tests
{
    [TestFixture]
    public class SocketTest
    {
        [Test]
        public void TcpAsyncSocketTest()
        {
            var r = new Random();
            const int iterations = 100;
            const int nrOfClients = 5;
            const int bufferSize = 1000;

            var tasks = new Task[nrOfClients];
            var results = new bool[nrOfClients][];
            for (int i = 0; i < nrOfClients; i++)
            {
                results[i] = new bool[iterations];
            }

            const string serverName = "localhost";
            const int serverPort = 5150;
            var serverEp = new IPEndPoint(IPAddress.Any, serverPort);

            // start server socket on a separate thread
            var server = new TcpServerSocket(serverEp, nrOfClients, bufferSize);
            new Thread(server.Start).Start();

            // run the async clients on separate threads
            for (int i = 0; i < nrOfClients; i++)
            {
                int i1 = i;
                var t = Task.Run(async () =>
                {
                    var client = new TcpClientSocket();
                    await client.ConnectAsync(serverName, serverPort);
                    for (int j = 0; j < iterations; j++)
                    {
                        // send random bytes and expect same bytes as echo
                        var sendBytes = new byte[bufferSize];
                        var recvBytes = new byte[bufferSize];
                        r.NextBytes(sendBytes);

                        await client.SendAsync(sendBytes);
                        await client.ReceiveAsync(recvBytes);

                        var res = sendBytes.SequenceEqual(recvBytes);
                        results[i1][j] = res;
                    }
                    await client.DisconnectAsync();
                    client.Close();
                });
                tasks[i] = t;
            }

            // await all tasks
            Task.WaitAll(tasks);

            server.Stop();

            // check all results for true
            for (int i = 0; i < results.Length; i++)
            {
                for (int j = 0; j < results[i].Length; j++)
                {
                    Assert.IsTrue(results[i][j]);
                }
            }
        }

        [Test]
        public void UdpAsyncSocketTest()
        {
            var r = new Random();
            const int iterations = 100;
            const int nrOfClients = 5;
            const int bufferSize = 1000;
            
            var tasks = new Task[nrOfClients];
            var results = new bool[nrOfClients][];
            for (int i = 0; i < nrOfClients; i++)
            {
                results[i] = new bool[iterations];
            }

            const int serverPort = 5150;
            const int clientPort = 5151;
            var serverEp = new IPEndPoint(IPAddress.Any, serverPort);
            var serverEp2 = new IPEndPoint(IPAddress.Parse("127.0.0.1"), serverPort);

            // start server socket on a separate thread
            var server = new UdpServerSocket(serverEp, nrOfClients, bufferSize);
            new Thread(server.Start).Start();

            // run the async clients on separate threads
            for (int i = 0; i < nrOfClients; i++)
            {
                int i1 = i;
                var t = Task.Run(async () =>
                {
                    var client = new UdpClientSocket(new IPEndPoint(IPAddress.Any, clientPort + i1));
                    for (int j = 0; j < iterations; j++)
                    {
                        // send random bytes and expect same bytes as echo
                        var sendBytes = new byte[bufferSize];
                        var recvBytes = new byte[bufferSize];
                        r.NextBytes(sendBytes);

                        await client.SendAsync(sendBytes, serverEp2);
                        await client.ReceiveAsync(recvBytes, serverEp2);

                        var res = sendBytes.SequenceEqual(recvBytes);
                        results[i1][j] = res;
                    }
                    client.Close();
                });
                tasks[i] = t;
            }

            // await all tasks
            Task.WaitAll(tasks);

            server.Stop();

            // check all results for true
            for (int i = 0; i < results.Length; i++)
            {
                for (int j = 0; j < results[i].Length; j++)
                {
                    Assert.IsTrue(results[i][j]);
                }
            }
        }
    }
}
*/