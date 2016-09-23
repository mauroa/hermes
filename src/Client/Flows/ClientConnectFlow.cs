using System.Threading.Tasks;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Net.Mqtt.Exceptions;

namespace System.Net.Mqtt.Flows
{
	internal class ClientConnectFlow : IProtocolFlow
	{
        readonly IPacketDispatcherProvider dispatcherProvider;
        readonly IRepository<ClientSession> sessionRepository;
		readonly IPublishSenderFlow senderFlow;

		public ClientConnectFlow (IPacketDispatcherProvider dispatcherProvider,
            IRepository<ClientSession> sessionRepository,
			IPublishSenderFlow senderFlow)
		{
            this.dispatcherProvider = dispatcherProvider;
			this.sessionRepository = sessionRepository;
			this.senderFlow = senderFlow;
		}

		public async Task ExecuteAsync (string clientId, IPacket input, IMqttChannel<IPacket> channel)
		{
			if (input.Type != MqttPacketType.ConnectAck) {
				return;
			}

			var ack = input as ConnectAck;

			if (ack.Status != MqttConnectionStatus.Accepted) {
				return;
			}

			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			await SendPendingMessagesAsync (session, channel)
				.ConfigureAwait (continueOnCapturedContext: false);
			await SendPendingAcknowledgementsAsync (session, channel)
				.ConfigureAwait (continueOnCapturedContext: false);
		}

		async Task SendPendingMessagesAsync (ClientSession session, IMqttChannel<IPacket> channel)
		{
			foreach (var pendingMessage in session.GetPendingMessages ()) {
				var publish = new Publish (pendingMessage.Topic, pendingMessage.QualityOfService,
					pendingMessage.Retain, pendingMessage.Duplicated, pendingMessage.PacketId);
                var orderId = dispatcherProvider.GetDispatcher (session.ClientId).CreateOrder (DispatchPacketType.Publish);

                publish.AssignOrder (orderId);

				await senderFlow
					.SendPublishAsync (session.ClientId, publish, channel, PendingMessageStatus.PendingToAcknowledge)
					.ConfigureAwait (continueOnCapturedContext: false);
			}
		}

		async Task SendPendingAcknowledgementsAsync (ClientSession session, IMqttChannel<IPacket> channel)
		{
            foreach (var pendingAcknowledgement in session.GetPendingAcknowledgements ()) {
				var ack = default (IOrderedPacket);

                if (pendingAcknowledgement.Type == MqttPacketType.PublishReceived) {
					ack = new PublishReceived (pendingAcknowledgement.PacketId);
                } else if (pendingAcknowledgement.Type == MqttPacketType.PublishRelease) {
					ack = new PublishRelease (pendingAcknowledgement.PacketId);
                } else {
                    //TODO: Check if no other ack should be analyzed
                    continue;
                }

                var orderId = dispatcherProvider.GetDispatcher (session.ClientId).CreateOrder (ack.Type.ToDispatchPacketType ());

                ack.AssignOrder (orderId);

                await senderFlow.SendAckAsync (session.ClientId, ack, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			}
		}
	}
}
