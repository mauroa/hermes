using System.Collections.Generic;
using System.Net.Mqtt.Packets;

namespace System.Net.Mqtt
{
	internal enum DispatchState
	{
		Pending = 1,
		Ready = 2,
		Cancelled = 3
	}

	internal class DispatchOrder
	{
		readonly IList<Tuple<IDispatchUnit, IChannel<IPacket>>> items;

		internal DispatchOrder (Guid id)
		{
			this.items = new List<Tuple<IDispatchUnit, IChannel<IPacket>>>();
			this.Id = id;
			this.State = DispatchState.Pending;
		}

		internal Guid Id { get; private set; }

		internal DispatchState State { get; set; }

		internal IEnumerable<Tuple<IDispatchUnit, IChannel<IPacket>>> Items { get { return this.items; } }

		internal void Add(IDispatchUnit unit, IChannel<IPacket> channel)
		{
			this.items.Add (new Tuple<IDispatchUnit, IChannel<IPacket>>(unit, channel));
		}
	}
}