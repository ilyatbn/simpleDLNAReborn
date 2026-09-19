using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Server.Ssdp
{
  internal sealed class Datagram : Logging
  {
    public readonly IPEndPoint EndPoint;

    public readonly IPAddress LocalAddress;

    public readonly string Message;

    public readonly bool Sticky;

    public Datagram(IPEndPoint endPoint, IPAddress localAddress,
      string message, bool sticky)
    {
      EndPoint = endPoint;
      LocalAddress = localAddress;
      Message = message;
      Sticky = sticky;
      SendCount = 0;
    }

    public uint SendCount { get; private set; }

    public void Send()
    {
      var msg = Encoding.ASCII.GetBytes(Message);
      try {
        var client = new UdpClient();
        client.Client.Bind(new IPEndPoint(LocalAddress, 0));
        client.Ttl = 10;
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);
        client.BeginSend(msg, msg.Length, EndPoint, result =>
        {
          try {
            client.EndSend(result);
          }
          catch (Exception ex) {
            Debug(ex);
          }
          finally {
            try {
              client.Close();
            }
            catch (Exception) {
              // ignored
            }
          }
        }, null);
      }
      catch (SocketException ex)
        when (ex.SocketErrorCode == SocketError.AddressNotAvailable) {
        // WSAEADDRNOTAVAIL: LocalAddress belonged to a network this machine
        // has since left. Expected between a Wi-Fi switch and the re-advertise
        // that follows it, and it used to bury the log in stack traces, so it
        // is reported as the one-line fact it is.
        DebugFormat("Dropping a datagram for the stale address {0}",
          LocalAddress);
      }
      catch (Exception ex) {
        Error(ex);
      }
      ++SendCount;
    }
  }
}
