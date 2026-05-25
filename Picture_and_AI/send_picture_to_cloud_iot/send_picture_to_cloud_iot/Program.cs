using System;
using System.Windows.Forms;

namespace send_picture_to_cloud_iot
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new form_backend());
        }
    }
}
