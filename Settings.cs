using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	public class AppSettings {
		public string Database = "SQLite";
		public string ConnectionString = "Data Source=" + Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).Replace(@"\", "/") 
			+ @"/AccountsServer/AccountServer.db";
		[JsonIgnore]
		public string Filename;
		public int Port = 8080;
		public int SlowQuery = 100;
		public string WebFolder = "html";
		public bool SessionLogging;
		public int DatabaseLogging;
		public bool PostLogging;
		static public NameValueCollection CommandLineFlags;

		public static AppSettings Default = new AppSettings();

		public void Save(string filename) {
			using (StreamWriter w = new StreamWriter(filename))
				w.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
		}

		static public void Load(string filename) {
			WebServer.Log("Loading config from {0}", filename);
			using (StreamReader s = new StreamReader(filename)) {
				Default = Utils.JsonTo<AppSettings>(s.ReadToEnd());
				Default.Filename = Path.GetFileNameWithoutExtension(filename);
			}
		}

		public static DateTime YearEnd(DateTime date) {
			date = date.Date;
			DateTime result = yearStart(date);
			if (result <= date)
				result = yearStart(result.AddMonths(13));
			return result.AddDays(-1);
		}

		public static DateTime YearStart(DateTime date) {
			date = date.Date;
			DateTime result = yearStart(date);
			if (result > date)
				result = yearStart(date.AddMonths(-12));
			return result;
		}

		public static DateTime QuarterStart(DateTime date) {
			DateTime result = YearStart(date);
			result = result.AddDays(1 - (int)result.Day);
			for (DateTime end = result.AddMonths(3); end < date; end = result.AddMonths(3))
				result = end;
			return result;
		}

		static DateTime yearStart(DateTime date) {
			int month = 1;
			if (month == 0) month = 1;
			int day = 0;
			// First day of the month
			DateTime dayOfMonth = new DateTime(date.Year, month, 1);
			if (day > 0) {
				DayOfWeek dayOfWeek = (DayOfWeek)(day % 7);

				// Find first dayOfWeek of this month
				if (dayOfMonth.DayOfWeek > dayOfWeek) {
					dayOfMonth = dayOfMonth.AddDays(7 - (int)dayOfMonth.DayOfWeek + (int)dayOfWeek);
				} else {
					dayOfMonth = dayOfMonth.AddDays((int)dayOfWeek - (int)dayOfMonth.DayOfWeek);
				}
			}
			return dayOfMonth;
		}
	}

	public class Admin : AppModule {

		public Admin() {
			Menu = new MenuOption[] {
				new MenuOption("Settings", "/admin/default.html"),
				new MenuOption("Integrity Check", "/admin/integritycheck.html"),
				new MenuOption("Import", "/admin/import.html"),
				new MenuOption("Backup", "/admin/backup.html"),
				new MenuOption("Restore", "/admin/restore.html")
			};
		}

		public override void Default() {
			JObject header = _settings.ToJObject();
			header.Add("YearStart", Settings.YearStart(Utils.Today));
			header.Add("YearEnd", Settings.YearEnd(Utils.Today));
			string skinFolder = Path.Combine(Config.WebFolder, "skin");
			Record = new JObject().AddRange("header", header,
				"BankAccounts", new Select().BankAccount(""),
				"Skins", Directory.EnumerateFiles(skinFolder, "*.css")
						.Where(f => File.Exists(Path.ChangeExtension(f, ".js")))
						.Select(f => new { value = Path.GetFileNameWithoutExtension(f) })
					);
		}

		public AjaxReturn DefaultPost(Settings json) {
			Database.BeginTransaction();
			Database.Update(json);
			_settings = Database.QueryOne<Settings>("SELECT * FROM Settings");
			Database.Commit();
			return new AjaxReturn() { message = "Settings saved", redirect = "/Admin" };
		}

		public AjaxReturn BatchStatus(int id) {
			AjaxReturn result = new AjaxReturn();
			BatchJob batch = AppModule.GetBatchJob(id);
			if (batch == null) {
				Log("Invalid batch id");
				result.error = "Invalid batch id";
			} else {
				if (batch == null) {
					Log("Invalid batch id");
					result.error = "Invalid batch id";
				} else {
					Log("Batch {0}:{1}%:{2}", batch.Id, batch.PercentComplete, batch.Status);
					result.data = batch;
					if (batch.Finished) {
						result.error = batch.Error;
						result.redirect = batch.Redirect;
						Log("Batch finished - redirecting to {0}", batch.Redirect);
					}
				}
			}
			return result;
		}

		public void Import() {
		}

		public void ImportFile(UploadedFile file, string dateFormat) {
			Method = "Import";
			Stream s = null;
			try {
				s = file.Stream();
				if (Path.GetExtension(file.Name).ToLower() == ".qif") {
					QifImporter qif = new QifImporter();
					new ImportBatchJob(this, qif, delegate() {
						try {
							Batch.Records = file.Content.Length;
							Batch.Status = "Importing file " + file.Name + " as QIF";
							Database.BeginTransaction();
							qif.DateFormat = dateFormat;
							qif.Import(new StreamReader(s), this);
							Database.Commit();
						} catch (Exception ex) {
							throw new CheckException(ex, "Error at line {0}\r\n{1}", qif.Line, ex.Message);
						} finally {
							s.Dispose();
						}
						Message = "File " + file.Name + " imported successfully as QIF";
					});
				} else {
					CsvParser csv = new CsvParser(new StreamReader(s));
					Importer importer = Importer.ImporterFor(csv);
					Utils.Check(importer != null, "No importer for file {0}", file.Name);
					new ImportBatchJob(this, csv, delegate() {
						try {
							Batch.Records = file.Content.Length;
							Batch.Status = "Importing file " + file.Name + " as " + importer.Name + " to ";
							Database.BeginTransaction();
							importer.DateFormat = dateFormat;
							importer.Import(csv, this);
							Database.Commit();
						} catch (Exception ex) {
							throw new CheckException(ex, "Error at line {0}\r\n{1}", csv.Line, ex.Message);
						} finally {
							s.Dispose();
						}
						Message = "File " + file.Name + " imported successfully as " + importer.Name + " to " + importer.TableName;
					});
				}
			} catch (Exception ex) {
				Log(ex.ToString());
				Message = ex.Message;
				if (s != null)
					s.Dispose();
			}
		}

		class ImportBatchJob : BatchJob {
			FileProcessor _file;
			bool _recordReset;

			public ImportBatchJob(AppModule module, FileProcessor file, Action action)
				: base(module, action) {
				_file = file;
			}

			public override int Record {
				get {
					return _recordReset ? base.Record : _file.Character;
				}
				set {
					base.Record = value;
					_recordReset = true;
				}
			}
		}

		public Importer[] Importers {
			get { return Importer.Importers; }
		}

		public void ImportHelp() {
		}

		public void IntegrityCheck() {
			List<string> errors = new List<string>();
			foreach (JObject r in Database.Query(@"SELECT * FROM 
(SELECT DocumentId, SUM(Amount) AS Amount 
FROM Journal 
GROUP BY DocumentId) AS r 
LEFT JOIN Document ON idDocument = DocumentId 
LEFT JOIN DocumentType ON idDocumentType = DocumentTypeId
WHERE Amount <> 0"))
				errors.Add(string.Format("{0} {1} {2} {3:d} does not balance {4:0.00}", r.AsString("DocType"),
					r.AsString("DocumentId"), r.AsString("DocumentIdentifier"), r.AsDate("DocumentDate"), r.AsDecimal("Amount")));
			foreach (JObject r in Database.Query(@"SELECT * FROM 
(SELECT NameAddressId, SUM(Amount) AS Amount, Sum(Outstanding) As Outstanding 
FROM Journal 
WHERE AccountId IN (1, 2) 
GROUP BY NameAddressId) AS r 
LEFT JOIN NameAddress ON idNameAddress = NameAddressId 
WHERE Amount <> Outstanding "))
				errors.Add(string.Format("Name {0} {1} {2} amount {3:0.00} does not equal outstanding {4:0.00} ",
					r.AsString("NameAddressId"), r.AsString("Type"), r.AsString("Name"), r.AsDecimal("Amount"), r.AsDecimal("Outstanding")));
			foreach (JObject r in Database.Query(@"SELECT * FROM 
(SELECT DocumentId, COUNT(idJournal) AS JCount, MAX(JournalNum) AS MaxJournal, COUNT(idLine) AS LCount 
FROM Journal 
LEFT JOIN Line ON idLine = idJournal 
GROUP BY DocumentId) AS r 
LEFT JOIN Document ON idDocument = DocumentId
LEFT JOIN DocumentType ON idDocumentType = DocumentTypeId
WHERE JCount < LCount + 1 
OR JCount > LCount + 2 
OR JCount != MaxJournal"))
				errors.Add(string.Format("{0} {1} {2} {3:d} Journals={4} Lines={5} Max journal num={6}", r.AsString("DocType"),
					r.AsString("DocumentId"), r.AsString("DocumentIdentifier"), r.AsDate("DocumentDate"), r.AsInt("JCount"), 
					r.AsInt("LCount"), r.AsInt("MaxJournal")));
			if (errors.Count == 0)
				errors.Add("No errors");
			Record = errors;
		}

		public void Backup() {
			Database.Logging = LogLevel.None;
			Database.BeginTransaction();
			DateTime now = Utils.Now;
			JObject result = new JObject().AddRange("BackupDate", now.ToString("yyyy-MM-dd HH:mm:ss"));
			foreach (string name in Database.TableNames) {
				result.Add(name, Database.Query("SELECT * FROM " + name));
			}
			Response.AddHeader("Content-disposition", "attachment; filename=AccountsBackup-" + now.ToString("yyyy-MM-dd-HH-mm-ss") + ".json");
			WriteResponse(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented), "application/json", System.Net.HttpStatusCode.OK);
		}

		public void Restore() {
			if (PostParameters != null && PostParameters["file"] != null) {
				new BatchJob(this, delegate() {
					Batch.Status = "Loading new data";
					UploadedFile data = PostParameters.As<UploadedFile>("file");
					Database.Logging = LogLevel.None;
					Database.BeginTransaction();
					JObject d = data.Content.JsonTo<JObject>();
					List<Table> tables = Database.TableNames.Select(n => Database.TableFor(n)).ToList();
					Batch.Records = tables.Count * 4;
					foreach (Table t in tables) {
						if (d[t.Name] != null) {
							Batch.Records += ((JArray)d[t.Name]).Count;
						}
					}
					Batch.Status = "Deleting existing data";
					TableList orderedTables = new TableList(tables);
					foreach(Table t in orderedTables) {
						Database.Execute("DELETE FROM " + t.Name);
						Batch.Record += 4;
					}
					Database.Logging = LogLevel.None;
					foreach (Table t in orderedTables.Reverse<Table>()) {
						if (d[t.Name] != null) {
							Batch.Status = "Restoring " + t.Name + " data";
							foreach (JObject record in (JArray)d[t.Name]) {
								Database.Insert(t.Name, record);
								Batch.Record++;
							}
						}
					}
					Batch.Status = "Checking database version";
					Database.Upgrade();
					Database.Commit();
					Batch.Status = "Compacting database";
					Database.Clean();
					_settings = Database.QueryOne<Settings>("SELECT * FROM Settings");
					Batch.Status = Message = "Database restored successfully";
				});
			}
		}

	}
}
