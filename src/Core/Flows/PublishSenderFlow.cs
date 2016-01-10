using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Storage;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Ordering;

namespace System.Net.Mqtt.Flows
{
	internal class PublishSenderFlow : PublishFlow, IPublishSenderFlow
	{
		private static readonly ITracer tracer = Tracer.Get<PublishSenderFlow> ();

		IDictionary<PacketType, Func<string, IDispatchUnit, IDispatchUnit>> senderRules;

		public PublishSenderFlow (IPacketDispatcherProvider dispatcherProvider,
			IRepository<ClientSession> sessionRepository,
			ProtocolConfiguration configuration)
			: base(dispatcherProvider, sessionRepository, configuration)
		{
			this.DefineSenderRules ();
		}

		public override async Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel)
		{
			var senderRule = default (Func<string, IDispatchUnit, IDispatchUnit>);

			if (!this.senderRules.TryGetValue (input.Type, out senderRule)) {
				return;
			}

			var unit = input as IDispatchUnit;

			if (unit == null) {
				return;
			}

			var ackPacket = senderRule (clientId, unit);

			if (ackPacket == default (IDispatchUnit)) {
				this.dispatcherProvider
					.Get (clientId)
					.Complete (unit.DispatchId);
			} else {
				await this.SendAckAsync (clientId, ackPacket, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			}
		}

		public async Task SendPublishAsync (string clientId, Publish message, IChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if (channel == null || !channel.IsConnected) {
				this.SaveMessage (message, clientId, PendingMessageStatus.PendingToSend);
				return;
			}

			var qos = this.configuration.GetSupportedQos(message.QualityOfService);

			if (qos != QualityOfService.AtMostOnce && status == PendingMessageStatus.PendingToSend) {
				this.SaveMessage (message, clientId, PendingMessageStatus.PendingToAcknowledge);
			}

			await this.DispatchAsync (clientId, message, channel)
				.FirstOrDefaultAsync(item => item.Unit.PacketId == message.PacketId);

			if(qos == QualityOfService.AtLeastOnce) {
				await this.MonitorAckAsync<PublishAck> (message, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			} else if (qos == QualityOfService.ExactlyOnce) {
				await this.MonitorAckAsync<PublishReceived> (message, clientId, channel).ConfigureAwait(continueOnCapturedContext: false);
				await channel.Receiver
					.ObserveOn (NewThreadScheduler.Default)
					.OfType<PublishComplete> ()
					.FirstOrDefaultAsync (x => x.PacketId == message.PacketId);
			}

			this.CompleteDispatch (clientId, message);
		}

		protected virtual IObservable<DispatchOrderItem> DispatchAsync(string clientId, Publish message, IChannel<IPacket> channel)
		{
			return this.dispatcherProvider.Get (clientId).DispatchAsync (message, channel);
		}

		protected virtual void CompleteDispatch(string clientId, Publish message)
		{
			this.dispatcherProvider.Get (clientId).Complete (message.DispatchId);
		}

		protected void RemovePendingMessage(string clientId, IDispatchUnit unit)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format(Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingMessage = session
				.GetPendingMessages()
				.FirstOrDefault(p => p.PacketId == unit.PacketId);

			session.RemovePendingMessage (pendingMessage);

			this.sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T>(Publish sentMessage, string clientId, IChannel<IPacket> channel)
			where T : IIdentifiablePacket
		{
			var intervalSubscription = Observable
				.Interval (TimeSpan.FromSeconds (this.configuration.WaitingTimeoutSecs), NewThreadScheduler.Default)
				.Subscribe (_ => {
					if (channel.IsConnected) {
						tracer.Warn (Properties.Resources.Tracer_PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

						var duplicated = new Publish (sentMessage.Topic, sentMessage.QualityOfService,
							sentMessage.Retain, duplicated: true, packetId: sentMessage.PacketId, dispatchId: sentMessage.DispatchId) {
								Payload = sentMessage.Payload
							};

						this.DispatchAsync (clientId, duplicated, channel);
					}
				});
			
			await channel.Receiver
				.ObserveOn (NewThreadScheduler.Default)
				.OfType<T> ()
				.FirstOrDefaultAsync (x => x.PacketId == sentMessage.PacketId);

			intervalSubscription.Dispose ();
		}

		private void DefineSenderRules ()
		{
			this.senderRules = new Dictionary<PacketType, Func<string, IDispatchUnit, IDispatchUnit>> ();

			this.senderRules.Add (PacketType.PublishAck, (clientId, unit) => {
				this.RemovePendingMessage (clientId, unit);

				return default (IDispatchUnit);
			});

			this.senderRules.Add (PacketType.PublishReceived, (clientId, unit) => {
				this.RemovePendingMessage (clientId, unit);

				return new PublishRelease(unit.PacketId, unit.DispatchId);
			});

			this.senderRules.Add (PacketType.PublishComplete, (clientId, unit) => {
				this.RemovePendingAcknowledgement (clientId, unit, PacketType.PublishRelease);

				return default (IDispatchUnit);
			});
		}

		private void SaveMessage(Publish message, string clientId, PendingMessageStatus status)
		{
			if (message.QualityOfService == QualityOfService.AtMostOnce) {
				return;
			}

			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format(Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var savedMessage = new PendingMessage {
				Status = status,
				QualityOfService = message.QualityOfService,
				Duplicated = message.Duplicated,
				Retain = message.Retain,
				Topic = message.Topic,
				PacketId = message.PacketId,
				Payload = message.Payload
			};

			session.AddPendingMessage (savedMessage);

			this.sessionRepository.Update (session);
		}
	}
}
