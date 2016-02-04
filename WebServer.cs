using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Web;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Web Server - listens for connections, and services them
	/// </summary>
	public class WebServer {
		HttpListener _listener;
		bool _running;
		Dictionary<string, Session> _sessions;
		static object _lock = new object();
		Session _empty;

		public WebServer() {
			_listener = new HttpListener();
			_listener.Prefixes.Add("http://+:" + AppSettings.Default.Port + "/");
			Log("Listening on port {0}", AppSettings.Default.Port);
			_sessions = new Dictionary<string, Session>();
			_empty = new Session(null);
			// Start thread to expire sessions after 30 mins of inactivity
			new Task(delegate() {
				for (; ; ) {
					Thread.Sleep(180000);	// 30 mins
					DateTime now = Utils.Now;
					lock (_sessions) {
						foreach (string key in _sessions.Keys.ToList()) {
							Session s = _sessions[key];
							if (s.Expires < now)
								_sessions.Remove(key);
						}
					}
				}
			}).Start();
		}

		/// <summary>
		/// Log message to console and trace
		/// </summary>
		static public void Log(string s) {
			s = s.Trim();
			lock (_lock) {
				System.Diagnostics.Trace.WriteLine(s);
				Console.WriteLine(s);
			}
		}

		/// <summary>
		/// Log message to console and trace
		/// </summary>
		static public void Log(string format, params object[] args) {
			try {
				Log(string.Format(format, args));
			} catch (Exception ex) {
				Log(string.Format("{0}:Error logging {1}", format, ex.Message));
			}
		}

		/// <summary>
		/// Start WebServer listening for connections
		/// </summary>
		public void Start() {
			try {
				_running = true;
				_listener.Start();
				while (_running) {
					try {
						HttpListenerContext request = _listener.GetContext();
						ThreadPool.QueueUserWorkItem(ProcessRequest, request);
					} catch {
					}
				}
			} catch (HttpListenerException ex) {
				Log(ex.ToString());
			} catch (ThreadAbortException) {
			} catch (Exception ex) {
				Log(ex.ToString());
			}
		}

		public void Stop() {
			_running = false;
			_listener.Stop(); 
		}

		/// <summary>
		/// All Active Sessions
		/// </summary>
		public IEnumerable<Session> Sessions {
			get {
				return _sessions.Values;
			}
		}

		/// <summary>
		/// Process a single request
		/// </summary>
		/// <param name="listenerContext"></param>
		void ProcessRequest(object listenerContext) {
			DateTime started = DateTime.Now;			// For timing response
			HttpListenerContext context = null;
			AppModule module = null;
			StringBuilder log = new StringBuilder();	// Session log writes to here, and it is displayed at the end
			try {
				context = (HttpListenerContext)listenerContext;
				log.AppendFormat("{0} {1}:{2}:[ms]:", 
					context.Request.RemoteEndPoint.Address,
					context.Request.Headers["X-Forwarded-For"],
					context.Request.RawUrl);
				Session session = null;
				string filename = HttpUtility.UrlDecode(context.Request.Url.AbsolutePath).Substring(1);
				if (filename == "") filename = "company";			// Default page is Company
				string moduleName = null;
				string methodName = null;
				string baseName = filename.Replace(".html", "");	// Ignore .html - treat as a program request
				if (baseName.IndexOf(".") < 0) {
					// Urls of the form /ModuleName[/MethodName][.html] call a C# AppModule
					string[] parts = baseName.Split('/');
					if (parts.Length <= 2) {
						Type type = AppModule.GetModule(parts[0]);
						if (type != null) {
							// The AppModule exists - create the object
							module = (AppModule)Activator.CreateInstance(type);
							moduleName = parts[0];
							if (parts.Length == 2) methodName = parts[1];
						}
					}
				}
				if (moduleName == null) {
					// No AppModule found - treat url as a file request
					moduleName = "FileSender";
					module = new FileSender(filename);
					session = new Session(null);
				} else {
					// AppModule found - retrieve or create a session for it
					Cookie cookie = context.Request.Cookies["session"];
					if (cookie != null) {
						_sessions.TryGetValue(cookie.Value, out session);
						if (AppSettings.Default.SessionLogging)
							log.AppendFormat("[{0}{1}]", cookie.Value, session == null ? " not found" : "");
					}
					if (session == null) {
						session = new Session(this);
						cookie = new Cookie("session", session.Cookie, "/");
						if (AppSettings.Default.SessionLogging)
							log.AppendFormat("[{0} new session]", cookie.Value);
					}
					context.Response.Cookies.Add(cookie);
					cookie.Expires = session.Expires = Utils.Now.AddHours(1);
				}
				// Set up module
				module.Session = session;
				module.LogString = log;
				if (moduleName.EndsWith("Module"))
					moduleName = moduleName.Substring(0, moduleName.Length - 6);
				using (module) {
					// Call method
					module.Call(context, moduleName, methodName);
				}
			} catch (Exception ex) {
				while (ex is TargetInvocationException)
					ex = ex.InnerException;
				if (ex is System.Net.Sockets.SocketException) {
					log.AppendFormat("Request error: {0}\r\n", ex.Message);
				} else {
					log.AppendFormat("Request error: {0}\r\n", ex);
					if (module == null || !module.ResponseSent) {
						try {
							module = new AppModule();
							module.Session = _empty;
							module.LogString = log;
							module.Context = context;
							module.Module = "exception";
							module.Method = "default";
							module.Title = "Exception";
							module.Exception = ex;
							module.WriteResponse(module.Template("exception", module), "text/html", HttpStatusCode.InternalServerError);
						} catch (Exception ex1) {
							log.AppendFormat("Error displaying exception: {0}\r\n", ex1);
							if (module == null || !module.ResponseSent) {
								try {
									module.WriteResponse("Error displaying exception:" + ex.Message, "text/plain", HttpStatusCode.InternalServerError);
								} catch {
								}
							}
						}
					}
				}
			}
			if (context != null) {
				try {
						context.Response.Close();
				} catch {
				}
			}
			try {
				Log(log.ToString().Replace(":[ms]:", ":" + Math.Round((DateTime.Now - started).TotalMilliseconds, 0) + " ms:"));
			} catch {
			}
		}

		/// <summary>
		/// Simple session (we will actually use the derived class Session)
		/// </summary>
		public class BaseSession {
			public JObject Object { get; private set; }
			public DateTime Expires;
			public string Cookie { get; private set; }
			public WebServer Server;

			public BaseSession(WebServer server) {
				if (server != null) {
					Session session;
					Random r = new Random();

					lock (server._sessions) {
						do {
							Cookie = "";
							for (int i = 0; i < 20; i++)
								Cookie += (char)('A' + r.Next(26));
						} while (server._sessions.TryGetValue(Cookie, out session));
						Object = new JObject();
						server._sessions[Cookie] = (Session)this;
					}
					Server = server;
				}
			}
		}
	}

}
