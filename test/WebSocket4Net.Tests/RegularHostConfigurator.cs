using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperSocket;
using SuperSocket.Client;
using SuperSocket.ProtoBase;

namespace WebSocket4Net.Tests
{
    public class RegularHostConfigurator : IHostConfigurator
    {
        public string WebSocketSchema => "ws";

        public bool IsSecure => false;

        public ListenOptions Listener { get; private set; }

        public void Configure(HostBuilderContext context, IServiceCollection services)
        {
            services.Configure<ServerOptions>((options) =>
                {
                    var listener = options.Listeners[0];
                    Listener = listener;
                });
        }

        public IEasyClient<TPackageInfo> ConfigureEasyClient<TPackageInfo>(IEasyClient<TPackageInfo> client) where TPackageInfo : class
        {
            return client;
        }
    }
}