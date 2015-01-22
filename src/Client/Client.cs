﻿using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Hermes.Diagnostics;
using Hermes.Flows;
using Hermes.Packets;
using Hermes.Properties;
using Hermes.Storage;

namespace Hermes
{
    public class Client : IClient, IDisposable
    {
		static readonly ITracer tracer = Tracer.Get<Client> ();

		bool disposed;
		bool isConnected;

		readonly Subject<ApplicationMessage> receiver = new Subject<ApplicationMessage> ();
		readonly Subject<IPacket> sender = new Subject<IPacket> ();

		readonly IChannel<IPacket> protocolChannel;
		readonly IProtocolFlowProvider flowProvider;
		readonly IRepository<ClientSession> sessionRepository;
		readonly IRepository<PacketIdentifier> packetIdentifierRepository;
		readonly ProtocolConfiguration configuration;
		readonly ILogger logger;

        public Client(IChannel<byte[]> binaryChannel, 
			IPacketChannelFactory channelFactory, 
			IPacketChannelAdapter channelAdapter,
			IProtocolFlowProvider flowProvider,
			IRepositoryProvider repositoryProvider,
			ProtocolConfiguration configuration,
			ILogger logger)
        {
			var channel = channelFactory.Create (binaryChannel);

			this.protocolChannel = channelAdapter.Adapt (channel);
			this.flowProvider = flowProvider;
			this.sessionRepository = repositoryProvider.GetRepository<ClientSession>();
			this.packetIdentifierRepository = repositoryProvider.GetRepository<PacketIdentifier>();
			this.configuration = configuration;
			this.logger = logger;

			this.protocolChannel.Receiver.OfType<Publish>().Subscribe (publish => {
				this.logger.Log ("Message Received - Client: {0} - Topic: {1} - Lenght: {2}", this.Id, publish.Topic, publish.Payload.Length);

				var message = new ApplicationMessage (publish.Topic, publish.Payload);

				this.receiver.OnNext (message);
			});

			this.protocolChannel.Sender
				.Subscribe (_ => { }, 
					ex => {
						tracer.Error (ex);
						this.receiver.OnError (ex);
						this.sender.OnError (ex);
						this.Close (ClosedReason.Error, ex.Message);
					}, () => {
						this.receiver.OnCompleted ();
						this.sender.OnCompleted ();
						this.logger.Log ("Sender Completed - Client: {0}", this.Id);
					});

			this.protocolChannel.Receiver
				.Subscribe (_ => { }, 
					ex => { 
						tracer.Error (ex);
						this.receiver.OnError (ex);
						this.sender.OnError (ex);
						this.Close (ClosedReason.Error, ex.Message);
					}, () => {
						this.receiver.OnCompleted ();
						this.sender.OnCompleted();
						this.logger.Log ("Receiver Completed - Client: {0}", this.Id);
					});
        }

		public event EventHandler<ClosedEventArgs> Closed = (sender, args) => { };

		public string Id { get; private set; }

		public bool IsConnected
		{
			get
			{
				this.CheckUnderlyingConnection ();

				return this.isConnected && this.protocolChannel.IsConnected;
			}
			private set
			{
				this.isConnected = value;
			}
		}

		public IObservable<ApplicationMessage> Receiver { get { return this.receiver; } }

		public IObservable<IPacket> Sender { get { return this.sender; } }

		/// <exception cref="ClientException">ClientException</exception>
		public async Task ConnectAsync (ClientCredentials credentials, bool cleanSession = false)
		{
			await this.ConnectAsync (credentials, null, cleanSession);
		}

		/// <exception cref="ClientException">ClientException</exception>
		public async Task ConnectAsync (ClientCredentials credentials, Will will, bool cleanSession = false)
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			this.OpenClientSession (credentials.ClientId, cleanSession);

			var connect = new Connect (credentials.ClientId, cleanSession) {
				UserName = credentials.UserName,
				Password = credentials.Password,
				Will = will,
				KeepAlive = this.configuration.KeepAliveSecs
			};

			var ack = default (ConnectAck);
			var connectTimeout = new TimeSpan(0, 0, this.configuration.WaitingTimeoutSecs);

			try {
				var connectTask = this.SendPacket (connect);

				ack = await this.protocolChannel.Receiver
					.OfType<ConnectAck> ()
					.FirstOrDefaultAsync ()
					.Timeout(connectTimeout);
			} catch(TimeoutException timeEx) {
				throw new ClientException (Resources.Client_ConnectionTimeout, timeEx);
			} catch (Exception ex) {
				throw new ClientException (Resources.Client_ConnectionError, ex);
			}

			if (ack == null) {
				var message = string.Format(Resources.Client_ConnectionDisconnected, credentials.ClientId);

				throw new ClientException (message);
			}

			this.Id = credentials.ClientId;
			this.IsConnected = true;
		}

		/// <exception cref="ClientException">ClientException</exception>
		public async Task SubscribeAsync (string topicFilter, QualityOfService qos)
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			var packetId = this.packetIdentifierRepository.GetUnusedPacketIdentifier(new Random());
			var subscribe = new Subscribe (packetId, new Subscription (topicFilter, qos));

			var ack = default (SubscribeAck);
			var subscribeTimeout = new TimeSpan(0, 0, this.configuration.WaitingTimeoutSecs);

			try {
				var subscribeTask = this.SendPacket (subscribe);

				ack = await this.protocolChannel.Receiver
					.OfType<SubscribeAck> ()
					.FirstOrDefaultAsync (x => x.PacketId == packetId)
					.Timeout(subscribeTimeout);;
			} catch(TimeoutException timeEx) {
				var message = string.Format (Resources.Client_SubscribeTimeout, this.Id, topicFilter);

				throw new ClientException (message, timeEx);
			} catch (Exception ex) {
				var message = string.Format (Resources.Client_SubscribeError, this.Id, topicFilter);

				throw new ClientException (message, ex);
			}

			if (ack == null) {
				var message = string.Format(Resources.Client_SubscriptionDisconnected, this.Id, topicFilter);

				throw new ClientException (message);
			}
		}

		public async Task PublishAsync (ApplicationMessage message, QualityOfService qos, bool retain = false)
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			var packetId = this.packetIdentifierRepository.GetPacketIdentifier(qos);
			var publish = new Publish (message.Topic, qos, retain, duplicated: false, packetId: packetId)
			{
				Payload = message.Payload
			};

			var senderFlow = this.flowProvider.GetFlow<PublishSenderFlow> ();

			await senderFlow.SendPublishAsync (this.Id, publish, this.protocolChannel);

			this.logger.Log ("Message Sent - Client: {0} - Topic: {1} - Lenght: {2}", this.Id, publish.Topic, publish.Payload.Length);
		}

		public async Task UnsubscribeAsync (params string[] topics)
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			var packetId = this.packetIdentifierRepository.GetUnusedPacketIdentifier(new Random());
			var unsubscribe = new Unsubscribe(packetId, topics);

			await this.SendPacket (unsubscribe);
		}

		public async Task DisconnectAsync ()
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			this.CloseClientSession ();

			var disconnect = new Disconnect ();

			await this.SendPacket (disconnect);

			this.Close (ClosedReason.Disconnect);
		}

		public void Close ()
		{
			this.Close (ClosedReason.Disconnect);
		}

		void IDisposable.Dispose ()
		{
			this.Close (ClosedReason.Dispose);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (this.disposed) return;

			if (disposing) {
				this.protocolChannel.Dispose ();
				this.IsConnected = false; 
				this.Id = null;
				this.disposed = true;
			}
		}

		private void Close (ClosedReason reason, string message = null)
		{
			if (reason == ClosedReason.Error) {
				this.logger.Log ("Error - Client: {0} - Message: {1}", this.Id, message ?? "N/A");
			} else {
				this.logger.Log ("Closed - Client: {0} - Message: {1}", this.Id, message ?? "N/A");
			}

			this.Dispose (true);
			this.Closed (this, new ClosedEventArgs(reason, message));
			GC.SuppressFinalize (this);
		}

		private void OpenClientSession(string clientId, bool cleanSession)
		{
			var session = this.sessionRepository.Get (s => s.ClientId == clientId);
			var sessionPresent = cleanSession ? false : session != null;

			if (cleanSession && session != null) {
				this.sessionRepository.Delete(session);
				session = null;
			}

			if (session == null) {
				session = new ClientSession { ClientId = clientId, Clean = cleanSession };

				this.sessionRepository.Create (session);
			}
		}

		private void CloseClientSession()
		{
			var session = this.sessionRepository.Get (s => s.ClientId == this.Id);

			if (session.Clean) {
				this.sessionRepository.Delete (session);
			}
		}

		private async Task SendPacket(IPacket packet)
		{
			await this.protocolChannel.SendAsync (packet);
			this.sender.OnNext (packet);
		}

		private void CheckUnderlyingConnection ()
		{
			if (this.isConnected && !this.protocolChannel.IsConnected) {
				this.Close (ClosedReason.Error, Resources.Client_UnexpectedChannelDisconnection);
			}
		}
	}
}
