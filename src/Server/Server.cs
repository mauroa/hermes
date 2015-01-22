﻿using System;
using System.Collections.Generic;
using System.Linq;
using Hermes.Diagnostics;
using Hermes.Packets;

namespace Hermes
{
	public class Server : IDisposable
	{
		static readonly ITracer tracer = Tracer.Get<Server> ();

		bool disposed;
		IDisposable channelSubscription;

		readonly IObservable<IChannel<byte[]>> binaryChannelProvider;
		readonly IPacketChannelFactory channelFactory;
		readonly IPacketChannelAdapter channelAdapter;
		readonly IConnectionProvider connectionProvider;
		readonly ProtocolConfiguration configuration;
		readonly ILogger logger;

		readonly IList<IChannel<IPacket>> channels = new List<IChannel<IPacket>> ();

		public Server (
			IObservable<IChannel<byte[]>> binaryChannelProvider, 
			IPacketChannelFactory channelFactory, 
			IPacketChannelAdapter channelAdapter,
			IConnectionProvider connectionProvider,
			ProtocolConfiguration configuration,
			ILogger logger)
		{
			this.binaryChannelProvider = binaryChannelProvider;
			this.channelFactory = channelFactory;
			this.channelAdapter = channelAdapter;
			this.connectionProvider = connectionProvider;
			this.configuration = configuration;
			this.logger = logger;
		}

		public event EventHandler<ClosedEventArgs> Stopped = (sender, args) => { };

		public int ActiveChannels { get { return this.channels.Where(c => c.IsConnected).Count(); } }

		public IEnumerable<string> ActiveClients { get { return this.connectionProvider.ActiveClients; } }

		public void Start()
		{
			if (this.disposed)
				throw new ObjectDisposedException (this.GetType ().FullName);

			this.channelSubscription = this.binaryChannelProvider.Subscribe (
				binaryChannel => this.ProcessChannel(binaryChannel), 
				ex => { tracer.Error (ex); }, 
				() => {}	
			);
		}

		public void Stop ()
		{
			this.Stop (ClosedReason.Disconnect);
		}

		void IDisposable.Dispose ()
		{
			this.Stop (ClosedReason.Dispose);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (this.disposed) return;

			if (disposing) {
				foreach (var channel in channels) {
					channel.Dispose ();
				}

				if (this.channelSubscription != null) {
					this.channelSubscription.Dispose ();
				}
				
				this.disposed = true;
			}
		}

		private void Stop (ClosedReason reason, string message = null)
		{
			this.Dispose (true);
			this.Stopped (this, new ClosedEventArgs(reason, message));
			GC.SuppressFinalize (this);
		}

		private void ProcessChannel(IChannel<byte[]> binaryChannel)
		{
			var packetChannel = this.channelFactory.Create (binaryChannel);
			var protocolChannel = this.channelAdapter.Adapt (packetChannel);

			protocolChannel.Sender.Subscribe (_ => {}, ex => {
				tracer.Error (ex);
				this.logger.Log ("Error - Message: {0}", ex.ToString ());
				this.CloseChannel (protocolChannel);
			});

			protocolChannel.Receiver.Subscribe (_ => {}, ex => { 
				tracer.Error (ex);
				this.logger.Log ("Error - Message: {0}", ex.ToString ());
				this.CloseChannel (protocolChannel);
			});

			this.channels.Add (protocolChannel);
		}

		private void CloseChannel(IChannel<IPacket> channel)
		{
			this.channels.Remove (channel);
			channel.Dispose ();
		}
	}
}
