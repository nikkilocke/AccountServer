using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// File import
	/// </summary>
	public class Importer {
		protected AppModule _module;
		protected Table _table;
		/// <summary>
		/// For detecting duplicate keys
		/// </summary>
		protected HashSet<string> _keys;

		public Importer(string name, string tableName, params ImportField [] fields) {
			Name = name;
			TableName = tableName;
			Fields = fields;
			foreach (ImportField f in fields)
				f.Importer = this;
		}

		/// <summary>
		/// Expected date format, or null to use DateTime.Parse
		/// </summary>
		public string DateFormat;

		public ImportField[] Fields { get; private set; }

		/// <summary>
		/// Set up import, import the data, commit to the database if no errors
		/// </summary>
		public void Import(CsvParser csv, AppModule module) {
			lock (this) {
				_module = module;
				if (!string.IsNullOrEmpty(TableName))
					_table = _module.Database.TableFor(TableName);
				_keys = new HashSet<string>();
				ImportData(csv);
				_module.Database.Commit();
			}
		}

		/// <summary>
		/// Import the actual data
		/// </summary>
		public virtual void ImportData(CsvParser csv) {
			if (_module.Batch != null)
				_module.Batch.Status += TableName;
			foreach (JObject dataIn in csv.Read()) {
				ImportDataLine(dataIn);
			}
		}

		/// <summary>
		/// Import 1 line of data
		/// </summary>
		public virtual void ImportDataLine(JObject dataIn) {
			JObject dataOut = new JObject();
			foreach (ImportField field in Fields) {
				if(!string.IsNullOrEmpty(field.OurName))
					dataOut[field.OurName] = field.Value(_module.Database, dataIn);
			}
			Update(dataOut);
		}

		/// <summary>
		/// Given a file, read its headers, and find a suitable importer for it
		/// </summary>
		public static Importer ImporterFor(CsvParser csv) {
			return Importers.FirstOrDefault(i => i.Matches(csv));
		}

		/// <summary>
		/// All the available importers
		/// </summary>
		public static Importer[] Importers = new Importer[] {
				new IIFImporter(),
				new Importer("Vat Code List", "VatCode", 
					new ImportField("Code", "VAT Code"),
					new ImportField("VatDescription", "Description"),
					new ImportRegex("Rate", "Rate", @"([0-9\.]*)")
				),
				new Importer("Product List", "Product", 
					new ImportField("ProductName", "Item"),
					new ImportField("ProductDescription", "Description"),
					new ImportRegex("UnitPrice", "Price", @"([0-9\.]*)"),
					new ImportForeignKey("VatCodeId", "Sales Tax Code", "VatCode", "Code"),
					new ImportForeignKey("AccountId", "Account", "Account", "AccountName")
				),
				new AccountsImporter("Account List", "Account", 
					new ImportField("AccountName", "Account"),
					new ImportField("AccountDescription", "Description"),
					new ImportForeignKey("AccountTypeId", "Type", "AccountType", "AcctType")
				),
				new Importer("Customer List", "NameAddress", 
					new ImportFixed("Type", "C"),
					new ImportField("Name", "Customer"),
					new ImportField("Address", "Bill to"),
					new ImportField("Telephone", "Phone"),
					new ImportField("Email", "Email"),
					new ImportField("Contact", "Contact")
				),
				new Importer("Supplier List", "NameAddress", 
					new ImportFixed("Type", "S"),
					new ImportField("Name", "Supplier"),
					new ImportField("Address", "Address"),
					new ImportField("Telephone", "Phone"),
					new ImportField("Email", "Email"),
					new ImportField("Contact", "Contact")
				),
				new JournalImporter()
			};

		/// <summary>
		/// Whether the csv file has all the fields required for this importer
		/// </summary>
		public bool Matches(CsvParser csv) {
			HashSet<string> fieldNames = new HashSet<string>(csv.Headers);
			foreach (ImportField f in Fields) {
				if (f.TheirName != null && !fieldNames.Contains(f.TheirName)) {
					System.Diagnostics.Trace.WriteLine("Does not match " + this.Name + " " + f.TheirName + " missing");
					return false;
				}
			}
			return true;
		}

		public string Name;

		public string TableName { get; protected set; }

		/// <summary>
		/// Save an individual record to the database
		/// </summary>
		public virtual void Update(JObject dataOut) {
			Index index = _table.IndexFor(dataOut);
			if (index != null) {
				string key = index.Where(dataOut);
				Utils.Check(!_keys.Contains(key), "Duplicate key in import {0}", key);
				_keys.Add(key);
			}
			_module.Database.Update(TableName, dataOut);
		}

		/// <summary>
		/// Special import for IIF files , which can contain multiple data sets
		/// </summary>
		public class IIFImporter : Importer {
			/// <summary>
			/// Importer for current data set
			/// </summary>
			Importer _importer;
			/// <summary>
			/// List of importers for all data sets
			/// </summary>
			Importer[] _importers;

			public IIFImporter()
				: base("IIF Import File", "", new ImportField("", "!HDR")) {
				_importers = new Importer[] {
				new Importer("Vat Code List", "VatCode", 
					new ImportField("", "VATCODE"),
					new ImportField("Code", "NAME"),
					new ImportField("VatDescription", "DESC"),
					new ImportRegex("Rate", "RATE", @"([0-9\.]*)")
				),
				new Importer("Product List", "Product", 
					new ImportField("", "INVITEM"),
					new ImportField("ProductName", "NAME"),
					new ImportField("ProductDescription", "DESC"),
					new ImportRegex("UnitPrice", "PRICE", @"([0-9\.]*)"),
					new ImportForeignKey("VatCodeId", "VATCODE", "VatCode", "Code"),
					new ImportForeignKey("AccountId", "ACCNT", "Account", "AccountName")
				),
				new AccountsImporter("Account List", "Account", 
					new ImportField("", "ACCNT"),
					new ImportField("AccountName", "NAME"),
					new ImportField("AccountDescription", "DESC"),
					new ImportACCNTTYPE()
				),
				new Importer("Customer List", "NameAddress", 
					new ImportField("", "CUST"),
					new ImportFixed("Type", "C"),
					new ImportField("Name", "NAME"),
					new ImportAddress("Address", "BADDR1", "BADDR2", "BADDR3", "BADDR4", "BADDR5"),
					new ImportField("Telephone", "PHONE1"),
					new ImportField("Email", "EMAIL"),
					new ImportField("Contact", "CONT1")
				),
				new Importer("Supplier List", "NameAddress", 
					new ImportField("", "VEND"),
					new ImportFixed("Type", "S"),
					new ImportField("Name", "NAME"),
					new ImportAddress("Address", "ADDR1", "ADDR2", "ADDR3", "ADDR4", "ADDR5"),
					new ImportField("Telephone", "PHONE1"),
					new ImportField("Email", "EMAIL"),
					new ImportField("Contact", "CONT1")
				),
			};
			}

			/// <summary>
			/// Look for "!" lines (which start a new data set), and import each data set
			/// </summary>
			public override void ImportData(CsvParser csv) {
				_importer = null;
				csv.PermitAnyFieldCount = true;
				string delimiter = "";
				TableName = "";
				foreach (JObject dataIn in csv.Read()) {
					string[] line = csv.Data;
					if (line[0].StartsWith("!")) {
						// New data set - remove the ! from the first field
						line[0] = line[0].Substring(1);
						// This will be the headers for the new import
						csv.Headers = line;
						_importer = _importers.FirstOrDefault(i => i.Matches(csv));
						if (_importer != null) {
							// We want this data set - set up the importer, and tell the user
							_importer._module = _module;
							_importer._keys = new HashSet<string>();
							_importer.DateFormat = DateFormat;
							if (_module.Batch != null) {
								_module.Batch.Status += delimiter + _importer.TableName;
							}
							TableName += delimiter + _importer.TableName;
							delimiter = ",";
						}
						continue;
					}
					if (_importer != null)
						_importer.ImportDataLine(dataIn);
				}
			}

		}
	}

	/// <summary>
	/// Special importer for QuickBooks transaction detail report
	/// </summary>
	public class JournalImporter : Importer {
		int _tranId;
		int _line;
		int _vat;
		decimal _vatAmount;
		int _lastInvoiceNumber;
		int _lastBillNumber;
		int _lastJournalNumber;
		Dictionary<int, int> _lastChequeNumber;
		Dictionary<int, int> _lastDepositNumber;

		public JournalImporter()
			: base("Transaction Detail Report", "Document", 
			new ImportField("idDocument", "Trans no"),
			new ImportDocumentType("DocumentTypeId", "Type"),
			new ImportField("DocumentDate", "Date"),
			new ImportField("DocumentIdentifier", "Num"),
			new ImportName("NameAddressId", "Name"),
			new ImportField("Address1", "Address 1"),
			new ImportField("Address2", "Address 2"),
			new ImportField("Address3", "Address 3"),
			new ImportField("Address4", "Address 4"),
			new ImportField("Address5", "Address 5"),
			new ImportField("DocumentMemo", "Memo"),
			new ImportForeignKey("ProductId", "Item", "Product", "ProductName"),
			new ImportAccount("AccountId", "Account"),
			new ImportField("Cleared", "Clr"),
			new ImportField("Outstanding", "Open Balance"),
			new ImportField("Qty", "Qty"),
			new ImportForeignKey("VatCodeId", "VAT Code", "VatCode", "Code"),
			new ImportRegex("VatRate", "VAT Rate", @"([0-9\.]*)"),
			new ImportField("VatAmount", "VAT Amount"),
			new ImportField("Amount", "Amount")
			) {
			_lastChequeNumber = new Dictionary<int, int>();
			_lastDepositNumber = new Dictionary<int, int>();
		}

		public override void ImportData(CsvParser csv) {
			_tranId = 0;
			_line = 0;
			// Do the import
			base.ImportData(csv);
			// Now update the last cheque numbers, etc.
			Settings settings = _module.Settings;
			settings.RegisterNumber(_module, (int)DocType.Invoice, _lastInvoiceNumber);
			settings.RegisterNumber(_module, (int)DocType.Bill, _lastBillNumber);
			settings.RegisterNumber(_module, (int)DocType.GeneralJournal, _lastJournalNumber);
			foreach (int k in _lastChequeNumber.Keys.Union(_lastDepositNumber.Keys)) {
				FullAccount acct = _module.Database.Get<FullAccount>(k);
				int n;
				bool save = false;
				if (_lastChequeNumber.TryGetValue(k, out n))
					save |= acct.RegisterNumber(DocType.Cheque, n);
				if (_lastDepositNumber.TryGetValue(k, out n))
					save |= acct.RegisterNumber(DocType.Deposit, n);
				if (save)
					_module.Database.Update(acct);
			}
			// Try to guess what VAT payments apply to each document
			foreach(JObject vatPayment in ((AppModule)_module).SelectVatPayments()) {
				int id = vatPayment.AsInt("id");
				DateTime q = settings.QuarterStart(vatPayment.AsDate("value"));
				_module.Database.Execute(@"UPDATE Document 
JOIN Journal ON IdDocument = DocumentId
JOIN Line ON idLine = idJournal
SET VatPaid = " + id + @"
WHERE (DocumentTypeId IN (1, 3, 4, 6) OR Line.VatCodeId IS NOT NULL)
AND VatPaid = 0
AND idDocument < " + id + @"
AND DocumentDate < " + _module.Database.Quote(q));
				_module.Database.Execute("UPDATE Document SET VatPaid = " + id + " WHERE idDocument = " + id);
			}
			// Set the remainder to null (importer doesn't do nulls)
			_module.Database.Execute(@"UPDATE Document SET VatPaid = NULL WHERE VatPaid = 0");
		}

		/// <summary>
		/// Update a journal line
		/// </summary>
		public override void Update(JObject dataOut) {
			if (dataOut["AccountId"] == null) return;	// Can't post if no account
			int id = dataOut.AsInt("idDocument");
			if (id == 0) 
				return;			// Can't post if no document
			bool newTran = id != _tranId;
			DocType docType = (DocType)dataOut.AsInt("DocumentTypeId");
			if (newTran) {
				// New document
				_vat = 0;
				_vatAmount = 0;
				dataOut["DocumentAddress"] = string.Join("\r\n", Enumerable.Range(1, 5).Select(i => dataOut.AsString("Address" + i)).Where(s => !string.IsNullOrEmpty(s)).ToArray());
				if (!_module.Database.RecordExists("Document", dataOut.AsInt("idDocument"))) {
					dataOut["VatPaid"] = 0;
				}
				base.Update(dataOut);		// Save the document
				_tranId = id;
				_line = 0;
				// Save the last invoice/cheque/etc. no
				int number = Utils.ExtractNumber(dataOut.AsString("DocumentIdentifier"));
				switch (docType) {
					case DocType.Invoice:
					case DocType.CreditMemo:
						if (number > _lastInvoiceNumber) _lastInvoiceNumber = number;
						break;
					case DocType.Bill:
					case DocType.Credit:
						if (number > _lastBillNumber) _lastBillNumber = number;
						break;
					case DocType.Cheque:
					case DocType.CreditCardCharge:
						registerNumber(_lastChequeNumber, dataOut.AsInt("AccountId"), number);
						break;
					case DocType.Deposit:
					case DocType.CreditCardCredit:
						registerNumber(_lastDepositNumber, dataOut.AsInt("AccountId"), number);
						break;
					case DocType.GeneralJournal:
						if (number > _lastJournalNumber) _lastJournalNumber = number;
						break;
				}
				// Delete any existing lines
				_module.Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + _tranId + ")");
				_module.Database.Execute("DELETE FROM Journal WHERE DocumentId = " + _tranId);
			}
			dataOut["DocumentId"] = _tranId;
			dataOut["JournalNum"] = ++_line;
			dataOut["Memo"] = dataOut["DocumentMemo"];
			_module.Database.Update("Journal", dataOut);	// Save the journal
			if (dataOut.AsInt("AccountId") == (int)Acct.VATControl) {
				// This is the VAT journal
				_vatAmount += dataOut.AsDecimal("Amount");
				if (_vat != 0) {
					// There is already a VAT journal - delete it
					_module.Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + _tranId + " AND JournalNum = " + _vat + ")");
					_module.Database.Execute("DELETE FROM Journal WHERE DocumentId = " + _tranId + " AND JournalNum = " + _vat);
					_module.Database.Execute("UPDATE Journal SET JournalNum = JournalNum - 1 WHERE DocumentId = " + _tranId + " AND JournalNum > " + _vat);
					// Bump this journal a line earlier
					dataOut["JournalNum"] = --_line;
					dataOut["Amount"] = _vatAmount;
					_module.Database.Update("Journal", dataOut);
				}
				_vat = _line;	// Remember, to avoid 2 VAT lines
			}
			// 2nd and subsequent journals (except VAT journal) have lines
			// NB VAT Payments to HMRC have are exceptions - they have a line for the VAT payment
			if (!newTran && (_line == 2 || dataOut.AsInt("AccountId") != (int)Acct.VATControl)) {
				int sign = AppModule.SignFor(docType);
				dataOut["idLine"] = dataOut["idJournal"];
				dataOut["LineAmount"] = sign * dataOut.AsDecimal("Amount");
				dataOut["Qty"] = sign * dataOut.AsDecimal("Qty");
				dataOut["VatRate"] = dataOut.AsDecimal("VatRate");
				dataOut["VatAmount"] = sign * dataOut.AsDecimal("VatAmount");
				_module.Database.Update("Line", dataOut);
			}
		}

		void registerNumber(Dictionary<int, int> dict, int account, int number) {
			if(number == 0)
				return;
			int current;
			if (dict.TryGetValue(account, out current) && current >= number)
				return;
			dict[account] = number;
		}
	}

	/// <summary>
	/// Importer for accounts - checks subaccount names for duplicates as well.
	/// This is because the  Quick Books transaction detail report does not give the full account name
	/// </summary>
	public class AccountsImporter : Importer {
		public AccountsImporter(string name, string tableName, params ImportField[] fields)
			: base(name, tableName, fields) {
		}

		public override void Update(JObject dataOut) {
			base.Update(dataOut);
			string name = dataOut.AsString("AccountName");
			Utils.Check(!_keys.Contains(name), "Account {0} has duplicate name", name);
			_keys.Add(name);
			string [] parts = name.Split(':');
			if(parts.Length > 1) {
				string key = parts[parts.Length - 1];
				Utils.Check(!_keys.Contains(key), "Subaccount of {0} has duplicate name {1}", name, key);
				_keys.Add(key);
			}
		}

	}

	/// <summary>
	/// A field to import
	/// </summary>
	public class ImportField {

		public ImportField(string ourName, string theirName) {
			OurName = ourName;
			TheirName = theirName;
		}

		public Importer Importer;

		public string OurName { get; private set; }

		public string TheirName { get; private set; }

		public override string ToString() {
			return base.ToString() + " " + TheirName + "=>" + OurName;
		}

		public virtual JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			return data[TheirName];
		}
	}

	/// <summary>
	/// Parses dates according to DateFormat
	/// </summary>
	public class ImportDate : ImportField {
		public ImportDate(string ourName, string theirName)
			: base(ourName, theirName) {
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			string d = data.AsString(TheirName);
			return string.IsNullOrWhiteSpace(d) ? (JToken)null : string.IsNullOrWhiteSpace(Importer.DateFormat) ? DateTime.Parse(d) : DateTime.ParseExact(d, Importer.DateFormat, System.Globalization.CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// Import field with pattern to extract part of their value to use for our value
	/// </summary>
	public class ImportRegex : ImportField {
		Regex _regex;

		public ImportRegex(string ourName, string theirName, string regex) : base(ourName, theirName) {
			_regex = new Regex(regex, RegexOptions.Compiled);
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			return _regex.Match(data[TheirName].ToString()).Groups[1].Value;
		}
	}

	/// <summary>
	/// Import field which is a key on another table
	/// </summary>
	public class ImportForeignKey : ImportField {

		public ImportForeignKey(string ourName, string theirName, string table, string foreignKey)
			: base(ourName, theirName) {
			Table = table;
			ForeignKey = foreignKey;
		}

		public string ForeignKey { get; private set; }

		public string Table { get; private set; }

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			JObject keyData = new JObject().AddRange(ForeignKey, base.Value(db, data));
			return db.ForeignKey(Table, keyData);
		}
	}

	/// <summary>
	/// IIF import ACCNTTYPE recogniser
	/// </summary>
	public class ImportACCNTTYPE : ImportField {
		public ImportACCNTTYPE()
			: base("AccountTypeId", "ACCNTTYPE") {
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			string value = base.Value(db, data).ToString();
			switch (value) {
				case "INC": value = "Income"; break;
				case "EXP": value = "Expense"; break;
				case "COGS": value = "Expense"; break;	// ???
				case "EXINC": value = "Other Income"; break;
				case "EXEXP": value = "Other Expense"; break;
				case "FIXASSET": value = "Fixed Asset"; break;
				case "OASSET": value = "Other Asset"; break;
				case "AR": value = "Accounts Receivable"; break;
				case "BANK": value = "Bank"; break;
				case "OCASSET": value = "Other Current Asset"; break;
				case "CCARD": value = "Credit Card"; break;
				case "AP": value = "Accounts Payable"; break;
				case "OCLIAB": value = "Other Current Liability"; break;
				case "LTLIAB": value = "Long Term Liability"; break;
				case "OLIAB": value = "Other Liability"; break;	// No example found
				case "EQUITY": value = "Equity"; break;
				default:
					throw new CheckException("Unknown account type {0}", value);
			}
			JObject keyData = new JObject().AddRange("AcctType", value);
			return db.ForeignKey("AccountType", keyData);
		}
	}

	/// <summary>
	/// Transaction detail report abbreviates DocType
	/// </summary>
	public class ImportDocumentType : ImportField {
		public ImportDocumentType(string ourName, string theirName)
			: base(ourName, theirName) {
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			string value = base.Value(db, data).ToString();
			if (value.IndexOf("Bill Pmt") == 0)
				value = "Bill Payment";
			JObject keyData = new JObject().AddRange("DocType", value);
			return db.ForeignKey("DocumentType", keyData);
		}
	}

	/// <summary>
	/// Name types depend on what kind of record we are processing
	/// </summary>
	public class ImportName : ImportField {
		public ImportName(string ourName, string theirName)
			: base(ourName, theirName) {
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			string nameType;
			switch (data.AsString("Account")) {
				case "Purchase Ledger":
					nameType = "S";
					break;
				case "Sales Ledger":
					nameType = "C";
					break;
				default:
					string type = data.AsString("Type");
					switch (type) {
						case "Invoice":
						case "Payment":
						case "Credit Memo":
							nameType = "C";
							break;
						case "Bill":
						case "Bill Pmt":
						case "Credit":
							nameType = "S";
							break;
						default:
							nameType = type.IndexOf("Bill Pmt") == 0 ? "S" : "O";
							break;
					}
					break;
			}
			JObject keyData = new JObject().AddRange("Type", nameType, "Name", base.Value(db, data));
			return db.ForeignKey("NameAddress", keyData);
		}
	}

	/// <summary>
	/// Transaction detail report does not give full account name, just subaccount
	/// </summary>
	public class ImportAccount : ImportField {
		Regex _r = new Regex(@"(.*?)( \([^)]*\))?$", RegexOptions.Compiled);

		public ImportAccount(string ourName, string theirName)
			: base(ourName, theirName) {
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			Match m = _r.Match(base.Value(db, data).ToString());
			string ac = m.Groups[1].Value;
			if (ac == "") return null;
			JObject id = db.QueryOne("SELECT idAccount FROM Account WHERE AccountName = " + db.Quote(ac));
			if (id != null) return id["idAccount"];
			id = db.QueryOne("SELECT idAccount FROM Account WHERE AccountName LIKE " + db.Quote("%:" + ac));
			if (id != null) return id["idAccount"];
			throw new CheckException("Account '{0}' not found", ac);
		}
	}

	/// <summary>
	/// This field always has the same value
	/// </summary>
	public class ImportFixed : ImportField {
		JToken _value;

		public ImportFixed(string ourName, object value)
			: base(ourName, null) {
			_value = value.ToJToken();
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			return _value;
		}
	}

	/// <summary>
	/// IIF address import combines multiple fields into address
	/// </summary>
	public class ImportAddress : ImportField {
		string[] _theirNames;

		public ImportAddress(string ourName, params string[] theirNames)
			: base(ourName, theirNames[0]) {
				_theirNames = theirNames;
		}

		public override JToken Value(CodeFirstWebFramework.Database db, JObject data) {
			return string.Join("\r\n", _theirNames.Select(n => data.AsString(n)).Where(d => !string.IsNullOrEmpty(d)).ToArray());
		}
	}
}
