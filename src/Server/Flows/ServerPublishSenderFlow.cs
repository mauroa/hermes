using System.Collections.Generic;
using System.Net.Mqtt.Ordering;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Server;
using System.Net.Mqtt.Storage;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
	internal class ServerPublishSenderFlow : PublishSenderFlow, IServerPublishSenderFlow
	{
		readonly IConnectionProvider connectionProvider;
		readonly IPacketIdProvider packetIdProvider;

		public ServerPublishSenderFlow (IConnectionProvider connectionProvider, 
			IPacketIdProvider packetIdProvider,
			IPacketDispatcherProvider dispatcherProvider,
			IRepository<ClientSession> sessionRepository,
			ProtocolConfiguration configuration) : base(dispatcherProvider, sessionRepository, configuration)
		{
			this.connectionProvider = connectionProvider;
			this.packetIdProvider = packetIdProvider;
		}

		public async Task ForwardPublishAsync (IEnumerable<ClientSubscription> subscriptions, Publish message, bool isWill = false)
		{
			var publishTasks = new List<Task> ();

			foreach (var subscription in subscriptions) {
				var requestedQos = isWill ? message.QualityOfService : subscription.MaximumQualityOfService;
				var supportedQos = configuration.GetSupportedQos(requestedQos);
				var retain = isWill ? message.Retain : false;
				var packetId = supportedQos == QualityOfService.AtMostOnce ? default(ushort) : this.packetIdProvider.GetPacketId ();
				var subscriptionPublish = new Publish (message.Topic, supportedQos, retain, duplicated: false, packetId: packetId, dispatchId: message.DispatchId) {
					Payload = message.Payload
				};
				var clientChannel = this.connectionProvider.GetConnection (subscription.ClientId);

				publishTasks.Add (this.SendPublishAsync (subscription.ClientId, subscriptionPublish, clientChannel));
			}

			await Task.WhenAll (publishTasks).ConfigureAwait (continueOnCapturedContext: false);

			this.dispatcherProvider.Get ().Complete (message.DispatchId);
		}

		protected override IObservable<DispatchOrderItem> DispatchAsync (string clientId, Publish message, IChannel<IPacket> channel)
		{
			return this.dispatcherProvider.Get ().DispatchAsync (message, channel);
		}

		protected override void CompleteDispatch (string clientId, Publish message)
		{
			//TODO: We do nothing here because we want to remove the message 
			//from the queue only when it has been forwarded to all subscribers
		}
	}
}
