using System;
using System.Windows.Forms;

namespace Gibraltar.Agent.Windows.Packager
{
    public partial class frmMain : Form
    {
        private readonly string m_ProductName;
        private readonly string m_ApplicationName;

        public frmMain( string productName, string applicationName)
        {
            InitializeComponent();

            m_ProductName = productName;
            m_ApplicationName = applicationName;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            //we really don't want to show us, we just want to show the packager

            PackagerDialog dialog = new PackagerDialog(m_ProductName, m_ApplicationName);
            dialog.Send();

            //now we want to exit
            Hide();
            Application.Exit();
        }

        protected override void OnShown(EventArgs e)
        {
            Hide();
        }
    }
}
