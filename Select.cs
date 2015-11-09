using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	public class Select : AppModule {

		public JObjectEnumerable Account(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				like("WHERE HideAccount = 0 or HideAccount is null", "AccountName", term) + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable AllAccounts(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount as hide",
				like("", "AccountName", term) + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable AccountTypes(string term) {
			return Database.Query(@"idAccountType AS id, AcctType AS value",
				like("", "AcctType", term) + " ORDER BY idAccountType",
				"AccountType");
		}

		public IEnumerable<JObject> AuditTypes() {
			for (AuditType t = AuditType.Insert; t <= AuditType.Delete; t++) {
				yield return new JObject().AddRange("id", (int)t, "value", t.UnCamel());
			}
		}

		public JObjectEnumerable BankAccount(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount AS hide",
				like("WHERE AccountTypeId in (" + (int)AcctType.Bank + "," + (int)AcctType.CreditCard + ")", "AccountName", term) 
				+ " ORDER BY idAccountType, AccountName",
					 "Account");
		}

		public JObjectEnumerable BankOrStockAccount(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount AS hide",
				like("WHERE AccountTypeId in (" + (int)AcctType.Bank + "," + (int)AcctType.CreditCard + "," + (int)AcctType.Investment + ")", "AccountName", term)
				+ " ORDER BY idAccountType, AccountName",
					 "Account");
		}

		public JObjectEnumerable Customer(string term) {
			return Name("C", term);
		}

		public JObjectEnumerable DocumentType(string term) {
			return Database.Query(@"idDocumentType AS id, DocType AS value",
				like("", "DocType", term) + " ORDER BY idDocumentType",
				"DocumentType");
		}

		public JObjectEnumerable ExpenseAccount(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				like("WHERE AccountTypeId " + Database.In(AcctType.Expense, AcctType.OtherExpense), "AccountName", term) + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable IncomeAccount(string term) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				like("WHERE AccountTypeId = " + (int)AcctType.Income, "AccountName", term) + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable Name(string term) {
			return Database.Query(@"idNameAddress AS id, Name AS value, CASE Type WHEN 'C' THEN 'Customers' WHEN 'S' THEN 'Suppliers' ELSE 'Others' END AS category, Hidden as hide",
				like("", "Name", term) + " ORDER BY Type, Name",
				"NameAddress");
		}

		public JObjectEnumerable Name(string nameType, string term) {
			return Database.Query(@"idNameAddress AS id, Name AS value, Hidden as hide, Address, Telephone",
				like("WHERE Type = " + Database.Quote(nameType), "Name", term) + " ORDER BY Name",
				"NameAddress");
		}

		public IEnumerable<JObject> NameTypes() {
			return new JObject [] {
				new JObject().AddRange("id", "C", "value", "Customer"),
				new JObject().AddRange("id", "S", "value", "Supplier"),
				new JObject().AddRange("id", "O", "value", "Other")
			};
		}

		public JObjectEnumerable Other(string term) {
			return Name("O", term);
		}

		public JObjectEnumerable Product(string term) {
			return Database.Query(@"idProduct AS id, ProductName AS value, ProductDescription, UnitPrice, VatCodeId, Code, VatDescription, Rate, AccountId, Unit",
				like("", "ProductName", term) + " ORDER BY ProductName",
				"Product");
		}

		public JObjectEnumerable ReportGroup(string term) {
			return Database.Query(@"ReportGroup AS id, ReportGroup AS value",
				like("", "ReportGroup", term) + " GROUP BY ReportGroup ORDER BY ReportGroup",
				"Report");
		}

		public JObjectEnumerable Security(string term) {
			return Database.Query(@"idSecurity AS id, SecurityName AS value",
				like("", "SecurityName", term) + " ORDER BY SecurityName",
				"Security");
		}

		public JObjectEnumerable Supplier(string term) {
			return Name("S", term);
		}

		public IEnumerable<JObject> VatCode(string term) {
			List<JObject> result = Database.Query(@"idVatCode AS id, Code, VatDescription, Rate",
				like("", "Code", term) + " ORDER BY Code",
				"VatCode").ToList();
			foreach (JObject o in result)
				o["value"] = o.AsString("Code") + " (" + o.AsDecimal("Rate") + "%)";
			result.Insert(0, new JObject().AddRange("id", null,
				"value", "",
				"Rate", 0));
			return result;
		}

		public IEnumerable<JObject> VatTypes() {
			return new JObject[] {
				new JObject().AddRange("id", -1, "value", "Sales"),
				new JObject().AddRange("id", 1, "value", "Purchases")
			};
		}

		public JObjectEnumerable VatPayments() {
			return Database.Query(@"SELECT idDocument as id, DocumentDate as value
FROM Document
JOIN Journal ON DocumentId = idDocument
WHERE AccountId = 8
AND JournalNum = 2
AND DocumentTypeId IN (7, 8, 9, 10)
ORDER BY idDocument");
		}

		public string like(string sql, string name, string term) {
			if (string.IsNullOrEmpty(term)) return sql;
			term = name + " LIKE '" + term + "%'";
			return string.IsNullOrEmpty(sql) ? "WHERE " + term : sql + " AND " + term;
		}

	}
}
