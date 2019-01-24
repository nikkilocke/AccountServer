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
using CodeFirstWebFramework;

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
		VATControl,
		Spare9,
		Spare10,
		Spare11,
		Spare12,
		Spare13,
		Spare14,
		Spare15,
		Spare16,
		SPare17,
		Spare18,
		Spare19,
		SubscriptionsIncome
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
		Withdrawal,
		Deposit,
		CreditCardCharge,
		CreditCardCredit,
		GeneralJournal,
		Transfer,
		OpeningBalance,
		Buy,
		Sell,
		Gain,
		Subscriptions
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
	public class Database : CodeFirstWebFramework.Database {
		public override int CurrentDbVersion { get { return 4; } }

		/// <summary>
		/// Coded updates - make sure all required records exist, etc.
		/// </summary>
		public override void PostUpgradeFromVersion(int version) {
			base.PostUpgradeFromVersion(version);
			Table table = TableFor("Settings");
			if (!RecordExists(table, 1)) {
				Update("Settings", new JObject().AddRange("idSettings", 1,
					"YearStartMonth", 1,
					"YearStartDay", 0,
					"DbVersion", CurrentDbVersion));
			}
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
			table = TableFor("Account");
			ensureRecordExists(table, Acct.SalesLedger,
				"AccountTypeId", AcctType.AccountsReceivable);
			ensureRecordExists(table, Acct.PurchaseLedger,
				"AccountTypeId", AcctType.AccountsPayable);
			ensureDocTypeExists(DocType.Invoice, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.Payment, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.CreditMemo, "C", (int)Acct.SalesLedger);
			ensureDocTypeExists(DocType.Bill, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.BillPayment, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.Credit, "S", (int)Acct.PurchaseLedger);
			ensureDocTypeExists(DocType.Withdrawal, "O", null);
			ensureDocTypeExists(DocType.Deposit, "O", null);
			ensureDocTypeExists(DocType.CreditCardCharge, "O", null);
			ensureDocTypeExists(DocType.CreditCardCredit, "O", null);
			ensureDocTypeExists(DocType.GeneralJournal, "O", null);
			ensureDocTypeExists(DocType.Transfer, "O", null);
			ensureDocTypeExists(DocType.OpeningBalance, "O", null);
			ensureDocTypeExists(DocType.Buy, "O", null);
			ensureDocTypeExists(DocType.Sell, "O", null);
			ensureDocTypeExists(DocType.Gain, "O", null);
			ensureDocTypeExists(DocType.Subscriptions, "M", null);
			switch (version) {
				case -1:	// New database
					break;
				case 0:
				case 1:
					// Version 2 introduced some new account types
					Execute("UPDATE Account SET AccountTypeId = AccountTypeId + 1 WHERE AccountTypeId >= 3");
					Execute("UPDATE Account SET AccountTypeId = AccountTypeId + 1 WHERE AccountTypeId >= 10");
					goto case 2;	// We have just upgraded to version 2
				case 2:
					// Version 3 introduced some new standard account numbers - move up existing accounts with the reserved numbers
					foreach (Account a in Query<Account>("SELECT * FROM Account WHERE idAccount >= 9 AND idAccount <= 20").ToList()) {
						Account n = a.Clone<Account>();
						n.idAccount = null;
						a.AccountName = "\tTemp\t";
						Update(a);
						Insert(n);
						Execute("UPDATE Journal SET AccountId = " + n.idAccount + " WHERE AccountId =" + a.idAccount);
						Execute("UPDATE Product SET AccountId = " + n.idAccount + " WHERE AccountId =" + a.idAccount);
						Execute("UPDATE StockTransaction SET ParentAccountId = " + n.idAccount + " WHERE ParentAccountId =" + a.idAccount);
						Delete(a);
					}
					break;
				case 3:
				case 4:
					break;
				default:
					throw new CheckException("Database has more recent version {0} than program {1}",
						version, CurrentDbVersion);
			}
			table = TableFor("Account");
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
			ensureRecordExists(table, Acct.SubscriptionsIncome,
				"AccountTypeId", AcctType.Income,
				"AccountDescription", "Subscriptions received");
			table = TableFor("NameAddress");
			if (!RecordExists(table, 1)) {
				JObject d = new JObject().AddRange("idNameAddress", 1,
					"Type", "O",
					"Name", "");
				update(table, d);
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

		public Database(ServerConfig server) : base(server) {
		}

		/// <summary>
		/// Write an update transaction pair or other type of transaction to the audit trail
		/// </summary>
		public void Audit(AuditType type, string table, int? id, string transaction, string previous) {
			if (transaction == previous)
				return;
			Utils.Check(id != null, "Attempt to audit null record id");
			string date = Utils.Now.ToString("yyyy-MM-dd HH:mm:ss");
			int? userId = Module != null && Module.Session != null && Module.Session.User != null ? Module.Session.User.idUser : null;
			Execute("INSERT INTO AuditTrail (TableName, UserId, ChangeType, DateChanged, RecordId, Record) VALUES("
				+ Quote(table) + ", " + Quote(userId) + ", " + (int)type + ", " + Quote(date) + ", " + id + ", " + Quote(transaction) + ")");
			if(!string.IsNullOrEmpty(previous))
				Execute("INSERT INTO AuditTrail (TableName, UserId, ChangeType, DateChanged, RecordId, Record) VALUES("
				+ Quote(table) + ", " + Quote(userId) + ", " + (int)AuditType.Previous + ", " + Quote(date) + ", " + id + ", " + Quote(previous) + ")");
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

		/// <summary>
		/// Update the record uniquely identified by the data.
		/// If there is no such record, insert one, and fill in the Id field.
		/// Optionally save an audit trail.
		/// </summary>
		public void Update(JsonObject data, bool withAudit) {
			Table table = TableFor(data.GetType());
			JObject d = data.ToJObject();
			JObject result = null;
			if (withAudit) {
				// Primary index.
				Index index = table.Indexes[0];
				// Retrieve any existing record with the same primary key.
				// If auditing, get all the foreign key fields too.
				result = QueryOne("+", "WHERE " + index.Where(d), table.Name);
			}
			base.update(table.UpdateTable, d);
			if (withAudit) {
				Field idField = table.PrimaryKey;
				string idName = idField.Name;
				int id = d.AsInt(idName);
				// Retrieve the new record with all foreign key fields
				d = QueryOne("+", "WHERE " + idName + " = " + id, table.Name);
				AuditUpdate(table.Name, id, result, d);
			}
		}

		/// <summary>
		/// Delete the record from the table, optionally record in audit trail.
		/// </summary>
		public void Delete(string tableName, int id, bool withAudit) {
			Table t = TableFor(tableName);
			if (withAudit) {
				JObject old = withAudit ? QueryOne("+", "WHERE " + t.PrimaryKey.Name + '=' + id, t.Name) : null;
				if (old != null && !old.IsAllNull()) {
					Execute("DELETE FROM " + t.UpdateTable.Name + " WHERE " + t.PrimaryKey.Name + '=' + id);
					Audit(AuditType.Delete, t.Name, id, old);
				}
			} else {
				Execute("DELETE FROM " + t.UpdateTable.Name + " WHERE " + t.PrimaryKey.Name + '=' + id);
			}
		}

	}

}
