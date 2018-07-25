using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NetCoreClientTest.Utils;
using WebSocketSharp;
using Xunit;
using Xunit.Abstractions;

namespace NetCoreClientTest
{
    public class HttpsConnectionTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public HttpsConnectionTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task ClientCanSendModifiedTextWhenReceivedFromServer()
        {
            //Arrange
            const string clientAddress = "wss://localhost:54321/ws";
            const string serverAddress = "https://localhost:54321";

            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            const string textData = "Hello World";
            var serverTestPassed = false;
            var clientTestPassed = false;

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

                    var serverRecv = $"{textData} from Https client";
                    Assert.Equal(serverRecv, DataHelper.GetReadableString(serverBuffer));

                    while (!receiveResult.CloseStatus.HasValue)
                    {
                        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer),
                            CancellationToken.None);
                    }

                    await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription,
                        CancellationToken.None);

                    serverTestPassed = true;
                }
            });

            //Act & Assert
            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, serverAction, true))
            {
                await server.StartAsync();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    var hasComplete = false;
                    
                    ws.OnMessage += (sender, args) =>
                    {
                        Assert.True(args.IsText,"websocket should be text mode");
                        var data = args.Data;
                        Assert.Equal(textData, data);
                        var clientSend = $"{data} from Https client";
                        (sender as WebSocketSharp.WebSocket)?.Send(clientSend);
                         hasComplete = true;
                    };
                    ws.Connect();
                    SpinWait.SpinUntil(() =>  hasComplete, new TimeSpan(0, 0, 5));
                    Assert.True(hasComplete, "client timeout");
                    ws.Close();
                    clientTestPassed = true;
                }
            }

            Assert.True(serverTestPassed);
            Assert.True(clientTestPassed);
        }

        [Fact]
        public async Task ClientCannotUseWrongProtolToConnectServer()
        {
            //Arrange
            const string clientAddress = "ws://localhost:54322/ws";
            const string serverAddress = "https://localhost:54322";

            var config = NetCoreWebSocketHelper.CreateConfigWithUrl(serverAddress);
            const string textData = "Hello World";
            var clientTestPassed = false;

            var serverAction = new Func<HttpContext, Task>(async httpContext =>
            {
                if (httpContext.Request.Path == "/ws")
                {
                    throw new Exception("Should not be able to create web socket connection successfully");
                }
            });
            
            var logger = new Logger(LogLevel.Debug,null,(logData, _) =>
            {
                var msg = logData.Message;
                _testOutputHelper.WriteLine(msg);
                if ("An exception has occurred while reading an HTTP request/response.".Equals(msg))
                {
                    clientTestPassed = true;
                }
            });
            
            //Act 
            using (var server = NetCoreWebSocketHelper.CreateTestServer(config, _testOutputHelper, serverAction, true))
            {
                await server.StartAsync();
                using (var ws = new WebSocketSharp.WebSocket(clientAddress))
                {
                    ws.Log = logger;
                    //ws.Log.Level = LogLevel.Debug;
                    var testCompleted = false;
                    ws.OnError += (sender, args) =>
                    {
                        testCompleted = true;
                        Assert.True(args.Exception != null);
                    };

                    ws.OnOpen += (sender, args) =>
                    {
                        testCompleted = true;
                        Assert.True(false, "The connection doesn't use wss url should failed!");
                    };
                    ws.Connect();
                    SpinWait.SpinUntil(() => testCompleted, new TimeSpan(0,0,5));
                }
            }
            
            Assert.True(clientTestPassed);
        }
    }
}