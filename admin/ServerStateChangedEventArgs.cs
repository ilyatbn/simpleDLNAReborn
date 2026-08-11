using System;

namespace NMaier.SimpleDlna.Admin
{
  public sealed class ServerStateChangedEventArgs : EventArgs
  {
    internal ServerStateChangedEventArgs(ManagedServer server)
    {
      Server = server;
    }

    public ManagedServer Server { get; }
  }
}
