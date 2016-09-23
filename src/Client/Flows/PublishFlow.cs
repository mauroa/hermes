using System.Diagnostics;
using System.Linq;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace System.Net.Mqtt.Flows
{
    internal abstract class PublishFlow : IPublishFlow
	{
		static readonly ITracer tracer = Tracer.Get<PublishFlow> ();

        protected readonly IPacketDispatcherProvider dispatcherProvider;
        protected readonly IRepository<ClientSession> sessionRepository;
		protected readonly MqttConfiguration configuration;

		protected PublishFlow (IPacketDispatcherProvider dispatcherProvider, 
            IRepository<ClientSession> sessionRepository,
			MqttConfiguration configuration)
		{
            this.dispatcherProvider = dispatcherProvider;
			this.sessionRepository = sessionRepository;
			this.configuration = configuration;
		}

		public abstract Task ExecuteAsync (string clientId, IPacket input, IMqttChannel<IPacket> channel);

		public async Task SendAckAsync (string clientId, IOrderedPacket ack, IMqttChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if ((ack.Type == MqttPacketType.PublishReceived || ack.Type == MqttPacketType.PublishRelease) &&
				status == PendingMessageStatus.PendingToSend) {
				SavePendingAcknowledgement (ack, clientId);
			}

			if (!channel.IsConnected) {
				return;
			}

            await dispatcherProvider
                .GetDispatcher (clientId)
                .DispatchAsync (ack, channel)
                .ConfigureAwait (continueOnCapturedContext: false);

			if (ack.Type == MqttPacketType.PublishReceived) {
				await MonitorAckAsync<PublishRelease> (ack, clientId, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			} else if (ack.Type == MqttPacketType.PublishRelease) {
				await MonitorAckAsync<PublishComplete> (ack, clientId, channel)
					.ConfigureAwait (continueOnCapturedContext: false);
			}

            dispatcherProvider.GetDispatcher (clientId).CompleteOrder (ack.Type.ToDispatchPacketType (), ack.OrderId);
		}

		protected void RemovePendingAcknowledgement (string clientId, IIdentifiablePacket packet, MqttPacketType type)
		{
			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingAcknowledgement = session
				.GetPendingAcknowledgements ()
				.FirstOrDefault (u => u.Type == type && u.PacketId == packet.PacketId);

			session.RemovePendingAcknowledgement (pendingAcknowledgement);

			sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T> (IOrderedPacket sentMessage, string clientId, IMqttChannel<IPacket> channel)
			where T : IIdentifiablePacket
		{
			var intervalSubscription = Observable
				.Interval (TimeSpan.FromSeconds (configuration.WaitTimeoutSecs), NewThreadScheduler.Default)
				.Subscribe (async _ => {
					if (channel.IsConnected) {
						tracer.Warn (Properties.Resources.PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

                        await dispatcherProvider
                            .GetDispatcher (clientId)
                            .DispatchAsync (sentMessage, channel)
                            .ConfigureAwait (continueOnCapturedContext: false);
					}
				});

			await channel
                .ReceiverStream
				.ObserveOn (NewThreadScheduler.Default)
				.OfType<T> ()
				.FirstOrDefaultAsync (x => x.PacketId == sentMessage.PacketId);

			intervalSubscription.Dispose ();
		}

		void SavePendingAcknowledgement (IIdentifiablePacket ack, string clientId)
		{
			if (ack.Type != MqttPacketType.PublishReceived && ack.Type != MqttPacketType.PublishRelease) {
				return;
			}

			var unacknowledgeMessage = new PendingAcknowledgement {
				PacketId = ack.PacketId,
				Type = ack.Type
			};

			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			session.AddPendingAcknowledgement (unacknowledgeMessage);

			sessionRepository.Update (session);
		}
	}
}
