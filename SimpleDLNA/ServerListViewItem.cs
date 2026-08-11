using System.Windows.Forms;
using NMaier.SimpleDlna.Admin;

namespace NMaier.SimpleDlna.GUI
{
  /// <summary>
  ///   A row rendering one <see cref="ManagedServer" />.
  /// </summary>
  /// <remarks>
  ///   Purely presentational now. Everything this class used to do - building
  ///   the FileServer, registering the mount, tracking state - lives in
  ///   <see cref="ManagedServer" /> so the REST API can drive the same code.
  /// </remarks>
  internal sealed class ServerListViewItem : ListViewItem
  {
    internal ServerListViewItem(ManagedServer server)
    {
      Server = server;
      Render();
    }

    internal ManagedServer Server { get; }

    internal ServerDescription Description => Server.Description;

    /// <summary>Must be called on the UI thread.</summary>
    internal void Render()
    {
      SubItems.Clear();
      Text = Description.Name;
      SubItems.Add(Description.Directories.Length.ToString());
      SubItems.Add(Server.State.ToString());
      ImageIndex = (int)Server.State;
    }
  }
}
