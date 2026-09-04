using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Principal;


namespace Mentality
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Request elevation if not running as administrator
            if (!IsRunAsAdministrator())
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    Process.Start(startInfo);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // User refused the elevation or an error occurred - show a friendly message and exit
                    MessageBox.Show("Please enable admin privileges for Mentality to work.", "Admin required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return; // Exit current (non-elevated) instance
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static bool IsRunAsAdministrator()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
