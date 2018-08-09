using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using NetCoreClientTest.Utils;
using Xunit;
using Xunit.Abstractions;

namespace NetCoreClientTest
{
    public class ConnectionRedirectTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ConnectionRedirectTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task TestHttp302Redirect()
        {
            var originData = Encoding.UTF8.GetBytes("Hello World");
            const string serverOrigin = "localhost:54399";
            string serverOriginAddress = $"http://{serverOrigin}";
            const string serverReal = "localhost:54400";
            string serverRealAddress = $"http://{serverReal}";
            string clientOriginAddress = $"ws://{serverOrigin}/ws";
            string clientRealAddress = $"ws://{serverReal}/ws_new";

            var serverTestComplte = false;

            var configForOrigin = NetCoreWebSocketHelper.CreateConfigWithUrl(serverOriginAddress);
            var configForReal = NetCoreWebSocketHelper.CreateConfigWithUrl(serverRealAddress);

            var originServerAction = new Func<HttpContext, Task>(async context =>
            {
                if (context.Request.Path == "/ws")
                {
                    Assert.True(context.WebSockets.IsWebSocketRequest);
                    context.Response.Redirect(clientRealAddress);
                }
            });

            var realServerAction = new Func<HttpContext, Task>(async context =>
            {
                if (context.Request.Path == "/ws_new")
                {
                    Assert.True(context.WebSockets.IsWebSocketRequest);
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync().OrTimeout();

                    var serverBuffer = new byte[originData.Length];
                    var receiveResult =
                        await webSocket.ReceiveAsync(new ArraySegment<byte>(serverBuffer), CancellationToken.None)
                            .OrTimeout();

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
            using (IWebHost originServer =
                    NetCoreWebSocketHelper.CreateTestServer(configForOrigin, _testOutputHelper, originServerAction),
                realServer =
                    NetCoreWebSocketHelper.CreateTestServer(configForReal, _testOutputHelper, realServerAction))
            {
                await originServer.StartAsync().OrTimeout();
                await realServer.StartAsync().OrTimeout();

                using (var ws = new WebSocketSharp.WebSocket(clientOriginAddress, CreateWebSocketLogger()))
                {
                    ws.EnableRedirection = true;
                    ws.Connect();
                    Assert.Equal(clientRealAddress, ws.Url.OriginalString);
                    ws.Send(originData);
                    ws.Close();
                }
            }

            Assert.True(serverTestComplte, "server assertion fail");
        }


        private WebSocketSharp.Logger CreateWebSocketLogger()
        {
            var logger = new WebSocketSharp.Logger(WebSocketSharp.LogLevel.Debug, null,
                (logData, _) => { _testOutputHelper.WriteLine(logData.Message); });
            return logger;
        }
    }
}