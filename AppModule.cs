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
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// Base class for all app modules
	/// </summary>
	[Auth(AccessLevel.ReadOnly)]
	public abstract class AppModule : CodeFirstWebFramework.AppModule {

		public new Database Database {
			get {
				return (Database)base.Database;
			}
		}

		public new Settings Settings {
			get { return (Settings)base.Settings; }
		}

		public string NewVersion {
			get { return Program.NewVersion; }
		}

		/// <summary>
		/// True if user does have Authorise
		/// </summary>
		public bool Authorise
		{
			get { return UserAccessLevel >= AccessLevel.Authorise; }
		}

		/// <summary>
		/// Generic object for templates to use - usually contains data from the database
		/// </summary>
		public object Record;

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
					"detail", Database.Query("idJournal, DocumentId, Line.VatCodeId, VatRate, JournalNum, Journal.AccountId, Memo, LineAmount, VatAmount, LineAmount + VatAmount AS Gross",
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
				FullAccount acct = null;
				DocType type = (DocType)document.DocumentTypeId;
				switch (type) {
					case DocType.Withdrawal:
					case DocType.Deposit:
					case DocType.CreditCardCharge:
					case DocType.CreditCardCredit:
						acct = Database.QueryOne<FullAccount>("*", "WHERE idAccount = " + document.DocumentAccountId, "Account");
						break;
				}
				allocateDocumentIdentifier(document, acct);
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
					case DocType.Withdrawal:
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
			} else if(record.VatPaid > 0) {
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

		/// <summary>
		/// List all the journals that post to this account, along with document info and the balance after the posting.
		/// Detects documents which are splits (i.e. have more than 2 journals) and sets the DocumentAccountName to "-split-"
		/// </summary>
		protected IEnumerable<JObject> detailsWithBalance(int id) {
			JObject last = null;	// previous document
			int lastId = 0;			// Id of previous journal
			decimal balance = 0;	// Running total balance
			// Query gets all journals to this account, joined to document header
			// Then joins to any other journals for this document, so documents with only 2 journals
			// will appear once, but if there are more than 2 journals the document will appear more times
			foreach (JObject l in Database.Query(@"SELECT Journal.idJournal, Document.*, NameAddress.Name As DocumentName, DocType, Journal.Cleared AS Clr, Journal.Amount As DocumentAmount, AccountName As DocumentAccountName
FROM Journal
LEFT JOIN Document ON idDocument = Journal.DocumentId
LEFT JOIN DocumentType ON DocumentType.idDocumentType = Document.DocumentTypeId
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN Journal AS J ON J.DocumentId = Journal.DocumentId AND J.AccountId <> Journal.AccountId
LEFT JOIN Account ON Account.idAccount = J.AccountId
WHERE Journal.AccountId = " + id + @"
ORDER BY DocumentDate, idDocument")) {
				if (last != null) {
					if (lastId == l.AsInt("idJournal")) {
						// More than 1 line in this document
						last["DocumentAccountName"] = "-split-";
						// Only emit each journal to this account once
						continue;
					}
					balance += last.AsDecimal("DocumentAmount");
					last["Balance"] = balance;
					yield return last;
				}
				last = l;
				lastId = l.AsInt("idJournal");
			}
			if (last != null) {
				balance += last.AsDecimal("DocumentAmount");
				last["Balance"] = balance;
				yield return last;
			}
		}

		/// <summary>
		/// Make sure the DocumentnameAddressId for a document is filled in,
		/// creating a new record if DocumentName does not already exist in the NameAddress table.
		/// </summary>
		/// <param name="nameType">The type the NameAddress record must be (S, C or O)</param>
		protected void fixNameAddress(Extended_Document document, string nameType) {
			if (document.DocumentNameAddressId == null || document.DocumentNameAddressId == 0) {
				document.DocumentNameAddressId = string.IsNullOrWhiteSpace(document.DocumentName) ? 1 : 
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
		/// Fill in the "next" and "previous" variables in record with the next and previous
		/// document ids.
		/// </summary>
		/// <param name="sql">Sql to add to document select to limit the documents returned,
		/// e.g. to the next cheque from this bank account.</param>
		protected void nextPreviousDocument(JObject record, string sql) {
			JObject header = (JObject)record["header"];
			int id = header.AsInt("idDocument");
			string d = Database.Quote(header.AsDate("DocumentDate"));
			JObject next = id == 0 ? null : Database.QueryOne("SELECT idDocument FROM Document " + sql
				+ " AND (DocumentDate > " + d + " OR (DocumentDate = " + d + " AND idDocument > " + id + "))"
				+ " ORDER BY DocumentDate, idDocument");
			if(next != null || ReadWrite)
				record["next"] = next == null ? 0 : next.AsInt("idDocument");
			JObject previous = Database.QueryOne("SELECT idDocument FROM Document " + sql
				+ (id == 0 ? "" : " AND (DocumentDate < " + d + " OR (DocumentDate = " + d + " AND idDocument < " + id + "))")
				+ " ORDER BY DocumentDate DESC, idDocument DESC");
			if (previous != null || ReadWrite)
				record["previous"] = previous == null ? 0 : previous.AsInt("idDocument");
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
		public AjaxReturn SaveRecord(JsonObject record, bool audit) {
			AjaxReturn retval = new AjaxReturn();
			try {
				if (record.Id <= 0)
					record.Id = null;
				Database.Update(record, audit);
				retval.id = record.Id;
			} catch (Exception ex) {
				Message = ex.Message;
				retval.error = ex.Message;
			}
			return retval;
		}

		// Select values
		public JObjectEnumerable SelectAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				"WHERE HideAccount = 0 or HideAccount is null ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable SelectAllAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount as hide",
				" ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable SelectAccountTypes() {
			return Database.Query(@"idAccountType AS id, AcctType AS value",
				" ORDER BY idAccountType",
				"AccountType");
		}

		public IEnumerable<JObject> SelectAuditTypes() {
			for (AuditType t = AuditType.Insert; t <= AuditType.Delete; t++) {
				yield return new JObject().AddRange("id", (int)t, "value", t.UnCamel());
			}
		}

		public JObjectEnumerable SelectBankAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount AS hide",
				"WHERE AccountTypeId " + Database.In(AcctType.Bank,AcctType.CreditCard)
				+ " ORDER BY idAccountType, AccountName",
					 "Account");
		}

		public JObjectEnumerable SelectBankOrOtherALAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount AS hide",
				"WHERE AccountTypeId " + Database.In(AcctType.Bank, AcctType.CreditCard, AcctType.OtherAsset, AcctType.OtherLiability)
				+ " ORDER BY idAccountType, AccountName",
					 "Account");
		}

		public JObjectEnumerable SelectBankOrStockAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, HideAccount AS hide",
				"WHERE AccountTypeId " + Database.In(AcctType.Bank, AcctType.CreditCard, AcctType.Investment, AcctType.OtherAsset, AcctType.OtherLiability)
				+ " ORDER BY idAccountType, AccountName",
					 "Account");
		}

		public JObjectEnumerable SelectCustomers() {
			return SelectNames("C");
		}

		public JObjectEnumerable SelectDocumentTypes() {
			return Database.Query(@"idDocumentType AS id, DocType AS value",
				" ORDER BY idDocumentType",
				"DocumentType");
		}

		public JObjectEnumerable SelectExpenseAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				"WHERE AccountTypeId " + Database.In(AcctType.Expense, AcctType.OtherExpense) + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable SelectIncomeAccounts() {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				"WHERE AccountTypeId = " + (int)AcctType.Income + " ORDER BY idAccountType, AccountName",
				"Account");
		}

		public JObjectEnumerable SelectNames() {
			return Database.Query(@"idNameAddress AS id, Name AS value, CASE Type WHEN 'C' THEN 'Customers' WHEN 'S' THEN 'Suppliers' ELSE 'Others' END AS category, Hidden as hide",
				" ORDER BY Type, Name",
				"NameAddress");
		}

		public JObjectEnumerable SelectNames(string nameType) {
			return Database.Query(@"idNameAddress AS id, Name AS value, Hidden as hide, Address, Telephone",
				"WHERE Type = " + Database.Quote(nameType) + " ORDER BY Name",
				"NameAddress");
		}

		public IEnumerable<JObject> SelectNameTypes() {
			return new JObject[] {
				new JObject().AddRange("id", "C", "value", "Customer"),
				new JObject().AddRange("id", "S", "value", "Supplier"),
				new JObject().AddRange("id", "M", "value", "Member"),
				new JObject().AddRange("id", "O", "value", "Other")
			};
		}

		public JObjectEnumerable SelectMemberTypes() {
			return Database.Query(@"SELECT idMemberType AS id, MemberTypeName AS value, AnnualSubscription, NumberOfPayments
FROM MemberType
ORDER BY MemberTypeName");
		}

		public JObjectEnumerable SelectOthers() {
			return SelectNames("O");
		}

		public JObjectEnumerable SelectProducts() {
			return Database.Query(@"idProduct AS id, ProductName AS value, ProductDescription, UnitPrice, VatCodeId, Code, VatDescription, Rate, AccountId, Unit",
				" ORDER BY ProductName",
				"Product");
		}

		public JObjectEnumerable SelectReportGroups() {
			return Database.Query(@"ReportGroup AS id, ReportGroup AS value",
				" GROUP BY ReportGroup ORDER BY ReportGroup",
				"Report");
		}

		public JObjectEnumerable SelectSecurities() {
			return Database.Query(@"idSecurity AS id, SecurityName AS value",
				" ORDER BY SecurityName",
				"Security");
		}

		public JObjectEnumerable SelectSuppliers() {
			return SelectNames("S");
		}

		public JObjectEnumerable SelectUsers() {
			return Database.Query(@"idUser AS id, Login AS value",
				"ORDER BY Login",
				"User");
		}

		public IEnumerable<JObject> SelectVatCodes() {
			List<JObject> result = Database.Query(@"idVatCode AS id, Code, VatDescription, Rate",
				" ORDER BY Code",
				"VatCode").ToList();
			foreach (JObject o in result)
				o["value"] = o.AsString("Code") + " (" + o.AsDecimal("Rate") + "%)";
			result.Insert(0, new JObject().AddRange("id", null,
				"value", "",
				"Rate", 0));
			return result;
		}

		public IEnumerable<JObject> SelectVatTypes() {
			return new JObject[] {
				new JObject().AddRange("id", -1, "value", "Sales"),
				new JObject().AddRange("id", 1, "value", "Purchases")
			};
		}

		public JObjectEnumerable SelectVatPayments() {
			return Database.Query(@"SELECT idDocument as id, DocumentDate as value
FROM Document
JOIN Journal ON DocumentId = idDocument
WHERE AccountId = 8
AND JournalNum = 2
AND DocumentTypeId IN (7, 8, 9, 10)
ORDER BY idDocument");
		}

	}

	/// <summary>
	/// Class to show errors
	/// </summary>
	public class ErrorModule : AppModule {
	}

}
