using System;
using System.Drawing;
using System.Windows.Forms;

namespace RandomChipRNG
{
    internal class RangeDialog : Form
    {
        private NumericUpDown nudMin;
        private NumericUpDown nudMax;
        private Button btnOk;
        private Button btnCancel;

        public int MinValue => (int)nudMin.Value;
        public int MaxValue => (int)nudMax.Value;

        public RangeDialog(int min, int max)
        {
            // Layout below is in 96-DPI pixels; let WinForms scale it on high-DPI displays.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "Set Range";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(244, 116);

            var lbl1 = new Label { Text = "Min:", Left = 12, Top = 15, Width = 50 };
            var lbl2 = new Label { Text = "Max:", Left = 12, Top = 45, Width = 50 };

            nudMin = new NumericUpDown { Left = 70, Top = 12, Width = 160, Minimum = -100000, Maximum = 100000, Value = min };
            nudMax = new NumericUpDown { Left = 70, Top = 42, Width = 160, Minimum = -100000, Maximum = 100000, Value = max };

            btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 70, Top = 80, Width = 70 };
            btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 160, Top = 80, Width = 70 };

            Controls.Add(lbl1);
            Controls.Add(lbl2);
            Controls.Add(nudMin);
            Controls.Add(nudMax);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            FormClosing += (_, e) =>
            {
                if (DialogResult == DialogResult.OK && nudMax.Value < nudMin.Value)
                {
                    MessageBox.Show(this, "Max must be >= Min.", "Invalid Range", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }
    }
}
