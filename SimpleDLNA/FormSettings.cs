using System;
using System.Windows.Forms;
using NMaier.SimpleDlna.GUI.Properties;
using Form = NMaier.Windows.Forms.Form;
using SystemInformation = NMaier.SimpleDlna.Utilities.SystemInformation;

namespace NMaier.SimpleDlna.GUI
{
  public partial class FormSettings : Form
  {
    private const string APP_KEY_NAME = "SimpleDLNA";

    private readonly StartupUtilities startUpUtilities;

    public FormSettings()
    {
      InitializeComponent();
      Icon = Resources.preferencesIcon;

      // Populated in code rather than data-bound: a DropDownList bound through
      // SelectedItem silently keeps the old value when the stored string is not
      // in the list, which would hide a corrupt setting.
      comboLogLevel.Items.AddRange(FormMain.LogLevels);
      var level = Settings.Default.loglevel;
      comboLogLevel.SelectedItem = comboLogLevel.Items.Contains(level)
        ? level
        : FormMain.DefaultLogLevel;

      if (!SystemInformation.IsRunningOnMono()) {
        startUpUtilities = new StartupUtilities(StartupUtilities.StartupUserScope.CurrentUser);
        checkAutoStart.Checked = startUpUtilities.CheckIfRunAtWinBoot(APP_KEY_NAME);
      }
      else {
        checkAutoStart.Visible = false;
      }
    }

    private void buttonBrowseCacheFile_Click(object sender, EventArgs e)
    {
      if (folderBrowserDialog.ShowDialog() == DialogResult.OK) {
        textCacheFile.Text = folderBrowserDialog.SelectedPath;
      }
    }

    private void comboLogLevel_SelectedIndexChanged(object sender, EventArgs e)
    {
      // FormMain saves the settings and reconfigures logging once the dialog
      // closes.
      Settings.Default.loglevel = (string)comboLogLevel.SelectedItem;
    }

    private void checkAutoStart_CheckedChanged(object sender, EventArgs e)
    {
      if (checkAutoStart.Checked) {
        startUpUtilities.InstallAutoRun(APP_KEY_NAME);
      }
      else {
        startUpUtilities.UninstallAutoRun(APP_KEY_NAME);
      }
    }
  }
}
