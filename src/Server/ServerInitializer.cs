using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Hermes.Flows;
using Hermes.Storage;

namespace Hermes
{
	public class ServerInitializer : IInitalizer<Server>
	{
		public Server Initialize(ProtocolConfiguration configuration)
		{
			var logger = new Logger ();

			var listener = new TcpListener(IPAddress.Any, configuration.Port);

			listener.Start ();

			var socketProvider = Observable
				.FromAsync (() => {
					return Task.Factory.FromAsync<TcpClient> (listener.BeginAcceptTcpClient, 
						listener.EndAcceptTcpClient, TaskCreationOptions.AttachedToParent);
				})
				.Repeat ()
				.Select (client => new TcpChannel (client, new PacketBuffer (), configuration));

			var topicEvaluator = new TopicEvaluator (configuration);
			var channelFactory = new PacketChannelFactory (topicEvaluator);
			var repositoryProvider = new InMemoryRepositoryProvider ();
			var connectionProvider = new ConnectionProvider ();
			var flowProvider = new ServerProtocolFlowProvider (connectionProvider, topicEvaluator, repositoryProvider, configuration, logger);
			var channelAdapter = new ServerPacketChannelAdapter (connectionProvider, flowProvider, configuration, logger);

			return new Server (socketProvider, channelFactory, channelAdapter, connectionProvider, configuration, logger);
		}
	}
}
