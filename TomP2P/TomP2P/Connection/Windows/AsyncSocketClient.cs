﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TomP2P.Connection.Windows
{
    // TODO make operations awaitable -> async /await can be used

    // inspired by http://www.codeproject.com/Articles/22918/How-To-Use-the-SocketAsyncEventArgs-Class

    /// <summary>
    /// Implements the connection logic for an asynchronous socket client.
    /// </summary>
    public class AsyncSocketClient : IDisposable
    {
        private const int SendOperation = 0;
        private const int ReceiveOperation = 1;

        private readonly Socket _client;
        private readonly IPEndPoint _hostEndpoint;
        private const int BufferSize = 10000; // TODO set appropriate value

        private bool _isConnected;

        // signals a connection
        private static readonly AutoResetEvent AutoConnectEvent = new AutoResetEvent(false);

        // signals the send/receive operation
        private static readonly AutoResetEvent[] AutoSendReceiveEvents =
        {
            new AutoResetEvent(false),
            new AutoResetEvent(false)
        };

        /// <summary>
        /// Creates an uninitialized client instance.
        /// To start the send/receive processing, call Connect() followed by SendReceive().
        /// </summary>
        /// <param name="hostName">Name of the host where the <see cref="AsyncSocketServer"/> is running.</param>
        /// <param name="port">Number of the port of the <see cref="AsyncSocketServer"/>.</param>
        public AsyncSocketClient(string hostName, int port)
        {
            IPHostEntry hostInfo = Dns.GetHostEntry(hostName);

            // instantiate the client endpoint and socket
            // TODO try all addresses of the host
            _hostEndpoint = new IPEndPoint(hostInfo.AddressList[0], port); // TODO correct address? or Length-1?
            _client = new Socket(_hostEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp); // TODO make UDP
        }

        /// <summary>
        /// Connect to the host.
        /// </summary>
        public void Connect()
        {
            var connectArgs = new SocketAsyncEventArgs();
            connectArgs.RemoteEndPoint = _hostEndpoint;
            connectArgs.Completed += Connect_Completed;

            _client.ConnectAsync(connectArgs);
            AutoConnectEvent.WaitOne(); // waits for Set()

            var errorCode = connectArgs.SocketError;
            if (errorCode != SocketError.Success)
            {
                throw new SocketException((int)errorCode);
            }
        }

        /// <summary>
        /// Disconnect from the host.
        /// </summary>
        public void Disconnect()
        {
            _client.Disconnect(false);
        }

        public void SendReceive(object message)
        {
            if (_isConnected)
            {
                // create a buffer to send
                var sendBuffer = new byte[BufferSize]; // TODO take bytes from the message

                // prepare send/receive operations
                var completeArgs = new SocketAsyncEventArgs();
                completeArgs.SetBuffer(sendBuffer, 0, sendBuffer.Length);
                completeArgs.UserToken = _client;
                completeArgs.RemoteEndPoint = _hostEndpoint;
                completeArgs.Completed += Send_Completed;

                // start async send
                _client.SendAsync(completeArgs);

                // wait for the send/receive completion
                WaitHandle.WaitAll(AutoSendReceiveEvents);

                // return data from SocketAsyncEventArgs buffer
                return; // TODO completeArgs.Buffer, completeArgs.Offset, completeArgs.BytesTransferred
            }
            throw new SocketException((int) SocketError.NotConnected);
        }

        private void Connect_Completed(object sender, SocketAsyncEventArgs args)
        {
            // signal the end of connection
            AutoConnectEvent.Set();

            // set flag for socket connected
            _isConnected = (args.SocketError == SocketError.Success);
        }

        private void Send_Completed(object sender, SocketAsyncEventArgs args)
        {
            // signal the end of send
            AutoSendReceiveEvents[ReceiveOperation].Set(); // TODO not vice-versa?

            if (args.SocketError == SocketError.Success)
            {
                if (args.LastOperation == SocketAsyncOperation.Send)
                {
                    // prepare receiving
                    var socket = args.UserToken as Socket;
                    var recvBuffer = new byte[BufferSize];
                    args.SetBuffer(recvBuffer, 0, recvBuffer.Length);
                    args.Completed += Receive_Completed;

                    // start async receive
                    socket.ReceiveAsync(args);
                }
            }
            else
            {
                ProcessError(args);
            }
        }

        private void Receive_Completed(object sender, SocketAsyncEventArgs args)
        {
            // signal the end of receive
            AutoSendReceiveEvents[SendOperation].Set();
        }

        private void ProcessError(SocketAsyncEventArgs args)
        {
            var socket = args.UserToken as Socket;
            if (socket.Connected)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception)
                {
                    // throws if client process has already closed
                }
                finally
                {
                    if (socket.Connected)
                    {
                        socket.Close();
                    }
                }
            }
            throw new SocketException((int) args.SocketError);
        }

        public void Dispose()
        {
            AutoConnectEvent.Close();
            AutoSendReceiveEvents[SendOperation].Close();
            AutoSendReceiveEvents[ReceiveOperation].Close();
            if (_client.Connected)
            {
                _client.Close();
            }
        }
    }
}
