using System.Linq;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
    internal class PublishReceiverFlow : PublishFlow
	{
		protected readonly IMqttTopicEvaluator topicEvaluator;
		protected readonly IRepository<RetainedMessage> retainedRepository;

		public PublishReceiverFlow (IMqttTopicEvaluator topicEvaluator,
            IPacketDispatcherProvider dispatcherProvider,
            IRepository<RetainedMessage> retainedRepository,
			IRepository<ClientSession> sessionRepository,
			MqttConfiguration configuration)
			: base (dispatcherProvider, sessionRepository, configuration)
		{
			this.topicEvaluator = topicEvaluator;
			this.retainedRepository = retainedRepository;
		}

		public override async Task ExecuteAsync (string clientId, IPacket input, IMqttChannel<IPacket> channel)
		{
			if (input.Type == MqttPacketType.Publish) {
				var publish = input as Publish;

				await HandlePublishAsync (clientId, publish, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			} else if (input.Type == MqttPacketType.PublishRelease) {
				var publishRelease = input as PublishRelease;

				await HandlePublishReleaseAsync (clientId, publishRelease, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			}
		}

		protected virtual Task ProcessPublishAsync (Publish publish, string clientId)
		{
			return Task.Delay (0);
		}

        protected virtual void Validate (Publish publish, string clientId)
        {
            if (publish.QualityOfService != MqttQualityOfService.AtMostOnce && !publish.HasPacketId ())
            {
                throw new MqttException(Properties.Resources.PublishReceiverFlow_PacketIdRequired);
            }

            if (publish.QualityOfService == MqttQualityOfService.AtMostOnce && publish.HasPacketId ())
            {
                throw new MqttException(Properties.Resources.PublishReceiverFlow_PacketIdNotAllowed);
            }
        }

        async Task HandlePublishAsync (string clientId, Publish publish, IMqttChannel<IPacket> channel)
		{
            Validate (publish, clientId);

			var qos = configuration.GetSupportedQos (publish.QualityOfService);
			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			if (qos == MqttQualityOfService.ExactlyOnce && 
                session
                    .GetPendingAcknowledgements ()
                    .Any (ack => ack.Type == MqttPacketType.PublishReceived && ack.PacketId == publish.PacketId)) {
				await SendQosAck (clientId, qos, publish, channel)
					.ConfigureAwait (continueOnCapturedContext: false);

				return;
			}

			await SendQosAck (clientId, qos, publish, channel)
				.ConfigureAwait (continueOnCapturedContext: false);
			await ProcessPublishAsync (publish, clientId)
				.ConfigureAwait (continueOnCapturedContext: false);
		}

		async Task HandlePublishReleaseAsync (string clientId, PublishRelease publishRelease, IMqttChannel<IPacket> channel)
		{
			RemovePendingAcknowledgement (clientId, publishRelease, MqttPacketType.PublishReceived);

            var publishComplete = new PublishComplete (publishRelease.PacketId);

            publishComplete.AssignOrder (publishRelease.OrderId);

            await SendAckAsync (clientId, publishComplete, channel)
				.ConfigureAwait (continueOnCapturedContext: false);
		}

		async Task SendQosAck (string clientId, MqttQualityOfService qos, Publish publish, IMqttChannel<IPacket> channel)
		{
			if (qos == MqttQualityOfService.AtMostOnce) {
                dispatcherProvider.GetDispatcher (clientId).CompleteOrder (DispatchPacketType.PublishAck1, publish.OrderId);

                return;
			}

            var ack = default (IOrderedPacket);

            if (qos == MqttQualityOfService.AtLeastOnce) {
                ack = new PublishAck (publish.PacketId);
			} else {
                ack = new PublishReceived (publish.PacketId);
			}

            ack.AssignOrder (publish.OrderId);

            await SendAckAsync (clientId, ack, channel)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
	}
}
