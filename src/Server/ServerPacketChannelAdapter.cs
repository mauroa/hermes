﻿using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Hermes.Flows;
using Hermes.Packets;
using Hermes.Properties;

namespace Hermes
{
	public class ServerPacketChannelAdapter : IPacketChannelAdapter
	{
		readonly IConnectionProvider connectionProvider;
		readonly IProtocolFlowProvider flowProvider;
		readonly ProtocolConfiguration configuration;
		readonly ILogger logger;

		public ServerPacketChannelAdapter (IConnectionProvider connectionProvider, 
			IProtocolFlowProvider flowProvider,
			ProtocolConfiguration configuration,
			ILogger logger)
		{
			this.connectionProvider = connectionProvider;
			this.flowProvider = flowProvider;
			this.configuration = configuration;
			this.logger = logger;
		}

		public IChannel<IPacket> Adapt (IChannel<IPacket> channel)
		{
			var protocolChannel = new ProtocolChannel (channel);
			var clientId = string.Empty;
			var keepAlive = 0;

			var packetDueTime = new TimeSpan(0, 0, this.configuration.WaitingTimeoutSecs);

			protocolChannel.Receiver
				.FirstAsync ()
				.Timeout (packetDueTime)
				.Subscribe(async packet => {
					var connect = packet as Connect;

					if (connect == null) {
						protocolChannel.NotifyError (Resources.ServerPacketChannelAdapter_FirstPacketMustBeConnect);
						return;
					}

					clientId = connect.ClientId;
					keepAlive = connect.KeepAlive;
					this.connectionProvider.AddConnection (clientId, protocolChannel);

					await this.DispatchPacketAsync (connect, clientId, protocolChannel);
				}, async ex => {
					await this.HandleConnectionExceptionAsync (ex, protocolChannel);
				});

			protocolChannel.Sender
				.OfType<ConnectAck> ()
				.FirstAsync ()
				.Subscribe (connectAck => {
					if (keepAlive > 0) {
						this.MonitorKeepAlive (protocolChannel, clientId, keepAlive);
					}
				}, ex => {});

			protocolChannel.Receiver
				.Skip (1)
				.Subscribe (async packet => {
					if (packet is Connect) {
						this.NotifyError (Resources.ServerPacketChannelAdapter_SecondConnectNotAllowed, clientId, protocolChannel);
						return;
					}

					await this.DispatchPacketAsync (packet, clientId, protocolChannel);
				}, ex => {
					this.NotifyError (ex, clientId, protocolChannel);
				}, () => {
					this.connectionProvider.RemoveConnection (clientId);
				});

			return protocolChannel;
		}

		private async Task HandleConnectionExceptionAsync(Exception ex, ProtocolChannel channel)
		{
			if (ex is TimeoutException) {
				channel.NotifyError (Resources.ServerPacketChannelAdapter_NoConnectReceived, ex);
			} else if (ex is ConnectProtocolException) {
				var connectEx = ex as ConnectProtocolException;
				var errorAck = new ConnectAck (connectEx.ReturnCode, existingSession: false);

				await channel.SendAsync (errorAck);

				channel.NotifyError (ex.Message, ex);
			} else {
				channel.NotifyError (ex);
			}
		}

		private async Task DispatchPacketAsync(IPacket packet, string clientId, ProtocolChannel channel)
		{
			var flow = this.flowProvider.GetFlow (packet.Type);

			if (flow != null) {
				try {
					await flow.ExecuteAsync (clientId, packet, channel);

					if (packet.Type == PacketType.Publish) {
						var publish = packet as Publish;

						this.logger.Log ("Dispatched Publish from client: {0} - Topic: {1} - Length: {2}", clientId, publish.Topic, publish.Payload.Length);
					}
				} catch (Exception ex) {
					this.NotifyError (ex, clientId, channel);
				}
			}
		}

		private static TimeSpan GetKeepAliveTolerance(int keepAlive)
		{
			keepAlive = (int)(keepAlive * 1.5);

			return new TimeSpan (0, 0, keepAlive);
		}

		private void MonitorKeepAlive(ProtocolChannel channel, string clientId, int keepAlive)
		{
			channel.Receiver
				.Timeout (GetKeepAliveTolerance(keepAlive))
				.Subscribe(_ => {}, ex => {
					var message = string.Format (Resources.ServerPacketChannelAdapter_KeepAliveTimeExceeded, keepAlive);

					this.NotifyError(message, ex, clientId, channel);
				});
		}

		private void NotifyError(Exception exception, string clientId, ProtocolChannel channel)
		{
			this.RemoveClient (clientId);
			channel.NotifyError (exception);
		}

		private void NotifyError(string message, string clientId, ProtocolChannel channel)
		{
			this.RemoveClient (clientId);
			channel.NotifyError (message);
		}

		private void NotifyError(string message, Exception exception, string clientId, ProtocolChannel channel)
		{
			this.RemoveClient (clientId);
			channel.NotifyError (message, exception);
		}

		private void RemoveClient(string clientId)
		{
			this.connectionProvider.RemoveConnection (clientId);
		}
	}
}
