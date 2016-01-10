using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Reactive.Concurrency;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Ordering;

namespace System.Net.Mqtt.Flows
{
	internal abstract class PublishFlow : IPublishFlow
	{
		private static readonly ITracer tracer = Tracer.Get<PublishFlow> ();

		protected IPacketDispatcherProvider dispatcherProvider;
		protected readonly IRepository<ClientSession> sessionRepository;
		protected readonly ProtocolConfiguration configuration;

		protected PublishFlow (IPacketDispatcherProvider dispatcherProvider,
			IRepository<ClientSession> sessionRepository, 
			ProtocolConfiguration configuration)
		{
			this.dispatcherProvider = dispatcherProvider;
			this.sessionRepository = sessionRepository;
			this.configuration = configuration;
		}

		public abstract Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel);

		public async Task SendAckAsync (string clientId, IDispatchUnit ack, IChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if((ack.Type == PacketType.PublishReceived || ack.Type == PacketType.PublishRelease) &&
				status == PendingMessageStatus.PendingToSend) {
				this.SavePendingAcknowledgement (ack, clientId);
			}

			if (!channel.IsConnected) {
				return;
			}

			await this.dispatcherProvider
				.Get (clientId)
				.DispatchAsync (ack, channel)
				.FirstOrDefaultAsync(item => item.Unit.PacketId == ack.PacketId);

			if(ack.Type == PacketType.PublishReceived) {
				await this.MonitorAckAsync<PublishRelease> (ack, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			} else if (ack.Type == PacketType.PublishRelease) {
				await this.MonitorAckAsync<PublishComplete> (ack, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			}

			this.dispatcherProvider.Get (clientId).Complete (ack.DispatchId);
		}

		protected void RemovePendingAcknowledgement(string clientId, IDispatchUnit unit, PacketType type)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format(Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingAcknowledgement = session
				.GetPendingAcknowledgements()
				.FirstOrDefault(u => u.Type == type && u.PacketId == unit.PacketId);

			session.RemovePendingAcknowledgement (pendingAcknowledgement);

			this.sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T>(IDispatchUnit sentMessage, string clientId, IChannel<IPacket> channel)
			where T : IIdentifiablePacket
		{
			var intervalSubscription = Observable
				.Interval (TimeSpan.FromSeconds (this.configuration.WaitingTimeoutSecs), NewThreadScheduler.Default)
				.Subscribe (_ => {
					if (channel.IsConnected) {
						tracer.Warn (Properties.Resources.Tracer_PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

						this.dispatcherProvider.Get (clientId).DispatchAsync (sentMessage, channel);
					}
				});
			
			await channel.Receiver
				.ObserveOn (NewThreadScheduler.Default)
				.OfType<T> ()
				.FirstOrDefaultAsync (x => x.PacketId == sentMessage.PacketId);

			intervalSubscription.Dispose ();
		}

		private void SavePendingAcknowledgement(IDispatchUnit ack, string clientId)
		{
			if (ack.Type != PacketType.PublishReceived && ack.Type != PacketType.PublishRelease) {
				return;
			}

			var unacknowledgeMessage = new PendingAcknowledgement {
				PacketId = ack.PacketId,
				Type = ack.Type
			};
			
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			session.AddPendingAcknowledgement (unacknowledgeMessage);

			this.sessionRepository.Update (session);
		}
	}
}
