using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace NetCoreClientTest.Utils
{
    public static class NetCoreWebSocketHelper
    {
        public static IWebHost CreateTestServer(IConfiguration config, ITestOutputHelper testOutputHelper, Func<HttpContext, Task> app, bool forceHttps = false)
        {
            Action<IApplicationBuilder> startup = builder =>
            {
                if (forceHttps)
                {
                    builder.UseHttpsRedirection();
                    //builder.UseHsts();
                }
                builder.Use(async (context, next) =>
                {
                    try
                    {
                        // Kestrel does not return proper error responses:
                        // https://github.com/aspnet/KestrelHttpServer/issues/43
                        await next();
                    }
                    catch (Exception ex)
                    {
                        if (context.Response.HasStarted)
                        {
                            throw;
                        }

                        context.Response.StatusCode = 500;
                        context.Response.Headers.Clear();
                        await context.Response.WriteAsync(ex.ToString());
                    }
                });
                builder.UseWebSockets();
                builder.Run(httpContext => app(httpContext));
            };
            
            var host = new WebHostBuilder()
                .ConfigureServices(s => s.AddSingleton(CreateLogFactory(testOutputHelper)))
                .UseConfiguration(config)
                .UseKestrel()
                .Configure(startup)
                .Build();

            return host;
        }

        public static IConfiguration CreateConfigWithUrl(string url)
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection();
            var config = configBuilder.Build();
            config["server.urls"] = url;
            return config;
        }

        private static ILoggerFactory CreateLogFactory(ITestOutputHelper testOutputHelper)
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new XunitLoggerProvider(testOutputHelper));
            return loggerFactory;
        }
    }
}
