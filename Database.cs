using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Types of audit record
	/// </summary>
	public enum AuditType {
		Insert = 1,
		Update,
		Previous,	// The old version of the record on updates
		Delete,
		Reconcile
	};
	/// <summary>
	/// Account Types - these correspond to records in the AccountType table
	/// </summary>
	public enum AcctType {
		Income = 1,
		Expense,
		Security,
		OtherIncome,
		OtherExpense,
		FixedAsset,
		OtherAsset,
		AccountsReceivable,
		Bank,
		Investment,
		OtherCurrentAsset,
		CreditCard,
		AccountsPayable,
		OtherCurrentLiability,
		LongTermLiability,
		OtherLiability,
		Equity
	}
	/// <summary>
	/// Predefined accounts - these correspond to records in the Account table
	/// </summary>
	public enum Acct {
		SalesLedger = 1,
		PurchaseLedger,
		OpeningBalEquity,
		RetainedEarnings,
		ShareCapital,
		UndepositedFunds,
		UninvoicedSales,
		VATControl
	}
	/// <summary>
	/// Document Types - these correspond to records in the DocumentType table
	/// </summary>
	public enum DocType {
		Invoice = 1,
		Payment,
		CreditMemo,
		Bill,
		BillPayment,
		Credit,
		Cheque,
		Deposit,
		CreditCardCharge,
		CreditCardCredit,
		GeneralJournal,
		Transfer,
		OpeningBalance,
		Buy,
		Sell,
		Gain
	}
	/// <summary>
	/// For database logging
	/// </summary>
	public enum LogLevel {
		None = 0,
		Writes,
		Reads
	};
	/// <summary>
	/// Class for accessing the database
	/// </summary>
	public class Database : IDisposable {
		public const int CurrentDbVersion = 2;
		DbInterface _db;
		/// <summary>
		/// Data dictionary
		/// </summary>
		static Dictionary<string, Table> _tables;

		/// <summary>
		/// Upgrade a table to correspond to the class in the code
		/// </summary>
		/// <param name="code">Table info from code</param>
		/// <param name="database">Table info from database (or null, if none)</param>
		static void Upgrade(DbInterface db, Table code, Table database) {
			if (database == null) {
				db.CreateTable(code);
				return;
			}
			bool view = code is View;
			if (view != database is View) {
				// Class has switched from View to a Table, or vice versa
				db.DropTable(database);
				db.CreateTable(code);
				return;
			}
			if (view) {
				bool? result = db.ViewsMatch(code as View, database as View);
				if (result == true)
					return;
				if (result == false) {
					db.DropTable(database);
					db.CreateTable(code);
				return;
				}
			}
			// Lists of changes
			List<Field> insert = new List<Field>();
			List<Field> update = new List<Field>();
			List<Field> remove = new List<Field>();
			List<Field> insertFK = new List<Field>();
			List<Field> dropFK = new List<Field>();
			List<Index> insertIndex = new List<Index>();
			List<Index> dropIndex = new List<Index>();
			// Check all code fields are in database
			foreach (Field f1 in code.Fields) {
				Field f2 = database.FieldFor(f1.Name);
				if (f2 == null)
					insert.Add(f1);
				else {
					if (!db.FieldsMatch(code, f1, f2)) {
						update.Add(f1);
					}
					if (f1.ForeignKey == null) {
						if (f2.ForeignKey != null)
							dropFK.Add(f2);
					} else {
						if (f2.ForeignKey == null)
							insertFK.Add(f1);
						else if (f1.ForeignKey.Table.Name != f2.ForeignKey.Table.Name) {
							dropFK.Add(f2);
							insertFK.Add(f1);
						}
					}
				}
			}
			// Remove any database fields not in code
			foreach (Field f2 in database.Fields) {
				if (code.FieldFor(f2.Name) == null) {
					remove.Add(f2);
					if (f2.ForeignKey != null)
						dropFK.Add(f2);
				}
			}
			// Check all code indexes are in database
			foreach (Index i1 in code.Indexes) {
				Index i2 = database.Indexes.Where(i => i.FieldList == i1.FieldList).FirstOrDefault();
				if (i2 == null) {
					insertIndex.Add(i1);
				}
			}
			// Remove any indexes not in code
			foreach (Index i2 in database.Indexes) {
				if (code.Indexes.Where(i => i.FieldList == i2.FieldList).FirstOrDefault() == null)
					dropIndex.Add(i2);
			}
			if (view) {
				if (insert.Count == 0 && update.Count == 0 && remove.Count == 0)
					return;
				// View has changed - recreate it
				db.DropTable(database);
				db.CreateTable(code);
				return;
			}
			if (insert.Count != 0 || update.Count != 0 || remove.Count != 0 || insertFK.Count != 0 || dropFK.Count != 0 || insertIndex.Count != 0 || dropIndex.Count != 0) {
				// Table has changed - upgrade it
				db.UpgradeTable(code, database, insert, update, remove, insertFK, dropFK, insertIndex, dropIndex);
				if (insert.Count != 0 || update.Count != 0) {
					// Fill any new/changed fields with their default values
					foreach (Field f in insert.Concat(update).Where(f => !string.IsNullOrEmpty(f.DefaultValue))) {
						int lastInsertId;
						db.Execute(string.Format("UPDATE {0} SET {1} = {2} WHERE {1} IS NULL",
							code.Name, f.Name, f.Quote(f.DefaultValue)), out lastInsertId);
					}
				}
			}
		}

		/// <summary>
		/// Upgrade the database to match the code
		/// </summary>
		static void Upgrade(DbInterface db) {
			Dictionary<string, Table> dbTables = db.Tables();
			CodeFirst code = new CodeFirst();
			_tables = code.Tables;
			// Process the tables in order to avoid foreign key violations
			TableList orderedTables = new TableList(_tables.Values);
			foreach (Table t in orderedTables.Reverse<Table>()) {
				Table database;
				dbTables.TryGetValue(t.Name, out database);
				Upgrade(db, t, database);
			}

		}

		/// <summary>
		/// Static constructor upgrades database to match code, then does any coded updates.
		/// </summary>
		static Database() {
			try {
				using (DbInterface dbi = getDatabase(AppSettings.Default.ConnectionString)) {
					Upgrade(dbi);
					using (Database db = new Database(dbi)) {
						db.BeginTransaction();
						db.Upgrade();
						db.Commit();
					}
				}
			} catch (Exception ex) {
				WebServer.Log(ex.ToString());
				throw;
			}
		}

		/// <summary>
		/// Coded updates - make sure all required records exist, etc.
		/// </summary>
		public void Upgrade() {
			Table table = TableFor("Settings");
			if (!RecordExists(table, 1)) {
				Update("Settings", new JObject().AddRange("idSettings", 1,
					"YearStartMonth", 1,
					"YearStartDay", 0,
					"DbVersion", CurrentDbVersion));
			}
			LogLevel originalLevel = Logging;
			Settings settings = Get<Settings>(1);
			if (settings.DbVersion < CurrentDbVersion && Logging < LogLevel.Writes)
				Logging = LogLevel.Writes;
			table = TableFor("AccountType");
			ensureRecordExists(table, AcctType.Income,
				"Negate", true, "Heading", "Gross Profit", "BalanceSheet", false);
			ensureRecordExists(table, AcctType.Expense,
				"Negate", false, "Heading", "Gross Profit", "BalanceSheet", false);
			ensureRecordExists(table, AcctType.Security,
				"Negate", true, "Heading", "Net Profit", "BalanceSheet", false);
			ensureRecordExists(table, AcctType.OtherIncome,
				"Negate", true, "Heading", "Net Profit", "BalanceSheet", false);
			ensureRecordExists(table, AcctType.OtherExpense,
				"Negate", false, "Heading", "Net Profit", "BalanceSheet", false);
			ensureRecordExists(table, AcctType.FixedAsset,
				"Negate", false, "Heading", "Fixed Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.OtherAsset,
				"Negate", false, "Heading", "Fixed Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.AccountsReceivable,
				"Negate", false, "Heading", "Current Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.Bank,
				"Negate", false, "Heading", "Current Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.Investment,
				"Negate", false, "Heading", "Current Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.OtherCurrentAsset,
				"Negate", false, "Heading", "Current Assets", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.CreditCard,
				"Negate", true, "Heading", "Current Liabilities", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.AccountsPayable,
				"Negate", true, "Heading", "Current Liabilities", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.OtherCurrentLiability,
				"Negate", true, "Heading", "Current Liabilities", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.LongTermLiability,
				"Negate", true, "Heading", "Long Term and Other Liabilities", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.OtherLiability,
				"Negate", true, "Heading", "Long Term and Other Liabilities", "BalanceSheet", true);
			ensureRecordExists(table, AcctType.Equity,
				"Negate", true, "Heading", "Equities", "BalanceSheet", true);
			ensureDocTypeExists(DocType.Invoice, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.Payment, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.CreditMemo, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.Bill, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.BillPayment, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.Credit, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.Cheque, "O", null);
			ensureDocTypeExists(DocType.Deposit, "O", null);
			ensureDocTypeExists(DocType.CreditCardCharge, "O", null);
			ensureDocTypeExists(DocType.CreditCardCredit, "O", null);
			ensureDocTypeExists(DocType.GeneralJournal, "O", null);
			ensureDocTypeExists(DocType.Transfer, "O", null);
			ensureDocTypeExists(DocType.OpeningBalance, "O", null);
			ensureDocTypeExists(DocType.Buy, "O", null);
			ensureDocTypeExists(DocType.Sell, "O", null);
			ensureDocTypeExists(DocType.Gain, "O", null);
			switch (settings.DbVersion) {
				case 0:
				case 1:
					// Version 2 introduced some new account types
					Execute("UPDATE Account SET AccountTypeId = AccountTypeId + 1 WHERE AccountTypeId >= 3");
					Execute("UPDATE Account SET AccountTypeId = AccountTypeId + 1 WHERE AccountTypeId >= 10");
					goto case 2;	// We have just upgraded to version 2
				case 2:
					break;
				default:
					throw new CheckException("Database has more recent version {0} than program {1}",
						settings.DbVersion, CurrentDbVersion);
			}
			if (settings.DbVersion < CurrentDbVersion)
				Execute("UPDATE Settings SET DbVersion = " + CurrentDbVersion);
			Logging = originalLevel;
			table = TableFor("Account");
			ensureRecordExists(table, Acct.SalesLedger,
				"AccountTypeId", AcctType.AccountsReceivable);
			ensureRecordExists(table, Acct.PurchaseLedger,
				"AccountTypeId", AcctType.AccountsPayable);
			ensureRecordExists(table, Acct.OpeningBalEquity,
				"AccountTypeId", AcctType.Equity);
			ensureRecordExists(table, Acct.RetainedEarnings,
				"AccountTypeId", AcctType.Equity,
				"Protected", true);
			ensureRecordExists(table, Acct.ShareCapital,
				"AccountTypeId", AcctType.Equity);
			ensureRecordExists(table, Acct.UndepositedFunds,
				"AccountTypeId", AcctType.OtherCurrentAsset);
			ensureRecordExists(table, Acct.UninvoicedSales,
				"AccountTypeId", AcctType.OtherCurrentAsset);
			ensureRecordExists(table, Acct.VATControl,
				"AccountTypeId", AcctType.OtherCurrentLiability,
				"AccountDescription", "VAT to pay/receive");
			table = TableFor("NameAddress");
			if (!RecordExists(table, 1)) {
				JObject d = new JObject().AddRange("idNameAddress", 1,
					"Type", "O",
					"Name", "");
				update(table, d, false);
			}
		}

		/// <summary>
		/// Ensure a record matching an enum value exists
		/// </summary>
		/// <param name="args">Additional field values: name, value, name, value, ...</param>
		void ensureRecordExists(Table table, object enumValue, params object [] args) {
			JObject d = new JObject().AddRange(table.PrimaryKey.Name, (int)enumValue, 
				table.Indexes[1].Fields[0].Name, enumValue.UnCamel());
			d.AddRange(args);
			updateIfChanged(table, d);
		}

		/// <summary>
		/// Ensure a record matching a doc type exists
		/// </summary>
		void ensureDocTypeExists(DocType enumValue, string nameType, int? primaryAccountid) {
			Table table = TableFor("DocumentType");
			JObject d = new JObject().AddRange(table.PrimaryKey.Name, (int)enumValue,
				table.Indexes[1].Fields[0].Name, enumValue.UnCamel(),
				"NameType", nameType,
				"Sign", AppModule.SignFor(enumValue),
				"PrimaryAccountId", primaryAccountid);
			updateIfChanged(table, d);
		}

		/// <summary>
		/// Get a DbInterface to talk to the type of database in use (SQLite or MySql at present)
		/// </summary>
		static DbInterface getDatabase(string connectionString) {
			switch(AppSettings.Default.Database.ToLower()) {
				case "sqlite":
					return new SQLiteDatabase(connectionString);
				case "mysql":
					return new MySqlDatabase(connectionString);
				default:
					throw new CheckException("Unknown database type {0}", AppSettings.Default.Database);
			}
		}

		public Database()
			: this(AppSettings.Default.ConnectionString) {
		}

		public Database(string connectionString) {
			_db = getDatabase(connectionString);
		}

		Database(DbInterface db) {
			_db = db;
		}

		/// <summary>
		/// Write an update transaction pair or other type of transaction to the audit trail
		/// </summary>
		public void Audit(AuditType type, string table, int? id, string transaction, string previous) {
			if (transaction == previous)
				return;
			Utils.Check(id != null, "Attempt to audit null record id");
			int lastInsertId;
			string date = Utils.Now.ToString("yyyy-MM-dd HH:mm:ss");
			execute("INSERT INTO AuditTrail (TableName, ChangeType, DateChanged, RecordId, Record) VALUES("
				+ Quote(table) + ", " + (int)type + ", " + Quote(date) + ", " + id + ", " + Quote(transaction) + ")", out lastInsertId);
			if(!string.IsNullOrEmpty(previous))
				execute("INSERT INTO AuditTrail (TableName, ChangeType, DateChanged, RecordId, Record) VALUES("
				+ Quote(table) + ", " + (int)AuditType.Previous + ", " + Quote(date) + ", " + id + ", " + Quote(previous) + ")", out lastInsertId);
		}

		/// <summary>
		/// Write a transaction to the audit trail
		/// </summary>
		public void Audit(AuditType type, string table, int? id, JObject transaction) {
			Audit(type, table, id, transaction.ToString(), null);
		}

		/// <summary>
		/// Write an update transaction pair, or an insert transaction to the audit trail
		/// </summary>
		public void AuditUpdate(string table, int? id, JObject oldTransaction, JObject newTransaction) {
			if (oldTransaction == null) {
				Audit(AuditType.Insert, table, id, newTransaction.ToString(), null);
			} else {
				Audit(AuditType.Update, table, id, newTransaction.ToString(), oldTransaction.ToString());
			}
		}

		public void BeginTransaction() {
			_db.BeginTransaction();
		}

		/// <summary>
		/// Return SQL to cast a value to a type (database dependent)
		/// </summary>
		public string Cast(string value, string type) {
			return _db.Cast(value, type);
		}

		/// <summary>
		/// Check a field name is valid (i.e. all letters)
		/// </summary>
		static public void CheckValidFieldname(string f) {
			if (!IsValidFieldname(f))
				throw new CheckException("'{0}' is not a valid field name", f);
		}

		/// <summary>
		/// Clean up the database (database dependent)
		/// </summary>
		public void Clean() {
			_db.CleanDatabase();
		}

		/// <summary>
		/// Commit current transaction
		/// </summary>
		public void Commit() {
			_db.Commit();
		}

		/// <summary>
		/// Delete the supplied record from the supplied table.
		/// Data must contain at least a unique key for the table.
		/// </summary>
		public void Delete(string tableName, JObject data) {
			delete(TableFor(tableName), data);
		}

		/// <summary>
		/// Delete the record from the table, optionally record in audit trail.
		/// </summary>
		public void Delete(string tableName, int id, bool withAudit) {
			Table t = TableFor(tableName);
			if (withAudit) {
				JObject old = withAudit ? QueryOne("+", "WHERE " + t.PrimaryKey.Name + '=' + id, tableName) : null;
				if (old != null && !old.IsAllNull()) {
					Execute("DELETE FROM " + tableName + " WHERE " + t.PrimaryKey.Name + '=' + id);
					Audit(AuditType.Delete, tableName, id, old);
				}
			} else {
				Execute("DELETE FROM " + tableName + " WHERE " + t.PrimaryKey.Name + '=' + id);
			}
		}

		/// <summary>
		/// Delete the supplied record from its table.
		/// Data must contain at least a unique key for the table.
		/// </summary>
		public void Delete(JsonObject data) {
			delete(TableFor(data.GetType()).UpdateTable, data.ToJObject());
		}

		public void delete(Table table, JObject data) {
			Index index = table.IndexFor(data);
			Utils.Check(index != null, "Deleting from {0}:data does not specify unique record", table.Name);
			Execute("DELETE FROM " + table.Name + " WHERE " + index.Where(data));
		}

		/// <summary>
		/// Close database, rolling back any uncommitted transaction.
		/// </summary>
		public void Dispose() {
			Rollback();
			_db.Dispose();
			_db = null;
		}

		/// <summary>
		/// Return an empty record for the table.
		/// All fields will have their default value.
		/// </summary>
		static public JObject EmptyRecord(string tableName) {
			return emptyRecord(TableFor(tableName));
		}

		/// <summary>
		/// Return an empty C# object of the type.
		/// All fields will have their default value.
		/// </summary>
		static public T EmptyRecord<T>() where T : JsonObject {
			JObject record = emptyRecord(TableFor(typeof(T)));
			return record.ToObject<T>();
		}

		static JObject emptyRecord(Table table) {
			JObject record = new JObject();
			foreach (Field field in table.Fields.Where(f => f.ForeignKey == null && !f.Nullable)) {
				record[field.Name] = field.Type == typeof(string) ? "" : Activator.CreateInstance(field.Type).ToJToken();
			}
			record[table.PrimaryKey.Name] = null;
			return record;
		}

		/// <summary>
		/// Execute some Sql on the database.
		/// </summary>
		public int Execute(string sql) {
			int lastInsertId;
			return execute(sql, out lastInsertId);
		}

		/// <summary>
		/// Execute some sql, and return the id of any inserted record.
		/// </summary>
		int execute(string sql, out int lastInserttId) {
			using (new Timer(sql)) {
				if (Logging >= LogLevel.Writes) Log(sql);
				try {
					return _db.Execute(sql, out lastInserttId);
				} catch (Exception ex) {
					throw new DatabaseException(ex, sql);
				}
			}
		}

		/// <summary>
		/// Determine if there is a record on the table with the given id.
		/// </summary>
		public bool Exists(string tableName, int? id) {
			Table table = TableFor(tableName);
			string idName = table.PrimaryKey.Name;
			return id != null && QueryOne("SELECT " + idName + " FROM " + tableName + " WHERE "
				+ idName + " = " + id) != null;
		}

		/// <summary>
		/// Return the id of the record in tableName matching data.
		/// If there is no such record, create one using data as the field values.
		/// </summary>
		public int? ForeignKey(string tableName, JObject data) {
			int? result = LookupKey(tableName, data);
			return result ?? insert(TableFor(tableName), data);
		}

		/// <summary>
		/// Return the id of the record in tableName matching data.
		/// If there is no such record, create one using data as the field values.
		/// <param name="data">Of the form: name, value, ...</param>
		/// </summary>
		public int? ForeignKey(string tableName, params object[] data) {
			return ForeignKey(tableName, new JObject().AddRange(data));
		}

		/// <summary>
		/// Get the record with the given id, as a C# object.
		/// </summary>
		public T Get<T>(int id) where T : JsonObject {
			Table table = TableFor(typeof(T));
			return QueryOne<T>("SELECT * FROM " + table.Name + " WHERE " + table.PrimaryKey.Name + " = " + id);
		}

		/// <summary>
		/// Get the record with a unique key matching criteria, as a C# object.
		/// </summary>
		public T Get<T>(T criteria) where T : JsonObject {
			Table table = TableFor(typeof(T));
			JObject data = criteria.ToJObject();
			Index index = table.IndexFor(data);
			if (index != null) {
				data = QueryOne("SELECT * FROM " + table.Name + " WHERE " + index.Where(data));
			} else {
				data = null;
			}
			if (data == null || data.IsAllNull())
				data = emptyRecord(table);
			return data.ToObject<T>();
		}

		/// <summary>
		/// Get the record from the given table, with the given id, as a JObject.
		/// </summary>
		public JObject Get(string tableName, int id) {
			Table table = TableFor(tableName);
			JObject result = QueryOne("SELECT * FROM " + table.Name + " WHERE " + table.PrimaryKey.Name + " = " + id);
			return result == null ? emptyRecord(table) : result;
		}

		/// <summary>
		/// Produce an "IN(...)" SQL statement from a list of values
		/// </summary>
		public static string In(params object[] args) {
			return "IN(" + string.Join(",", args.Select(o => Quote(o is Enum ? (int)o : o)).ToArray()) + ")";
		}

		/// <summary>
		/// Produce an "IN(...)" SQL statement from a list of values
		/// </summary>
		public static string In<T>(IEnumerable<T> args) {
			return "IN(" + string.Join(",", args.Select(o => Quote(o)).ToArray()) + ")";
		}

		/// <summary>
		/// Insert a new record in the given table.
		/// On return, data's Id field will be filled in.
		/// </summary>
		public void Insert(string tableName, JObject data) {
			insert(TableFor(tableName), data);
		}

		/// <summary>
		/// Insert a new record in the given table.
		/// On return, data's Id field will be filled in.
		/// </summary>
		public void Insert(string tableName, JsonObject data) {
			Table table = TableFor(tableName);
			JObject d = data.ToJObject();
			insert(table, d);
			data.Id = (int)d[table.PrimaryKey.Name];
		}

		/// <summary>
		/// Insert a C# object as a new record.
		/// On return, data's Id field will be filled in.
		/// </summary>
		public void Insert(JsonObject data) {
			Table table = TableFor(data.GetType()).UpdateTable;
			JObject d = data.ToJObject();
			insert(table, d);
			data.Id = (int)d[table.PrimaryKey.Name];
		}

		int insert(Table table, JObject data) {
			Field idField = table.PrimaryKey;
			string idName = idField.Name;
			List<Field> fields = table.Fields.Where(f => data[f.Name] != null).ToList();
			checkForMissingFields(table, data, true);
			try {
				int lastInsertId;
				execute("INSERT INTO " + table.Name + " ("
					+ string.Join(", ", fields.Select(f => f.Name).ToArray()) + ") VALUES ("
					+ string.Join(", ", fields.Select(f => f.Quote(data[f.Name])).ToArray()) + ")", out lastInsertId);
				data[idName] = lastInsertId;
				return lastInsertId;
			} catch (DatabaseException ex) {
				throw new DatabaseException(ex, table);
			}
		}

		void checkForMissingFields(Table table, JObject data, bool insert) {
			Field idField = table.PrimaryKey;
			string[] errors = table.Indexes.SelectMany(i => i.Fields)
				.Distinct()
				.Where(f => f != idField && !f.Nullable && string.IsNullOrWhiteSpace(data.AsString(f.Name)) && (insert || data[f.Name] != null))
				.Select(f => f.Name)
				.ToArray();
			Utils.Check(errors.Length == 0, "Table {0} {1}:Missing key fields {2}", 
				table.Name, insert ? "insert" : "update", string.Join(", ", errors));
		}

		/// <summary>
		/// Check a field name is all letters.
		/// </summary>
		/// <param name="f"></param>
		/// <returns></returns>
		static public bool IsValidFieldname(string f) {
			return Regex.IsMatch(f, @"^[a-z]+$", RegexOptions.IgnoreCase);
		}

		public void Log(string sql) {
			WebServer.Log(sql);
		}

		public LogLevel Logging;


		/// <summary>
		/// Return the id of the record in tableName matching data.
		/// If there is no such record, return null.
		/// </summary>
		public int? LookupKey(string tableName, JObject data) {
			Table table = TableFor(tableName);
			string idName = table.PrimaryKey.Name;
			Index index = table.IndexFor(data);
			if (index == null || index.Fields.FirstOrDefault(f => data[f.Name].ToString() != "") == null) return null;
			JObject result = QueryOne("SELECT " + idName + " FROM " + tableName + " WHERE "
				+ index.Where(data));
			return result == null ? null : result[idName].To<int?>();
		}

		/// <summary>
		/// Return the id of the record in tableName matching data.
		/// If there is no such record, return null.
		/// <param name="data">Of the form: name, value, ...</param>
		/// </summary>
		public int? LookupKey(string tableName, params object[] data) {
			return LookupKey(tableName, new JObject().AddRange(data));
		}

		/// <summary>
		/// Fill in the "next" and "previous" variables in record with the next and previous
		/// document ids.
		/// </summary>
		/// <param name="sql">Sql to add to document select to limit the documents returned,
		/// e.g. to the next cheque from this bank account.</param>
		public void NextPreviousDocument(JObject record, string sql) {
			JObject header = (JObject)record["header"];
			int id = header.AsInt("idDocument");
			string d = Quote(header.AsDate("DocumentDate"));
			JObject next = id == 0 ? null : QueryOne("SELECT idDocument FROM Document " + sql
				+ " AND (DocumentDate > " + d + " OR (DocumentDate = " + d + " AND idDocument > " + id + "))"
				+ " ORDER BY DocumentDate, idDocument");
			record["next"] = next == null ? 0 : next.AsInt("idDocument");
			JObject previous = QueryOne("SELECT idDocument FROM Document " + sql
				+ (id == 0 ? "" : " AND (DocumentDate < " + d + " OR (DocumentDate = " + d + " AND idDocument < " + id + "))")
				+ " ORDER BY DocumentDate DESC, idDocument DESC");
			record["previous"] = previous == null ? 0 : previous.AsInt("idDocument");
		}

		/// <summary>
		/// Run any sql query.
		/// </summary>
		public JObjectEnumerable Query(string sql) {
			if (Logging >= LogLevel.Reads) Log(sql);
			try {
				using (new Timer(sql)) {
					return new JObjectEnumerable(_db.Query(sql));
				}
			} catch (Exception ex) {
				throw new DatabaseException(ex, sql);
			}
		}

		/// <summary>
		/// Run an sql query, returning the specified fields.
		/// Creates an automatic join for any foreign keys.
		/// </summary>
		/// <param name="fields">Fields to return, null, empty or "+" means return all fields from all tables, but with 
		/// the fields from the first index on foreign key master tables added (+) or replacing the foreign key id (null or empty).</param>
		/// <param name="conditions">To limit the records returned.</param>
		/// <param name="tableNames">Table names to query - joins will be constructed between them if there is more than 1.</param>
		public JObjectEnumerable Query(string fields, string conditions, params string[] tableNames) {
			return Query(buildQuery(fields, conditions, tableNames));
		}

		string buildQuery(string fields, string conditions, params string[] tableNames) {
			List<string> joins = new List<string>();
			List<Table> tables = tableNames.Select(n => Database.TableFor(n)).ToList();
			List<Field> allFields = new List<Field>();	// Field list to use if fields is null, empty or "+"
			List<Table> processed = new List<Table>();
			foreach (Table q in tables) {
				processed.Add(q);
				Field pk = q.PrimaryKey;
				if (joins.Count == 0) {
					joins.Add("FROM " + q.Name);
					allFields.AddRange(q.Fields);
				} else {
					Table detail = processed.FirstOrDefault(t => t.ForeignKeyFieldFor(q) != null);
					if (detail != null) {
						// q is master to a table already processed - add a join from detail to master
						Field fk = detail.ForeignKeyFieldFor(q);
						joins.Add("LEFT JOIN " + q.Name + " ON " + q.Name + "." + fk.ForeignKey.Field.Name + " = " + detail.Name + "." + fk.Name);
						allFields.AddRange(q.Fields.Where(f => f != pk));
					} else {
						// q is detail, hopefully to a master already processed
						Table master = processed.FirstOrDefault(t => q.ForeignKeyFieldFor(t) != null);
						if (master == null)
							throw new CheckException("No joins between {0} and any of {1}",
								q.Name, string.Join(",", processed.Select(t => t.Name).ToArray()));
						// Add a join from master to detail
						Field fk = q.ForeignKeyFieldFor(master);
						joins.Add("LEFT JOIN " + q.Name + " ON " + q.Name + "." + fk.Name + " = " + master.Name + "." + fk.ForeignKey.Field.Name);
						allFields.AddRange(q.Fields.Where(f => f != pk));
					}
				}
				// Now create joins for any foreign keys which are for other tables (not in tableNames)
				foreach (Field fk in q.Fields.Where(f => f.ForeignKey != null && f.ForeignKey.Table.Indexes.Length > 1 && tables.IndexOf(f.ForeignKey.Table) < 0)) {
					Table master = fk.ForeignKey.Table;
					string joinName = q.Name + "_" + master.Name;
					joins.Add("LEFT JOIN " + master.Name + " AS " + joinName + " ON " + joinName + "." + fk.ForeignKey.Field.Name + " = " + q.Name + "." + fk.Name);
					int i = allFields.IndexOf(fk);
					if (i <= 0)		// Do not remove first field, which will be key of first file
						i = allFields.Count;
					else if(fields != "+")
						allFields.RemoveAt(i);
					allFields.InsertRange(i, master.Indexes[1].Fields);
				}
			}
			if (string.IsNullOrEmpty(fields) || fields == "+")
				fields = string.Join(",", allFields.Select(f => f.Name).ToArray());
			return "SELECT " + fields + "\r\n" + string.Join("\r\n", joins) + "\r\n" + conditions;
		}

		/// <summary>
		/// Run any sql query, returning C# objects.
		/// </summary>
		public IEnumerable<T> Query<T>(string sql) {
			return Query(sql).Select(r => r.ToObject<T>());
		}

		/// <summary>
		/// Run an sql query, filling the returned C# objects from the specified fields.
		/// Creates an automatic join for any foreign keys.
		/// </summary>
		/// <param name="fields">Fields to return, null, empty or "+" means return all fields from all tables, but with 
		/// the fields from the first index on foreign key master tables added (+) or replacing the foreign key id (null or empty).</param>
		/// <param name="conditions">To limit the records returned.</param>
		/// <param name="tableNames">Table names to query - joins will be constructed between them if there is more than 1.</param>
		public IEnumerable<T> Query<T>(string fields, string conditions, params string[] tableNames) {
			return Query(fields, conditions, tableNames).Select(r => r.ToObject<T>());
		}

		/// <summary>
		/// Run any Sql query, returning the first matching record, or null if none.
		/// </summary>
		public JObject QueryOne(string query) {
			return _db.QueryOne(query);
		}

		/// <summary>
		/// Run an sql query, returning the first matching record, or null if none.
		/// Creates an automatic join for any foreign keys.
		/// </summary>
		/// <param name="fields">Fields to return, null, empty or "+" means return all fields from all tables, but with 
		/// the fields from the first index on foreign key master tables added (+) or replacing the foreign key id (null or empty).</param>
		/// <param name="conditions">To limit the records returned.</param>
		/// <param name="tableNames">Table names to query - joins will be constructed between them if there is more than 1.</param>
		public JObject QueryOne(string fields, string conditions, params string[] tableNames) {
			return QueryOne(buildQuery(fields, conditions, tableNames));
		}

		/// <summary>
		/// Run any sql query, filling the returned C# object from the first record, or an empty record if none.
		/// </summary>
		public T QueryOne<T>(string query) where T : JsonObject {
			JObject data = QueryOne(query);
			return data == null || data.IsAllNull() ? EmptyRecord<T>() : data.To<T>();
		}

		/// <summary>
		/// Run an sql query, filling the returned C# object from the specified fields in the first record, or an empty record if none.
		/// Creates an automatic join for any foreign keys.
		/// </summary>
		/// <param name="fields">Fields to return, null, empty or "+" means return all fields from all tables, but with 
		/// the fields from the first index on foreign key master tables added (+) or replacing the foreign key id (null or empty).</param>
		/// <param name="conditions">To limit the records returned.</param>
		/// <param name="tableNames">Table names to query - joins will be constructed between them if there is more than 1.</param>
		public T QueryOne<T>(string fields, string conditions, params string[] tableNames) where T : JsonObject {
			JObject data = QueryOne(fields, conditions, tableNames);
			return data == null || data.IsAllNull() ? EmptyRecord<T>() : data.To<T>();
		}

		/// <summary>
		/// Quote a field for inclusion in an Sql statement
		/// </summary>
		static public string Quote(object o) {
			if (o == null || o == DBNull.Value) return "NULL";
			if (o is int || o is long || o is double) return o.ToString();
			if (o is decimal) return ((decimal)o).ToString("0.00");
			if (o is double) return (Math.Round((decimal)o, 4)).ToString();
			if (o is double) return ((decimal)o).ToString("0");
			if (o is bool) return (bool)o ? "1" : "0";
			if(o is DateTime) return "'" + ((DateTime)o).ToString("yyyy-MM-dd") + "'";
			return "'" + o.ToString().Replace("'", "''") + "'";
		}

		/// <summary>
		/// Test if a record with the specified id exists in the specified table.
		/// </summary>
		public bool RecordExists(string table, int id) {
			return RecordExists(TableFor(table), id);
		}

		/// <summary>
		/// Test if a record with the specified id exists in the specified table.
		/// </summary>
		public bool RecordExists(Table table, int id) {
			Field idField = table.PrimaryKey;
			string idName = idField.Name;
			return QueryOne("SELECT " + idName + " FROM " + table.Name + " WHERE " + idName + " = " + id) != null;
		}

		/// <summary>
		/// Rollback the current transaction.
		/// </summary>
		public void Rollback() {
			_db.Rollback();
		}

		/// <summary>
		/// List of all table names in the data dictionary.
		/// </summary>
		static public IEnumerable<string> TableNames {
			get { return _tables.Where(t => !t.Value.IsView).Select(t => t.Key); }
		}

		/// <summary>
		/// List of all view names in the data dictionary.
		/// </summary>
		static public IEnumerable<string> ViewNames {
			get { return _tables.Where(t => t.Value.IsView).Select(t => t.Key); }
		}

		/// <summary>
		/// Get the data dictionary info for a table name.
		/// </summary>
		static public Table TableFor(string name) {
			Table table;
			Utils.Check(_tables.TryGetValue(name, out table), "Table '{0}' does not exist", name);
			return table;
		}

		/// <summary>
		/// Get the data dictionary info for a C# type.
		/// </summary>
		static public Table TableFor(Type type) {
			Type t = type;
			while (!_tables.ContainsKey(t.Name)) {
				t = t.BaseType;
				Utils.Check(t != typeof(JsonObject), "Unable to find a table for type {0}", type.Name);
			}
			return TableFor(t.Name);
		}

		/// <summary>
		/// Update the record uniquely identified by the data.
		/// If there is no such record, insert one, and fill in the Id field.
		/// </summary>
		public void Update(string tableName, JObject data) {
			update(TableFor(tableName), data, false);
		}

		/// <summary>
		/// Update the record uniquely identified by the data.
		/// If there is no such record, insert one, and fill in the Id field.
		/// </summary>
		public void Update(JsonObject data) {
			Update(data, false);
		}

		/// <summary>
		/// Update the record uniquely identified by the data.
		/// If there is no such record, insert one, and fill in the Id field.
		/// Optionally save an audit trail.
		/// </summary>
		public void Update(JsonObject data, bool withAudit) {
			Table table = TableFor(data.GetType()).UpdateTable;
			JObject d = data.ToJObject();
			update(table, d, withAudit);
			data.Id = (int)d[table.PrimaryKey.Name];
		}

		void update(Table table, JObject data, bool withAudit) {
			Field idField = table.PrimaryKey;
			string idName = idField.Name;
			JToken idValue = null;
			// Find the first unique index we have data for. Will be Primary (Id) index if that is included.
			Index index = table.IndexFor(data);
			JObject result = null;
			if (index != null) {
				// Retrieve any existing record that matches the index.
				// If auditing, get all the foreign key fields too.
				result = QueryOne(withAudit ? "+" : idName, "WHERE " + index.Where(data), table.Name);
				if (result != null) {
					// Set the id field
					data[idName] = idValue = result[idName];
				}
			}
			List<Field> fields = table.Fields.Where(f => data[f.Name] != null).ToList();
			checkForMissingFields(table, data, idValue == null);
			try {
				int id;
				if (idValue != null) {
					// It's an existing record - update all supplied fields
					execute("UPDATE " + table.Name + " SET "
						+ string.Join(", ", fields.Where(f => f != idField).Select(f => f.Name + '=' + f.Quote(data[f.Name])).ToArray())
						+ " WHERE " + index.Where(data), out id);
					id = idValue.To<int>();
				} else {
					// It's a new record - insert it and record the inserted id
					execute("INSERT INTO " + table.Name + " ("
						+ string.Join(", ", fields.Select(f => f.Name).ToArray()) + ") VALUES ("
						+ string.Join(", ", fields.Select(f => f.Quote(data[f.Name])).ToArray()) + ")", out id);
					data[idName] = id;
				}
				if (withAudit) {
					// Retrieve the new record with all foreign key fields
					data = QueryOne("+", "WHERE " + idName + " = " + id, table.Name);
					AuditUpdate(table.Name, id, result, data);
				}
			} catch (DatabaseException ex) {
				throw new DatabaseException(ex, table);
			}
		}

		/// <summary>
		/// Update a record only if it has changed (used by initial record existence checks)
		/// </summary>
		void updateIfChanged(Table table, JObject data) {
			Field idField = table.PrimaryKey;
			string idName = idField.Name;
			JToken idValue = null;
			List<Field> fields = table.Fields.Where(f => data[f.Name] != null).ToList();
			Index index = table.IndexFor(data);
			JObject result = null;
			try {
				result = QueryOne("SELECT * FROM " + table.Name + " WHERE " + index.Where(data));
				if (result != null)
					data[idName] = idValue = result[idName];
				int id;
				if (idValue != null) {
					fields = fields.Where(f => data.AsString(f.Name) != result.AsString(f.Name)).ToList();
					if (fields.Count == 0)
						return;
					execute("UPDATE " + table.Name + " SET "
						+ string.Join(", ", fields.Where(f => f != idField).Select(f => f.Name + '=' + f.Quote(data[f.Name])).ToArray())
						+ " WHERE " + index.Where(data), out id);
					id = idValue.To<int>();
				} else {
					execute("INSERT INTO " + table.Name + " ("
						+ string.Join(", ", fields.Select(f => f.Name).ToArray()) + ") VALUES ("
						+ string.Join(", ", fields.Select(f => f.Quote(data[f.Name])).ToArray()) + ")", out id);
					data[idName] = id;
				}
			} catch (DatabaseException ex) {
				throw new DatabaseException(ex, table);
			}
		}

		/// <summary>
		/// For measuring query performance
		/// </summary>
		public class Timer : IDisposable {
			DateTime _start;
			string _message;

			public Timer(string message) {
				_start = Utils.Now;
				_message = message;
			}

			public void Dispose() {
				double elapsed = (Utils.Now - _start).TotalMilliseconds;
				if (elapsed > MaxTime)
					WebServer.Log("{0}:{1}", elapsed, _message);
			}

			public double MaxTime = AppSettings.Default.SlowQuery;
		}

	}

	/// <summary>
	/// Data Dictionary information
	/// </summary>
	public class ForeignKey {
		public ForeignKey(Table table, Field field) {
			Table = table;
			Field = field;
		}

		public Table Table { get; private set; }

		public Field Field { get; private set; }
	}

	/// <summary>
	/// Data Dictionary information
	/// </summary>
	public class Field {

		public Field(string name) {
			Name = name;
			Type = typeof(string);
		}

		public Field(string name, Type type, decimal length, bool nullable, bool autoIncrement, string defaultValue) {
			Name = name;
			Type = type;
			Length = length;
			Nullable = nullable;
			AutoIncrement = autoIncrement;
			if (type == typeof(decimal) && defaultValue != null) {
				try {
					defaultValue = decimal.Parse(defaultValue).ToString("0.####");
				} catch {
				}
			}
			if (defaultValue == null && !nullable) {
				if (type == typeof(bool) || type == typeof(int) || type == typeof(decimal) || type == typeof(double)) {
					defaultValue = "0";
				} else if (type == typeof(string)) {
					defaultValue = "";
				}
			}
			DefaultValue = defaultValue;
		}

		/// <summary>
		/// AutoIncrement primary key
		/// </summary>
		public bool AutoIncrement { get; private set; }

		public string DefaultValue { get; private set; }

		/// <summary>
		/// This field points to a master record on another table
		/// </summary>
		public ForeignKey ForeignKey;

		/// <summary>
		/// Set to 0 for a memo type string field (unlimited length).
		/// Default for strings is 45
		/// </summary>
		public decimal Length { get; private set; }

		public string Name { get; private set; }

		public bool Nullable { get; private set; }

		/// <summary>
		/// Quote this field for inclusion in sql statements
		/// </summary>
		public string Quote(object o) {
			if (o == null || o == DBNull.Value) return "NULL";
			if ((Type == typeof(int) || Type == typeof(decimal) || Type == typeof(double) || Type == typeof(DateTime)) && o.ToString() == "") return "NULL";
			try {
				o = Convert.ChangeType(o.ToString(), Type);
			} catch (Exception ex) {
				throw new CheckException(ex, "Invalid value for {0} field {1} '{2}'", Type.Name, Name, o);
			}
			if (o is int || o is long || o is double) return o.ToString();
			if (o is decimal) return ((decimal)o).ToString("0.00");
			if (o is double) return (Math.Round((decimal)o, 4)).ToString();
			if (o is bool) return (bool)o ? "1" : "0";
			if (o is DateTime) return "'" + ((DateTime)o).ToString("yyyy-MM-dd") + "'";
			return "'" + o.ToString().Replace("'", "''") + "'";
		}

		/// <summary>
		/// C# type for this field
		/// </summary>
		public Type Type { get; private set; }

		/// <summary>
		/// C# type name for this field (e.g. "int?")
		/// </summary>
		public string TypeName {
			get {
				string name = Type.Name;
				switch (name) {
					case "Int32":
						return Nullable ? "int?" : "int";
					case "Decimal":
						return Nullable ? "decimal?" : "decimal";
					case "Double":
						return Nullable ? "double?" : "double";
					case "Boolean":
						return Nullable ? "bool?" : "bool";
					case "DateTime":
						return Nullable ? "DateTime?" : "DateTime";
					case "String":
						return "string";
					default:
						return name;
				}
			}
		}

		public override string ToString() {
			return Name + "(" + TypeName + ")";
		}
	}

	/// <summary>
	/// Data Dictionary information
	/// </summary>
	public class Index {

		public Index(string name, params Field[] fields) {
			Name = name;
			Fields = fields;
		}

		public Index(string name, params string[] fields) {
			Name = name;
			Fields = fields.Select(f => new Field(f)).ToArray();
		}

		/// <summary>
		/// Whether the data has non-null values for all the fields in this index
		/// </summary>
		public bool CoversData(JObject data) {
			return (Fields.Where(f => data[f.Name] == null || data[f.Name].Type == JTokenType.Null).FirstOrDefault() == null);
		}

		/// <summary>
		/// Field names separated by commas, for inclusion in Sql
		/// </summary>
		public string FieldList {
			get { return string.Join(",", Fields.Select(f => f.ToString()).ToArray()); }
		}

		public Field[] Fields { get; private set; }

		public string Name { get; private set; }

		/// <summary>
		/// A SQL clause to select the record which matches the data
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public string Where(JObject data) {
			return string.Join(" AND ", Fields.Select(f => f.Name + "=" + f.Quote(data[f.Name])).ToArray());
		}

		public override string ToString() {
			return "I:" + Name + "=" + FieldList;
		}
	}

	/// <summary>
	/// Data Dictionary information
	/// </summary>
	public class Table {

		/// <summary>
		/// First index must be primary key
		/// </summary>
		public Table(string name, Field [] fields, Index[] indexes) {
			Name = name;
			Fields = fields;
			Indexes = indexes;
		}

		public Field[] Fields;

		/// <summary>
		/// Find field by name - returns null if none.
		/// </summary>
		public Field FieldFor(string name) {
			return Fields.FirstOrDefault(f => f.Name == name);
		}

		/// <summary>
		/// Field in this file which is a foreign key for table
		/// </summary>
		public Field ForeignKeyFieldFor(Table table) {
			return Fields.FirstOrDefault(f => f.ForeignKey != null && f.ForeignKey.Table.Name == table.Name);
		}

		public Index[] Indexes { get; private set; }

		/// <summary>
		/// First index which covers supplied data (i.e. for which data has all non-null values)
		/// </summary>
		public Index IndexFor(JObject data) {
			return Indexes.Where(i => i.CoversData(data)).FirstOrDefault();
		}

		public string Name { get; private set; }

		public Field PrimaryKey {
			get { return Indexes[0].Fields[0]; }
		}

		/// <summary>
		/// The table to update when writing - this for Tables, but something else for Views
		/// </summary>
		public virtual Table UpdateTable { get { return this; } }

		public virtual bool IsView { get { return false; } }

		public override string ToString() {
			return "T:" + string.Join(",", Fields.Select(f => f.ToString()).ToArray()) + "\r\n" 
				+ string.Join("\r\n", Indexes.Select(i => i.ToString()).ToArray());
		}
	}

	/// <summary>
	/// Data Dictionary information
	/// </summary>
	public class View : Table {
		Table _updateTable;

		public View(string name, Field[] fields, Index[] indexes, string sql, Table updateTable) : base(name, fields, indexes) {
			Sql = sql;
			_updateTable = updateTable;
		}

		public string Sql { get; private set; }

		public override Table UpdateTable { get { return _updateTable; } }

		public override bool IsView { get { return true; } }

	}

	/// <summary>
	/// IEnumerable<JObject> easily convertable to JArray, and with ToString method for debugging.
	/// </summary>
	public class JObjectEnumerable : IEnumerable<JObject> {
		IEnumerable<JObject> _e;

		public JObjectEnumerable(IEnumerable<JObject> e) {
			_e = e;
		}

		public List<JObject> ToList() {
			List<JObject> e = _e.ToList();
			_e = e;
			return e;
		}

		public override string ToString() {
			return this.ToJson();
		}

		public IEnumerator<JObject> GetEnumerator() {
			return _e.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
			return _e.GetEnumerator();
		}

		static public implicit operator JArray(JObjectEnumerable o) {
			JArray j = new JArray();
			foreach (JObject jo in o) {
				j.Add(jo);
			}
			return j;
		}
	}

	/// <summary>
	/// Base class for all the C# types which represent a record
	/// </summary>
	public class JsonObject {

		public JObject ToJObject() {
			return JObject.FromObject(this);
		}

		public T Clone<T>() {
			return this.ToJObject().To<T>();
		}

		/// <summary>
		/// The single unique Id field
		/// </summary>
		public virtual int? Id {
			get { return null; }
			set { }
		}

		public override string ToString() {
			return this.ToJson();
		}

	}

	/// <summary>
	/// Sorted list of tables, with views first, then bottom level details, with master tables last.
	/// Used to avoid creating foreign key conflicts.
	/// </summary>
	public class TableList : List<Table> {
		List<Table> _allTables;

		public TableList(IEnumerable<Table> allTables) {
			_allTables = allTables.ToList();
			foreach (Table t in _allTables.Where(t => t is View))
				add(t);
			foreach (Table t in _allTables.Where(t => !(t is View)))
				add(t);
		}

		void add(Table table) {
			if (IndexOf(table) >= 0) return;
			foreach (Table detail in _allTables.Where(t => t.ForeignKeyFieldFor(table) != null)) {
				add(detail);
			}
			if (table is View) {
				foreach (Table detail in _allTables.Where(t => t is View && (t as View).Sql.Contains(table.Name))) {
					add(detail);
				}

			}
			Add(table);
		}
	}

	/// <summary>
	/// Database exception records table name and Sql for later logging.
	/// </summary>
	public class DatabaseException : Exception {

		public DatabaseException(DatabaseException ex, Table table)
			: base(ex.InnerException.Message, ex.InnerException) {
			Sql = ex.Sql;
			Table = table.Name;
		}

		public DatabaseException(Exception ex, string sql)
			: base(ex.Message, ex) {
				Sql = sql;
		}

		public string Sql;

		public string Table;

		public override string Message {
			get {
				return Table == null ? base.Message : Table + ":" + base.Message;
			}
		}

		public override string ToString() {
			return base.ToString() + "\r\nSQL:" + Sql;

		}
	}

}
