using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	public class AdminModule : AppModule {

		public AdminModule() {
			Menu = new MenuOption[] {
				new MenuOption("Settings", "/admin/default.html"),
				new MenuOption("Integrity Check", "/admin/integritycheck.html"),
				new MenuOption("Import", "/admin/import.html"),
				new MenuOption("Backup", "/admin/backup.html"),
				new MenuOption("Restore", "/admin/restore.html")
			};
		}

		public override void Default() {
			JObject header = Settings.ToJObject();
			header.Add("YearStart", Settings.YearStart(Utils.Today));
			header.Add("YearEnd", Settings.YearEnd(Utils.Today));
			DirectoryInfo skinFolder = Server.DirectoryInfo("skin");
			Record = new JObject().AddRange("header", header,
				"BankAccounts", SelectBankAccounts(),
				"Skins", skinFolder.EnumerateFiles("*.css")
						.Where(f => File.Exists(Path.ChangeExtension(f.FullName, ".js")))
						.Select(f => new { value = Path.GetFileNameWithoutExtension(f.Name) })
					);
		}

		public AjaxReturn DefaultPost(Settings json) {
			Database.BeginTransaction();
			Database.Update(json);
			ReloadSettings();
			Database.Commit();
			return new AjaxReturn() { message = "Settings saved", redirect = "/Admin" };
		}

		public AjaxReturn BatchStatus(int id) {
			return new AdminHelper(this).BatchStatus(id);
		}

		public void Backup() {
			new AdminHelper(this).Backup();
		}

		public void Restore() {
			new AdminHelper(this).Restore();
		}

		public void Import() {
		}

		public void ImportFile(UploadedFile file, string dateFormat) {
			Method = "import";
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

			public ImportBatchJob(CodeFirstWebFramework.AppModule module, FileProcessor file, Action action)
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
			foreach (JObject r in Database.Query(@"SELECT NameAddress.* FROM NameAddress
LEFT JOIN Member ON NameAddressId = idNameAddress
WHERE Type = 'M'
AND idMember IS NULL"))
				errors.Add(string.Format("{0} {1} member address is not associated with a member", r.AsString("idNameAddress"),
					r.AsString("Name")));
			if (errors.Count == 0)
				errors.Add("No errors");
			Record = errors;
		}

	}
}
