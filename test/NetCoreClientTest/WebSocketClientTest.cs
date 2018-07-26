using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NetCoreClientTest.Utils;
using Xunit;
using Xunit.Abstractions;

namespace NetCoreClientTest
{
    public class WebSocketClientTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public WebSocketClientTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task ClientCanSentBinaryData()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54321/ws";
            const string serverAddress = "http://localhost:54321";
            var serverTestComplte = false;
            
            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            var originData = Encoding.UTF8.GetBytes("Hello World");
            var serverAction = new Func<HttpContext, Task>(async context =>
            {
                if (context.Request.Path == "/ws")
                {
                    Assert.True(context.WebSockets.IsWebSocketRequest);
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync().OrTimeout();

                    var serverBuffer = new byte[originData.Length];
                    var receiveResult =
                        await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer), CancellationToken.None).OrTimeout();

                    while (!receiveResult.CloseStatus.HasValue)
                    {
                        //Assert
                        Assert.True(receiveResult.EndOfMessage);
                        Assert.Equal(originData.Length, receiveResult.Count);
                        Assert.Equal(WebSocketMessageType.Binary, receiveResult.MessageType);
                        Assert.Equal(originData, serverBuffer);

                        receiveResult =
                            await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer),
                                CancellationToken.None).OrTimeout();
                    }

                    await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription,
                        CancellationToken.None).OrTimeout();
                    serverTestComplte = true;
                }
            });

            //Act
            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, serverAction))
            {
                await server.StartAsync().OrTimeout();

                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    ws.Log = CreateWebSocketLogger();
                    ws.Connect();
                    ws.Send(originData);
                    ws.Close();
                }
            }
            Assert.True(serverTestComplte, "server test assertion fail");
        }

        [Fact]
        public async Task ClientCanReceiveBinaryData()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54323/ws";
            const string serverAddress = "http://localhost:54323";
            const string sendText = "Hello World";
            var sendData = Encoding.UTF8.GetBytes(sendText);
            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            var serverTestComplete = false;
            var clientTestComplete = false;

            //Act & Assert
            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, async httpContext =>
            {
                if (httpContext.Request.Path == "/ws")
                {
                    Assert.True(httpContext.WebSockets.IsWebSocketRequest);
                    var websocket = await httpContext.WebSockets.AcceptWebSocketAsync().OrTimeout();
                    var serverBuffer = new byte[1024 * 4];
                    WebSocketReceiveResult receiveResult;
                    do
                    {
                        await websocket.SendAsync(sendData, WebSocketMessageType.Binary, true, CancellationToken.None).OrTimeout();
                        receiveResult = await websocket.ReceiveAsync(serverBuffer, CancellationToken.None);
                    } while (!receiveResult.CloseStatus.HasValue);

                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop connection",
                        CancellationToken.None).OrTimeout();
                    serverTestComplete = true;
                }
            }))
            {
                await server.StartAsync().OrTimeout();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    ws.Log = CreateWebSocketLogger();
                    ws.OnMessage += (sender, args) =>
                    {
                        Assert.True(args.IsBinary);
                        var data = args.RawData;
                        Assert.Equal(sendData.Length, data.Length);
                        Assert.Equal(sendData, data);

                        clientTestComplete = true;
                    };
                    ws.Connect();
                    await Task.Factory.StartNew(() => SpinWait.SpinUntil(() => clientTestComplete)).OrTimeout();
                }
            }
            Assert.True(serverTestComplete, "server test assertion fail");
            Assert.True(clientTestComplete, "client test assertion fail");
        }

        [Fact]
        public async Task ClientCanReceiveTextData()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54324/ws";
            const string serverAddress = "http://localhost:54324";

            const string sendText = "Hello World";
            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);

            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper,
                async httpContext =>
                {
                    if (httpContext.Request.Path == "/ws")
                    {
                        Assert.True(httpContext.WebSockets.IsWebSocketRequest);
                        var websocket = await httpContext.WebSockets.AcceptWebSocketAsync();
                        var serverBuffer = new byte[1024 * 4];
                        WebSocketReceiveResult receiveResult;
                        do
                        {
                            await websocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(sendText)),
                                WebSocketMessageType.Text, true, CancellationToken.None);
                            receiveResult = await websocket.ReceiveAsync(serverBuffer, CancellationToken.None);
                        } while (!receiveResult.CloseStatus.HasValue);

                        await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop connection",
                            CancellationToken.None);
                    }
                }))
            {
                await server.StartAsync();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    var hasReceive = false;
                    ws.OnMessage += (sender, args) =>
                    {
                        Assert.True(args.IsText);
                        var data = args.Data;
                        Assert.Equal(sendText.Length, data.Length);
                        Assert.Equal(sendText, data);
                        hasReceive = true;
                    };
                    ws.Connect();
                    SpinWait.SpinUntil(() => hasReceive);
                }
            }
        }


        [Fact]
        public async Task ClientCanSendTextData()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54322/ws";
            const string serverAddress = "http://localhost:54322";

            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            const string textData = "Hello World";

            using (var server =
                NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, async httpContext =>
                {
                    if (httpContext.Request.Path == "/ws")
                    {
                        Assert.True(httpContext.WebSockets.IsWebSocketRequest);
                        var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();

                        var serverBuffer = new byte[1024 * 4];

                        var receiveResult =
                            await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer), CancellationToken.None);

                        while (!receiveResult.CloseStatus.HasValue)
                        {
                            //Assert
                            Assert.True(receiveResult.EndOfMessage);
                            Assert.Equal(WebSocketMessageType.Text, receiveResult.MessageType);
                            var recvString = DataHelper.GetReadableString(serverBuffer);
                            Assert.Equal(textData, recvString);

                            receiveResult =
                                await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer),
                                    CancellationToken.None);
                        }

                        await webSocket.CloseAsync(receiveResult.CloseStatus.Value,
                            receiveResult.CloseStatusDescription,
                            CancellationToken.None);
                    }
                }))
            {
                await server.StartAsync();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    ws.Connect();
                    ws.Send(textData);
                    ws.Close();
                }
            }
        }

        [Fact]
        public async Task ClientCanSendWhatTextIsReceived()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54325/ws";
            const string serverAddress = "http://localhost:54325";

            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            const string textData = "Hello World";
            var serverAction = new Func<HttpContext, Task>(async httpContext =>
            {
                if (httpContext.Request.Path == "/ws")
                {
                    Assert.True(httpContext.WebSockets.IsWebSocketRequest);
                    var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();

                    var serverBuffer = new byte[1024 * 4];

                    await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(textData)),
                        WebSocketMessageType.Text, true, CancellationToken.None);

                    var receiveResult =
                        await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer), CancellationToken.None);
                    Assert.True(receiveResult.EndOfMessage);
                    Assert.Equal(WebSocketMessageType.Text, receiveResult.MessageType);
                    Assert.Equal(textData, DataHelper.GetReadableString(serverBuffer));

                    while (!receiveResult.CloseStatus.HasValue)
                    {
                        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer),
                            CancellationToken.None);
                    }

                    await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription,
                        CancellationToken.None);
                }
            });
            
            //Act & Assert
            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, serverAction))
            {
                await server.StartAsync();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    var hasSent = false;
                    ws.OnMessage += (sender, args) =>
                    {
                        var data = args.Data;
                        Assert.Equal(textData, data);
                        (sender as WebSocketSharp.WebSocket)?.Send(data);
                        hasSent = true;
                    };
                    ws.Connect();
                    SpinWait.SpinUntil(() => hasSent);
                    ws.Close();
                }
            }
        }
        
        private WebSocketSharp.Logger CreateWebSocketLogger()
        {
            var logger = new WebSocketSharp.Logger(WebSocketSharp.LogLevel.Debug, null,
                (logData, _) => { _testOutputHelper.WriteLine(logData.Message); });
            return logger;
        }
    }
}
