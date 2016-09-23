using System.Collections.Generic;
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
    internal class PublishSenderFlow : PublishFlow, IPublishSenderFlow
	{
		static readonly ITracer tracer = Tracer.Get<PublishSenderFlow> ();

		IDictionary<MqttPacketType, Func<string, IOrderedPacket, IOrderedPacket>> ackRules;

		public PublishSenderFlow (IPacketDispatcherProvider dispatcherProvider,
            IRepository<ClientSession> sessionRepository,
			MqttConfiguration configuration)
			: base (dispatcherProvider, sessionRepository, configuration)
		{
			DefineAckRules ();
		}

		public override async Task ExecuteAsync (string clientId, IPacket input, IMqttChannel<IPacket> channel)
		{
			var ackRule = default (Func<string, IOrderedPacket, IOrderedPacket>);

			if (!ackRules.TryGetValue (input.Type, out ackRule)) {
				return;
			}

			var orderedPacket = input as IOrderedPacket;

			if (orderedPacket == null) {
				return;
			}

			var ackPacket = ackRule (clientId, orderedPacket);

            if (ackPacket != default (IIdentifiablePacket)) {
                await SendAckAsync (clientId, ackPacket, channel)
                    .ConfigureAwait (continueOnCapturedContext: false);
            }
		}

		public async Task SendPublishAsync (string clientId, Publish message, IMqttChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if (channel == null || !channel.IsConnected) {
				SaveMessage (message, clientId, PendingMessageStatus.PendingToSend);
				return;
			}

			var qos = configuration.GetSupportedQos (message.QualityOfService);

			if (qos != MqttQualityOfService.AtMostOnce && status == PendingMessageStatus.PendingToSend) {
				SaveMessage (message, clientId, PendingMessageStatus.PendingToAcknowledge);
			}

            await dispatcherProvider
                .GetDispatcher (clientId)
                .DispatchAsync (message, channel)
                .ConfigureAwait (continueOnCapturedContext: false);

			if (qos == MqttQualityOfService.AtLeastOnce) {
				await MonitorAckAsync<PublishAck> (message, clientId, channel)
					.ConfigureAwait (continueOnCapturedContext: false);

                dispatcherProvider.GetDispatcher (clientId).CompleteOrder (DispatchPacketType.Publish, message.OrderId);
            } else if (qos == MqttQualityOfService.ExactlyOnce) {
				await MonitorAckAsync<PublishReceived> (message, clientId, channel)
                    .ConfigureAwait (continueOnCapturedContext: false);

                dispatcherProvider.GetDispatcher (clientId).CompleteOrder (DispatchPacketType.Publish, message.OrderId);

                await channel
                    .ReceiverStream
					.ObserveOn (NewThreadScheduler.Default)
					.OfType<PublishComplete> ()
					.FirstOrDefaultAsync (x => x.PacketId == message.PacketId);
			}
        }

        protected void RemovePendingMessage (string clientId, IIdentifiablePacket packet)
		{
			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingMessage = session
				.GetPendingMessages()
				.FirstOrDefault(p => p.PacketId == packet.PacketId);

			session.RemovePendingMessage (pendingMessage);

			sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T> (Publish sentMessage, string clientId, IMqttChannel<IPacket> channel)
			where T : IIdentifiablePacket
		{
			var intervalSubscription = Observable
				.Interval (TimeSpan.FromSeconds (configuration.WaitTimeoutSecs), NewThreadScheduler.Default)
				.Subscribe (async _ => {
					if (channel.IsConnected) {
						tracer.Warn (Properties.Resources.PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

						var duplicated = new Publish (sentMessage.Topic, sentMessage.QualityOfService,
							sentMessage.Retain, duplicated: true, packetId: sentMessage.PacketId) {
							Payload = sentMessage.Payload
						};

                        duplicated.AssignOrder (sentMessage.OrderId);

                        await dispatcherProvider
                            .GetDispatcher (clientId)
                            .DispatchAsync (duplicated, channel)
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

		void DefineAckRules ()
		{
			ackRules = new Dictionary<MqttPacketType, Func<string, IOrderedPacket, IOrderedPacket>> ();

			ackRules.Add (MqttPacketType.PublishAck, (clientId, packet) => {
				RemovePendingMessage (clientId, packet);

				return default (IOrderedPacket);
			});

			ackRules.Add (MqttPacketType.PublishReceived, (clientId, packet) => {
				RemovePendingMessage (clientId, packet);

				var result = new PublishRelease (packet.PacketId);

                result.AssignOrder (packet.OrderId);

                return result;
			});

			ackRules.Add (MqttPacketType.PublishComplete, (clientId, packet) => {
				RemovePendingAcknowledgement (clientId, packet, MqttPacketType.PublishRelease);

				return default (IOrderedPacket);
			});
		}

		void SaveMessage (Publish message, string clientId, PendingMessageStatus status)
		{
			if (message.QualityOfService == MqttQualityOfService.AtMostOnce) {
				return;
			}

			var session = sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new MqttException (string.Format (Properties.Resources.SessionRepository_ClientSessionNotFound, clientId));
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

			sessionRepository.Update (session);
		}
	}
}
