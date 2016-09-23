using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
    internal class ServerSubscribeFlow : IProtocolFlow
	{
		static readonly ITracer tracer = Tracer.Get<ServerSubscribeFlow> ();
		
		readonly IPublishSenderFlow senderFlow;
        readonly IPacketDispatcherProvider dispatcherProvider;
        readonly IPacketIdProvider packetIdProvider;
        readonly IMqttTopicEvaluator topicEvaluator;
        readonly IRepository<ClientSession> sessionRepository;
        readonly IRepository<RetainedMessage> retainedRepository;
        readonly MqttConfiguration configuration;

		public ServerSubscribeFlow (IPublishSenderFlow senderFlow, 
            IPacketDispatcherProvider dispatcherProvider,
			IPacketIdProvider packetIdProvider,
            IMqttTopicEvaluator topicEvaluator,
            IRepository<ClientSession> sessionRepository,
            IRepository<RetainedMessage> retainedRepository,
            MqttConfiguration configuration)
		{
			this.senderFlow = senderFlow;
            this.dispatcherProvider = dispatcherProvider;
            this.packetIdProvider = packetIdProvider;
            this.topicEvaluator = topicEvaluator;
            this.sessionRepository = sessionRepository;
            this.retainedRepository = retainedRepository;
            this.configuration = configuration;
		}

		public async Task ExecuteAsync (string clientId, IPacket input, IMqttChannel<IPacket> channel)
		{
			if (input.Type != MqttPacketType.Subscribe) {
				return;
			}

			var subscribe = input as Subscribe;
			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format(Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var returnCodes = new List<SubscribeReturnCode> ();

			foreach (var subscription in subscribe.Subscriptions) {
				try {
					if (!topicEvaluator.IsValidTopicFilter (subscription.TopicFilter)) {
						tracer.Error (Server.Properties.Resources.ServerSubscribeFlow_InvalidTopicSubscription, subscription.TopicFilter, clientId);

						returnCodes.Add (SubscribeReturnCode.Failure);
						continue;
					}

					var clientSubscription = session
						.GetSubscriptions()
						.FirstOrDefault(s => s.TopicFilter == subscription.TopicFilter);

					if (clientSubscription != null) {
						clientSubscription.MaximumQualityOfService = subscription.MaximumQualityOfService;
					} else {
						clientSubscription = new ClientSubscription {
							ClientId = clientId,
							TopicFilter = subscription.TopicFilter,
							MaximumQualityOfService = subscription.MaximumQualityOfService
						};

						session.AddSubscription (clientSubscription);
					}

					await SendRetainedMessagesAsync (clientSubscription, channel)
						.ConfigureAwait (continueOnCapturedContext: false);

					var supportedQos = configuration.GetSupportedQos(subscription.MaximumQualityOfService);
					var returnCode = supportedQos.ToReturnCode ();

					returnCodes.Add (returnCode);
				} catch (MqttRepositoryException repoEx) {
					tracer.Error (repoEx, Server.Properties.Resources.ServerSubscribeFlow_ErrorOnSubscription, clientId, subscription.TopicFilter);

					returnCodes.Add (SubscribeReturnCode.Failure);
				}
			}

			sessionRepository.Update (session);

			await channel.SendAsync (new SubscribeAck (subscribe.PacketId, returnCodes.ToArray ()))
				.ConfigureAwait (continueOnCapturedContext: false);
		}

		async Task SendRetainedMessagesAsync (ClientSubscription subscription, IMqttChannel<IPacket> channel)
		{
			var retainedMessages = retainedRepository
                .GetAll ()
				.Where (r => topicEvaluator.Matches (r.Topic, subscription.TopicFilter));

            if (retainedMessages == null) {
                return;
            }

			foreach (var retainedMessage in retainedMessages) {
				var packetId = subscription.MaximumQualityOfService == MqttQualityOfService.AtMostOnce ?
					default (ushort) : packetIdProvider.GetPacketId ();
				var publish = new Publish (retainedMessage.Topic, subscription.MaximumQualityOfService,
					retain: true, duplicated: false, packetId: packetId) {
					Payload = retainedMessage.Payload
				};
                var orderId = dispatcherProvider.GetDispatcher (subscription.ClientId).CreateOrder (DispatchPacketType.Publish);

                publish.AssignOrder (orderId);

				await senderFlow.SendPublishAsync (subscription.ClientId, publish, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			}
		}
	}
}
