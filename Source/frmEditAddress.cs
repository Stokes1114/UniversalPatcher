﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static upatcher;

namespace UniversalPatcher
{
    public partial class frmEditAddress : Form
    {
        public frmEditAddress()
        {
            InitializeComponent();
        }

        public string Result = "";

        public void ParseAddress(string OldAddr, bool extra = false)
        {
            if (!extra)
                txtName.Enabled = false;
            if (OldAddr.Length > 0)
            {
                string[] Parts = OldAddr.Split(':');
                txtAddress.Text = Parts[0].Replace("#", "");
                if (Parts[0].StartsWith("#"))
                    radioRelative.Checked = true;
                else
                    radioAbsolute.Checked = true;
                if (Parts.Length > 1)
                {
                    ushort x;
                    if (HexToUshort(Parts[1], out x))
                        numBytes.Value = x;
                }
                if (Parts.Length > 2)
                {
                    if (Parts[2].ToLower().Contains("hex"))
                        radioHEX.Checked = true;
                    else if (Parts[2].ToLower().Contains("text") || Parts[2].ToLower().Contains("txt"))
                        radioText.Checked = true;
                }

            }

        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (txtAddress.Text.Length == 0)
                return;
            if (txtName.Enabled && txtName.Text.Length == 0)
                return;
            if (txtName.Enabled)
                Result = txtName.Text +":";
            else
                Result = "";
            if (radioRelative.Checked)
                Result += "#";
            Result += txtAddress.Text + ":" + numBytes.Value.ToString() + ":";

            if (radioHEX.Checked)
                Result += "hex";
            else if (radioText.Checked)
                Result += "text";
            else
                Result += "int";

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
