using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Configuration;
using System.Globalization;
using System.Threading;
using System.Net.Sockets;
using CodeFirstWebFramework;

namespace AccountServer {
	static class Program {
		static void Main(string[] args) {
			Config.Load(args);
			bool windows = false;
			switch (Environment.OSVersion.Platform) {
				case PlatformID.Win32NT:
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
					windows = true;
					break;
			}
			// Default to UK culture and time (specify empty culture and/or tz to use machine values)
			if (Config.CommandLineFlags["culture"] != "") {
				CultureInfo c = new CultureInfo(Config.CommandLineFlags["culture"] ?? "en-GB");
				Thread.CurrentThread.CurrentCulture = c;
				CultureInfo.DefaultThreadCurrentCulture = c;
				CultureInfo.DefaultThreadCurrentUICulture = c;
			}
			if (!string.IsNullOrEmpty(Config.CommandLineFlags["tz"]))
				Utils._tz = TimeZoneInfo.FindSystemTimeZoneById(Config.CommandLineFlags["tz"] ?? (windows ? "GMT Standard Time" : "GB"));
			string startPage = "";
			bool serverRunning = false;
			if (windows) {
				if (Config.CommandLineFlags["nolaunch"] == null) {
					if (Config.CommandLineFlags["url"] != null)
						startPage = Config.CommandLineFlags["url"];
					// Is server already running?
					try {
						using (var client = new TcpClient()) {
							var result = client.BeginConnect(Config.Default.DefaultServer.ServerName, Config.Default.Port, null, null);

							result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
							serverRunning = client.Connected;
						}
					} catch {
					}
					if(!startPage.StartsWith("http"))
						startPage = "http://" + Config.Default.DefaultServer.ServerName + ":" + Config.Default.Port + "/" + startPage;
				}
			}
			if (serverRunning) {
				if (!string.IsNullOrEmpty(startPage))
					System.Diagnostics.Process.Start(startPage);
			} else {
				WebServer server = new WebServer();
				if (!string.IsNullOrEmpty(startPage))
					System.Diagnostics.Process.Start(startPage);
				server.Start();
			}
		}

		/// <summary>
		/// Translate NameType letter to human-readable form
		/// </summary>
		public static string NameType(this string type) {
			switch (type) {
				case "C":
					return "Customer";
				case "S":
					return "Supplier";
				case "O":
					return "Other Name";
				case "M":
					return "Member";
				case "":
				case null:
					return "Unknown";
				default:
					return "Type " + type;
			}
		}

	}

}
