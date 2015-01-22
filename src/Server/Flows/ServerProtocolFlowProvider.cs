﻿using System.Collections.Generic;
using Hermes.Packets;
using Hermes.Storage;

namespace Hermes.Flows
{
	public class ServerProtocolFlowProvider : ProtocolFlowProvider
	{
		readonly IConnectionProvider connectionProvider;

		public ServerProtocolFlowProvider (IConnectionProvider connectionProvider,
			ITopicEvaluator topicEvaluator,
			IRepositoryProvider repositoryProvider, 
			ProtocolConfiguration configuration,
			ILogger logger)
			: base(topicEvaluator, repositoryProvider, configuration, logger)
		{
			this.connectionProvider = connectionProvider;
		}

		protected override IDictionary<ProtocolFlowType, IProtocolFlow> InitializeFlows ()
		{
			var flows = new Dictionary<ProtocolFlowType, IProtocolFlow>();

			var sessionRepository = repositoryProvider.GetRepository<ClientSession>();
			var willRepository = repositoryProvider.GetRepository<ConnectionWill> ();
			var retainedRepository = repositoryProvider.GetRepository<RetainedMessage> ();
			var packetIdentifierRepository = repositoryProvider.GetRepository<PacketIdentifier> ();

			var senderFlow = new PublishSenderFlow (sessionRepository, packetIdentifierRepository, configuration, this.logger);

			flows.Add (ProtocolFlowType.Connect, new ServerConnectFlow (sessionRepository, willRepository, 
				packetIdentifierRepository, senderFlow));
			flows.Add (ProtocolFlowType.PublishSender, senderFlow);
			flows.Add (ProtocolFlowType.PublishReceiver, new ServerPublishReceiverFlow (topicEvaluator, connectionProvider,
				senderFlow, retainedRepository, sessionRepository, packetIdentifierRepository, configuration));
			flows.Add (ProtocolFlowType.Subscribe, new ServerSubscribeFlow (topicEvaluator, sessionRepository, 
				packetIdentifierRepository, retainedRepository, senderFlow, configuration));
			flows.Add (ProtocolFlowType.Unsubscribe, new ServerUnsubscribeFlow (sessionRepository, packetIdentifierRepository));
			flows.Add (ProtocolFlowType.Ping, new PingFlow ());
			flows.Add (ProtocolFlowType.Disconnect, new DisconnectFlow (this.connectionProvider, sessionRepository, willRepository));

			return flows;
		}

		protected override bool IsValidPacketType (PacketType packetType)
		{
			return packetType == PacketType.Connect ||
				packetType == PacketType.Subscribe ||
				packetType == PacketType.Unsubscribe ||
				packetType == PacketType.Publish ||
				packetType == PacketType.PublishAck ||
				packetType == PacketType.PublishComplete ||
				packetType == PacketType.PublishReceived ||
				packetType == PacketType.PublishRelease ||
				packetType == PacketType.PingRequest ||
				packetType == PacketType.Disconnect;
		}
	}
}
