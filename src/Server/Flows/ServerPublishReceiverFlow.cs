using System.Diagnostics;
using System.Linq;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using ServerProperties = System.Net.Mqtt.Server.Properties;

namespace System.Net.Mqtt.Flows
{
    internal class ServerPublishReceiverFlow : PublishReceiverFlow, IServerPublishReceiverFlow
    {
		static readonly ITracer tracer = Tracer.Get<ServerPublishReceiverFlow> ();

		readonly IConnectionProvider connectionProvider;
		readonly IServerPublishSenderFlow senderFlow;
		readonly IRepository<ConnectionWill> willRepository;
		readonly IPacketIdProvider packetIdProvider;
		readonly ISubject<MqttUndeliveredMessage> undeliveredMessagesListener;

		public ServerPublishReceiverFlow (IMqttTopicEvaluator topicEvaluator,
            IConnectionProvider connectionProvider,
            IPacketDispatcherProvider dispatcherProvider,
            IServerPublishSenderFlow senderFlow,
			IRepository<RetainedMessage> retainedRepository,
			IRepository<ClientSession> sessionRepository,
			IRepository<ConnectionWill> willRepository,
			IPacketIdProvider packetIdProvider,
            ISubject<MqttUndeliveredMessage> undeliveredMessagesListener,
			MqttConfiguration configuration)
			: base (topicEvaluator, dispatcherProvider, retainedRepository, sessionRepository, configuration)
		{
			this.connectionProvider = connectionProvider;
			this.senderFlow = senderFlow;
			this.willRepository = willRepository;
			this.packetIdProvider = packetIdProvider;
			this.undeliveredMessagesListener = undeliveredMessagesListener;
		}

        public async Task SendWillAsync (string clientId)
        {
            var will = willRepository.Get (w => w.ClientId == clientId);

            if (will != null && will.Will != null)
            {
                var willPublish = new Publish (will.Will.Topic, will.Will.QualityOfService, will.Will.Retain, duplicated: false)
                {
                    Payload = Encoding.UTF8.GetBytes (will.Will.Message)
                };

                tracer.Info (ServerProperties.Resources.ServerPublishReceiverFlow_SendingWill, clientId, willPublish.Topic);

                await ForwardPublishAsync (willPublish, clientId, isWill: true)
                    .ConfigureAwait (continueOnCapturedContext: false);
            }
        }

        protected override async Task ProcessPublishAsync (Publish publish, string clientId)
		{
			if (publish.Retain) {
				var existingRetainedMessage = retainedRepository.Get (r => r.Topic == publish.Topic);

				if (existingRetainedMessage != null) {
					retainedRepository.Delete (existingRetainedMessage);
				}

				if (publish.Payload.Length > 0) {
					var retainedMessage = new RetainedMessage {
						Topic = publish.Topic,
						QualityOfService = publish.QualityOfService,
						Payload = publish.Payload
					};

					retainedRepository.Create (retainedMessage);
				}
			}

			await ForwardPublishAsync (publish, clientId)
				.ConfigureAwait (continueOnCapturedContext: false);
		}

        protected override void Validate (Publish publish, string clientId)
        {
            base.Validate (publish, clientId);

            if (publish.Topic.Trim ().StartsWith ("$") && !connectionProvider.PrivateClients.Contains (clientId))
            {
                throw new MqttException (ServerProperties.Resources.ServerPublishReceiverFlow_SystemMessageNotAllowedForClient);
            }
        }

		async Task ForwardPublishAsync (Publish publish, string clientId, bool isWill = false)
		{
			var subscriptions = sessionRepository
				.GetAll ().ToList ()
				.SelectMany (s => s.GetSubscriptions ())
				.Where (x => topicEvaluator.Matches (publish.Topic, x.TopicFilter));

			if (subscriptions.Any ()) {
                await senderFlow
                    .ForwardPublishAsync (subscriptions, publish, isWill)
                    .ConfigureAwait (continueOnCapturedContext: false);
            } else {
                tracer.Verbose (ServerProperties.Resources.ServerPublishReceiverFlow_TopicNotSubscribed, publish.Topic, clientId);

                undeliveredMessagesListener.OnNext (new MqttUndeliveredMessage { SenderId = clientId, Message = new MqttApplicationMessage (publish.Topic, publish.Payload) });
            }
		}
	}
}
