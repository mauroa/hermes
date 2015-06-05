﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Timers;
using Hermes.Diagnostics;
using Hermes.Packets;
using Hermes.Properties;
using Hermes.Storage;
using System.Reactive.Threading.Tasks;

namespace Hermes.Flows
{
	public class PublishSenderFlow : PublishFlow, IPublishSenderFlow
	{
		private static readonly ITracer tracer = Tracer.Get<PublishSenderFlow> ();

		readonly IPublishDispatcher dispatcher;
		IDictionary<PacketType, Func<string, ushort, IFlowPacket>> senderRules;

		public PublishSenderFlow (IPublishDispatcher publishDispatcher,
			IRepository<ClientSession> sessionRepository,
			ProtocolConfiguration configuration)
			: base(sessionRepository, configuration)
		{
			this.dispatcher = publishDispatcher;

			this.DefineSenderRules ();
		}

		public override async Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel)
		{
			var senderRule = default (Func<string, ushort, IFlowPacket>);

			if (!this.senderRules.TryGetValue (input.Type, out senderRule)) {
				return;
			}

			var flowPacket = input as IFlowPacket;

			if (flowPacket == null) {
				return;
			}

			var ackPacket = senderRule (clientId, flowPacket.PacketId);

			if (ackPacket != default(IFlowPacket)) {
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

			this.dispatcher.Dispatch (message, channel);

			if(qos == QualityOfService.AtLeastOnce) {
				await this.MonitorAckAsync<PublishAck> (message, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			} else if (qos == QualityOfService.ExactlyOnce) {
				await this.MonitorAckAsync<PublishReceived> (message, clientId, channel)
					.ConfigureAwait(continueOnCapturedContext: false);
			}
		}

		protected void RemovePendingMessage(string clientId, ushort packetId)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new ProtocolException (string.Format(Resources.SessionRepository_ClientSessionNotFound, clientId));
			}

			var pendingMessage = session.PendingMessages.FirstOrDefault(p => p.PacketId.HasValue 
				&& p.PacketId.Value == packetId);

			session.PendingMessages.Remove (pendingMessage);

			this.sessionRepository.Update (session);
		}

		protected async Task MonitorAckAsync<T>(Publish sentMessage, string clientId, IChannel<IPacket> channel)
			where T : IFlowPacket
		{
			var ackSubject = new Subject<T> ();
			var retries = 0;
			var qosTimer = new Timer();

			qosTimer.AutoReset = true;
			qosTimer.Interval = this.configuration.WaitingTimeoutSecs * 1000;
			qosTimer.Elapsed += async (sender, e) => {
				if (retries == this.configuration.QualityOfServiceAckRetries) {
					ackSubject.OnError (new ProtocolException (string.Format (Resources.PublishFlow_AckMonitor_ExceededMaximumAckRetries, this.configuration.QualityOfServiceAckRetries)));
				}

				tracer.Warn (Resources.Tracer_PublishFlow_RetryingQoSFlow, sentMessage.Type, clientId);

				try {
					var duplicated = new Publish (sentMessage.Topic, sentMessage.QualityOfService,
						sentMessage.Retain, duplicated: true, packetId: sentMessage.PacketId) {
							Payload = sentMessage.Payload
						};

					if (channel.IsConnected) {
						await channel.SendAsync (duplicated)
							.ConfigureAwait(continueOnCapturedContext: false);
					} else {
						ackSubject.OnCompleted ();
					}		
				} catch (Exception ex) {
					ackSubject.OnError (ex);
				}

				retries++;
			};
			qosTimer.Start ();

			var ackSubscription = channel.Receiver
				.ObserveOn(NewThreadScheduler.Default)
				.OfType<T> ()
				.Where (x => x.PacketId == sentMessage.PacketId.Value)
				.Subscribe (x => {
					ackSubject.OnNext (x);
				});

			try {
				await ackSubject.FirstOrDefaultAsync ()
					.ToTask().ConfigureAwait(continueOnCapturedContext: false);
			} finally {
				ackSubscription.Dispose ();
				ackSubject.Dispose ();
				qosTimer.Dispose ();
			}
		}

		private void DefineSenderRules ()
		{
			this.senderRules = new Dictionary<PacketType, Func<string, ushort, IFlowPacket>> ();

			this.senderRules.Add (PacketType.PublishAck, (clientId, packetId) => {
				this.RemovePendingMessage (clientId, packetId);

				return default (IFlowPacket);
			});

			this.senderRules.Add (PacketType.PublishReceived, (clientId, packetId) => {
				this.RemovePendingMessage (clientId, packetId);

				return new PublishRelease(packetId);
			});

			this.senderRules.Add (PacketType.PublishComplete, (clientId, packetId) => {
				this.RemovePendingAcknowledgement (clientId, packetId, PacketType.PublishRelease);

				return default (IFlowPacket);
			});
		}

		private void SaveMessage(Publish message, string clientId, PendingMessageStatus status)
		{
			if (message.QualityOfService == QualityOfService.AtMostOnce) {
				return;
			}

			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			if (session == null) {
				throw new ProtocolException (string.Format(Resources.SessionRepository_ClientSessionNotFound, clientId));
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

			session.PendingMessages.Add (savedMessage);

			this.sessionRepository.Update (session);
		}
	}
}
