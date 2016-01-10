using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Net.Mqtt.Diagnostics;
using System.Net.Mqtt.Packets;
using System.Net.Mqtt.Exceptions;
using System.Net.Mqtt.Ordering;

namespace System.Net.Mqtt
{
	internal class PacketChannel : IChannel<IPacket>
	{
		static readonly ITracer tracer = Tracer.Get<PacketChannel> ();

		bool disposed;

		readonly IChannel<byte[]> innerChannel;
		readonly IPacketManager manager;
		readonly IPacketDispatcher dispatcher;
		readonly ReplaySubject<IPacket> receiver;
		readonly ReplaySubject<IPacket> sender;
		readonly IDisposable subscription;

		public PacketChannel (IChannel<byte[]> innerChannel, IPacketManager manager, IPacketDispatcher dispatcher, ProtocolConfiguration configuration)
		{
			this.innerChannel = innerChannel;
			this.manager = manager;
			this.dispatcher = dispatcher;

			this.receiver = new ReplaySubject<IPacket> (window: TimeSpan.FromSeconds (configuration.WaitingTimeoutSecs));
			this.sender = new ReplaySubject<IPacket> (window: TimeSpan.FromSeconds (configuration.WaitingTimeoutSecs));
			this.subscription = innerChannel.Receiver
				.Subscribe (async bytes => {
					try {
						var packet = await this.manager.GetPacketAsync (bytes)
							.ConfigureAwait(continueOnCapturedContext: false);
						var dispatchUnit = packet as IDispatchUnit;

						if (dispatchUnit != null) {
							this.dispatcher.Register (dispatchUnit.DispatchId);
						}

						this.receiver.OnNext (packet);
					} catch (MqttException ex) {
						this.receiver.OnError (ex);
					}
			}, onError: ex => this.receiver.OnError (ex), onCompleted: () => this.receiver.OnCompleted());
		}

		public bool IsConnected { get { return innerChannel != null && innerChannel.IsConnected; } }

		public IObservable<IPacket> Receiver { get { return this.receiver; } }

		public IObservable<IPacket> Sender { get { return this.sender; } }

		public async Task SendAsync (IPacket packet)
		{
			if (this.disposed) {
				throw new ObjectDisposedException (this.GetType ().FullName);
			}

			var bytes = await this.manager.GetBytesAsync (packet)
				.ConfigureAwait(continueOnCapturedContext: false);

			this.sender.OnNext (packet);

			await this.innerChannel.SendAsync (bytes)
				.ConfigureAwait(continueOnCapturedContext: false);
		}

		public void Dispose ()
		{
			this.Dispose (true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (this.disposed) return;

			if (disposing) {
				tracer.Info (Properties.Resources.Tracer_Disposing, this.GetType ().FullName);

				this.subscription.Dispose ();
				this.receiver.OnCompleted ();
				this.innerChannel.Dispose ();
				this.disposed = true;
			}
		}
	}
}
