﻿using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Reactive.Concurrency;
using System.Net.Mqtt.Exceptions;

namespace System.Net.Mqtt.Flows
{
	internal abstract class PublishFlow : IPublishFlow
	{
		private static readonly ITracer tracer = Tracer.Get<PublishFlow> ();

		protected readonly IRepository<ClientSession> sessionRepository;
		protected readonly ProtocolConfiguration configuration;

		protected PublishFlow (IRepository<ClientSession> sessionRepository, 
			ProtocolConfiguration configuration)
		{
			this.sessionRepository = sessionRepository;
			this.configuration = configuration;
		}

		public abstract Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel);

		public async Task SendAckAsync (string clientId, IIdentifiablePacket ack, IChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if((ack.Type == PacketType.PublishReceived || ack.Type == PacketType.PublishRelease) &&
				status == PendingMessageStatus.PendingToSend) {
				this.SavePendingAcknowledgement (ack, clientId);
			}

			if (!channel.IsConnected) {
				return;
			}

			await channel.SendAsync (ack)
				.ConfigureAwait(continueOnCapturedContext: false);

			if(ack.Type == PacketType.PublishReceived) {
				await this.MonitorAckAsync<PublishRelease> (ack, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			} else if (ack.Type == PacketType.PublishRelease) {
				await this.MonitorAckAsync<PublishComplete> (ack, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			}
		}

		protected void RemovePendingAcknowledgement(string clientId, ushort packetId, PacketType type)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format(Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingAcknowledgement = session
				.GetPendingAcknowledgements()
				.FirstOrDefault(u => u.Type == type && u.PacketId == packetId);

			session.RemovePendingAcknowledgement (pendingAcknowledgement);

			this.sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T>(IIdentifiablePacket sentMessage, string clientId, IChannel<IPacket> channel)
			where T : IIdentifiablePacket
		{
			var intervalSubscription = Observable
				.Interval (TimeSpan.FromSeconds (this.configuration.WaitingTimeoutSecs), NewThreadScheduler.Default)
				.Subscribe (async _ => {
					if (channel.IsConnected) {
						tracer.Warn (Properties.Resources.Tracer_PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

						await channel.SendAsync (sentMessage);
					}
				});
			
			await channel.Receiver
				.ObserveOn (NewThreadScheduler.Default)
				.OfType<T> ()
				.FirstOrDefaultAsync (x => x.PacketId == sentMessage.PacketId);

			intervalSubscription.Dispose ();
		}

		private void SavePendingAcknowledgement(IIdentifiablePacket ack, string clientId)
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
