using System;
using System.Net.Mqtt;
using Xunit;

namespace Tests
{
    public class PacketDispatcherProviderSpec
    {
        [Fact]
        public void when_getting_dispatcher_then_succeeds ()
        {
            var dispatcherProvider = new PacketDispatcherProvider ();

            var fooDispatcher = dispatcherProvider.GetDispatcher (Guid.NewGuid ().ToString ());
            var barDispatcher = dispatcherProvider.GetDispatcher (Guid.NewGuid ().ToString ());

            Assert.NotNull (fooDispatcher);
            Assert.NotNull (barDispatcher);
        }
    }
}
