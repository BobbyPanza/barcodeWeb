using BollaImpianto;
using System.Windows.Forms;

namespace WinFormsApp1
{
    partial class Form1
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
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            txtBolla = new System.Windows.Forms.TextBox();
            lblBolla = new System.Windows.Forms.Label();
            txtImpianto = new System.Windows.Forms.TextBox();
            LblImpianto = new System.Windows.Forms.Label();
            txtLog = new System.Windows.Forms.TextBox();
            dataGridViewPDL = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize)dataGridViewPDL).BeginInit();
            SuspendLayout();
            // 
            // txtBolla
            // 
            txtBolla.Location = new System.Drawing.Point(82, 12);
            txtBolla.Name = "txtBolla";
            txtBolla.Size = new System.Drawing.Size(234, 23);
            txtBolla.TabIndex = 1;
            txtBolla.TextChanged += TxtBolla_TextChanged;
            txtBolla.KeyDown += TxtBolla_KeyDown;
            // 
            // lblBolla
            // 
            lblBolla.AutoSize = true;
            lblBolla.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            lblBolla.Location = new System.Drawing.Point(7, 11);
            lblBolla.Name = "lblBolla";
            lblBolla.Size = new System.Drawing.Size(43, 20);
            lblBolla.TabIndex = 5;
            lblBolla.Text = "Bolla";
            // 
            // txtImpianto
            // 
            txtImpianto.Location = new System.Drawing.Point(82, 50);
            txtImpianto.Name = "txtImpianto";
            txtImpianto.Size = new System.Drawing.Size(234, 23);
            txtImpianto.TabIndex = 2;
            txtImpianto.TextChanged += TxtImpianto_TextChanged;
            txtImpianto.KeyDown += TxtImpianto_KeyDown;
            // 
            // LblImpianto
            // 
            LblImpianto.AutoSize = true;
            LblImpianto.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            LblImpianto.Location = new System.Drawing.Point(7, 49);
            LblImpianto.Name = "LblImpianto";
            LblImpianto.Size = new System.Drawing.Size(69, 20);
            LblImpianto.TabIndex = 6;
            LblImpianto.Text = "Impianto";
            // 
            // txtLog
            // 
            txtLog.AccessibleDescription = "txtLog";
            txtLog.AccessibleName = "txtLog";
            txtLog.BackColor = System.Drawing.SystemColors.Info;
            txtLog.Location = new System.Drawing.Point(338, 12);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.PlaceholderText = "-- Log di esecuzione --";
            txtLog.Size = new System.Drawing.Size(460, 180);
            txtLog.TabIndex = 7;
            txtLog.TextChanged += textBox1_TextChanged;
            // 
            // dataGridViewPDL
            // 
            dataGridViewPDL.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewPDL.Location = new System.Drawing.Point(7, 200);
            dataGridViewPDL.Name = "dataGridViewPDL";
            dataGridViewPDL.RowTemplate.Height = 25;
            dataGridViewPDL.Size = new System.Drawing.Size(800, 400);
            dataGridViewPDL.TabIndex = 8;
            dataGridViewPDL.CellContentClick += dataGridViewPDL_CellContentClick;
            dataGridViewPDL.CellEndEdit += DataGridViewPDL_CellEndEdit;
            dataGridViewPDL.AllowUserToAddRows = false;

            // Colonne della DataGridView
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "ID",
                DataPropertyName = "IDNES",
                ReadOnly = true,
                Width = 40 // Non editabile
            });
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Programma",
                DataPropertyName = "NSDSC",
                ReadOnly = true // Non editabile
            });
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Impianto",
                DataPropertyName = "MADSC",
                ReadOnly = true // Non editabile
            });
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Ore Taglio",
                DataPropertyName = "TEMPO",
                ReadOnly = true,
                Width = 60 // Non editabile
            });
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Rip",
                DataPropertyName = "RIPET",
                ReadOnly = true, // Non editabile
                Width = 40
            });
            dataGridViewPDL.Columns.Add(new DataGridViewCalendarColumn
            {
                HeaderText = "Data Assegnazione",
                DataPropertyName = "DTEXP",
                DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy" }, // Solo la data
                ReadOnly = false // Editabile
            });
            dataGridViewPDL.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Note",
                DataPropertyName = "NSNOT",
                ReadOnly = false, // Editabile
                Width = 300
            });

            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(810, 600);
            Controls.Add(dataGridViewPDL);
            Controls.Add(txtLog);
            Controls.Add(LblImpianto);
            Controls.Add(txtImpianto);
            Controls.Add(lblBolla);
            Controls.Add(txtBolla);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "Assegnazione Impianti a Piani di Lavoro";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)dataGridViewPDL).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TextBox txtBolla;
        private System.Windows.Forms.Label lblBolla;
        private System.Windows.Forms.TextBox txtImpianto;
        private System.Windows.Forms.Label LblImpianto;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.DataGridView dataGridViewPDL;
    }
}
