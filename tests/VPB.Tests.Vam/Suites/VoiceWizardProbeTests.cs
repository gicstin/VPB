using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class VoiceWizardProbeTests
    {
        public VoiceWizardProbeTests(VamFixture vam) { }

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("0.0.0.0", true)]
        [InlineData("127.0.0.2", false)]
        public void ListenerMustMatchOwnerAndVpbDestination(string address, bool reachable)
        {
            int port;
            using (Process process = Process.GetCurrentProcess())
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ExclusiveAddressUse = true;
                    socket.Bind(new IPEndPoint(IPAddress.Parse(address), 0));
                    port = ((IPEndPoint)socket.LocalEndPoint).Port;
                    Assert.Equal(reachable, VpbNetVoiceWizardProbe.IsListening(process.Id, port));
                    Assert.False(VpbNetVoiceWizardProbe.IsListening(-1, port));
                }
                Assert.False(VpbNetVoiceWizardProbe.IsListening(process.Id, port));
            }
        }
    }
}
