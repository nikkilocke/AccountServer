using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using CodeFirstWebFramework;

namespace AccountServer {
	public class Reports : AppModule {
		public enum DateRange {
			All = 1,
			Today,
			ThisWeek,
			ThisMonth,
			ThisQuarter,
			ThisYear,
			Yesterday,
			LastWeek,
			LastMonth,
			LastQuarter,
			LastYear,
			Custom,
			NDays,
			NMonths,
		}
		/// <summary>
		/// Fields which can be displayed in the report
		/// </summary>
		List<ReportField> _fields;
		/// <summary>
		/// Filters which can be applied
		/// </summary>
		List<Filter> _filters;
		/// <summary>
		/// Sort orders which can be chosen
		/// </summary>
		JArray _sortOrders;
		/// <summary>
		/// Sort order to use
		/// </summary>
		string _sortOrder;
		/// <summary>
		/// Sort order split into fields
		/// </summary>
		string[] _sortFields;
		bool _sortDescending;
		/// <summary>
		///  Whether to total
		/// </summary>
		bool _total;
		/// <summary>
		/// Whether to show a grand total (not, e.g., for the VAT detail report!)
		/// </summary>
		bool _grandTotal = true;
		/// <summary>
		/// Whether to split lines
		/// </summary>
		bool _split;
		/// <summary>
		/// Whether change type required in Audit reports
		/// </summary>
		bool _changeTypeNotRequired;
		/// <summary>
		/// All reports have a date filter
		/// </summary>
		DateFilter _dates;

		/// <summary>
		/// Reports menu
		/// </summary>
		public override void Default() {
			SessionData.Report = new JObject();
			Dictionary<string, List<JObject>> groups = new Dictionary<string, List<JObject>>();
			groups["Memorised Reports"] = new List<JObject>();
			List<JObject> reports = new List<JObject>();
			reports.Add(new JObject().AddRange("ReportName", "Document Report", "ReportType", "Documents", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Transaction Report", "ReportType", "Transactions", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Journals Report", "ReportType", "Journals", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Profit and Loss", "ReportType", "ProfitAndLoss", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Balance Sheet", "ReportType", "BalanceSheet", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Trial Balance", "ReportType", "TrialBalance", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "VAT Detail Report", "ReportType", "VatDetail", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Ageing Report", "ReportType", "Ageing", "idReport", 0));
			groups["Standard Reports"] = reports;
			reports = new List<JObject>();
			reports.Add(new JObject().AddRange("ReportName", "Accounts List", "ReportType", "Accounts", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Names List", "ReportType", "Names", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Members List", "ReportType", "Members", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Products List", "ReportType", "Products", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "VAT Codes List", "ReportType", "VatCodes", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Securities List", "ReportType", "Securities", "idReport", 0));
			groups["Lists"] = reports;
			reports = new List<JObject>();
			reports.Add(new JObject().AddRange("ReportName", "Audit Transactions Report", "ReportType", "AuditTransactions", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit Accounts Report", "ReportType", "AuditAccounts", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit Names Report", "ReportType", "AuditNames", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit Members Report", "ReportType", "AuditMembers", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit Products Report", "ReportType", "AuditProducts", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit VAT Codes Report", "ReportType", "AuditVatCodes", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Audit Securities Report", "ReportType", "AuditSecurities", "idReport", 0));
			reports.Add(new JObject().AddRange("ReportName", "Reconciliation Report", "ReportType", "AuditReconciliation", "idReport", 0));
			groups["Audit Reports"] = reports;
			foreach (JObject report in Database.Query("SELECT idReport, ReportGroup, ReportName, ReportType FROM Report ORDER BY ReportGroup, ReportName")) {
				string group = report.AsString("ReportGroup");
				if (!groups.TryGetValue(group, out reports)) {
					reports = new List<JObject>();
					groups[group] = reports;
				}
				reports.Add(report);
			}
			Record = groups;
		}

		public void Accounts(int id) {
			Record = AccountsPost(getJson(id, "Accounts List"));
		}

		public object AccountsPost(JObject json) {
			initialiseReport(json);
			accountSetup();
			setDefaultFields(json, "AccountName", "AccountDescription", "AcctType");
			makeSortable("AccountName", "AcctType", "AccountCode,AccountName=AccountCode");
			return finishReport(json, "Account", "AccountName", "LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId");
		}

		void accountSetup() {
			addTable("Account");
			addTable("AccountType", "idAccountType", "AcctType");
			fieldFor("idAccountType").MakeEssential().Hide();
			_filters.Add(new StringFilter("AccountName", "Account.AccountName"));
			_filters.Add(new StringFilter("AccountDescription", "Account.AccountDescription"));
			_filters.Add(new RecordFilter("AccountType", "Account.AccountTypeId", SelectAccountTypes()));
		}

		public void AuditAccounts(int id) {
			Record = AuditAccountsPost(getJson(id, "Accounts Audit Report"));
			Method = "accounts";
		}

		public object AuditAccountsPost(JObject json) {
			initialiseAuditReport(json);
			accountSetup();
			return auditReportData(json, "Account", "AccountName", "AccountDescription", "AcctType");
		}

		/// <summary>
		/// Audit history of an arbitrary record in an arbitrary table
		/// </summary>
		public void AuditHistory(string table, int id) {
			Utils.Check(id > 0, "Invalid record id {0}", id);
			JObject json = new JObject().AddRange(
				"ReportName", "Audit trail",
				"ReportType", "Audit" + table,
				"idReport", null,
				"recordId", id);
			Record = AuditHistoryPost(json);
		}

		public object AuditHistoryPost(JObject json) {
			OriginalMethod = json.AsString("ReportType");
			Method = OriginalMethod.Substring(5).ToLower();
			MethodInfo method = this.GetType().GetMethod(OriginalMethod + "Post", BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
			Utils.Check(method != null, "Invalid table {0}", Method);
			return method.Invoke(this, new object[] { json });
		}

		public void AuditNames(int id) {
			Record = AuditNamesPost(getJson(id, "Names Audit Report"));
			Method = "names";
		}

		public object AuditNamesPost(JObject json) {
			initialiseAuditReport(json);
			namesSetup();
			return auditReportData(json, "NameAddress", "Type", "Name", "Address", "PostCode", "Telephone", "Email", "Contact");
		}

		public void AuditMembers(int id) {
			Record = AuditMembersPost(getJson(id, "Members Audit Report"));
			Method = "members";
		}

		public object AuditMembersPost(JObject json) {
			initialiseAuditReport(json);
			membersSetup();
			return auditReportData(json, "Full_Member", "MemberTypeName", "MemberNo", "Name", "Address", "PostCode", "Telephone", "Email", "Contact", "AnnualSubscription", "PaymentAmount", "AmountDue");
		}

		public void AuditProducts(int id) {
			Record = AuditProductsPost(getJson(id, "Products Audit Report"));
			Method = "products";
		}

		public object AuditProductsPost(JObject json) {
			initialiseAuditReport(json);
			addTable("Product");
			addTable("Account", "AccountName");
			addTable("VatCode");
			_filters.Add(new StringFilter("ProductName", "Product.ProductName"));
			_filters.Add(new StringFilter("ProductDescription", "Product.ProductDescription"));
			_filters.Add(new DecimalFilter("UnitPrice", "Product.UnitPrice"));
			return auditReportData(json, "Product", "ProductName", "ProductDescription", "UnitPrice", "Code", "AccountName");
		}

		public void AuditSecurities(int id) {
			Record = AuditSecuritiesPost(getJson(id, "Securities Audit Report"));
			Method = "securities";
		}

		public object AuditSecuritiesPost(JObject json) {
			initialiseAuditReport(json);
			addTable("Security");
			_filters.Add(new StringFilter("SecurityName", "Security.SecurityName"));
			_filters.Add(new StringFilter("Ticker", "Security.Ticker"));
			return auditReportData(json, "Security", "SecurityName", "Ticker");
		}

		public void AuditReconciliation(int id) {
			Record = AuditReconciliationPost(getJson(id, "Reconciliation Report"));
			Method = "transactions";
		}

		public object AuditReconciliationPost(JObject json) {
			// Not looking at changes - reconciliations are stored as created
			_changeTypeNotRequired = true;
			initialiseAuditReport(json);
			addTable("!Account", "AccountName", "AccountDescription", "EndingBalance");
			_fields.Add(new ReportField("OpeningBalance", "decimal", "Opening Balance") { Table = "Account" });
			_fields.Add(new ReportField("ClearedBalance", "decimal", "Cleared Balance") { Table = "Account" });
			addTable("Extended_Document", "idDocument", "DocumentDate", "DocumentIdentifier", "DocumentName", "DocumentAddress", "DocumentAmount", "DocumentOutstanding", "DocType", "DocumentTypeId");
			fieldFor("idDocument")["heading"] = "Trans no";
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentTypeId").MakeEssential().Hide();
			addTable("Journal", "Amount", "Cleared");
			fieldFor("Cleared")["type"] = "checkbox";
			_filters.Add(new RecordFilter("Account", "idAccount", SelectBankAccounts()));
			_split = true;
			return auditReportData(json, "Reconciliation", "AccountName", "OpeningBalance", "EndingBalance", "ClearedBalance", "DocumentDate", "DocType", "DocumentIdentifier", "DocumentName", "Cleared", "Amount");
		}

		public void AuditTransactions(int id) {
			Record = AuditTransactionsPost(getJson(id, "Transactions Audit Report"));
			Method = "transactions";
		}

		public object AuditTransactionsPost(JObject json) {
			initialiseAuditReport(json);
			addTable("Extended_Document", "idDocument", "DocumentDate", "DocumentIdentifier", "DocumentName", "DocumentAddress", "DocumentAmount", "DocumentOutstanding", "DocType", "DocumentTypeId", "DocumentMemo");
			fieldFor("idDocument")["heading"] = "Trans no";
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentTypeId").MakeEssential().Hide();
			addTable("Journal");
			addTable("Account", "AccountName");
			addTable("NameAddress", "Name");
			addTable("VatCode", "Code");
			addTable("Line");
			addTable("Product", "ProductName");
			_filters.Add(new DateFilter(Settings, "DocumentDate", DateRange.All));
			_filters.Add(new StringFilter("Id", "DocumentIdentifier"));
			_filters.Add(new DecimalFilter("DocumentAmount", "Extended_Document.DocumentAmount"));
			_filters.Add(new DecimalFilter("DocumentOutstanding", "Extended_Document.DocumentOutstanding"));
			_filters.Add(new RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new RecordFilter("Account", "Journal.AccountId", SelectAccounts()));
			_filters.Add(new RecordFilter("NameAddress", "Journal.NameAddressId", SelectNames()));
			_filters.Add(new RecordFilter("VatCode", "Line.VatCodeId", SelectVatCodes()));
			_filters.Add(new RecordFilter("Product", "Line.ProductId", SelectProducts()));
			_filters.Add(new DecimalFilter("JournalAmount", "Journal.Amount"));
			_filters.Add(new StringFilter("Memo", "Journal.Memo"));
			_split = true;
			return auditReportData(json, "Document", "idDocument", "DocType", "DocumentDate", "Name", "DocumentIdentifier", "DocumentAmount", "DocumentOutstanding", "AccountName", "Debit", "Credit", "Qty", "Memo", "Code", "VatRate", "VatAmount");
		}

		public void AuditVatCodes(int id) {
			Record = AuditVatCodesPost(getJson(id, "VAT Codes Audit Report"));
			Method = "vatcodes";
		}

		public object AuditVatCodesPost(JObject json) {
			initialiseAuditReport(json);
			vatCodeSetup();
			return auditReportData(json, "Code", "VatDescription", "Rate");
		}

		/// <summary>
		/// Ageing report splits outstdanding debt by date
		/// </summary>
		public void Ageing(int id) {
			Record = AgeingPost(getJson(id, "Ageing Report"));
		}

		public object AgeingPost(JObject json) {
			initialiseReport(json);
			// Can select Sales or Purchases
			JObject [] accountSelect = new JObject[] {
				new JObject().AddRange("id", (int)Acct.SalesLedger, "value", "Sales Ledger"),
				new JObject().AddRange("id", (int)Acct.PurchaseLedger, "value", "Purchase Ledger")
			};
			ReportField acct = new ReportField("AccountId", "select", "Account");
			acct["selectOptions"] = new JArray(accountSelect);
			acct.Essential = true;
			_fields.Add(acct);
			_fields.Add(new ReportField("NameAddressId", "int", "NameAddressId").Hide().MakeEssential());
			_fields.Add(new ReportField("Name", "string", "Name"));
			// Fields for each ageing bucket
			_fields.Add(new ReportField("SUM(CASE WHEN age BETWEEN 0 AND 29 THEN Outstanding ELSE 0 END) AS Current", "decimal", "Current"));
			for(int i = 1; i < 90; i += 30)
				_fields.Add(new ReportField("SUM(CASE WHEN age BETWEEN " + (i + 29) + " AND " + (i + 58) + " THEN Outstanding ELSE 0 END) AS b" + i, "decimal", i + "-" + (i + 29)));
			_fields.Add(new ReportField("SUM(CASE WHEN age > 120 THEN Outstanding ELSE 0 END) AS old", "decimal", ">90"));
			_fields.Add(new ReportField("SUM(Outstanding) AS Total", "decimal", "Total"));
			RecordFilter account = new RecordFilter("Account", "Journal.AccountId", accountSelect) {
				Apply = false
			};
			_filters.Add(account);
			_sortOrder = "";
			_total = true;
			setDefaultFields(json, "AccountId", "Name", "Current", "b1", "b31", "b61", "old", "Total");
			setFilters(json);	// we account filter value setting now
			string where = account.Active ? account.Where(Database) : "AccountId IN (1, 2)";
			return finishReport(json, @"(SELECT AccountId, NameAddressId, Name, Outstanding, 
    DATEDIFF(" + Database.Quote(Utils.Today) + @", DocumentDate) AS age
FROM Journal
JOIN Document ON idDocument = DocumentId
JOIN NameAddress ON idNameAddress = NameAddressId
WHERE " + where + @"
AND Outstanding <> 0
) AS DaysDue", "AccountId,Name", 
			 "GROUP BY AccountId, Name");
		}

		public void BalanceSheet(int id) {
			Record = BalanceSheetPost(getJson(id, "Balance Sheet"));
		}

		public object BalanceSheetPost(JObject json) {
			_total = false;
			initialiseReport(json);
			addTable("!AccountType");
			addTable("Account", "idAccount", "AccountCode", "AccountName", "AccountDescription");
			fieldFor("idAccount").Hide();
			fieldFor("AccountName")["sClass"] = "sa";
			fieldFor("Heading").MakeEssential();
			fieldFor("Negate").MakeEssential().Hide();
			fieldFor("BalanceSheet").MakeEssential().Hide();
			DateFilter date = new DateFilter(Settings, "DocumentDate", DateRange.LastYear);
			ReportField cp = new ReportField("CurrentPeriod", "decimal", "Current Period");
			_fields.Add(cp);
			ReportField lp = new ReportField("PreviousPeriod", "decimal", "Previous Period");
			_fields.Add(lp);
			_filters.Add(date);
			setDefaultFields(json, "Heading", "AcctType", "AccountName", "CurrentPeriod", "PreviousPeriod");
			_sortOrder = "AcctType";
			setFilters(json);
			// Balance sheet needs 2 period buckets for the 2 columns
			DateTime[] cPeriod = date.CurrentPeriod();
			cp["heading"] = date.PeriodName(cPeriod);
			DateTime[] lPeriod = date.PreviousPeriod();
			lp["heading"] = date.PeriodName(lPeriod);
			string[] sort = new string[] { "AccountTypeId", "AccountCode", "AccountName" };
			string[] fields = _fields.Where(f => f.Include || f.Essential || _sortFields.Contains(f.Name)).Select(f => f.FullFieldName).Distinct().ToArray();
			// We want one record per account, with totals for each bucket, and an Old value 
			// which is sum of all transactions before first bucket (opening balance)
			JObjectEnumerable report = Database.Query("SELECT " + string.Join(",", fields) + @", Old
FROM AccountType
LEFT JOIN Account ON Account.AccountTypeId = AccountType.idAccountType
JOIN (SELECT AccountId,
SUM(CASE WHEN DocumentDate < " + Database.Quote(cPeriod[1]) + " AND DocumentDate >= " + Database.Quote(cPeriod[0]) + @" THEN Amount ELSE 0 END) AS CurrentPeriod,
SUM(CASE WHEN DocumentDate < " + Database.Quote(lPeriod[1]) + " AND DocumentDate >= " + Database.Quote(lPeriod[0]) + @" THEN Amount ELSE 0 END) AS PreviousPeriod,
SUM(CASE WHEN DocumentDate < " + Database.Quote(lPeriod[0]) + @" THEN Amount ELSE 0 END) AS Old
FROM Journal
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
WHERE DocumentDate < " + Database.Quote(cPeriod[1]) + @"
GROUP BY AccountId
) AS Summary ON AccountId = idAccount
ORDER BY " + string.Join(",", sort.Select(s => s + (_sortDescending ? " DESC" : "")).ToArray())
				);
			_sortFields = new string[] { "Heading", "AcctType", "AccountCode", "AccountName" };
			// Report now needs further processing to:
			// Calculate retained earnings account
			// Add investment gains
			// Consolidate P & L accounts and produce totals
			return reportJson(json, fixBalanceSheet(addInvestmentGains(addRetainedEarnings(report), "Old", lPeriod[0], "PreviousPeriod", cPeriod[0], "CurrentPeriod", cPeriod[1])), "AccountType", "Account");
		}

		public void Documents(int id) {
			Record = DocumentsPost(getJson(id, "Documents Report"));
			Method = "transactions";
		}

		public object DocumentsPost(JObject json) {
			initialiseReport(json);
			addTable("Extended_Document");
			fieldFor("idDocument")["heading"] = "Trans no";
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentTypeId").MakeEssential().Hide();
			fieldFor("DocumentNameAddressId").Hide();
			fieldFor("DocumentAccountId").Hide();
			fieldFor("VatPaid")["type"] = "checkbox";
			addTable("NameAddress", "Type", "Telephone", "Email", "Contact");
			fieldFor("Type")["type"] = "select";
			fieldFor("Type")["selectOptions"] = new JArray(SelectNameTypes());
			fieldFor("Email")["type"] = "email";
			_filters.Add(new DateFilter(Settings, "DocumentDate", DateRange.ThisMonth));
			_filters.Add(new StringFilter("Id", "DocumentIdentifier"));
			_filters.Add(new DecimalFilter("DocumentAmount", "Extended_Document.DocumentAmount"));
			_filters.Add(new DecimalFilter("DocumentOutstanding", "Extended_Document.DocumentOutstanding"));
			_filters.Add(new RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new RecordFilter("NameAddress", "DocumentNameAddressId", SelectNames()));
			_filters.Add(new StringFilter("DocumentMemo", "DocumentMemo"));
			makeSortable("idDocument=Trans no", "DocumentDate", "DocumentIdentifier=Doc Id", "Type,DocumentName=Document Name", "DocumentAmount", "DocType");
			setDefaultFields(json, "idDocument", "DocType", "DocumentDate", "DocumentName", "DocumentIdentifier", "DocumentAmount", "DocumentOutstanding");
			return finishReport(json, "Extended_Document", "idDocument", "LEFT JOIN NameAddress ON NameAddress.idNameAddress = DocumentNameAddressId", 
				"Extended_Document");
		}

		public void Journals(int id) {
			Record = JournalsPost(getJson(id, "Journals Report"));
		}

		public object JournalsPost(JObject json) {
			initialiseReport(json);
			addTable("AccountType");
			fieldFor("idAccountType").Hide().Essential = false;
			addTable("Account", "idAccount", "AccountCode", "AccountName", "AccountDescription");
			addTable("!Journal");
			addTable("!NameAddress");
			addTable("Document", "idDocument", "DocumentDate", "DocumentIdentifier", "DocumentTypeId", "DocumentMemo");
			fieldFor("idDocument").MakeEssential()["heading"] = "Trans no";
			addTable("DocumentType", "DocType");
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentDate").FullFieldName = "rDocDate AS DocumentDate";
			fieldFor("DocumentTypeId").MakeEssential().Hide().FullFieldName = "rDocType AS DocumentTypeId";
			fieldFor("Amount").FullFieldName = "Result.Amount";
			fieldFor("Credit").FullFieldName = "Result.Amount";
			fieldFor("Debit").FullFieldName = "Result.Amount";
			fieldFor("idAccount").Hide().Essential = true;
			DateFilter date = new DateFilter(Settings, "DocumentDate", DateRange.ThisMonth);
			RecordFilter account = new RecordFilter("Account", "Journal.AccountId", SelectAccounts());
			date.Apply = false;
			account.Apply = false;
			_filters.Add(date);
			_filters.Add(account);
			_filters.Add(new StringFilter("Id", "DocumentIdentifier"));
			_filters.Add(new RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new RecordFilter("NameAddress", "Journal.NameAddressId", SelectNames()));
			_filters.Add(new DecimalFilter("JournalAmount", "Result.Amount"));
			_filters.Add(new StringFilter("Memo", "Journal.Memo"));
			_sortOrder = "idAccountType,AcctType,AccountName";
			makeSortable("idAccountType,AcctType,AccountCode,AccountName=Account Type", "AccountName", "AccountCode,AccountName=AccountCode", "Name", "DocumentDate", "DocumentIdentifier=Doc Id", "DocType");
			setDefaultFields(json, "AcctType", "AccountName", "Amount", "Memo", "Name", "DocType", "DocumentDate", "DocumentIdentifier");
			setFilters(json);	// we need account filter now!
			string where = account.Active ? "\r\nAND " + account.Where(Database) : "";
			// Need opening balance before start of period
			// Journals in period
			// Security gains/losses
			List<JObject> report = finishReport(@"(
SELECT * FROM 
(SELECT Account.idAccount AS rAccount, Account.AccountTypeId as rAcctType, SUM(Journal.Amount) AS Amount, " + (int)DocType.OpeningBalance + " AS rDocType, 0 as rJournal, 0 as rDocument, 0 AS rJournalNum, "
			+ Database.Cast(Database.Quote(date.CurrentPeriod()[0]), "DATETIME") + @" AS rDocDate
FROM Account
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN Journal ON Journal.AccountId = Account.idAccount
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
WHERE DocumentDate < " + Database.Quote(date.CurrentPeriod()[0]) + @"
AND BalanceSheet = 1" + where + @"
GROUP BY AccountName) AS OpeningBalances
WHERE Amount <> 0 OR rAcctType IN (" + (int)AcctType.Investment + "," + (int)AcctType.Security + @")
UNION
SELECT Account.idAccount AS rAccount, Account.AccountTypeId as rAcctType, Journal.Amount, DocumentTypeId As rDocType, idJournal AS rJournal, idDocument as rDocument, 
	JournalNum as rJournal, DocumentDate AS rDocDate
FROM Account
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN Journal ON Journal.AccountId = Account.idAccount
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
WHERE " + date.Where(Database) + where + @"
UNION
SELECT Account.idAccount AS rAccount, Account.AccountTypeId as rAcctType, 0 AS Amount, " + (int)DocType.Gain + " AS rDocType, 0 as rJournal, 0 as rDocument, 0 AS rJournalNum, "
			+ Database.Cast(Database.Quote(date.CurrentPeriod()[1].AddDays(-1)), "DATETIME") + @" AS rDocDate
FROM Account
WHERE AccountTypeId = " + (int)AcctType.Security + where.Replace("Journal.AccountId", "idAccount") + @"
) AS Result", "idAccountType,AccountName,DocumentDate,idDocument,JournalNum", @"
LEFT JOIN Account on Account.idAccount = rAccount
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN Journal ON Journal.idJournal = rJournal
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN Document ON Document.idDocument = rDocument
LEFT JOIN DocumentType ON DocumentType.idDocumentType = rDocType
", json).ToList();
			return reportJson(json, addInvestmentGains(date.CurrentPeriod(), account, report), "Account", "AccountType");
		}

		public void Names(int id) {
			Record = NamesPost(getJson(id, "Names List"));
		}

		public object NamesPost(JObject json) {
			initialiseReport(json);
			namesSetup();
			makeSortable("Name", "Type");
			setDefaultFields(json, "Type", "Name", "Address", "PostCode", "Telephone", "Email", "Contact");
			return finishReport(json, "NameAddress", "Type,Name", "");
		}

		void namesSetup() {
			addTable("NameAddress");
			fieldFor("Type").MakeEssential();
			fieldFor("Type")["type"] = "select";
			fieldFor("Type")["selectOptions"] = new JArray(SelectNameTypes());
			fieldFor("Email")["type"] = "email";
			_filters.Add(new SelectFilter("Type", "NameAddress.Type", SelectNameTypes()));
			_filters.Add(new StringFilter("Name", "NameAddress.Name"));
			_filters.Add(new StringFilter("PostCode", "NameAddress.PostCode"));
		}

		public void Members(int id) {
			Record = MembersPost(getJson(id, "Members List"));
		}

		public object MembersPost(JObject json) {
			initialiseReport(json);
			membersSetup();
			makeSortable("Name", "MemberTypeName");
			setDefaultFields(json, "MemberTypeName", "Name", "Address", "PostCode", "Telephone", "Email", "Contact", "AnnualSubscription", "PaymentAmount", "AmountDue");
			return finishReport(json, "Full_Member", "Name", "");
		}

		void membersSetup() {
			addTable("Full_Member");
			fieldFor("MemberTypeName")["heading"] = "Type";
			fieldFor("Email")["type"] = "email";
			_filters.Add(new StringFilter("Name", "Full_Member.Name"));
			_filters.Add(new StringFilter("PostCode", "Full_Member.PostCode"));
			_filters.Add(new DecimalFilter("Amount Due", "AmountDue"));
			_filters.Add(new SelectFilter("Type", "Member.MemberTypeId", SelectMemberTypes()));
		}

		public void Products(int id) {
			Record = ProductsPost(getJson(id, "Products List"));
		}

		public object ProductsPost(JObject json) {
			initialiseReport(json);
			addTable("Product");
			addTable("Account", "AccountCode", "AccountName", "AccountDescription");
			addTable("AccountType");
			addTable("VatCode");
			_filters.Add(new StringFilter("ProductName", "Product.ProductName"));
			_filters.Add(new StringFilter("ProductDescription", "Product.ProductDescription"));
			_filters.Add(new DecimalFilter("UnitPrice", "Product.UnitPrice"));
			makeSortable("ProductName", "UnitPrice", "Code", "AccountName", "AccountCode,AccountName=AccountCode");
			setDefaultFields(json, "ProductName", "ProductDescription", "UnitPrice", "Code", "AccountName");
			return finishReport(json, "Product", "ProductName", @"
LEFT JOIN Account ON idAccount = AccountId
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN VatCode ON idVatCode = VatCodeId
");
		}

		public void ProfitAndLoss(int id) {
			Record = ProfitAndLossPost(getJson(id, "Profit and Loss"));
		}

		public object ProfitAndLossPost(JObject json) {
			_total = false;
			initialiseReport(json);
			addTable("!AccountType");
			addTable("Account", "idAccount", "AccountCode", "AccountName", "AccountDescription");
			fieldFor("idAccount").Hide();
			fieldFor("AccountName")["sClass"] = "sa";
			fieldFor("Heading").MakeEssential().Hide();
			fieldFor("Negate").MakeEssential().Hide();
			fieldFor("BalanceSheet").MakeEssential().Hide();
			DateFilter date = new DateFilter(Settings, "DocumentDate", DateRange.LastYear);
			ReportField cp = new ReportField("SUM(Amount) AS CurrentPeriod", "decimal", "Current Period");
			_fields.Add(cp);
			ReportField lp = new ReportField("SUM(Amount) AS PreviousPeriod", "decimal", "Previous Period");
			_fields.Add(lp);
			_filters.Add(date);
			setDefaultFields(json, "AcctType", "AccountName", "CurrentPeriod", "PreviousPeriod");
			_sortOrder = "AcctType";
			setFilters(json);
			// P & L needs 2 period buckets for the 2 columns
			DateTime[] cPeriod = date.CurrentPeriod();
			cp.FullFieldName = "SUM(CASE WHEN DocumentDate >= " + Database.Quote(cPeriod[0]) + " AND DocumentDate < " + Database.Quote(cPeriod[1]) + " THEN Amount ELSE 0 END) AS CurrentPeriod";
			cp["heading"] = date.PeriodName(cPeriod);
			DateTime[] lPeriod = date.PreviousPeriod();
			lp.FullFieldName = "SUM(CASE WHEN DocumentDate >= " + Database.Quote(lPeriod[0]) + " AND DocumentDate < " + Database.Quote(lPeriod[1]) + " THEN Amount ELSE 0 END) AS PreviousPeriod";
			lp["heading"] = date.PeriodName(lPeriod);
			string [] sort = new string[] { "AccountTypeId", "AccountCode", "AccountName" };
			string[] fields = _fields.Where(f => f.Include || f.Essential || _sortFields.Contains(f.Name)).Select(f => f.FullFieldName).Distinct().ToArray();
			JObjectEnumerable report = Database.Query("SELECT " + string.Join(",", fields)
				+ @"
FROM AccountType
LEFT JOIN Account ON Account.AccountTypeId = AccountType.idAccountType
JOIN Journal ON Journal.AccountId = Account.idAccount
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
"
				+ "\r\nWHERE BalanceSheet = 0"
				+ "\r\nAND ((DocumentDate >= " + Database.Quote(lPeriod[0])
				+ "\r\nAND DocumentDate < " + Database.Quote(cPeriod[1]) + ")"
				+ "\r\nOR Account.AccountTypeId = " + (int)AcctType.Security + ")"
				+ "\r\nGROUP BY idAccount"
				+ "\r\nORDER BY " + string.Join(",", sort.Select(s => s + (_sortDescending ? " DESC" : "")).ToArray())
				);
			// Needs further processing to add investment gains
			// total, etc.
			return reportJson(json, fixProfitAndLoss(addInvestmentGains(report.ToList(), "Old", lPeriod[0], "PreviousPeriod", cPeriod[0], "CurrentPeriod", cPeriod[1])), "AccountType", "Account");
		}

		public void Securities(int id) {
			Record = SecuritiesPost(getJson(id, "Securities List"));
		}

		public object SecuritiesPost(JObject json) {
			initialiseReport(json);
			addTable("Security");
			addTable("StockPrice");
			_filters.Add(new StringFilter("SecurityName", "Security.SecurityName"));
			_filters.Add(new StringFilter("Ticker", "Security.Ticker"));
			makeSortable("SecurityName", "Ticker", "Date");
			setDefaultFields(json, "SecurityName", "Ticker", "Date", "Price");
			return finishReport(json, "Security", "SecurityName, Date", "JOIN StockPrice ON SecurityId = idSecurity", "Security");
		}

		public void Transactions(int id) {
			Record = TransactionsPost(getJson(id, "Transactions Report"));
		}

		public object TransactionsPost(JObject json) {
			initialiseReport(json);
			addTable("Extended_Document", "idDocument", "DocumentDate", "DocumentIdentifier", "DocumentName", "DocumentAddress", "DocumentAmount", "DocumentOutstanding", "DocType", "DocumentTypeId");
			fieldFor("idDocument")["heading"] = "Trans no";
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentTypeId").MakeEssential().Hide();
			addTable("Journal");
			addTable("Account", "AccountCode", "AccountName", "AccountDescription");
			addTable("AccountType");
			addTable("NameAddress");
			fieldFor("Type")["type"] = "select";
			fieldFor("Type")["selectOptions"] = new JArray(SelectNameTypes());
			fieldFor("Email")["type"] = "email";
			addTable("Line");
			addTable("Product", "ProductName", "ProductDescription", "UnitPrice");
			fieldFor("UnitPrice")["heading"] = "List Price";
			addTable("VatCode");
			_filters.Add(new DateFilter(Settings, "DocumentDate", DateRange.ThisMonth));
			_filters.Add(new StringFilter("Id", "DocumentIdentifier"));
			_filters.Add(new DecimalFilter("DocumentAmount", "Extended_Document.DocumentAmount"));
			_filters.Add(new DecimalFilter("DocumentOutstanding", "Extended_Document.DocumentOutstanding"));
			_filters.Add(new RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new RecordFilter("Account", "Journal.AccountId", SelectAccounts()));
			_filters.Add(new RecordFilter("NameAddress", "Journal.NameAddressId", SelectNames()));
			_filters.Add(new DecimalFilter("JournalAmount", "Journal.Amount"));
			_filters.Add(new StringFilter("Memo", "Journal.Memo"));
			_filters.Add(new RecordFilter("VatCode", "Line.VatCodeId", SelectVatCodes()));
			_filters.Add(new RecordFilter("Product", "Line.ProductId", SelectProducts()));
			makeSortable("idDocument=Trans no", "DocumentDate", "DocumentIdentifier=Doc Id", "Type,DocumentName=Document Name", "DocumentAmount", "DocType");
			setDefaultFields(json, "idDocument", "DocType", "DocumentDate", "DocumentName", "DocumentIdentifier", "DocumentAmount", "DocumentOutstanding", "AccountName", "Debit", "Credit", "Qty", "Memo", "Code", "VatRate", "VatAmount");
			return finishReport(json, "Journal", "idDocument,JournalNum", @"
LEFT JOIN Line ON Line.idLine = Journal.idJournal
LEFT JOIN Extended_Document ON Extended_Document.idDocument = Journal.DocumentId
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN VatCode ON VatCode.idVatCode = Line.VatCodeId
LEFT JOIN Account ON Account.idAccount = Journal.AccountId
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN Product ON Product.idProduct = Line.ProductId
", "Extended_Document", "DocumentType");
		}

		public void TrialBalance(int id) {
			Record = TrialBalancePost(getJson(id, "Trial Balance"));
		}

		public object TrialBalancePost(JObject json) {
			_total = false;
			initialiseReport(json);
			addTable("!AccountType", "Heading", "AcctType");
			addTable("Account", "idAccount", "AccountCode", "AccountName", "AccountDescription");
			fieldFor("idAccount").Hide();
			addTable("Journal", "Amount");
			fieldFor("Amount").FullFieldName = "Amount";
			fieldFor("Credit").FullFieldName = "Amount";
			fieldFor("Debit").FullFieldName = "Amount";
			DateFilter date = new DateFilter(Settings, "DocumentDate", DateRange.LastYear);
			_filters.Add(date);
			setDefaultFields(json, "AccountName", "Credit", "Debit");
			_sortOrder = "AcctType";
			setFilters(json);
			DateTime[] cPeriod = date.CurrentPeriod();
			string[] sort = new string[] { "AccountTypeId", "AccountCode", "AccountName" };
			string[] fields = _fields.Where(f => f.Include || f.Essential || _sortFields.Contains(f.Name)).Select(f => f.FullFieldName).Distinct().ToArray();
			// Need Old (= opening balance) and final values for each account
			JObjectEnumerable report = Database.Query("SELECT " + string.Join(",", fields) + @", BalanceSheet, Old
FROM AccountType
LEFT JOIN Account ON Account.AccountTypeId = AccountType.idAccountType
JOIN (SELECT AccountId,
SUM(CASE WHEN DocumentDate < " + Database.Quote(cPeriod[1]) + " AND DocumentDate >= " + Database.Quote(cPeriod[0]) + @" THEN Amount ELSE 0 END) AS Amount,
SUM(CASE WHEN DocumentDate < " + Database.Quote(cPeriod[0]) + @" THEN Amount ELSE 0 END) AS Old
FROM Journal
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
WHERE DocumentDate < " + Database.Quote(cPeriod[1]) + @"
GROUP BY AccountId
) AS Summary ON AccountId = idAccount
ORDER BY " + string.Join(",", sort.Select(s => s + (_sortDescending ? " DESC" : "")).ToArray())
				);
			_sortFields = new string[] { "Heading", "AcctType", "AccountCode", "AccountName" };
			// Need to add investment gains
			// then process further to sort, add opening balances where required, and total
			return reportJson(json, fixTrialBalance(addInvestmentGains(addRetainedEarnings(report), "Old", cPeriod[0], "Amount", cPeriod[1])), "AccountType", "Account");
		}

		public void VatCodes(int id) {
			Record = VatCodesPost(getJson(id, "VAT Codes List"));
		}

		public object VatCodesPost(JObject json) {
			initialiseReport(json);
			vatCodeSetup();
			makeSortable("Code", "VatDescription");
			setDefaultFields(json, "Code", "VatDescription", "Rate");
			return finishReport(json, "VatCode", "Code", "");
		}

		void vatCodeSetup() {
			addTable("VatCode");
			_filters.Add(new StringFilter("Code", "VatCode.Code"));
			_filters.Add(new StringFilter("VatCodeDescription", "VatCode.VatDescription"));
			_filters.Add(new DecimalFilter("Rate", "VatCode.Rate"));
		}

		public void VatDetail(int id) {
			Record = VatDetailPost(getJson(id, "VAT Detail Report"));
		}

		public object VatDetailPost(JObject json) {
			initialiseReport(json);
			_total = true;
			_grandTotal = false;
			addTable("Vat_Journal");
			fieldFor("idDocument")["heading"] = "Trans no";
			fieldFor("DocumentIdentifier")["heading"] = "Doc Id";
			fieldFor("DocumentTypeId").MakeEssential().Hide();
			fieldFor("DocumentAmount").FullFieldName = "DocumentAmount * Sign * VatType AS DocumentAmount";
			fieldFor("DocumentOutstanding").FullFieldName = "DocumentOutstanding * Sign * VatType AS DocumentOutstanding";
			fieldFor("VatType")["type"] = "select";
			fieldFor("VatType")["selectOptions"] = SelectVatTypes().ToJToken();
			fieldFor("LineAmount").FullFieldName = "LineAmount * Sign * VatType AS LineAmount";
			fieldFor("VatAmount").FullFieldName = "VatAmount * Sign * VatType AS VatAmount";
			addTable("VatCode");
			_fields.Add(new ReportField("Payment.VatPaidDate", "date", "Vat Paid Date"));
			positionField("VatType", 0);
			positionField("Code", _fields.IndexOf(fieldFor("VatRate")));
			_filters.Add(new DateFilter(Settings, "DocumentDate", DateRange.All));
			_filters.Add(new VatPaidFilter("VatPaid", "Vat_Journal.VatPaid", SelectVatPayments()));
			_filters.Add(new RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new SelectFilter("VatType", "VatType", SelectVatTypes()));
			makeSortable("idDocument=Trans no", "DocumentDate", "DocumentIdentifier=Doc Id", "Type,DocumentName=DocumentName", "DocumentAmount", "DocType", "Code");
			setDefaultFields(json, "VatType", "DocType", "DocumentDate", "DocumentIdentifier", "DocumentName", "Memo", "Code", "VatRate", "VatAmount", "LineAmount");
			return finishReport(json, "Vat_Journal", "VatType, DocumentDate", @"JOIN VatCode ON idVatCode = VatCodeId
LEFT JOIN (SELECT idDocument AS idVatPaid, DocumentDate AS VatPaidDate FROM Document) AS Payment ON Payment.idVatPaid = VatPaid", "Vat_Journal");
		}

		/// <summary>
		/// Delete memorised report
		/// </summary>
		public AjaxReturn DeleteReport(int id) {
			Report report = Database.Get<Report>(id);
			Utils.Check(report.ReportGroup == "Memorised Reports", "Report not found");
			Database.Delete(report);
			return new AjaxReturn() { message = "Report deleted" };
		}

		/// <summary>
		/// Add/update current report, with settings, to memorised reports.
		/// </summary>
		/// <param name="json"></param>
		/// <returns></returns>
		public AjaxReturn SaveReport(JObject json) {
			Report report = json.To<Report>();
			report.ReportGroup = "Memorised Reports";
			report.ReportSettings = json.ToString();
			Database.BeginTransaction();
			Database.Update(report);
			Database.Commit();
			return new AjaxReturn() { message = "Report saved", id = report.idReport };
		}

		/// <summary>
		/// Add a table to the report.
		/// If a field list is supplied, exactly those fields are added.
		/// Otherwise the id of the first table is added as an essential field, and all other non foreign key fields are added
		/// If table is preceded by "!", its id is also added as an essential field
		/// For Journal table, Credit &amp; Debit are also added, as an alternative to Amount.
		/// </summary>
		void addTable(string table, params string[] fields) {
			bool essential = _fields.FirstOrDefault(f => f.Essential) == null;
			if (table.StartsWith("!")) {
				table = table.Substring(1);
				essential = false;
			}
			Table t = Database.TableFor(table);
			foreach (Field f in fields.Length == 0 ? t.Fields.Where(f => (essential || f != t.PrimaryKey) && f.ForeignKey == null) : fields.Select(f => t.FieldFor(f))) {
				ReportField r = new ReportField(t.Name, f);
				if (essential) {
					r.Essential = true;
					if(Array.IndexOf(fields, f.Name) < 0)
						r.Hidden = true;
					essential = false;
				}
				_fields.Add(r);
				if (table == "Journal" && f.Name == "Amount") {
					ReportField rf = new ReportField(t.Name, f, "Debit") {
						Name = "Debit",
						FieldType = "debit"
					};
					_fields.Add(rf);
					rf = new ReportField(t.Name, f, "Credit") {
						Name = "Credit",
						FieldType = "credit"
					};
					_fields.Add(rf);
				}
			}
		}

		/// <summary>
		/// Flatten an audit header-detail set into multiple records containing the header and 1 detail
		/// </summary>
		IEnumerable<JObject> auditFlatten(IEnumerable<JObject> data) {
			foreach (JObject record in data) {
				JObject r = JObject.Parse(record.AsString("Record"));
				record.Remove("Record");
				JObject header = (JObject)r["header"];
				if (header == null) {
					record.AddRange(r);
					yield return record;
				} else {
					if(header["DocumentAmount"] == null) header["DocumentAmount"] = header["Amount"];
					if (header["DocumentName"] == null) header["DocumentName"] = header["Name"];
					if (header["DocumentOutstanding"] == null) header["DocumentOutstanding"] = header["Outstanding"];
					foreach (JObject detail in (JArray)r["detail"]) {
						JObject result = new JObject();
						result.AddRange(record, header, detail);
						yield return result;
					}
				}
			}
		}

		/// <summary>
		/// Apply filters to audit records (which can't be done in SQL as record is a json blob)
		/// </summary>
		IEnumerable<JObject> auditFilter(IEnumerable<JObject> data) {
			List<Filter> filters = _filters.Where(f => f.Active).ToList();
			HashSet<string> fields = new HashSet<string>();
			foreach (ReportField f in _fields.Where(f => f.Include || f.Essential))
				fields.Add(f.FieldName);
			foreach (JObject record in data) {
				if (filters.FirstOrDefault(f => !f.Test(record)) == null) {
					foreach (JProperty p in record.Properties().ToList())
						if (!fields.Contains(p.Name))
							record.Remove(p.Name);
					yield return record;
				}
			}
		}

		/// <summary>
		/// Return the audit report data, flattened and filtered
		/// </summary>
		/// <param name="json">Report settings</param>
		/// <param name="type">Report type</param>
		/// <param name="defaultFields">Fields that appear by default, if user has not changed settings</param>
		object auditReportData(JObject json, string type, params string[] defaultFields) {
			defaultFields = (_changeTypeNotRequired ? new string[] { "DateChanged" } : new string[] { "DateChanged", "ChangeType" }).Concat(defaultFields).ToArray();
			setDefaultFields(json, defaultFields);
			setFilters(json);
			string where = _dates.Active ? " AND " + _dates.Where(Database) : "";
			if (json.AsInt("recordId") > 0)
				where += " AND RecordId = " + json.AsInt("recordId");
			JObjectEnumerable report = Database.Query("SELECT ChangeType, DateChanged, Record FROM AuditTrail WHERE TableName = "
				+ Database.Quote(type)
				+ where
				+ " ORDER BY DateChanged, idAuditTrail");
			List<string> tables = new List<string>();
			tables.Add("AuditTrail");
			if (type == "Document") tables.Add("Extended_Document");
			else if (type == "Reconciliation") tables.Add("Account");
			_sortOrder = "idAuditTrail";
			return reportJson(json, auditFilter(auditFlatten(report)), tables.ToArray());
		}

		/// <summary>
		/// Get report settings from database or session (or generate default settings)
		/// </summary>
		JObject getJson(int id, string defaultTitle) {
			string reportType = OriginalMethod.ToLower();
			dynamic json = null;
			if (PostParameters == null || PostParameters["json"] == null && SessionData.Report != null) {
				json = SessionData.Report.reportType;
			}
			if (json == null || json.idReport != id || json.ReportType.ToString().ToLower() != reportType) {
				json = readReport(id, OriginalMethod, defaultTitle);
			}
			return json;
		}

		/// <summary>
		/// Find a field in the field list by name
		/// </summary>
		ReportField fieldFor(string name) {
			try {
				return _fields.First(f => f.Name == name);
			} catch {
				throw new CheckException("Field {0} not in list", name);
			}
		}

		/// <summary>
		/// Actually run the query for an ordinary report, and return the records
		/// </summary>
		IEnumerable<JObject> finishReport(string tableName, string defaultSort, string joins, JObject json) {
			setFilters(json);
			List<string> sort = new List<string>(defaultSort.Split(','));
			if (_sortFields == null || _sortFields.Length == 0) {
				_sortFields = new string[] { sort[0] };
			} else {
				int p = 0;
				foreach (string s in _sortFields) {
					sort.Remove(s);
					sort.Insert(p++, s);
				}
			}
			string[] fields = _fields.Where(f => f.Include || f.Essential || _sortFields.Contains(f.Name)).Select(f => f.FullFieldName).Distinct().ToArray();
			return Database.Query("SELECT " + string.Join(",", fields)
				+ "\r\nFROM " + tableName + "\r\n" + joins
				+ getFilterWhere()
				+ "\r\nORDER BY " + string.Join(",", sort.Select(s => s + (_sortDescending ? " DESC" : "")).ToArray())
				);
		}

		/// <summary>
		/// Run the query for an ordinary report, then package the result into a report display JObject
		/// </summary>
		object finishReport(JObject json, string tableName, string defaultSort, string joins, params string[] tables) {
			return reportJson(json, finishReport(tableName, defaultSort, joins, json), tables);
		}

		/// <summary>
		/// Get the WHERE clause needed to action the filters
		/// </summary>
		string getFilterWhere(params string [] extraWheres) {
			string[] where = _filters.Where(f => f.Active && f.Apply).Select(f => f.Where(Database)).Concat(extraWheres).ToArray();
			if (where.Length == 0)
				return "";
			return "\r\nWHERE " + string.Join("\r\nAND ", where);
		}

		/// <summary>
		/// Return the filters as a JObject, for the javascript to display in edit report dialog
		/// </summary>
		JObject getFilters() {
			JObject result = new JObject();
			foreach (Filter f in _filters) {
				result[f.AsString("data")] = f.Data();
			}
			return result;
		}

		/// <summary>
		/// Set up an audit trail report
		/// </summary>
		/// <param name="json">The posted report parameters</param>
		void initialiseAuditReport(JObject json) {
			initialiseReport(json);
			if (_changeTypeNotRequired) {
				addTable("!AuditTrail", "idAuditTrail", "DateChanged");
			} else {
				addTable("!AuditTrail", "idAuditTrail", "DateChanged", "ChangeType");
				fieldFor("ChangeType")["type"] = "select";
				fieldFor("ChangeType")["selectOptions"] = new JArray(SelectAuditTypes());
			}
			fieldFor("DateChanged")["type"] = "dateTime";
			fieldFor("idAuditTrail").Hide();
			_dates = new DateFilter(Settings, "DateChanged", DateRange.ThisMonth);
			_filters.Add(_dates);
		}

		/// <summary>
		/// Set up any report
		/// </summary>
		/// <param name="json">The posted report parameters</param>
		void initialiseReport(JObject json) {
			string reportType = OriginalMethod.ToLower().Replace("post", "");
			Utils.Check(json.AsString("ReportType").ToLower() == reportType, "Invalid report type");
			dynamic r = SessionData.Report;
			if(r == null)
				SessionData.Report = r = new JObject();
			r.reportType = json;
			_fields = new List<ReportField>();
			_filters = new List<Filter>();
		}

		/// <summary>
		/// Set up _sortOrders available for report
		/// </summary>
		/// <param name="fieldNames">Fields to sort on - multiple fields in a single sort expressed as "Sortname=field,field,..."</param>
		void makeSortable(params string[] fieldNames) {
			_sortOrders = new JArray();
			_sortOrders.Add(new JObject().AddRange("id", "", "value", "Default"));
			foreach (string field in fieldNames) {
				string value = field;
				string id = Utils.NextToken(ref value, "=");
				if (string.IsNullOrWhiteSpace(value)) value = id.UnCamel();
				_sortOrders.Add(new JObject().AddRange("id", id, "value", value));
			}
		}

		/// <summary>
		/// Read a memorised report (or set up a default report record if id doesn't exist)
		/// </summary>
		/// <param name="type">Default report type</param>
		/// <param name="defaultName">Default report name</param>
		JObject readReport(int id, string type, string defaultName) {
			Report report = Database.Get<Report>(id);
			JObject json;
			if (report.idReport == null) {
				report.ReportType = type;
				report.ReportName = defaultName;
				report.ReportSettings = "{}";
			}
			if (PostParameters != null) {
				JToken j = PostParameters["json"];
				if (j != null)
					report.ReportSettings = j.ToString();
			}
			json = JObject.Parse(report.ReportSettings);
			json["ReportName"] = report.ReportName;
			json["ReportType"] = report.ReportType;
			json["idReport"] = report.idReport;
			Utils.Check(report.ReportType == type, "Invalid report type");
			return json;
		}

		/// <summary>
		/// Add Retained Earnings account to list of accounts (if it is not already there)
		/// </summary>
		/// <param name="data">List of accounts</param>
		/// <returns>Modified list including retained earnings account</returns>
		List<JObject> addRetainedEarnings(IEnumerable<JObject> data) {
			List<JObject> result = data.ToList();
			if (result.FirstOrDefault(r => r.AsInt("idAccount") == (int)Acct.RetainedEarnings) == null) {
				result.Add(new JObject().AddRange(
						"idAccount", (int)Acct.RetainedEarnings,
						"Heading", "Equities",
						"BalanceSheet", 1,
						"AccountTypeId", (int)AcctType.Equity,
						"AcctType", "Equity",
						"AccountName", "Retained Earnings",
						"Negate", 1,
						"CurrentPeriod", 0M,
						"PreviousPeriod", 0M,
						"Old", 0M
				));
			}
			return result;
		}

		/// <summary>
		/// Create a new Journal record for an investment gain
		/// </summary>
		JObject newJournal(JObject jnl, decimal gain, bool loss, string securityName, DateTime [] period) {
			jnl["idJournal"] = 0;
			jnl["DocumentDate"] = period[1].AddDays(-1);
			jnl["DocumentTypeId"] = jnl["idDocumentType"] = (int)DocType.Gain;
			jnl["DocType"] = "Gain";
			jnl["Memo"] = (loss ? "Loss" : "Gain") + " on " + securityName + " for period " + period[0].ToShortDateString() + " to " + period[1].AddDays(-1).ToShortDateString();
			jnl["NameAddressId"] = jnl["idNameAddress"] = 1;
			jnl["Name"] = "";
			jnl["Amount"] = gain;
			return jnl;
		}

		/// <summary>
		/// Add journals for investment gains to data
		/// </summary>
		/// <param name="period">Start and end of period over which to calculate gains</param>
		/// <param name="account">Accounts to include</param>
		/// <param name="data">Exdisting list of journals</param>
		/// <returns>List with gain journals added</returns>
		List<JObject> addInvestmentGains(DateTime [] period, RecordFilter account, List<JObject> data) {
			// Current values of stock for each account id
			Dictionary<int, decimal> stockValues = new Dictionary<int, decimal>();
			// Get values for each stock at start of period
			foreach (Investments.SecurityValue securityValue in Database.Query<Investments.SecurityValue>(Investments.SecurityValues(Database, period[0].AddDays(-1)))) {
				JObject jnl = data.FirstOrDefault(a => a.AsInt("idAccount") == securityValue.ParentAccountId && a.AsInt("DocumentTypeId") == (int)DocType.OpeningBalance);
				if (jnl == null)
					continue;		// No opening balance, so not wanted
				decimal gain = securityValue.Value;
				stockValues[(int)securityValue.AccountId] = securityValue.Value;	// Save value at start of period
				jnl["Amount"] = jnl.AsDecimal("Amount") + gain;		// Add to the opening balance
			}
			// Now get values at end of period
			foreach (Investments.SecurityValueWithName securityValue in Database.Query<Investments.SecurityValueWithName>(
				"SELECT * FROM (" + Investments.SecurityValues(Database, period[1].AddDays(-1)) + @") AS SV
JOIN Security ON idSecurity = SecurityId")) {
				decimal gain = 0;
				int ind;
				stockValues.TryGetValue((int)securityValue.AccountId, out gain);
				gain = securityValue.Value - gain;	// Gain is difference between start and end of period values
				JObject jnl = data.LastOrDefault(a => a.AsInt("idAccount") == securityValue.ParentAccountId);
				if (jnl != null && gain != 0) {
					// Add extra journal for parent account
					ind = data.IndexOf(jnl);
					jnl = newJournal(new JObject(jnl), gain, gain < 0, securityValue.SecurityName, period);
					data.Insert(ind + 1, jnl);
				}
				jnl = data.LastOrDefault(a => a.AsInt("idAccount") == securityValue.AccountId && a.AsInt("DocumentTypeId") == (int)DocType.Gain);
				if (jnl != null) {
					if (gain == 0) {
						data.Remove(jnl);
					} else {
						jnl["AccountId"] = securityValue.AccountId;
						if (account.Active && !account.Test(jnl))
							continue;
						// Add extra journal for gain
						newJournal(jnl, -gain, gain < 0, securityValue.SecurityName, period);
					}
				}
			}
			return data;
		}

		/// <summary>
		/// Add values for investment gains in each of the supplied periods
		/// </summary>
		/// <param name="data">Records for each account that may have gains</param>
		/// <param name="p">List of field names (to add the gains to) and dates (at which the values are calculated), alternating</param>
		/// <returns>The modified data</returns>
		List<JObject> addInvestmentGains(List<JObject> data, params object[] p) {
			string priorPeriod = null;
			// To hold current value for each account
			Dictionary<int, decimal> stockValues = new Dictionary<int, decimal>();
			// Process each period in turn
			for (int i = 0; i < p.Length; i += 2) {
				string field = (string)p[i];		// First item is name of field to accumulate gains into
				DateTime date = (DateTime)p[i + 1];	// Second item is date at which to calculate value
				// Get the value of all securities on the date (actually, the day before)
				foreach (Investments.SecurityValue securityValue in Database.Query<Investments.SecurityValue>(Investments.SecurityValues(Database, date.AddDays(-1)))) {
					// Is the account in our data
					JObject securityAcct = data.FirstOrDefault(a => a.AsInt("idAccount") == securityValue.AccountId);
					if (securityAcct == null)
						continue;	// No - ignore
					decimal gain = securityValue.Value;		// Value as at date
					decimal priorPeriodValue;				// Value at at previous period
					if (priorPeriod != null && stockValues.TryGetValue((int)securityValue.AccountId, out priorPeriodValue))
						gain -= priorPeriodValue;			// Gain is the increase in value
					// "Add" the gain to the required field (It's a P & L account, so actually need to subtract)
					securityAcct[field] = securityAcct.AsDecimal(field) - gain;
					// Store the current value, for use when processing next period
					stockValues[(int)securityValue.AccountId] = securityValue.Value;
					// Roll values up to parent account, if required (it will be a Balance sheet account, so add this time)
					JObject parentAcct = data.FirstOrDefault(a => a.AsInt("idAccount") == securityValue.ParentAccountId);
					if (parentAcct != null) {
						parentAcct[field] = parentAcct.AsDecimal(field) + gain;
						Log(parentAcct.ToString());
					}
				}
				priorPeriod = field;
			}
			return data;
		}

		// TODO: Should use common totalling code
		IEnumerable<JObject> fixBalanceSheet(IEnumerable<JObject> data) {
			_total = false;
			JObject last = null;
			string lastTotalBreak = null;
			string lastHeading = null;
			int sign = 1;
			decimal retainedProfitCP = 0, retainedProfitPP = 0, retainedProfitOld = 0;
			Dictionary<string, decimal[]> totals = new Dictionary<string, decimal[]>();
			JObject spacer = new JObject().AddRange("@class", "totalSpacer");
			// Make list of fields to total
			foreach (ReportField f in _fields) {
				if (!f.Include) continue;
				string type = f.AsString("type");
				if (type != "decimal" && type != "double" && type != "credit" && type != "debit") continue;
				totals[f.Name] = new decimal[3];
			}
			// Function to add a total record (index is total level - 0=account type change, 1=heading change, 2=total assets/total liabilities & equity)
			Func<int, JObject> totalRecord = delegate(int index) {
				JObject t = new JObject().AddRange("@class", "total total" + index);
				foreach (string f in totals.Keys.ToList()) {
					t[f] = sign * totals[f][index];
					totals[f][index] = 0;
				}
				if (index == 0) {
					t["Heading"] = lastHeading;
					t[_sortOrder] = "Total " + lastTotalBreak;
				} else {
					t["Heading"] = "Total " + lastHeading;
				}
				return t;
			};
			if(data.FirstOrDefault(r => r.AsInt("idAccount") == (int)Acct.RetainedEarnings) == null) {
				// Need a retained earnings account line, so create one if missing
				data = data.Concat(Enumerable.Repeat(new JObject().AddRange(
						"idAccount", (int)Acct.RetainedEarnings,
						"Heading", "Equities",
						"BalanceSheet", 1,
						"AccountTypeId", (int)AcctType.Equity,
						"AcctType", "Equity",
						"AccountName", "Retained Earnings",
						"Negate", 1,
						"CurrentPeriod", 0M,
						"PreviousPeriod", 0M,
						"Old", 0M
				), 1));
			}
			foreach (JObject r in data) {
				JObject record = new JObject(r);
				string totalBreak = record.AsString(_sortOrder);
				string heading = record.AsString("Heading");
				if (record.AsInt("BalanceSheet") == 0) {
					// Accumulate profit and loss postings
					retainedProfitCP += record.AsDecimal("CurrentPeriod");
					retainedProfitPP += record.AsDecimal("PreviousPeriod");
					retainedProfitOld += record.AsDecimal("Old");
					continue;
				} else {
					if (r.AsInt("idAccount") == (int)Acct.RetainedEarnings) {
						// Add accumulated profit into retained earnings
						record["PreviousPeriod"] = record.AsDecimal("PreviousPeriod") + retainedProfitOld;
						record["CurrentPeriod"] = record.AsDecimal("CurrentPeriod") + retainedProfitPP;
						record.Remove("idAccount");		// So user can't click on it to expand
					}
					// Balance sheet shows totals so far, so add in previous periods
					record["PreviousPeriod"] = record.AsDecimal("PreviousPeriod") + record.AsDecimal("Old");
					record["CurrentPeriod"] = record.AsDecimal("CurrentPeriod") + record.AsDecimal("PreviousPeriod");
					record.Remove("Old");
				}
				if (totalBreak != lastTotalBreak) {
					if (last != null) {
						if (lastTotalBreak != null) {
							// Add total and spacer for account type change
							spacer["Heading"] = lastHeading;
							yield return totalRecord(0);
							yield return spacer;
							if (lastHeading != heading) {
								// Add total and spacer for heading change
								lastTotalBreak = lastHeading;
								yield return totalRecord(1);
								spacer.Remove("Heading");
								yield return spacer;
								if (lastHeading.Contains("Assets") && !heading.Contains("Assets")) {
									// Add total and spacer for total assets
									lastHeading = "Assets";
									sign = 1;
									yield return totalRecord(2);
									yield return spacer;
								}
							}
						}
					}
					if (lastHeading != heading)	// Next heading if required
						yield return new JObject().AddRange("@class", "title", "Heading", heading);
					lastTotalBreak = totalBreak;
					lastHeading = heading;		// Account type heading
					yield return new JObject().AddRange("@class", "title", "Heading", heading, "AcctType", totalBreak);
				}
				sign = record.AsBool("Negate") ? -1 : 1;
				// Accumulate totals
				foreach (string f in totals.Keys.ToList()) {
					decimal v = record.AsDecimal(f);
					decimal[] tots = totals[f];
					for (int i = 0; i < tots.Length; i++) {
						tots[i] += v;
					}
					record[f] = sign * v;
				}
				last = r;
				// The record itself (now all totals and headings taken care of)
				yield return record;
				if (r.AsInt("idAccount") == (int)Acct.RetainedEarnings) {
					// Generate Net Income posting
					record = new JObject(record);
					record.Remove("idAccount");
					record["AccountName"] = "Net Income";
					record["AccountDescription"] = "";
					record["CurrentPeriod"] = -retainedProfitCP;
					record["PreviousPeriod"] = -retainedProfitPP;
					foreach (string f in totals.Keys.ToList()) {
						decimal v = record.AsDecimal(f) * sign;
						decimal[] tots = totals[f];
						for (int i = 0; i < tots.Length; i++) {
							tots[i] += v;
						}
					}
					yield return record;
				}
			}
			if (last != null) {
				// Liabilites and equity total
				yield return totalRecord(0);
				yield return spacer;
				lastHeading = "Liabilities & Equity";
				sign = -1;
				yield return totalRecord(2);
				yield return spacer;
			}
		}

		// TODO: Should be more like fixBalanceSheet, and use common totalling code
		IEnumerable<JObject> fixProfitAndLoss(IEnumerable<JObject> data) {
			_total = false;
			JObject last = null;
			string lastTotalBreak = null;
			string lastHeading = null;
			int sign = 1;
			Dictionary<string, decimal[]> totals = new Dictionary<string, decimal[]>();
			JObject spacer = new JObject().AddRange("@class", "totalSpacer");
			// Make list of fields to total
			foreach (ReportField f in _fields) {
				if (!f.Include) continue;
				string type = f.AsString("type");
				if (type != "decimal" && type != "double" && type != "credit" && type != "debit") continue;
				totals[f.Name] = new decimal[3];
			}
			// Function to add a total record (index is total level - 0=account type change, 1=Gross Profit, 2=Net Profit)
			Func<int, JObject> totalRecord = delegate(int index) {
				JObject t = new JObject().AddRange("@class", "total total" + index);
				foreach (string f in totals.Keys.ToList()) {
					t[f] = sign * totals[f][index];
					totals[f][index] = 0;
				}
				t["Heading"] = lastHeading;
				t[_sortOrder] = index == 0 ? "Total " + lastTotalBreak : lastTotalBreak;
				return t;
			};
			foreach (JObject r in data) {
				JObject record = new JObject(r);
				string totalBreak = record.AsString(_sortOrder);
				string heading = record.AsString("Heading");
				if (totalBreak != lastTotalBreak) {
					if (last != null) {
						if (lastTotalBreak != null) {
							// Total and spacer for account type change
							yield return totalRecord(0);
							yield return spacer;
							if (lastHeading != heading) {
								// Total and spacer for gross profit
								lastTotalBreak = lastHeading;
								sign = -1;
								yield return totalRecord(1);
								yield return spacer;
							}
						}
					}
					lastTotalBreak = totalBreak;
					lastHeading = heading;
					// New account type heading
					yield return new JObject().AddRange("@class", "title", "Heading", heading, "AcctType", totalBreak);
				}
				sign = record.AsBool("Negate") ? -1 : 1;
				// Accumulate totals
				foreach (string f in totals.Keys.ToList()) {
					decimal v = record.AsDecimal(f);
					decimal[] tots = totals[f];
					for (int i = 0; i < tots.Length; i++) {
						tots[i] += v;
					}
					record[f] = sign * v;
				}
				last = r;
				yield return record;
			}
			if (last != null) {
				// Total and spacer for last account type change
				yield return totalRecord(0);
				yield return spacer;
				// Total and spacer for gross profit
				lastTotalBreak = lastHeading;
				sign = -1;
				yield return totalRecord(2);
			}
		}

		IEnumerable<JObject> fixTrialBalance(IEnumerable<JObject> data) {
			_total = false;
			JObject last = null;
			decimal retainedProfitOld = 0;
			Dictionary<string, decimal> totals = new Dictionary<string, decimal>();
			// Make list of fields to total
			foreach (ReportField f in _fields) {
				if (!f.Include) continue;
				string type = f.AsString("type");
				if (type != "decimal" && type != "double" && type != "credit" && type != "debit") continue;
				totals[f.Name] = 0;
			}
			// Function to add a total record
			Func<JObject> totalRecord = delegate() {
				JObject t = new JObject().AddRange("@class", "total");
				foreach (string f in totals.Keys.ToList()) {
					t[f] = totals[f];
				}
				t["AccountName"] = "Total";
				return t;
			};
			if (data.FirstOrDefault(r => r.AsInt("idAccount") == (int)Acct.RetainedEarnings) == null) {
				// Must have retained earnings account, so add if missing
				data = data.Concat(Enumerable.Repeat(new JObject().AddRange(
						"idAccount", (int)Acct.RetainedEarnings,
						"Heading", "Equities",
						"BalanceSheet", 1,
						"AccountTypeId", (int)AcctType.Equity,
						"AcctType", "Equity",
						"AccountName", "Retained Earnings",
						"Negate", 1,
						"CurrentPeriod", 0M,
						"PreviousPeriod", 0M,
						"Old", 0M
				), 1));
			}
			foreach (JObject r in data) {
				JObject record = new JObject(r);
				if (record.AsInt("BalanceSheet") == 0) {
					// Accumulate all P & L before previous period into retained profit
					retainedProfitOld += record.AsDecimal("Old");
				} else {
					// For balance sheet, add values before previous period into current value
					record["Amount"] = record.AsDecimal("Amount") + record.AsDecimal("Old");
					if (r.AsInt("idAccount") == (int)Acct.RetainedEarnings) {
						record["Amount"] = record.AsDecimal("Amount") + retainedProfitOld;
						record.Remove("idAccount");	// So user can't click on it to expand
					}
					record.Remove("Old");
				}
				decimal v = record.AsDecimal("Amount");
				if (v == 0)
					continue;
				// Accumulate totals
				foreach (string f in totals.Keys.ToList()) {
					decimal v1 = v;
					if (f == "Credit") {
						v1 = v1 < 0 ? -v1 : 0;
					} else if (f == "Debit") {
						if (v1 < 0) v1 = 0;
					} else {
						record[f] = v;
					}
					totals[f] += v1;
				}
				last = r;
				yield return record;
			}
			if (last != null) {
				yield return totalRecord();
			}
		}

		/// <summary>
		/// Move a field to a given position in the field list (e.g. to move sort fields to front)
		/// </summary>
		void positionField(string name, int position) {
			ReportField f = fieldFor(name);
			int p = _fields.IndexOf(f);
			_fields.RemoveAt(p);
			if (p < position)
				position--;
			_fields.Insert(position, f);
		}

		/// <summary>
		/// Remove data from certain tables which repeats in a series of records, and add totals as required
		/// </summary>
		/// <param name="tables">List of tables with potentially repeating data</param>
		/// <returns>Modified list</returns>
		IEnumerable<JObject> removeRepeatsAndTotal(IEnumerable<JObject> data, params string[] tables) {
			JObject last = null;
			JObject spacer = new JObject().AddRange("@class", "totalSpacer");
			// All fields in the repeating tables
			HashSet<string> flds = new HashSet<string>(tables.SelectMany(t => Database.TableFor(t).Fields.Select(f => f.Name)));
			// Our fields in the repeating tables
			HashSet<string> fields = new HashSet<string>();
			List<string> essentialFields = _fields.Where(f => f.Essential).Select(f => f.Name).ToList();
			foreach (string f in _fields.Where(f => tables.Contains(f.Table)).Select(f => f.Name))
				fields.Add(f);	// One of our fields in a potentially repeating table (need to be at front)
			foreach (string f in flds.Where(f => !fields.Contains(f)))
				fields.Add(f);	// Rest of potentially repeating fields
			string[] sortFields = _sortFields == null ? new string[0] : _sortFields.Where(f => fieldFor(f).Include).ToArray();
			string[] lastTotalBreak = new string[sortFields.Length + 1];
			Dictionary<string, decimal[]> totals = new Dictionary<string, decimal[]>();
			string firstStringField = null;
			if (_total) {
				// Build list of totalling fields
				foreach (ReportField f in _fields) {
					if (!f.Include) continue;
					string type = f.AsString("type");
					if (firstStringField == null && type == "string" && !sortFields.Contains(f.Name))
						firstStringField = f.Name;
					if (f.Name == "VatRate") continue;
					if (type != "decimal" && type != "double" && type != "credit" && type != "debit") continue;
					totals[f.Name] = new decimal [sortFields.Length + 1];
				}
			}
			// Function to generate total record - index is sort field number (sortFields.length for grand total)
			Func<int, JObject> totalRecord = delegate(int level) {
				JObject t = new JObject().AddRange("@class", "total");
				foreach (string f in totals.Keys.ToList()) {
					t[f] = totals[f][level];
					totals[f][level] = 0;
				}
				if (firstStringField != null)
					t[firstStringField] = level == sortFields.Length ? "Grand Total" : "Total";
				if(level < sortFields.Length)
					t[sortFields[level]] = lastTotalBreak[level];
				lastTotalBreak[level] = null;
				return t;
			};
			foreach (JObject r in data) {
				JObject record = new JObject(r);
				JObject id = null;
				if (essentialFields.Count > 0) {
					// Make recordId object with essential fields (for user click and expand)
					id = new JObject();
					foreach (string f in essentialFields) {
						id[f] = record[f];
						if (!fields.Contains(f))
							record.Remove(f);		// Don't want essential fields to interfere with checking for duplicates
					}
				}
				if (last != null) {
					if (_total) {
						for (int level = sortFields.Length; level-- > 0; ) {
							if (record.AsString(sortFields[level]) != lastTotalBreak[level]) {
								if (lastTotalBreak[level] != null) {
									// Output totals for this sort field
									yield return totalRecord(level);
									yield return spacer;
								}
							}
						}
					}
					// Now remove duplicate fields
					foreach (string f in fields) {
						if (last.AsString(f) == record.AsString(f))
							record.Remove(f);
						else
							break;
					}
					if (record.IsAllNull())
						continue;		// Everything repeats - ignore duplicate record
				}
				if(id != null)
					record["recordId"] = id;
				if (_total) {
					// Cache total break values
					for(int level = 0; level < sortFields.Length; level++)
						lastTotalBreak[level] = r.AsString(sortFields[level]);
					// Accumulate totals
					foreach (string f in totals.Keys.ToList()) {
						decimal v = record.AsDecimal(f);
						if (f == "Credit") {
							v = r.AsDecimal("Amount");
							v = v < 0 ? -v : 0;
						} else if (f == "Debit") {
							v = r.AsDecimal("Amount");
							if (v < 0) v = 0;
						}
						for(int level = 0; level <= sortFields.Length; level++)
							totals[f][level] += v;
					}
				}
				last = r;
				yield return record;
			}
			if (_total && last != null) {
				// Print any pending sort field totals
				for (int level = sortFields.Length; level-- > 0; ) {
					if (lastTotalBreak[level] != null) {
						yield return totalRecord(level);
						yield return spacer;
					}
				}
				if(_grandTotal)
					yield return totalRecord(sortFields.Length);
			}
		}

		/// <summary>
		/// Produce report output from original request, list of records, and list of potentially duplicate tables 
		/// (i.e. parent tables of main record, which you want to look like headings)
		/// </summary>
		/// <param name="json">Original request</param>
		/// <param name="report">Records</param>
		/// <param name="tables">Potentially duplicate tables</param>
		/// <returns>Json to send to javascript</returns>
		public JObject reportJson(JObject json, IEnumerable<JObject> report, params string [] tables) {
			// Use ReportName as right hand end of page title
			Title = Regex.Replace(Title, "-[^-]*$", "- " + json.AsString("ReportName"));
			if (_sortFields != null && _sortFields.Length > 0 && tables.Length > 0) {
				// SortField table is always potentially duplicated - i.e. all the data from the table for the first sort field
				// will repeat until sort field changes
				ReportField sortField = fieldFor(_sortFields[0]);
				tables[0] = sortField.Table;
				// Move sort fields to front of field list
				int p = 0;
				foreach(string f in _sortFields)
					positionField(f, p++);
				if (_split) {
					// Split report shows sort field data on 1 line, and main data on next line
					ReportField fld = _fields.FirstOrDefault(f => f.Include && !tables.Contains(f.Table));
					if(fld != null)
						fld["newRow"] = true;
				}
			}
			json["fields"] = _fields.Where(f => !f.Hidden).ToJToken();
			json["filters"] = getFilters().ToJToken();
			json["sorting"] = new JObject().AddRange(
						"sort", _sortOrder,
						"desc", _sortDescending,
						"total", _total,
						"split", _split);
			return new JObject().AddRange(
				"settings", json,
				"filters", new JArray(_filters),
				"sortOrders", _sortOrders,
				"report", removeRepeatsAndTotal(report, tables)
				);
		}

		/// <summary>
		/// Set the default fields to include if a new report, or copy include flags from posted/read json settings if an existing one
		/// </summary>
		void setDefaultFields(JObject settings, params string [] fields) {
			if (settings == null || settings["fields"] == null) {
				foreach (string field in fields)
					fieldFor(field).Include = true;
			} else {
				foreach (JObject f in (JArray)settings["fields"]) {
					string name = f.AsString("Name");
					ReportField fld = _fields.FirstOrDefault(x => x.Name == name);
					if(fld != null)
						fld.Include = f.AsInt("Include") != 0;
				}
			}
		}

		/// <summary>
		/// Read the filter settings from the posted json
		/// </summary>
		void setFilters(JObject json) {
			if (json != null && json["filters"] != null) {
				JObject fdata = (JObject)json["filters"];
				foreach (Filter f in _filters) {
					JToken data = fdata[f.AsString("data")];
					if (data == null)
						continue;
					f.Parse(data);
				}
				JObject sdata = (JObject)json["sorting"];
				if (sdata != null) {
					string s = sdata.AsString("sort");
					if (!string.IsNullOrWhiteSpace(s))
						_sortOrder = s;
					_sortDescending = sdata.AsBool("desc");
					_total = sdata.AsBool("total");
					_split = sdata.AsBool("split");
				}
			}
			if (!string.IsNullOrWhiteSpace(_sortOrder))
				_sortFields = _sortOrder.Split(',');
		}

		/// <summary>
		/// Class represents a field which may be included in a report
		/// </summary>
		public class ReportField : JObject {

			public ReportField(string table, Field f)
				: this(table, f, f.Name.UnCamel()) {
			}

			public ReportField(string table, Field f, string heading) {
				Table = table;
				this["data"] = f.Name;
				FullFieldName = Table + "." + f.Name;
				this["heading"] = heading;
				Name = f.Name;
				switch (f.Type.Name) {
					case "Int32":
						this["type"] = "int";
						break;
					case "Decimal":
						this["type"] = "decimal";
						break;
					case "Double":
						this["type"] = "double";
						break;
					case "Boolean":
						this["type"] = "checkbox";
						break;
					case "DateTime":
						this["type"] = "date";
						break;
					case "String":
						this["type"] = "string";
						break;
					default:
						throw new CheckException("Unexpected field type {0}", f.Type.Name);
				}
			}

			public ReportField(string fullName, string type, string heading) {
				FullFieldName = fullName;
				Name = FieldName;
				this["data"] = Name;
				this["heading"] = heading;
				this["type"] = type;
			}

			/// <summary>
			/// Always include the field in the data, even if the user does not select it
			/// </summary>
			public bool Essential;

			/// <summary>
			/// Table.Field
			/// </summary>
			public string FullFieldName;

			/// <summary>
			/// Just field name, without table
			/// </summary>
			public string FieldName {
				get {
					string[] parts = FullFieldName.Split('.', ' ');
					return parts[parts.Length - 1];
				}
			}

			/// <summary>
			/// Include the field in the report
			/// </summary>
			public bool Include {
				get { return this.AsInt("Include") != 0; }
				set { this["Include"] = value ? 1 : 0; }
			}

			/// <summary>
			/// Set Essential flag (for chaining)
			/// </summary>
			public ReportField MakeEssential() {
				Essential = true;
				return this;
			}

			/// <summary>
			/// Set Hidden flag (for chaining)
			/// </summary>
			public ReportField Hide() {
				Hidden = true;
				return this;
			}

			public string Name {
				get { return this.AsString("Name"); }
				set { this["Name"] = value; }
			}

			public string FieldType {
				get { return this.AsString("type"); }
				set { this["type"] = value; }
			}

			public bool Sortable {
				get { return this.AsInt("Sortable") != 0; }
				set { this["Sortable"] = value ? 1 : 0; }
			}

			public string Table;

			/// <summary>
			/// Don't include on field list in UI
			/// </summary>
			public bool Hidden;

			public override string ToString() {
				return FullFieldName + "/" + base.ToString();
			}

		}

		/// <summary>
		/// Class to filter data so only some items appear in the report.
		/// Underlying JObject is used in javascript as field object to show filter selection field.
		/// </summary>
		public abstract class Filter : JObject {
			string _fieldName;

			public Filter(string name) {
				Name = name;
				FieldName = name;
				this.AddRange(
					"data", name,
					"heading", name.UnCamel()
					);
			}

			public bool Active { get; protected set; }

			/// <summary>
			/// Apply the filter automatically when generating SQL.
			/// Set to false for filters used internally.
			/// </summary>
			public bool Apply = true;

			/// <summary>
			/// Full field name for use in WHERE statements
			/// </summary>
			protected string FieldName {
				get { return _fieldName; }
				set {
					_fieldName = value;
					string[] parts = value.Split('.', ' ');
					JObjectFieldName = parts[parts.Length - 1];
				}
			}

			/// <summary>
			/// Field name in ourpur JObject data
			/// </summary>
			protected string JObjectFieldName;

			/// <summary>
			/// Currently selected filter value, as a JObject.
			/// </summary>
			public abstract JToken Data();

			public string Name { get; private set; }

			/// <summary>
			/// Parse the current value from a JToken
			/// </summary>
			public abstract void Parse(JToken json);

			/// <summary>
			/// WHERE clause to match field to current value
			/// </summary>
			public abstract string Where(Database db);

			/// <summary>
			/// Test a JObject data to see if it satisfied the filter
			/// </summary>
			public abstract bool Test(JObject data);
		}

		public class BooleanFilter : Filter {
			int _value;

			public BooleanFilter(string name, string fieldName)
				: this(name, fieldName, null) {
			}

			/// <summary>
			/// Constructor
			/// </summary>
			/// <param name="name">Filter name</param>
			/// <param name="fieldName">Field being filtered</param>
			/// <param name="value">Default value (null = no filter)</param>
			public BooleanFilter(string name, string fieldName, bool? value)
				: base(name) {
				FieldName = fieldName;
				// Set up field object
				this.AddRange("type", "selectFilter", "selectOptions", new JObject[] {
					new JObject().AddRange("id", -1, "value", "No filter"),
					new JObject().AddRange("id", 0, "value", "No"),
					new JObject().AddRange("id", 1, "value", "Yes")
				});
				switch (value) {
					case null: _value = -1; break;
					case false: _value = 0; break;
					case true: _value = 1; break;
				}
				Active = _value >= 0;
			}

			public override JToken Data() {
				return _value.ToJToken();
			}

			public override void Parse(JToken json) {
				_value = json.To<int>();
				Active = _value >= 0;
			}

			public override string Where(Database db) {
				return FieldName + " = " + _value;
			}

			public override bool Test(JObject data) {
				int value = data.AsInt(JObjectFieldName);
				return _value == value;
			}
		}

		public class DateFilter : Filter {
			Settings _settings;
			DateRange _range;
			DateTime _start;	// inclusive
			DateTime _end;		// exclusive

			public DateFilter(Settings settings, string name, DateRange range)
				: base(name) {
					_settings = settings;
				Utils.Check(range < DateRange.Custom, "Invalid default date range");
				_range = range;
				Active = _range != DateRange.All;
				setDates();
				this["type"] = "dateFilter";
			}

			/// <summary>
			/// Return 2 DateTimes, representing the start and end of the filter
			/// </summary>
			public DateTime[] CurrentPeriod() {
				return new DateTime[] { _start, _end };
			}

			/// <summary>
			/// Return human-readable name for the filter selection
			/// </summary>
			public string PeriodName(DateTime[] period) {
				switch (_range) {
					case DateRange.All:
						return "All Dates";
					case DateRange.Today:
					case DateRange.Yesterday:
						return "Day " + period[0].ToString("d");
					case DateRange.ThisWeek:
					case DateRange.LastWeek:
						return "Week Commencing " + period[0].ToString("d");
					case DateRange.ThisMonth:
					case DateRange.LastMonth:
						return period[0].ToString("y");
					case DateRange.ThisQuarter:
					case DateRange.LastQuarter:
						return "Quarter Ending " + period[1].AddDays(-1).ToString("d");
					case DateRange.ThisYear:
					case DateRange.LastYear:
						return "Year Ending " + period[1].AddDays(-1).ToString("d");
					case DateRange.NDays:
						return (period[1] - period[0]).TotalDays + " days up to " + period[1].ToString("d");
					case DateRange.NMonths:
						return (period[1].Month - period[0].Month + 12 * (period[1].Year - period[0].Year)) + " months from " + period[0].ToString("d") + " - " + period[1].ToString("d");
					default:
						return period[0].ToString("d") + " - " + period[1].AddDays(-1).ToString("d");
				}
			}

			public override JToken Data() {
				JObject r = new JObject().AddRange(
					"range", _range,
					"start", _start,
					"end", _end);
				switch (_range) {
					case DateRange.NDays:
						r["count"] = (_end - _start).TotalDays;
						break;
					case DateRange.NMonths:
						r["count"] = _end.Month - _start.Month + 12 * (_end.Year - _start.Year);
						break;
				}
				return r;
			}

			public override void Parse(JToken json) {
				_range = (DateRange)(json as JObject).AsInt("range");
				switch (_range) {
					case DateRange.Custom:
						_start = json["start"].To<DateTime>();
						_end = json["end"].To<DateTime>();
						break;
					case DateRange.NDays:
						_end = DateTime.Today;
						_start = _end.AddDays(-(json as JObject).AsInt("count"));
						break;
					case DateRange.NMonths:
						_end = DateTime.Today;
						_start = _end.AddMonths(-(json as JObject).AsInt("count"));
						break;
					default:
						setDates();
						break;
				}
				Active = _range != DateRange.All;
			}

			/// <summary>
			/// Return 2 dates representing the date period before the selected one
			/// </summary>
			public DateTime[] PreviousPeriod() {
				DateTime [] result = new DateTime[2];
				result[1] = _start;
				switch (_range) {
					case DateRange.All:
						result[0] = _start;
						break;
					case DateRange.Today:
					case DateRange.Yesterday:
						result[0] = _start.AddDays(-1);
						break;
					case DateRange.ThisWeek:
					case DateRange.LastWeek:
						result[0] = _start.AddDays(-7);
						break;
					case DateRange.ThisMonth:
					case DateRange.LastMonth:
						result[0] = _start.AddMonths(-1);
						break;
					case DateRange.ThisQuarter:
					case DateRange.LastQuarter:
						result[0] = _start.AddMonths(-3);
						break;
					case DateRange.ThisYear:
					case DateRange.LastYear:
						result[0] = _settings.YearStart(_start.AddDays(-1));
						break;
					case DateRange.NDays:
						result[0] = result[1].AddDays((_end - _start).TotalDays);
						break;
					case DateRange.NMonths:
						result[0] = result[1].AddMonths(-(_end.Month - _start.Month + 12 * (_end.Year - _start.Year)));
						break;
					default:
						result[0] = _start.AddYears(-1);
						break;
				}
				return result;
			}

			/// <summary>
			/// Set the start and end dates for a selected range
			/// </summary>
			void setDates() {
				switch (_range) {
					case DateRange.All:
						_start = DateTime.Parse("1900-01-01");
						_end = DateTime.Parse("2100-01-01");
						break;
					case DateRange.Today:
						_start = Utils.Today;
						_end = _start.AddDays(1);
						break;
					case DateRange.ThisWeek:
						_start = Utils.Today;
						_start = _start.AddDays(-(int)_start.DayOfWeek);
						_end = _start.AddDays(7);
						break;
					case DateRange.ThisMonth:
						_start = Utils.Today;
						_start = _start.AddDays(1 - (int)_start.Day);
						_end = _start.AddMonths(1);
						break;
					case DateRange.ThisQuarter:
						_start = _settings.QuarterStart(Utils.Today);
						_end = _start.AddMonths(3);
						break;
					case DateRange.ThisYear:
						_start = _settings.YearStart(Utils.Today);
						_end = _settings.YearEnd(_start).AddDays(1);
						break;
					case DateRange.Yesterday:
						_end = Utils.Today;
						_start = _start.AddDays(-1);
						break;
					case DateRange.LastWeek:
						_end = Utils.Today;
						_end = _end.AddDays(-(int)_end.DayOfWeek);
						_start = _end.AddDays(-7);
						break;
					case DateRange.LastMonth:
						_end = Utils.Today;
						_end = _end.AddDays(1 - (int)_end.Day);
						_start = _end.AddMonths(-1);
						break;
					case DateRange.LastQuarter:
						_start = _settings.QuarterStart(Utils.Today.AddMonths(-3));
						_end = _start.AddMonths(3);
						break;
					case DateRange.LastYear:
						_end = _settings.YearStart(Utils.Today);
						_start = _settings.YearStart(_end.AddDays(-1));
						break;
					case DateRange.NDays:
						_end = Utils.Today;
						_start = _end.AddDays(-7);
						break;
					case DateRange.NMonths:
						_end = Utils.Today;
						_start = _end.AddMonths(-1);
						break;
				}
			}

			public override string Where(Database db) {
				return FieldName + " >= " + db.Quote(_start) + " AND " + Name + " < " + db.Quote(_end);
			}

			public override bool Test(JObject data) {
				DateTime date = data.AsDate(JObjectFieldName);
				return date >= _start && date < _end;
			}

		}

		/// <summary>
		/// Select 1 or more of a list of record numbers
		/// </summary>
		public class RecordFilter : Filter {
			protected List<int> _ids;

			/// <summary>
			/// Constructor
			/// </summary>
			/// <param name="name">Filter name</param>
			/// <param name="fieldName">Field selecting on</param>
			/// <param name="options">SelectOptions for the records</param>
			public RecordFilter(string name, string fieldName, IEnumerable<JObject> options)
				: base(name) {
				FieldName = fieldName;
				this.AddRange("type", "multiSelectFilter", "selectOptions", options);
				_ids = new List<int>();
			}

			public override JToken Data() {
				return _ids.ToJToken();
			}

			public override void Parse(JToken json) {
				if (json == null)
					_ids = null;
				else
					_ids = json.To<List<int>>();
				if (_ids == null)
					_ids = new List<int>();
				Active = _ids.Count != 0;
			}

			public void SelectAll() {
				_ids = new List<int>();
				foreach (JObject options in (IEnumerable<JObject>)this["selectOptions"]) {
					_ids.Add(options.AsInt("id"));
				}
			}

			public override string Where(Database db) {
				return FieldName + " " + db.In(_ids);
			}

			public override bool Test(JObject data) {
				int id = data.AsInt(JObjectFieldName);
				return _ids.Contains(id);
			}
		}

		/// <summary>
		/// Select 1 of a list of strings
		/// </summary>
		public class SelectFilter : Filter {
			List<string> _ids;

			public SelectFilter(string name, string fieldName, IEnumerable<JObject> options)
				: base(name) {
				FieldName = fieldName;
				this.AddRange("type", "multiSelectFilter", "selectOptions", options);
				_ids = new List<string>();
			}

			public override JToken Data() {
				return _ids.ToJToken();
			}

			public override void Parse(JToken json) {
				if (json == null)
					_ids = null;
				else
					_ids = json.To<List<string>>();
				if (_ids == null)
					_ids = new List<string>();
				Active = _ids.Count != 0;
			}

			public void SelectAll() {
				_ids = new List<string>();
				foreach (JObject options in (IEnumerable<JObject>)this["selectOptions"]) {
					_ids.Add(options.AsString("id"));
				}
			}

			public override string Where(Database db) {
				return FieldName + " " + db.In(_ids);
			}

			public override bool Test(JObject data) {
				string id = data.AsString(JObjectFieldName);
				return _ids.Contains(id);
			}
		}

		public class DecimalFilter : Filter {
			public enum Comparison {
				None = 0,
				Zero,
				NonZero,
				Less,
				Greater,
				Equal,
				NotEqual
			}
			Comparison _comparison;
			decimal _value;

			public DecimalFilter(string name, string fieldName)
				: this(name, fieldName, Comparison.None, 0) {
			}

			public DecimalFilter(string name, string fieldName, Comparison comparison)
				: this(name, fieldName, comparison, 0) {
			}

			public DecimalFilter(string name, string fieldName, Comparison comparison, decimal value)
				: base(name) {
				FieldName = fieldName;
				this["type"] = "decimalFilter";
				_comparison = comparison;
				_value = value;
				Active = _comparison > Comparison.None && _comparison <= Comparison.NotEqual;
			}

			public override JToken Data() {
				return new JObject().AddRange("comparison", _comparison, "value", _value);
			}

			public override void Parse(JToken json) {
				if (json == null)
					_comparison = Comparison.None;
				else {
					JObject data = json as JObject;
					_comparison = (Comparison)data.AsInt("comparison");
					if (_comparison > Comparison.NotEqual)
						_comparison = Comparison.None;
					_value = data.AsDecimal("value");
				}
				Active = _comparison > Comparison.None && _comparison <= Comparison.NotEqual;
			}


			public override string Where(Database db) {
				switch (_comparison) {
					case Comparison.Zero:
						return FieldName + " = 0";
					case Comparison.NonZero:
						return FieldName + " <> 0";
					case Comparison.Less:
						return FieldName + " <= " + _value;
					case Comparison.Greater:
						return FieldName + " >= " + _value;
					case Comparison.Equal:
						return FieldName + " = " + _value;
					case Comparison.NotEqual:
						return FieldName + " <> " + _value;
					default:
						return "";
				}
			}

			public override bool Test(JObject data) {
				decimal value = data.AsDecimal(JObjectFieldName);
				switch (_comparison) {
					case Comparison.Zero:
						return value == 0;
					case Comparison.NonZero:
						return value != 0;
					case Comparison.Less:
						return value <= _value;
					case Comparison.Greater:
						return value >= _value;
					case Comparison.Equal:
						return value == _value;
					case Comparison.NotEqual:
						return value != _value;
					default:
						return true;
				}
			}
		}

		public class StringFilter : Filter {
			public enum Comparison {
				None = 0,
				Empty,
				NonEmpty,
				Equal,
				Contains,
				StartsWith,
				EndsWith
			}
			Comparison _comparison;
			string _value;

			public StringFilter(string name, string fieldName)
				: this(name, fieldName, Comparison.None, "") {
			}

			public StringFilter(string name, string fieldName, Comparison comparison)
				: this(name, fieldName, comparison, "") {
			}

			public StringFilter(string name, string fieldName, Comparison comparison, string value)
				: base(name) {
				FieldName = fieldName;
				this["type"] = "stringFilter";
				_comparison = comparison;
				_value = value;
				Active = _comparison > Comparison.None && _comparison <= Comparison.EndsWith;
			}

			public override JToken Data() {
				return new JObject().AddRange("comparison", _comparison, "value", _value);
			}

			public override void Parse(JToken json) {
				if (json == null)
					_comparison = Comparison.None;
				else {
					JObject data = json as JObject;
					_comparison = (Comparison)data.AsInt("comparison");
					_value = data.AsString("value");
				}
				Active = _comparison > Comparison.None && _comparison <= Comparison.EndsWith;
			}


			public override string Where(Database db) {
				switch (_comparison) {
					case Comparison.Empty:
						return "(" + FieldName + " IS NULL OR " + FieldName + " = '')";
					case Comparison.NonEmpty:
						return FieldName + " <> ''";
					case Comparison.Equal:
						return FieldName + " = " + db.Quote(_value);
					case Comparison.Contains:
						return FieldName + " LIKE " + db.Quote("%" + _value + "%");
					case Comparison.StartsWith:
						return FieldName + " LIKE " + db.Quote("%" + _value);
					case Comparison.EndsWith:
						return FieldName + " LIKE " + db.Quote("%" + _value);
					default:
						return "";
				}
			}

			public override bool Test(JObject data) {
				string value = data.AsString(JObjectFieldName);
				switch (_comparison) {
					case Comparison.Empty:
						return string.IsNullOrEmpty(value);
					case Comparison.NonEmpty:
						return !string.IsNullOrEmpty(value);
					case Comparison.Equal:
						return value == _value;
					case Comparison.Contains:
						return value.Contains(_value);
					case Comparison.StartsWith:
						return value.StartsWith(_value);
					case Comparison.EndsWith:
						return value.EndsWith(_value);
					default:
						return true;
				}
			}
		}

		/// <summary>
		/// Special filter to select only transactions where the VAT has been paid on one or more dates
		/// </summary>
		public class VatPaidFilter : RecordFilter {
			bool _null;

			public VatPaidFilter(string name, string fieldName, IEnumerable<JObject> options)
				: base(name, fieldName, Enumerable.Repeat(new JObject().AddRange("id", "0", "value", "Not Paid"), 1).Concat(options)) {
				this["date"] = true;
				Active = true;
				_null = true;
				_ids.Add(0);
			}

			public override void Parse(JToken json) {
				base.Parse(json);
				_null = _ids.IndexOf(0) >= 0;
			}

			public override string Where(Database db) {
				List<string> clauses = new List<string>();
				List<int> list = _ids.Where(i => i != 0).ToList();
				if (list.Count > 0)
					clauses.Add(FieldName + " " + db.In(list));
				if (_null)
					clauses.Add("(" + FieldName + " IS NULL OR " + FieldName + " < 1)");
				return "(" + string.Join(" OR ", clauses.ToArray()) + ")";
			}

			public override bool Test(JObject data) {
				if (_null && (data[JObjectFieldName] == null || data[JObjectFieldName].Type == JTokenType.Null || data.AsInt(JObjectFieldName) == 0))
					return true;
				return base.Test(data);
			}
		}

	}
}
