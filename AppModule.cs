using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Web;
using System.IO;
using System.Reflection;
using System.Threading;
using Mustache;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Holds current session information
	/// </summary>
	public class Session : WebServer.BaseSession {
		public Session(WebServer server)
			: base(server) {
		}
	}

	/// <summary>
	/// Base class for all app modules
	/// </summary>
	
	public class AppModule : IDisposable {
		static Dictionary<string, Type> _appModules;	// List of all AppModule types by name ("Module" stripped off end)
		static int _lastJob;							// Last batch job
		static Dictionary<int, AppModule> _jobs = new Dictionary<int, AppModule>();
		protected static Settings _settings;			// Common settings, read from database

		static AppModule() {
			// Build the _appModules dictionary
			var baseType = typeof(AppModule);
			var assembly = baseType.Assembly;
			_appModules = new Dictionary<string, Type>();
			foreach (Type t in assembly.GetTypes().Where(t => t != baseType && t.IsSubclassOf(baseType))) {
				string name = t.Name;
				if (name.EndsWith("Module"))
					name = name.Substring(0, name.Length - 6);
				_appModules[name.ToLower()] = t;
			}
			using (Database db = new Database()) {
				_settings = db.QueryOne<Settings>("SELECT * FROM Settings");
			}
		}

		public static Encoding Encoding = Encoding.GetEncoding(1252);
		public static string Charset = "ANSI";

		Database _db;

		public void CloseDatabase() {
			if (_db != null) {
				_db.Dispose();
				_db = null;
			}
		}

		public Database Database {
			get {
				if (_db == null) {
					_db = new Database();
					_db.Logging = (LogLevel)_settings.DatabaseLogging;
				}
				return _db;
			}
		}

		/// <summary>
		/// Get the AppModule for a module name from the url
		/// </summary>
		static public Type GetModule(string name) {
			name = name.ToLower();
			return _appModules.ContainsKey(name) ? _appModules[name] : null;
		}

		/// <summary>
		/// So templates can access Session
		/// </summary>
		[JsonIgnore]
		public Session Session;

		/// <summary>
		/// Session data in dynamic form
		/// </summary>
		[JsonIgnore]
		public dynamic SessionData {
			get { return Session.Object; }
		}

		public StringBuilder LogString;

		public void Log(string s) {
			if (LogString != null) LogString.AppendLine(s);
		}

		public void Log(string format, params object[] args) {
			if (LogString != null) LogString.AppendFormat(format + "\r\n", args);
		}

		public void Dispose() {
			if (_db != null && Batch == null) {
				CloseDatabase();
			}
		}

		public HttpListenerContext Context;

		public Exception Exception;

		/// <summary>
		/// Module menu - line 2 of page top menu
		/// </summary>
		public MenuOption[] Menu;
		
		/// <summary>
		/// Alert message to show user
		/// </summary>
		public string Message;

		public string Method;

		public string Module;

		public string OriginalMethod;

		public string OriginalModule;

		/// <summary>
		/// Parameters from Url
		/// </summary>
		public NameValueCollection GetParameters;

		/// <summary>
		/// Get & Post parameters combined
		/// </summary>
		public JObject Parameters = new JObject();

		/// <summary>
		/// Parameters from POST
		/// </summary>
		public JObject PostParameters;

		public HttpListenerRequest Request {
			get { return Context.Request; }
		}

		public HttpListenerResponse Response {
			get { return Context.Response; }
		}

		/// <summary>
		/// Used for the web page title
		/// </summary>
		public string Title;

		public Settings Settings {
			get { return _settings; }
		}

		public AppSettings Config {
			get { return AccountServer.AppSettings.Default; }
		}

		static public Settings AppSettings {
			get { return _settings; }
		}

		/// <summary>
		/// Goes into the web page header
		/// </summary>
		public string Head;

		/// <summary>
		/// Goes into the web page body
		/// </summary>
		public string Body;

		public bool ResponseSent { get; private set; }

		public string Today {
			get { return Utils.Today.ToString("yyyy-MM-dd"); }
		}

		/// <summary>
		/// Generic object for templates to use - usually contains data from the database
		/// </summary>
		public object Record;

		/// <summary>
		/// Background batch job (e.g. import, restore)
		/// </summary>
		public class BatchJob {
			AppModule _module;
			string _redirect;
			int _record;

			/// <summary>
			/// Create a batch job that redirects back to the module's original method on completion
			/// </summary>
			/// <param name="module">Module containing Database, Session, etc.</param>
			/// <param name="action">Action to run the job</param>
			public BatchJob(AppModule module, Action action)
				: this(module, null, action) {
			}

			/// <summary>
			/// Create a batch job that redirects somewhere specific
			/// </summary>
			/// <param name="module">Module containing Database, Session, etc.</param>
			/// <param name="action">Action to run the job</param>
			public BatchJob(AppModule module, string redirect, Action action) {
				_module = module;
				// Get the next job number
				lock (_jobs) {
					Id = ++_lastJob;
					_jobs[Id] = module;
				}
				_redirect = redirect ?? "/" + module.Module.ToLower() + "/" + module.Method.ToLower() + ".html";
				Status = "";
				Records = 100;
				module.Log("Started batch job {0}", Id);
				new Thread(new ThreadStart(delegate() {
					try {
						action();
					} catch (Exception ex) {
						WebServer.Log("Batch job {0} Exception: {1}", Id, ex);
						Status = "An error occurred";
						Error = ex.Message;
					}
					WebServer.Log("Finished batch job {0}", Id);
					Finished = true;
					module.CloseDatabase();
					Thread.Sleep(60000);	// 1 minute delay in case of calls to get status
					lock (_jobs) {
						_jobs.Remove(Id);
					}
				})) {
					IsBackground = true,
					Name = this.GetType().Name
				}.Start();
				module.Batch = this;
				module.Module = "admin";
				module.Method = "batch";
			}

			/// <summary>
			/// Job id
			/// </summary>
			public int Id { get; private set; }

			/// <summary>
			/// Error message (e.g. on exception)
			/// </summary>
			public string Error;

			public bool Finished;

			/// <summary>
			/// For progress display
			/// </summary>
			public int PercentComplete {
				get {
					return Records == 0 ? 100 : 100 * Record / Records;
				}
			}

			/// <summary>
			/// To indicate progress (0...Records)
			/// </summary>
			public virtual int Record {
				get {
					return _record;
				}
				set {
					_record = value;
				}
			}

			/// <summary>
			/// Total number of records (for progress bar)
			/// </summary>
			public int Records;

			/// <summary>
			/// Where redirecting to on completion
			/// </summary>
			public string Redirect {
				get {
					return _redirect == null ? null : _redirect + (_redirect.Contains('?') ? '&' : '?') + "message=" 
						+ (string.IsNullOrEmpty(_module.Message) ? "Job completed" : HttpUtility.UrlEncode(_module.Message));
				}
			}

			/// <summary>
			/// For status/progress display
			/// </summary>
			public string Status;
		}

		/// <summary>
		/// BatchJob started by this module
		/// </summary>
		public BatchJob Batch;

		/// <summary>
		/// Get batch job from id (for status/progress display)
		/// </summary>
		public static AppModule GetBatchJob(int id) {
			AppModule job;
			return _jobs.TryGetValue(id, out job) ? job : null;
		}

		/// <summary>
		/// Responds to a Url request. Set up the AppModule variables and call the given method
		/// </summary>

		public void Call(HttpListenerContext context, string moduleName, string methodName) {
			Context = context;
			OriginalModule = Module = moduleName.ToLower();
			OriginalMethod = Method = (methodName ?? "default").ToLower();
			LogString.Append(GetType().Name + ":" + Title + ":");
			// Collect get parameters
			GetParameters = new NameValueCollection();
			for (int i = 0; i < Request.QueryString.Count; i++) {
				string key = Request.QueryString.GetKey(i);
				string value = Request.QueryString[i];
				if (key == null) {
					GetParameters[value] = "";
				} else {
					GetParameters[key] = value;
					if (key == "message")
						Message = value;
				}
			}
			// Add into parameters array
			Parameters.AddRange(GetParameters);
			// Collect POST parameters
			if (context.Request.HttpMethod == "POST") {
				PostParameters = new JObject();
				if (context.Request.ContentType != null) {
					string data;
					// Encoding 1252 will give exactly 1 character per input character, without translation
					using (StreamReader s = new StreamReader(context.Request.InputStream, Encoding.GetEncoding(1252))) {
						data = s.ReadToEnd();
					}
					if (context.Request.ContentType.StartsWith("multipart/form-data")) {
						string boundary = "--" + (Regex.Split(context.Request.ContentType, "boundary=")[1]);
						foreach (string part in Regex.Split("\r\n" + data, ".." + boundary, RegexOptions.Singleline)) {
							if (part.Trim() == "" || part.Trim() == "--") continue;
							int pos = part.IndexOf("\r\n\r\n");
							string headers = part.Substring(0, pos);
							string value = part.Substring(pos + 4);
							Match match = new Regex(@"form-data; name=""(\w+)""").Match(headers);
							if (match.Success) {
								// This is a file upload
								string field = match.Groups[1].Value;
								match = new Regex(@"; filename=""(.*)""").Match(headers);
								if (match.Success) {
									PostParameters.Add(field, new UploadedFile(Path.GetFileName(match.Groups[1].Value), value).ToJToken());
								} else {
									PostParameters.Add(field, value);
								}
							}
						}
					} else {
						PostParameters.AddRange(HttpUtility.ParseQueryString(data));
					}
					Parameters.AddRange(PostParameters);
				}
			}
			MethodInfo method = null;
			try {
				object o = CallMethod(out method);
				if (method == null) {
					WriteResponse("Page /" + Module + "/" + Method + ".html not found", "text/html", HttpStatusCode.NotFound);
					return;
				}
				if (!ResponseSent) {
					// Method has not sent a response - do the default response
					Response.AddHeader("Expires", DateTime.UtcNow.ToString("R"));
					if (method.ReturnType == typeof(void))
						Respond();									// Builds response from template
					else
						WriteResponse(o, null, HttpStatusCode.OK);	// Builds response from return value
				}
			} catch (Exception ex) {
				Log("Exception: {0}", ex);
				if (ex is DatabaseException)
					Log(((DatabaseException)ex).Sql);	// Log Sql of all database exceptions
				if (method == null || method.ReturnType == typeof(void)) throw;	// Will produce exception page
				while (ex is TargetInvocationException) {
					// Strip off TargetInvokationExceptions so message is meaningful
					ex = ex.InnerException;
				}
				// Send an AjaxReturn object indicating the error
				WriteResponse(new AjaxReturn() { error = ex.Message }, null, HttpStatusCode.OK);
			}
		}

		/// <summary>
		/// Call the method named by Method, and return its result
		/// </summary>
		/// <param name="method">Also return the MethodInfo so caller knows what return type it has.
		/// Will be set to null if there is no such named method.</param>

		public object CallMethod(out MethodInfo method) {
			List<object> parms = new List<object>();
			method = this.GetType().GetMethod(Method, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
			if (method == null) {
				return null;
			}
			string moduleName = GetType().Name;
			if (moduleName.EndsWith("Module"))
				moduleName = moduleName.Substring(0, moduleName.Length - 6);
			Title = moduleName.UnCamel();
			if (method.Name != "Default") Title += " - " + method.Name.UnCamel();
			// Collect any parameters required by the method from the GET/POST parameters
			foreach (ParameterInfo p in method.GetParameters()) {
				JToken val = Parameters[p.Name];
				object o;
				Utils.Check(val != null, "Missing parameter {0}", p.Name);
				try {
					if (p.ParameterType == typeof(int)
						|| p.ParameterType == typeof(decimal)
						|| p.ParameterType == typeof(string)
						|| p.ParameterType == typeof(DateTime)) {
						o = val.ToObject(p.ParameterType);
					} else if (p.ParameterType == typeof(UploadedFile)) {
						if (val.ToString() == "null")
							o = null;
						else
							o = val.ToObject(typeof(UploadedFile));
					} else if (val.Type == JTokenType.String && val.ToString() == "null") {
						o = null;		// "null" means null
					} else if (p.ParameterType == typeof(int?)
						|| p.ParameterType == typeof(decimal?)) {
						o = val.ToObject(p.ParameterType);
					} else {
						o = val.ToObject<string>().JsonTo(p.ParameterType);
					}
					parms.Add(o);
				} catch(Exception ex) {
					Match m = Regex.Match(ex.Message, "Error converting value (.*) to type '(.*)'. Path '(.*)', line");
					if(m.Success)
						throw new CheckException(ex, "{0} is an invalid value for {1}", m.Groups[1], m.Groups[3]);
					throw new CheckException(ex, "Could not convert {0} to {1}", val, p.ParameterType.Name);
				}
			}
			return method.Invoke(this, parms.Count == 0 ? null : parms.ToArray());
		}

		/// <summary>
		/// Method to call if no method supplied in url
		/// </summary>
		public virtual void Default() {
		}

		/// <summary>
		/// Get the last document of the given type with NameAddressId == id
		/// </summary>

		public object DocumentLast(int id, DocType type) {
			JObject result = new JObject();
			Extended_Document header = Database.QueryOne<Extended_Document>("SELECT * FROM Extended_Document WHERE DocumentTypeId = " + (int)type
				+ " AND DocumentNameAddressId = " + id
				+ " ORDER BY DocumentDate DESC, idDocument DESC");
			if (header.idDocument != null) {
				if (Utils.ExtractNumber(header.DocumentIdentifier) > 0)
					header.DocumentIdentifier = "";
				result.AddRange("header", header,
					"detail", Database.Query("idJournal, DocumentId, Line.VatCodeId, VatRate, JournalNum, Journal.AccountId, Memo, LineAmount, VatAmount",
						"WHERE Journal.DocumentId = " + header.idDocument + " AND idLine IS NOT NULL ORDER BY JournalNum",
						"Document", "Journal", "Line"));
			}
			return result;
		}

		/// <summary>
		/// Allocate the next unused cheque number/deposit number/etc.
		/// </summary>
		protected void allocateDocumentIdentifier(Extended_Document document) {
			if ((document.idDocument == null || document.idDocument == 0) && document.DocumentIdentifier == "<next>") {
				DocType type = (DocType)document.DocumentTypeId;
				int nextDocId = 0;
				switch (type) {
					case DocType.Invoice:
					case DocType.Payment:
					case DocType.CreditMemo:
					case DocType.Bill:
					case DocType.BillPayment:
					case DocType.Credit:
					case DocType.GeneralJournal:
						nextDocId = Settings.NextNumber(type);
						break;
					case DocType.Cheque:
					case DocType.Deposit:
					case DocType.CreditCardCharge:
					case DocType.CreditCardCredit:
						FullAccount acct = Database.QueryOne<FullAccount>("*", "WHERE idAccount = " + document.DocumentAccountId, "Account");
						nextDocId = acct.NextNumber(type);
						break;
				}
				document.DocumentIdentifier = nextDocId != 0 ? nextDocId.ToString() : "";
			}
		}

		/// <summary>
		/// Allocate the next unused cheque number/deposit number/etc.
		/// </summary>
		protected void allocateDocumentIdentifier(Extended_Document document, FullAccount acct) {
			if ((document.idDocument == null || document.idDocument == 0) && document.DocumentIdentifier == "<next>") {
				DocType type = (DocType)document.DocumentTypeId;
				int nextDocId = 0;
				switch (type) {
					case DocType.Invoice:
					case DocType.Payment:
					case DocType.CreditMemo:
					case DocType.Bill:
					case DocType.BillPayment:
					case DocType.Credit:
					case DocType.GeneralJournal:
						nextDocId = Settings.NextNumber(type);
						break;
					case DocType.Cheque:
					case DocType.Deposit:
					case DocType.CreditCardCharge:
					case DocType.CreditCardCredit:
						nextDocId = acct.NextNumber(type);
						break;
				}
				document.DocumentIdentifier = nextDocId != 0 ? nextDocId.ToString() : "";
			}
		}

		/// <summary>
		/// Check AcctType type is one of the supplied account tyes
		/// </summary>
		protected AcctType checkAcctType(int? type, params AcctType[] allowed) {
			Utils.Check(type != null, "Account Type missing");
			AcctType t = (AcctType)type;
			Utils.Check(Array.IndexOf(allowed, t) >= 0, "Cannot use this screen to edit {0}s", t.UnCamel());
			return t;
		}

		/// <summary>
		/// Check AcctType type is one of the supplied account tyes
		/// </summary>
		protected AcctType checkAcctType(JToken type, params AcctType[] allowed) {
			return checkAcctType(type.To<int?>(), allowed);
		}

		/// <summary>
		/// Check type of supplied account is one of the supplied account tyes
		/// </summary>
		protected AcctType checkAccountIsAcctType(int? account, params AcctType[] allowed) {
			Utils.Check(account != null, "Account missing");
			Account a = Database.Get<Account>((int)account);
			return checkAcctType(a.AccountTypeId, allowed);
		}

		/// <summary>
		/// Check type is one of the supplied document types
		/// </summary>
		protected DocType checkDocType(int? type, params DocType[] allowed) {
			Utils.Check(type != null, "Document Type missing");
			DocType t = (DocType)type;
			Utils.Check(Array.IndexOf(allowed, t) >= 0, "Cannot use this screen to edit {0}s", t.UnCamel());
			return t;
		}

		/// <summary>
		/// Check type is one of the supplied document types
		/// </summary>
		protected DocType checkDocType(JToken type, params DocType[] allowed) {
			return checkDocType(type.To<int?>(), allowed);
		}

		/// <summary>
		/// Check type is the supplied name type ("C" for customer, "S" for supplier, "O" for other)
		/// </summary>
		protected void checkNameType(string type, string allowed) {
			Utils.Check(type == allowed, "Name is not a {0}", allowed.NameType());
		}

		/// <summary>
		/// Check NameAddress record is the supplied name type ("C" for customer, "S" for supplier, "O" for other)
		/// </summary>
		protected void checkNameType(int? id, string allowed) {
			Utils.Check(id != null, allowed.NameType() + " missing");
			NameAddress n = Database.Get<NameAddress>((int)id);
			checkNameType(n.Type, allowed);
		}

		/// <summary>
		/// Check NameAddress record is the supplied name type ("C" for customer, "S" for supplier, "O" for other)
		/// </summary>
		protected void checkNameType(JToken id, string allowed) {
			checkNameType(id.To<int?>(), allowed);
		}

		/// <summary>
		/// Delete a document, first checking it is one of the supplied types
		/// </summary>
		protected AjaxReturn deleteDocument(int id, params DocType[] allowed) {
			AjaxReturn result = new AjaxReturn();
			Database.BeginTransaction();
			Extended_Document record = getDocument<Extended_Document>(id);
			Utils.Check(record != null && record.idDocument != null, "Record does not exist");
			DocType type = checkDocType(record.DocumentTypeId, allowed);
			if (record.DocumentOutstanding != record.DocumentAmount) {
				result.error = type.UnCamel() + " has been " +
					(type == DocType.Payment || type == DocType.BillPayment ? "used to pay or part pay invoices" : "paid or part paid")
					+ " it cannot be deleted";
			} else if(record.VatPaid != null) {
				result.error = "VAT has been declared on " + type.UnCamel() + " it cannot be deleted";
			} else {
				Database.Audit(AuditType.Delete, "Document", id, getCompleteDocument(record));
				Database.Execute("DELETE FROM StockTransaction WHERE idStockTransaction IN (SELECT idJournal FROM Journal WHERE DocumentId = " + id + ")");
				Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + id + ")");
				Database.Execute("DELETE FROM Journal WHERE DocumentId = " + id);
				Database.Execute("DELETE FROM Document WHERE idDocument = " + id);
				Database.Commit();
				result.message = type.UnCamel() + " deleted";
			}
			return result;
		}

		protected void fixNameAddress(Extended_Document document, string nameType) {
			if (document.DocumentNameAddressId == null || document.DocumentNameAddressId == 0) {
				document.DocumentNameAddressId = string.IsNullOrWhiteSpace(document.DocumentAddress) ? 1 : 
					Database.ForeignKey("NameAddress",
						"Type", nameType,
						"Name", document.DocumentName,
						"Address", document.DocumentAddress);
			} else {
				checkNameType(document.DocumentNameAddressId, nameType);
			}
		}

		/// <summary>
		/// Get a complete document (header and details) by id
		/// </summary>
		protected JObject getCompleteDocument(int? id) {
			Extended_Document doc = getDocument<Extended_Document>(id);
			if (doc.idDocument == null) return null;
			return getCompleteDocument(doc);
		}

		/// <summary>
		/// Get a complete document (including details) from the supplied document header
		/// </summary>
		protected JObject getCompleteDocument<T>(T document) where T : Extended_Document {
			return new JObject().AddRange("header", document,
				"detail", Database.Query(@"SELECT Journal.*, AccountName, Name, Qty, ProductId, ProductName, LineAmount, Line.VatCodeId, Code, VatRate, VatAmount
FROM Journal 
LEFT JOIN Line ON idLine = idJournal
LEFT JOIN Account ON idAccount = Journal.AccountId
LEFT JOIN NameAddress ON idNameAddress = NameAddressId
LEFT JOIN Product ON idProduct = ProductId
LEFT JOIN VatCode ON idVatCode = Line.VatCodeId
WHERE Journal.DocumentId = " + document.idDocument));
		}

		/// <summary>
		/// Read the current copy of the supplied document from the database
		/// </summary>
		protected T getDocument<T>(T document) where T : JsonObject {
			if (document.Id == null) return Database.EmptyRecord<T>();
			return getDocument<T>((int)document.Id);
		}

		/// <summary>
		/// Read the current copy of the supplied document id from the database
		/// </summary>
		protected T getDocument<T>(int? id) where T : JsonObject {
			return Database.QueryOne<T>("SELECT * FROM Extended_Document WHERE idDocument = " + (id == null ? "NULL" : id.ToString()));
		}

		/// <summary>
		/// Add an extra option to the menu, before any "New xxxx" options
		/// </summary>
		protected void insertMenuOption(MenuOption o) {
			int i;
			for (i = 0; i < Menu.Length; i++) {
				if (Menu[i].Text.StartsWith("New "))
					break;
			}
			List<MenuOption> list = Menu.ToList();
			list.Insert(i, o);
			Menu = list.ToArray();
		}

		/// <summary>
		/// Load the text of a template from a file, first processing any includes, 
		/// and making any required substitutions
		/// </summary>
		string loadFile(string filename) {
			using (StreamReader s = Utils.FileInfoForUrl(filename.ToLower()).OpenText()) {
				string text = s.ReadToEnd();
				// Process includes with a recursive call
				text = Regex.Replace(text, @"\{\{ *include +(.*) *\}\}", delegate(Match m) {
					return loadFile(m.Groups[1].Value);
				});
				// In javascript, you can comment Mustache parameters to avoid syntax errors. The comment "//" is removed
				text = Regex.Replace(text, @"//[\s]*{{([^{}]+)}}[\s]*$", "{{$1}}");
				// In javascript, you can place a Mustache parameter in a string with a pling ('!{{name}}') to avoid syntax errors.
				// The quotes and the pling are removed
				text = Regex.Replace(text, @"'!{{([^{}]+)}}'", "{{$1}}");
				// {{{name}}} is replaced by html quoted version of the parameter value - the quoting is done later
				// we just mark it with \001 and \002 characters for now.
				text = Regex.Replace(text, @"{{{([^{}]+)}}}", "\001{{$1}}\002");
				return text;
			}
		}

		/// <summary>
		/// Load the named template, render using Mustache to substite the parameters from the supplied object.
		/// E.g. {{Body} in the template will be replaced with the obj.Body.ToString()
		/// </summary>
		public string LoadTemplate(string filename, object obj) {
			try {
				FormatCompiler compiler = new FormatCompiler();
				compiler.RemoveNewLines = false;
				if (Path.GetExtension(filename) == "")
					filename += ".html";
				Generator generator = compiler.Compile(loadFile(filename));
				string result = generator.Render(obj);
				result = Regex.Replace(result, "\001(.*?)\002", delegate(Match m) {
					return HttpUtility.HtmlEncode(m.Groups[1].Value).Replace("\n", "\n<br />");
				}, RegexOptions.Singleline);
				return result;
			} catch (DatabaseException) {
				throw;
			} catch (Exception ex) {
				throw new CheckException(ex, "{0}.html:{1}", filename, ex.Message);
			}
		}

		/// <summary>
		/// Load the named template, and render using Mustache from the supplied object.
		/// E.g. {{Body} in the template will be replaced with the obj.Body.ToString()
		/// Then split into <head> (goes to this.Head) and <body> (goes to this.Body)
		/// If no head/body sections, the whole template goes into this.Body.
		/// Then render the default template from this.
		/// </summary>
		public string Template(string filename, object obj) {
			string body = LoadTemplate(filename, obj);
			Match m = Regex.Match(body, @"<head>(.*)</head>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
			if (m.Success) {
				this.Head = m.Groups[1].Value;
				body = body.Replace(m.Value, "");
			} else {
				this.Head = "";
			}
			m = Regex.Match(body, @"<body>(.*)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
			this.Body = m.Success ? m.Groups[1].Value : body;
			return LoadTemplate("default", this);
		}

		public void Redirect(string url) {
			if (Context == null)
				return;
			Response.Redirect(url);
			WriteResponse("", "text/plain", HttpStatusCode.Redirect);
		}

		/// <summary>
		/// Render the template Module/Method.html from this.
		/// </summary>
		public void Respond() {
			try {
				string filename = Path.Combine(Module, Method).ToLower();
				string page = Template(filename, this);
				WriteResponse(page, "text/html", HttpStatusCode.OK);
			} catch (System.IO.FileNotFoundException ex) {
				Log(ex.ToString());
				Exception = ex;
				WriteResponse(Template("exception", this), "text/html", HttpStatusCode.NotFound);
			} catch (Exception ex) {
				Log(ex.ToString());
				Exception = ex;
				WriteResponse(Template("exception", this), "text/html", HttpStatusCode.InternalServerError);
			}
		}

		/// <summary>
		/// Return the sign to use for documents of the supplied type.
		/// </summary>
		/// <returns>-1 or 1</returns>
		static public int SignFor(DocType docType) {
				switch (docType) {
					case DocType.Invoice:
					case DocType.Payment:
					case DocType.Credit:
					case DocType.Deposit:
					case DocType.CreditCardCredit:
					case DocType.GeneralJournal:
					case DocType.Sell:
						return -1;
					default: 
						return 1;
				}
		}

		/// <summary>
		/// Save an arbitrary JObject to the database, optionally also saving an audit trail
		/// </summary>
		public AjaxReturn PostRecord(JsonObject record, bool audit) {
			AjaxReturn retval = new AjaxReturn();
			try {
				Database.Update(record, audit);
				retval.id = record.Id;
			} catch (Exception ex) {
				Message = ex.Message;
				retval.error = ex.Message;
			}
			return retval;
		}

		/// <summary>
		/// Write the response to an Http request.
		/// </summary>
		/// <param name="o">The object to write ("Operation complete" if null)</param>
		/// <param name="contentType">The content type (suitable default is used if null)</param>
		/// <param name="status">The Http return code</param>
		public void WriteResponse(object o, string contentType, HttpStatusCode status) {
			if (ResponseSent) throw new CheckException("Response already sent");
			ResponseSent = true;
			Response.StatusCode = (int)status;
			Response.ContentEncoding = Encoding;
			switch (contentType) {
				case "text/plain":
				case "text/html":
					contentType += ";charset=" + Charset;
					break;
			}
			string logStatus = status.ToString();
			byte[] msg;
			if (o != null) {
				if (o is Stream) {
					// Stream is sent unchanged
					Response.ContentType = contentType ?? "application/binary";
					Response.ContentLength64 = ((Stream)o).Length;
					Log("{0}:{1} bytes ", status, Response.ContentLength64);
					using (Stream r = Response.OutputStream) {
						((Stream)o).CopyTo(r);
					}
					return;
				} else if (o is string) {
					// String is sent unchanged
					msg = Encoding.GetBytes((string)o);
					Response.ContentType = contentType ?? "text/plain;charset=" + Charset;
				} else {
					// Anything else is sent as json
					Response.ContentType = contentType ?? "application/json;charset=" + Charset;
					msg = Encoding.GetBytes(o.ToJson());
					if (o is AjaxReturn)
						logStatus = o.ToString();
				}
			} else {
				msg = Encoding.GetBytes("Operation complete");
				Response.ContentType = contentType ?? "text/plain;charset=" + Charset;
			}
			Response.ContentLength64 = msg.Length;
			Log("{0}:{1} bytes ", logStatus, Response.ContentLength64);
			using (Stream r = Response.OutputStream) {
				r.Write(msg, 0, msg.Length);
			}
		}

	}

	/// <summary>
	/// Class to serve files
	/// </summary>
	public class FileSender : AppModule {
		string _filename;

		public FileSender(string filename) {
			_filename = filename;
		}

		public override void Default() {
			Title = "";
			FileInfo file = Utils.FileInfoForUrl(_filename);
			if (!file.Exists) {
				WriteResponse("", "text/plain", HttpStatusCode.NotFound);
				return;
			}
			string ifModifiedSince = Request.Headers["If-Modified-Since"];
			if (!string.IsNullOrEmpty(ifModifiedSince)) {
				try {
					DateTime modifiedSince = DateTime.Parse(ifModifiedSince.Split(';')[0]);
					if (modifiedSince >= file.LastWriteTimeUtc) {
						WriteResponse("", "text/plain", HttpStatusCode.NotModified);
						return;
					}
				} catch {
				}
			}
			using (Stream i = file.OpenRead()) {
				string contentType;
				switch (Path.GetExtension(_filename).ToLower()) {
					case ".htm":
					case ".html":
						contentType = "text/html";
						break;
					case ".css":
						contentType = "text/css";
						break;
					case ".js":
						contentType = "text/javascript";
						break;
					case ".xml":
						contentType = "text/xml";
						break;
					case ".bmp":
						contentType = "image/bmp";
						break;
					case ".gif":
						contentType = "image/gif";
						break;
					case ".jpg":
						contentType = "image/jpeg";
						break;
					case ".jpeg":
						contentType = "image/jpeg";
						break;
					case ".png":
						contentType = "image/x-png";
						break;
					case ".txt":
						contentType = "text/plain";
						break;
					case ".doc":
						contentType = "application/msword";
						break;
					case ".pdf":
						contentType = "application/pdf";
						break;
					case ".xls":
						contentType = "application/x-msexcel";
						break;
					case ".wav":
						contentType = "audio/x-wav";
						break;
					default:
						contentType = "application/binary";
						break;
				}
				Response.AddHeader("Last-Modified", file.LastWriteTimeUtc.ToString("r"));
				WriteResponse(i, contentType, HttpStatusCode.OK);
			}
		}
	}

	/// <summary>
	/// Class to hold details of an uploaded file (from an <input type="file" />)
	/// </summary>
	public class UploadedFile {

		public UploadedFile(string name, string content) {
			Name = name;
			Content = content;
		}

		/// <summary>
		/// File contents - Windows1252 was used to read it in, so saving it as Windows1252 will be an exact binary copy
		/// </summary>
		public string Content { get; private set; }

		public string Name { get; private set; }

		/// <summary>
		/// The file contents as a stream
		/// </summary>
		public Stream Stream() {
			return new MemoryStream(Encoding.GetEncoding(1252).GetBytes(Content));
		}
	}

	/// <summary>
	/// Menu option for the second level menu
	/// </summary>
	public class MenuOption {
		public MenuOption(string text, string url) : this(text, url, true) {
		}

		public MenuOption(string text, string url, bool enabled) {
			Text = text;
			Url = url;
			Enabled = enabled;
		}

		public bool Disabled { 
			get { return !Enabled; } 
		}

		public bool Enabled;

		/// <summary>
		/// Html element id - text with no spaces
		/// </summary>
		public string Id {
			get { return Text.Replace(" ", ""); }
		}

		public string Text;

		public string Url;
	}

	/// <summary>
	/// Generic return type used for Ajax requests
	/// </summary>
	public class AjaxReturn {
		/// <summary>
		/// Exception message - if not null or empty, request has failed
		/// </summary>
		public string error;
		/// <summary>
		/// Message for user
		/// </summary>
		public string message;
		/// <summary>
		/// Where to redirect to on completion
		/// </summary>
		public string redirect;
		/// <summary>
		/// Ask the user to confirm something, and resubmit with confirm parameter if the user says yes
		/// </summary>
		public string confirm;
		/// <summary>
		/// If a record has been saved, this is the id of the record.
		/// Usually used to re-read the page, especially when the request was to create a new record.
		/// </summary>
		public int? id;
		/// <summary>
		/// Arbitrary data which the caller needs
		/// </summary>
		public object data;

		public override string ToString() {
			StringBuilder b = new StringBuilder("AjaxReturn");
			if (!string.IsNullOrEmpty(error)) b.AppendFormat(",error:{0}", error);
			if (!string.IsNullOrEmpty(message)) b.AppendFormat(",message:{0}", message);
			if (!string.IsNullOrEmpty(confirm)) b.AppendFormat(",confirm:{0}", confirm);
			if (!string.IsNullOrEmpty(redirect)) b.AppendFormat(",redirect:{0}", redirect);
			return b.ToString();
		}
	}

}
