using AntdUI;

namespace MainUI.Procedure.Mask
{
    public partial class LayerForm : Form
    {
        private Form _onLayerForm;
        public LayerForm(Form LayeredForm, Form onLayerForm)
        {
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            BackColor = Color.Black;
            Opacity = 0.4;
            ShowInTaskbar = false;
            //StartPosition = FormStartPosition.CenterScreen;
            //Size = new Size(1920, LayeredForm.Height);
            _onLayerForm = onLayerForm;
            Shown += LayerForm_Shown;

            StartPosition = FormStartPosition.Manual;
            Location = LayeredForm.PointToScreen(Point.Empty);
            //Size = LayeredForm.ClientSize;
            Size = new Size(1920, 1155);
        }

        private void LayerForm_Shown(object sender, EventArgs e)
        {
            _onLayerForm.ShowInTaskbar = false;
            _onLayerForm.StartPosition = FormStartPosition.CenterParent;
            _onLayerForm.FormClosed += OnLayerForm_FormClosed;
            _onLayerForm.ShowDialog();
        }

        private void OnLayerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Close();
        }
    }
}
