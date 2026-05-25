namespace send_picture_to_cloud_iot
{
    partial class form_backend
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnUpload = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblResult = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            //
            // pictureBox1
            //
            this.pictureBox1.BackColor = System.Drawing.Color.FromArgb(240, 244, 238);
            this.pictureBox1.Location = new System.Drawing.Point(24, 24);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(760, 420);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            //
            // btnUpload
            //
            this.btnUpload.Location = new System.Drawing.Point(24, 470);
            this.btnUpload.Name = "btnUpload";
            this.btnUpload.Size = new System.Drawing.Size(150, 42);
            this.btnUpload.TabIndex = 1;
            this.btnUpload.Text = "Chọn ảnh rác";
            this.btnUpload.UseVisualStyleBackColor = true;
            this.btnUpload.Click += new System.EventHandler(this.btnUpload_Click);
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(195, 481);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(168, 20);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "Đang kết nối Firebase...";
            //
            // lblResult
            //
            this.lblResult.AutoSize = true;
            this.lblResult.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Bold);
            this.lblResult.Location = new System.Drawing.Point(24, 530);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(202, 25);
            this.lblResult.TabIndex = 3;
            this.lblResult.Text = "Kết quả AI: Chưa có";
            //
            // form_backend
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(810, 580);
            this.Controls.Add(this.lblResult);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnUpload);
            this.Controls.Add(this.pictureBox1);
            this.Name = "form_backend";
            this.Text = "AI Trash Classifier - Firebase";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button btnUpload;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblResult;
    }
}
