
namespace GrouchyFiler
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;


        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) watcherService?.Dispose();
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            if (disposing) applicationIcon?.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            pictureBox1 = new PictureBox();
            textBox1 = new TextBox();
            chkDryRun = new CheckBox();
            chkPause = new CheckBox();
            chkGrouchy = new CheckBox();
            btnReloadConfig = new Button();
            lblStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(12, 12);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(128, 128);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // textBox1
            // 
            textBox1.Dock = DockStyle.Bottom;
            textBox1.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            textBox1.Location = new Point(0, 264);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ScrollBars = ScrollBars.Vertical;
            textBox1.Size = new Size(778, 180);
            textBox1.TabIndex = 1;
            // 
            // chkDryRun
            // 
            chkDryRun.AutoSize = true;
            chkDryRun.Location = new Point(12, 160);
            chkDryRun.Name = "chkDryRun";
            chkDryRun.Size = new Size(154, 29);
            chkDryRun.TabIndex = 2;
            chkDryRun.Text = "Dry Run Mode";
            chkDryRun.UseVisualStyleBackColor = true;
            // 
            // chkPause
            // 
            chkPause.AutoSize = true;
            chkPause.Location = new Point(174, 160);
            chkPause.Name = "chkPause";
            chkPause.Size = new Size(162, 29);
            chkPause.TabIndex = 3;
            chkPause.Text = "Pause Watching";
            chkPause.UseVisualStyleBackColor = true;
            // 
            // chkGrouchy
            // 
            chkGrouchy.AutoSize = true;
            chkGrouchy.Location = new Point(344, 160);
            chkGrouchy.Name = "chkGrouchy";
            chkGrouchy.Size = new Size(156, 29);
            chkGrouchy.TabIndex = 4;
            chkGrouchy.Text = "Grouchy Mode";
            chkGrouchy.UseVisualStyleBackColor = true;
            // 
            // btnReloadConfig
            // 
            btnReloadConfig.Location = new Point(12, 207);
            btnReloadConfig.Name = "btnReloadConfig";
            btnReloadConfig.Size = new Size(150, 34);
            btnReloadConfig.TabIndex = 5;
            btnReloadConfig.Text = "Reload Config";
            btnReloadConfig.UseVisualStyleBackColor = true;
            btnReloadConfig.Click += btnReloadConfig_Click;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblStatus.Location = new Point(183, 209);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(117, 28);
            lblStatus.TabIndex = 6;
            lblStatus.Text = "Status: Idle";
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(778, 444);
            Controls.Add(lblStatus);
            Controls.Add(btnReloadConfig);
            Controls.Add(chkGrouchy);
            Controls.Add(chkPause);
            Controls.Add(chkDryRun);
            Controls.Add(textBox1);
            Controls.Add(pictureBox1);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "MainForm";
            Text = "Grouchy Filer";
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion


        private PictureBox pictureBox1;
        private TextBox textBox1;
        private CheckBox chkDryRun;
        private CheckBox chkPause;
        private CheckBox chkGrouchy;
        private Button btnReloadConfig;
        private Label lblStatus;
    }
}
