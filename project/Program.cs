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
        static void Main(string[] args)
        {
            if (Migrate.TryApplyUpdateCommand(args)) return;

            // Run without forcing elevation. Request admin only when necessary and explain to users.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var startInTray = args != null && Array.Exists(args, argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            Application.Run(new Form1(startInTray));
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
