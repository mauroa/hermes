using System.Collections.Generic;
using System.Net.Mqtt.Packets;
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
            MqttConfiguration configuration) : base (dispatcherProvider, sessionRepository, configuration)
        {
            this.connectionProvider = connectionProvider;
            this.packetIdProvider = packetIdProvider;
        }

        public async Task ForwardPublishAsync (IEnumerable<ClientSubscription> subscriptions, Publish message, bool isWill = false)
        {
            var publishTasks = new List<Task> ();

            foreach (var subscription in subscriptions) {
                var requestedQos = isWill ? message.QualityOfService : subscription.MaximumQualityOfService;
                var supportedQos = configuration.GetSupportedQos (requestedQos);
                var retain = isWill ? message.Retain : false;
                var packetId = supportedQos == MqttQualityOfService.AtMostOnce ? default (ushort) : packetIdProvider.GetPacketId ();
                var subscriptionPublish = new Publish (message.Topic, supportedQos, retain, duplicated: false, packetId: packetId)
                {
                    Payload = message.Payload
                };
                var orderId = dispatcherProvider.GetDispatcher (subscription.ClientId).CreateOrder (DispatchPacketType.Publish);

                subscriptionPublish.AssignOrder (orderId);

                var clientChannel = connectionProvider.GetConnection (subscription.ClientId);

                publishTasks.Add (SendPublishAsync (subscription.ClientId, subscriptionPublish, clientChannel));
            }

            await Task.WhenAll (publishTasks).ConfigureAwait (continueOnCapturedContext: false);
        }
    }
}
