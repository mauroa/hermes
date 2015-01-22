﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Hermes.Packets;
using Hermes.Storage;

namespace Hermes.Flows
{
	public class PublishSenderFlow : PublishFlow, IPublishSenderFlow
	{
		readonly IRepository<PacketIdentifier> packetIdentifierRepository;
		readonly ILogger logger;

		IDictionary<PacketType, Func<string, ushort, IFlowPacket>> senderRules;

		public PublishSenderFlow (IRepository<ClientSession> sessionRepository,
			IRepository<PacketIdentifier> packetIdentifierRepository,
			ProtocolConfiguration configuration,
			ILogger logger)
			: base(sessionRepository, configuration)
		{
			this.packetIdentifierRepository = packetIdentifierRepository;
			this.logger = logger;

			this.DefineSenderRules ();
		}

		public override async Task ExecuteAsync (string clientId, IPacket input, IChannel<IPacket> channel)
		{
			var senderRule = default (Func<string, ushort, IFlowPacket>);

			if (!this.senderRules.TryGetValue (input.Type, out senderRule))
				return;

			var flowPacket = input as IFlowPacket;

			if (flowPacket == null)
				return;

			var ackPacket = senderRule (clientId, flowPacket.PacketId);

			if (ackPacket != default(IFlowPacket)) {
				await this.SendAckAsync (clientId, ackPacket, channel);
			}
		}

		public async Task SendPublishAsync (string clientId, Publish message, IChannel<IPacket> channel, PendingMessageStatus status = PendingMessageStatus.PendingToSend)
		{
			if (channel == null || !channel.IsConnected) {
				this.SaveMessage (message, clientId, PendingMessageStatus.PendingToSend);
				return;
			}

			if (message.QualityOfService != QualityOfService.AtMostOnce && status == PendingMessageStatus.PendingToSend) {
				this.SaveMessage (message, clientId, PendingMessageStatus.PendingToAcknowledge);
			}

			if(message.QualityOfService == QualityOfService.AtLeastOnce)
				this.MonitorAck<PublishAck> (message, channel);
			else if (message.QualityOfService == QualityOfService.ExactlyOnce)
				this.MonitorAck<PublishReceived> (message, channel);

			await channel.SendAsync (message);

			this.logger.Log ("Sent Publish to {0} - Topic: {1} - Length: {2}", clientId, message.Topic, message.Payload.Length);
		}

		private void DefineSenderRules ()
		{
			this.senderRules = new Dictionary<PacketType, Func<string, ushort, IFlowPacket>> ();

			this.senderRules.Add (PacketType.PublishAck, (clientId, packetId) => {
				this.RemovePendingMessage (clientId, packetId);

				this.packetIdentifierRepository.Delete (i => i.Value == packetId);

				return default (IFlowPacket);
			});

			this.senderRules.Add (PacketType.PublishReceived, (clientId, packetId) => {
				this.RemovePendingMessage (clientId, packetId);

				return new PublishRelease(packetId);
			});

			this.senderRules.Add (PacketType.PublishComplete, (clientId, packetId) => {
				this.RemovePendingAcknowledgement (clientId, packetId, PacketType.PublishRelease);

				this.packetIdentifierRepository.Delete (i => i.Value == packetId);

				return default (IFlowPacket);
			});
		}

		private void SaveMessage(Publish message, string clientId, PendingMessageStatus status)
		{
			if (message.QualityOfService == QualityOfService.AtMostOnce)
				return;

			var savedMessage = new PendingMessage {
				Status = status,
				QualityOfService = message.QualityOfService,
				Duplicated = message.Duplicated,
				Retain = message.Retain,
				Topic = message.Topic,
				PacketId = message.PacketId,
				Payload = message.Payload
			};
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);

			session.PendingMessages.Add (savedMessage);

			this.sessionRepository.Update (session);
		}

		protected void RemovePendingMessage(string clientId, ushort packetId)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);
			var pendingMessage = session.PendingMessages.FirstOrDefault(p => p.PacketId.HasValue 
				&& p.PacketId.Value == packetId);

			session.PendingMessages.Remove (pendingMessage);

			this.sessionRepository.Update (session);
		}

		protected void MonitorAck<T>(Publish sentPublish, IChannel<IPacket> channel)
			where T : IFlowPacket
		{
			channel.Receiver
				.OfType<T> ()
				.FirstAsync (ack => ack.PacketId == sentPublish.PacketId.Value)
				.Timeout (new TimeSpan (0, 0, this.configuration.WaitingTimeoutSecs))
				.Subscribe (_ => { }, async ex => {
					var duplicatedPublish = new Publish (sentPublish.Topic, sentPublish.QualityOfService,
						sentPublish.Retain, duplicated: true, packetId: sentPublish.PacketId);
					
					this.MonitorAck<T> (duplicatedPublish, channel);

					await channel.SendAsync (duplicatedPublish);
				});
		}
	}
}
