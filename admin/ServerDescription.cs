using System;
using NMaier.SimpleDlna.Server;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   The persisted configuration of one media server. Serialized to
  ///   descriptors.xml by <see cref="DescriptorStore" />.
  /// </summary>
  /// <remarks>
  ///   Moved here from the WinForms project: it is the storage model, not a UI
  ///   type, and both the tray host and the console need it.
  ///
  ///   XmlSerializer names elements after the type, not its namespace, so the
  ///   move does not change the on-disk format. <see cref="Id" /> is new and is
  ///   generated on load when absent, which keeps older files readable.
  /// </remarks>
  [Serializable]
  public sealed class ServerDescription
  {
    public ServerDescription()
    {
      UserAgents = Ips = Macs = Views = Directories = new string[0];
    }

    /// <summary>
    ///   Stable identity for the REST API. Empty in files written before this
    ///   existed; <see cref="EnsureId" /> fills it in.
    /// </summary>
    public Guid Id { get; set; }

    public bool Active { get; set; }

    public string[] Directories { get; set; }

    public string[] Ips { get; set; }

    public string[] Macs { get; set; }

    public string Name { get; set; }

    public string Order { get; set; }

    public bool OrderDescending { get; set; }

    public DlnaMediaTypes Types { get; set; }

    public string[] UserAgents { get; set; }

    public string[] Views { get; set; }

    /// <summary>Assigns an id when the loaded file did not carry one.</summary>
    public bool EnsureId()
    {
      if (Id != Guid.Empty) {
        return false;
      }
      Id = Guid.NewGuid();
      return true;
    }

    /// <summary>
    ///   Copies everything except <see cref="Active" /> and <see cref="Id" />,
    ///   so an edit neither stops a running server nor changes its identity.
    /// </summary>
    public void AdoptInfo(ServerDescription other)
    {
      if (other == null) {
        throw new ArgumentNullException(nameof(other));
      }
      Directories = other.Directories ?? new string[0];
      Name = other.Name;
      Order = other.Order;
      OrderDescending = other.OrderDescending;
      Types = other.Types;
      Views = other.Views ?? new string[0];
      Macs = other.Macs ?? new string[0];
      Ips = other.Ips ?? new string[0];
      UserAgents = other.UserAgents ?? new string[0];
    }

    public ServerDescription Clone()
    {
      var rv = new ServerDescription {Id = Id, Active = Active};
      rv.AdoptInfo(this);
      return rv;
    }

    public void ToggleActive()
    {
      Active = !Active;
    }
  }
}
