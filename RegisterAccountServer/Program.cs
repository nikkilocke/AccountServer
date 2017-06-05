using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Principal;

namespace RegisterAccountServer {
	class Program {
		static void Main(string[] args) {
			Console.WriteLine("Registering web server on this computer\n\n\n\n\n");
			ProcessStartInfo proc = new ProcessStartInfo() {
				UseShellExecute = true,
				WorkingDirectory = Environment.CurrentDirectory,
				FileName = "netsh",
				Arguments = "http add urlacl url=http://+:8080/ user=Everyone",
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Minimized
			};

			try {
				Process.Start(proc).WaitForExit();
			} catch {
				// The user refused to allow privileges elevation.
				MessageBox.Show("RegisterAccountServer requires administrator access", "Register Account Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}

}
