namespace NMaier.SimpleDlna.GUI
{
  partial class FormSettings
  {
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null)) {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      this.components = new System.ComponentModel.Container();
      this.groupBox1 = new System.Windows.Forms.GroupBox();
      this.numericPort = new System.Windows.Forms.NumericUpDown();
      this.toolTip = new System.Windows.Forms.ToolTip(this.components);
      this.textCacheFile = new System.Windows.Forms.TextBox();
      this.groupBox2 = new System.Windows.Forms.GroupBox();
      this.buttonBrowseCacheFile = new System.Windows.Forms.Button();
      this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
      this.checkStartMinimized = new System.Windows.Forms.CheckBox();
      this.groupBoxLogging = new System.Windows.Forms.GroupBox();
      this.comboLogLevel = new System.Windows.Forms.ComboBox();
      this.labelLogLevel = new System.Windows.Forms.Label();
      this.buttonOK = new System.Windows.Forms.Button();
      this.checkAutoStart = new System.Windows.Forms.CheckBox();
      this.groupBoxRefresh = new System.Windows.Forms.GroupBox();
      this.numericRescanDelay = new System.Windows.Forms.NumericUpDown();
      this.numericRescanInterval = new System.Windows.Forms.NumericUpDown();
      this.labelRescanDelay = new System.Windows.Forms.Label();
      this.labelRescanInterval = new System.Windows.Forms.Label();
      this.groupBox1.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.numericPort)).BeginInit();
      this.groupBox2.SuspendLayout();
      this.groupBoxRefresh.SuspendLayout();
      this.groupBoxLogging.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.numericRescanDelay)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.numericRescanInterval)).BeginInit();
      this.SuspendLayout();
      //
      // groupBox1
      //
      this.groupBox1.Controls.Add(this.numericPort);
      this.groupBox1.Location = new System.Drawing.Point(14, 14);
      this.groupBox1.Name = "groupBox1";
      this.groupBox1.Size = new System.Drawing.Size(303, 55);
      this.groupBox1.TabIndex = 1;
      this.groupBox1.TabStop = false;
      this.groupBox1.Text = "Port";
      //
      // numericPort
      //
      this.numericPort.DataBindings.Add(new System.Windows.Forms.Binding("Value", global::NMaier.SimpleDlna.GUI.Properties.Settings.Default, "port", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
      this.numericPort.Location = new System.Drawing.Point(7, 22);
      this.numericPort.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
      this.numericPort.Name = "numericPort";
      this.numericPort.Size = new System.Drawing.Size(80, 23);
      this.numericPort.TabIndex = 0;
      this.toolTip.SetToolTip(this.numericPort, "Port of the http server.\r\nLeave at 0 to automatically have a port selected on sta" +
  "rtup.\r\n\r\n(Requires restart)");
      this.numericPort.Value = global::NMaier.SimpleDlna.GUI.Properties.Settings.Default.port;
      //
      // textCacheFile
      //
      this.textCacheFile.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::NMaier.SimpleDlna.GUI.Properties.Settings.Default, "cache", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
      this.textCacheFile.Location = new System.Drawing.Point(7, 22);
      this.textCacheFile.Name = "textCacheFile";
      this.textCacheFile.Size = new System.Drawing.Size(194, 23);
      this.textCacheFile.TabIndex = 1;
      this.textCacheFile.Text = global::NMaier.SimpleDlna.GUI.Properties.Settings.Default.cache;
      this.toolTip.SetToolTip(this.textCacheFile, "Location of the cache directory.\r\nLeave blank to use the default location (TEMP)." +
  "\r\n\r\n(Requires restart)");
      //
      // groupBox2
      //
      this.groupBox2.Controls.Add(this.buttonBrowseCacheFile);
      this.groupBox2.Controls.Add(this.textCacheFile);
      this.groupBox2.Location = new System.Drawing.Point(14, 76);
      this.groupBox2.Name = "groupBox2";
      this.groupBox2.Size = new System.Drawing.Size(303, 55);
      this.groupBox2.TabIndex = 2;
      this.groupBox2.TabStop = false;
      this.groupBox2.Text = "Cache directory";
      //
      // buttonBrowseCacheFile
      //
      this.buttonBrowseCacheFile.Location = new System.Drawing.Point(209, 20);
      this.buttonBrowseCacheFile.Name = "buttonBrowseCacheFile";
      this.buttonBrowseCacheFile.Size = new System.Drawing.Size(87, 27);
      this.buttonBrowseCacheFile.TabIndex = 0;
      this.buttonBrowseCacheFile.Text = "Browse";
      this.buttonBrowseCacheFile.UseVisualStyleBackColor = true;
      this.buttonBrowseCacheFile.Click += new System.EventHandler(this.buttonBrowseCacheFile_Click);
      //
      // groupBoxRefresh
      //
      this.groupBoxRefresh.Controls.Add(this.labelRescanDelay);
      this.groupBoxRefresh.Controls.Add(this.numericRescanDelay);
      this.groupBoxRefresh.Controls.Add(this.labelRescanInterval);
      this.groupBoxRefresh.Controls.Add(this.numericRescanInterval);
      this.groupBoxRefresh.Location = new System.Drawing.Point(14, 138);
      this.groupBoxRefresh.Name = "groupBoxRefresh";
      this.groupBoxRefresh.Size = new System.Drawing.Size(303, 86);
      this.groupBoxRefresh.TabIndex = 3;
      this.groupBoxRefresh.TabStop = false;
      this.groupBoxRefresh.Text = "Library refresh";
      //
      // labelRescanDelay
      //
      this.labelRescanDelay.AutoSize = true;
      this.labelRescanDelay.Location = new System.Drawing.Point(75, 26);
      this.labelRescanDelay.Name = "labelRescanDelay";
      this.labelRescanDelay.Size = new System.Drawing.Size(180, 15);
      this.labelRescanDelay.TabIndex = 1;
      this.labelRescanDelay.Text = "seconds after a change is detected";
      //
      // numericRescanDelay
      //
      this.numericRescanDelay.DataBindings.Add(new System.Windows.Forms.Binding("Value", global::NMaier.SimpleDlna.GUI.Properties.Settings.Default, "rescandelay", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
      this.numericRescanDelay.Location = new System.Drawing.Point(7, 22);
      this.numericRescanDelay.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
      this.numericRescanDelay.Maximum = new decimal(new int[] {
            3600,
            0,
            0,
            0});
      this.numericRescanDelay.Name = "numericRescanDelay";
      this.numericRescanDelay.Size = new System.Drawing.Size(62, 23);
      this.numericRescanDelay.TabIndex = 0;
      this.toolTip.SetToolTip(this.numericRescanDelay, "How long to wait after a file or folder changes before rescanning.\r\nChanges arriv" +
  "ing during the wait are batched into one rescan.\r\n\r\n(Applies when a server is res" +
  "tarted)");
      this.numericRescanDelay.Value = global::NMaier.SimpleDlna.GUI.Properties.Settings.Default.rescandelay;
      //
      // labelRescanInterval
      //
      this.labelRescanInterval.AutoSize = true;
      this.labelRescanInterval.Location = new System.Drawing.Point(75, 55);
      this.labelRescanInterval.Name = "labelRescanInterval";
      this.labelRescanInterval.Size = new System.Drawing.Size(196, 15);
      this.labelRescanInterval.TabIndex = 3;
      this.labelRescanInterval.Text = "minutes between full rescans (0 = off)";
      //
      // numericRescanInterval
      //
      this.numericRescanInterval.DataBindings.Add(new System.Windows.Forms.Binding("Value", global::NMaier.SimpleDlna.GUI.Properties.Settings.Default, "rescaninterval", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
      this.numericRescanInterval.Location = new System.Drawing.Point(7, 51);
      this.numericRescanInterval.Maximum = new decimal(new int[] {
            1440,
            0,
            0,
            0});
      this.numericRescanInterval.Name = "numericRescanInterval";
      this.numericRescanInterval.Size = new System.Drawing.Size(62, 23);
      this.numericRescanInterval.TabIndex = 2;
      this.toolTip.SetToolTip(this.numericRescanInterval, "Safety net for changes the file watcher cannot see, such as edits made\r\non a netwo" +
  "rk share. Set to 0 to rely on the watcher alone.\r\n\r\n(Applies when a server is res" +
  "tarted)");
      this.numericRescanInterval.Value = global::NMaier.SimpleDlna.GUI.Properties.Settings.Default.rescaninterval;
      //
      // checkStartMinimized
      //
      this.checkStartMinimized.AutoSize = true;
      this.checkStartMinimized.Checked = global::NMaier.SimpleDlna.GUI.Properties.Settings.Default.startminimized;
      this.checkStartMinimized.DataBindings.Add(new System.Windows.Forms.Binding("Checked", global::NMaier.SimpleDlna.GUI.Properties.Settings.Default, "startminimized", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
      this.checkStartMinimized.Location = new System.Drawing.Point(14, 296);
      this.checkStartMinimized.Name = "checkStartMinimized";
      this.checkStartMinimized.Size = new System.Drawing.Size(109, 19);
      this.checkStartMinimized.TabIndex = 4;
      this.checkStartMinimized.Text = "Start minimized";
      this.checkStartMinimized.UseVisualStyleBackColor = true;
      //
      // groupBoxLogging
      //
      this.groupBoxLogging.Controls.Add(this.comboLogLevel);
      this.groupBoxLogging.Controls.Add(this.labelLogLevel);
      this.groupBoxLogging.Location = new System.Drawing.Point(14, 232);
      this.groupBoxLogging.Name = "groupBoxLogging";
      this.groupBoxLogging.Size = new System.Drawing.Size(303, 55);
      this.groupBoxLogging.TabIndex = 4;
      this.groupBoxLogging.TabStop = false;
      this.groupBoxLogging.Text = "Logging";
      //
      // comboLogLevel
      //
      this.comboLogLevel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
      this.comboLogLevel.FormattingEnabled = true;
      this.comboLogLevel.Location = new System.Drawing.Point(7, 21);
      this.comboLogLevel.Name = "comboLogLevel";
      this.comboLogLevel.Size = new System.Drawing.Size(100, 23);
      this.comboLogLevel.TabIndex = 0;
      this.toolTip.SetToolTip(this.comboLogLevel, "How much detail to write to sdlna.log in the cache directory.\r\nNone turns logging" +
  " off entirely. Debug is very noisy.");
      this.comboLogLevel.SelectedIndexChanged += new System.EventHandler(this.comboLogLevel_SelectedIndexChanged);
      //
      // labelLogLevel
      //
      this.labelLogLevel.AutoSize = true;
      this.labelLogLevel.Location = new System.Drawing.Point(113, 25);
      this.labelLogLevel.Name = "labelLogLevel";
      this.labelLogLevel.Size = new System.Drawing.Size(160, 15);
      this.labelLogLevel.TabIndex = 1;
      this.labelLogLevel.Text = "detail written to sdlna.log";
      //
      // buttonOK
      //
      this.buttonOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
      this.buttonOK.DialogResult = System.Windows.Forms.DialogResult.OK;
      this.buttonOK.Location = new System.Drawing.Point(230, 323);
      this.buttonOK.Name = "buttonOK";
      this.buttonOK.Size = new System.Drawing.Size(87, 27);
      this.buttonOK.TabIndex = 0;
      this.buttonOK.Text = "OK";
      this.buttonOK.UseVisualStyleBackColor = true;
      //
      // checkAutoStart
      //
      this.checkAutoStart.AutoSize = true;
      this.checkAutoStart.Location = new System.Drawing.Point(14, 323);
      this.checkAutoStart.Name = "checkAutoStart";
      this.checkAutoStart.Size = new System.Drawing.Size(203, 19);
      this.checkAutoStart.TabIndex = 5;
      this.checkAutoStart.Text = "Start automatically with Windows";
      this.checkAutoStart.UseVisualStyleBackColor = true;
      this.checkAutoStart.CheckedChanged += new System.EventHandler(this.checkAutoStart_CheckedChanged);
      //
      // FormSettings
      //
      this.AcceptButton = this.buttonOK;
      this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(331, 364);
      this.Controls.Add(this.checkAutoStart);
      this.Controls.Add(this.buttonOK);
      this.Controls.Add(this.groupBoxLogging);
      this.Controls.Add(this.checkStartMinimized);
      this.Controls.Add(this.groupBoxRefresh);
      this.Controls.Add(this.groupBox2);
      this.Controls.Add(this.groupBox1);
      this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
      this.Name = "FormSettings";
      this.ShowInTaskbar = false;
      this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
      this.Text = "Settings";
      this.groupBox1.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.numericPort)).EndInit();
      this.groupBox2.ResumeLayout(false);
      this.groupBox2.PerformLayout();
      this.groupBoxRefresh.ResumeLayout(false);
      this.groupBoxRefresh.PerformLayout();
      this.groupBoxLogging.ResumeLayout(false);
      this.groupBoxLogging.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.numericRescanDelay)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.numericRescanInterval)).EndInit();
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.GroupBox groupBox1;
    private System.Windows.Forms.NumericUpDown numericPort;
    private System.Windows.Forms.ToolTip toolTip;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.Button buttonBrowseCacheFile;
    private System.Windows.Forms.TextBox textCacheFile;
    private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
    private System.Windows.Forms.CheckBox checkStartMinimized;
    private System.Windows.Forms.GroupBox groupBoxLogging;
    private System.Windows.Forms.ComboBox comboLogLevel;
    private System.Windows.Forms.Label labelLogLevel;
    private System.Windows.Forms.Button buttonOK;
    private System.Windows.Forms.CheckBox checkAutoStart;
    private System.Windows.Forms.GroupBox groupBoxRefresh;
    private System.Windows.Forms.NumericUpDown numericRescanDelay;
    private System.Windows.Forms.NumericUpDown numericRescanInterval;
    private System.Windows.Forms.Label labelRescanDelay;
    private System.Windows.Forms.Label labelRescanInterval;
  }
}
