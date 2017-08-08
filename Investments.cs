using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	public class Investments : BankingAccounting {
		protected override void Init() {
			base.Init();
			insertMenuOptions(
				new MenuOption("Listing", "/investments/default.html"),
				new MenuOption("Securities", "/investments/securities.html")
				);
			if (!SecurityOn || UserAccessLevel >= AccessLevel.ReadWrite)
				insertMenuOptions(
					new MenuOption("New Account", "/investments/detail.html?id=0")
				);
		}

		/// <summary>
		/// List all security accounts, with their cash balance and current security value
		/// </summary>
		public object DefaultListing() {
			return Database.Query(@"SELECT Account.*, Amount AS CashBalance, Value
FROM Account
LEFT JOIN (SELECT AccountId, SUM(Amount) AS Amount FROM Journal GROUP BY AccountId) AS Balances ON Balances.AccountId = idAccount
JOIN AccountType ON idAccountType = AccountTypeId
LEFT JOIN (" + AccountValue(Database, Utils.Today) + @") AS AccountValues ON AccountValues.ParentAccountId = Balances.AccountId
WHERE AccountTypeId = " + (int)AcctType.Investment + @"
GROUP BY idAccount ORDER BY AccountName");
		}

		/// <summary>
		/// Retrieve a security account for editing
		/// </summary>
		public void Detail(int id) {
			InvestmentDetail record = Database.QueryOne<InvestmentDetail>("Account.*, AcctType, SUM(Amount) AS CashBalance",
				"WHERE idAccount = " + id,
				"Account", "Journal");
			record.CurrentBalance = record.CashBalance - Database.QueryOne("SELECT SUM(Amount) AS Future FROM Journal JOIN Document ON idDocument = DocumentId WHERE AccountId = "
				+ id + " AND DocumentDate > " + Database.Quote(Utils.Today)).AsDecimal("Future");
			record.Value = Database.QueryOne("SELECT Value FROM (" + AccountValue(Database, Utils.Today) + ") AS V WHERE ParentAccountid = " + id).AsDecimal("Value");
			if (record.Id == null) {
				record.AccountTypeId = (int)AcctType.Investment;
			} else {
				checkAcctType(record.AccountTypeId, AcctType.Investment);
				Title += " - " + record.AccountName;
			}
			Record = record;
		}

		/// <summary>
		/// List all transactions for account
		/// </summary>
		public IEnumerable<JObject> DetailListing(int id) {
			return detailsWithBalance(id).Reverse();
		}

		/// <summary>
		/// Update account info after editing
		/// </summary>
		public AjaxReturn DetailSave(Account json) {
			checkAcctType(json.AccountTypeId, AcctType.Investment);
			return SaveRecord(json, true);
		}

		/// <summary>
		/// Portfolio header - same as Detail header
		/// </summary>
		public void Portfolio(int id) {
			Detail(id);
		}

		/// <summary>
		/// List all securities for account, with quantity and current value
		/// </summary>
		public IEnumerable<JObject> PortfolioListing(int id) {
			return (from p in Database.Query("SELECT SecurityName, AccountName, SV.* FROM ("
				+ SecurityValues(Database, Utils.Today) + @") AS SV
JOIN Account ON idAccount = SV.AccountId
JOIN Security ON idSecurity = SecurityId
WHERE ParentAccountid = " + id).ToList()
						  let cb = SecurityCost(p.AsInt("AccountId"))
						  select new JObject(p).AddRange(
							  "CostBasis", cb,
							  "Change", cb == 0 ? 0 : 100 * (p.AsDecimal("Value") - cb) / cb
							  ));
		}

		/// <summary>
		/// Get a Buy or Sell document for editing
		/// </summary>
		public void Document(int id, DocType type) {
			Title = Title.Replace("Document", type.UnCamel());
			InvestmentDocument header = getDocument<InvestmentDocument>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)type;
				header.DocType = type.UnCamel();
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				if (GetParameters["acct"].IsInteger()) {
					FullAccount acct = Database.QueryOne<FullAccount>("*", "WHERE idAccount = " + GetParameters["acct"], "Account");
					if (acct.idAccount != null) {
						header.DocumentAccountId = (int)acct.idAccount;
						header.DocumentAccountName = acct.AccountName;
						header.FeeAccount = Database.QueryOne("SELECT idAccount FROM Account WHERE AccountName = " + Database.Quote(acct.AccountName + " fees")).AsInt("idAccount");
					}
				}
			} else {
				checkDocType(header.DocumentTypeId, DocType.Buy, DocType.Sell);
				List<JObject> journals = Database.Query(@"SELECT *
FROM Journal
LEFT JOIN StockTransaction ON idStockTransaction = idJournal 
LEFT JOIN Security ON idSecurity = SecurityId
WHERE JournalNum > 1 
AND DocumentId = " + id).ToList();
				header.SecurityId = journals[0].AsInt("SecurityId");
				header.SecurityName = journals[0].AsString("SecurityName");
				header.Quantity = journals[0].AsDouble("Quantity");
				header.Price = journals[0].AsDouble("Price");
				if (journals.Count > 1) {
					header.FeeAccount = journals[1].AsInt("AccountId");
					header.Fee = journals[1].AsDecimal("Amount");
					header.FeeMemo = journals[1].AsString("Memo");
				}
				if (type == DocType.Sell)
					header.Quantity = -header.Quantity;
			}
			JObject record = new JObject().AddRange("header", header);
			nextPreviousDocument(record, "JOIN Journal ON DocumentId = idDocument WHERE DocumentTypeId " 
				+ Database.In(DocType.Buy, DocType.Sell)
				+ (header.DocumentAccountId > 0 ? " AND AccountId = " + header.DocumentAccountId : ""));
			record.AddRange("Accounts", SelectExpenseAccounts(),
				"Names", SelectOthers(),
				"Securities", SelectSecurities());
			Record = record;
		}

		public AjaxReturn DocumentDelete(int id) {
			return deleteDocument(id, DocType.Buy, DocType.Sell, DocType.Transfer);
		}

		/// <summary>
		/// Update Buy/Sell after editing
		/// </summary>
		public AjaxReturn DocumentSave(InvestmentDocument json) {
			Database.BeginTransaction();
			JObject oldDoc = getCompleteDocument(json.idDocument);
			DocType t = checkDocType(json.DocumentTypeId, DocType.Buy, DocType.Sell);
			FullAccount acct = Database.Get<FullAccount>((int)json.DocumentAccountId);
			checkAcctType(acct.AccountTypeId, AcctType.Investment);
			int sign = SignFor(t);
			fixNameAddress(json, "O");
			if(json.SecurityId == 0) {
				Utils.Check(!string.IsNullOrEmpty(json.SecurityName), "No Security Name supplied");
				json.SecurityId = Database.ForeignKey("Security", "SecurityName", json.SecurityName);
			}
			if (string.IsNullOrEmpty(json.DocumentMemo))
				json.DocumentMemo = json.SecurityName;
			if (json.idDocument == null) {
				StockPrice p = Database.QueryOne<StockPrice>("SELECT * FROM " + LatestPrice(Database, json.DocumentDate) + " WHERE SecurityId = " + json.SecurityId);
				if (p.Price != json.Price) {
					// Stock price is different from file price, and its a new buy/sell - update file price for security date
					p.SecurityId = (int)json.SecurityId;
					p.Date = json.DocumentDate;
					p.Price = json.Price;
					Database.Update(p);
				}
			}
			decimal cost = (decimal)Math.Round(json.Price * json.Quantity, 2);
			decimal amount = json.Fee + sign * cost;
			Database.Update(json);
			// First journal is posting to this account
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)json.idDocument,
				JournalNum = 1
			});
			journal.DocumentId = (int)json.idDocument;
			journal.AccountId = json.DocumentAccountId;
			journal.NameAddressId = json.DocumentNameAddressId;
			journal.Memo = json.DocumentMemo;
			journal.JournalNum = 1;
			journal.Amount = -amount;
			journal.Outstanding = -amount;
			Database.Update(journal);
			// Second journal is to subaccount for this security (account:security)
			journal = Database.Get(new Journal() {
				DocumentId = (int)json.idDocument,
				JournalNum = 2
			});
			journal.DocumentId = (int)json.idDocument;
			journal.AccountId = (int)Database.ForeignKey("Account", 
				"AccountName", acct.AccountName + ":" + json.SecurityName,
				"AccountTypeId", (int)AcctType.Security);
			journal.NameAddressId = json.DocumentNameAddressId;
			journal.Memo = json.DocumentMemo;
			journal.JournalNum = 2;
			journal.Amount = journal.Outstanding = sign * cost;
			Database.Update(journal);
			// Corresponding line
			Line line = Database.Get<Line>((int)journal.idJournal);
			line.idLine = journal.idJournal;
			line.LineAmount = cost;
			Database.Update(line);
			// Now update the stock transaction
			StockTransaction st = Database.Get<StockTransaction>((int)journal.idJournal);
			st.idStockTransaction = journal.idJournal;
			st.ParentAccountId = json.DocumentAccountId;
			st.SecurityId = (int)json.SecurityId;
			st.Price = json.Price;
			st.Quantity = sign * json.Quantity;
			st.CostPer = Math.Round((double)amount / json.Quantity, 4);
			Database.Update(st);
			if(json.Fee != 0) {
				// Need another journal and line for the fee
				Utils.Check(json.FeeAccount > 0, "No Fee Account supplied");
				journal = Database.Get(new Journal() {
					DocumentId = (int)json.idDocument,
					JournalNum = 3
				});
				journal.DocumentId = (int)json.idDocument;
				journal.AccountId = (int)json.FeeAccount;
				journal.NameAddressId = json.DocumentNameAddressId;
				journal.Memo = json.FeeMemo;
				journal.JournalNum = 3;
				journal.Amount = journal.Outstanding = json.Fee;
				Database.Update(journal);
				line = Database.Get<Line>((int)journal.idJournal);
				line.idLine = journal.idJournal;
				line.LineAmount = sign * json.Fee;
				Database.Update(line);
			}
			// Delete any left over lines from the old transaction
			Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + json.idDocument + " AND JournalNum > " + journal.JournalNum + ")");
			Database.Execute("DELETE FROM Journal WHERE Documentid = " + json.idDocument + " AND JournalNum > " + journal.JournalNum);
			// Audit
			JObject newDoc = getCompleteDocument(json.idDocument);
			Database.AuditUpdate("Document", json.idDocument, oldDoc, newDoc);
			Database.Commit();
			return new AjaxReturn() { message = "Document saved", id = json.idDocument };
		}

		/// <summary>
		/// A balance adjustment posts enough to to reach the new balance.
		/// Important fields are ExistingBalance and NewBalance
		/// </summary>
		public void BalanceAdjustment(int id, int acct) {
			checkAccountIsAcctType(acct, AcctType.Investment);
			BalanceAdjustmentDocument doc = Database.Get<BalanceAdjustmentDocument>(id);
			doc.NewBalance = doc.ExistingBalance = Database.QueryOne(@"SELECT SUM(Amount) AS Amount FROM Journal WHERE AccountId = " + acct).AsDecimal("Amount");
			if (doc.idDocument == null) {
				doc.DocumentAccountId = acct;
				doc.DocumentDate = Utils.Today;
				doc.DocumentMemo = "Balance Adjustment";
				if(string.IsNullOrEmpty(doc.DocumentIdentifier))
					doc.DocumentIdentifier = "Balance Adjustment";
				JObject o = Database.QueryOne(@"SELECT J.AccountId, AccountName
FROM Journal
JOIN Document ON idDocument = Journal.DocumentId
JOIN Journal AS J ON J.DocumentId = idDocument AND J.JournalNum = 2
JOIN Account ON idAccount = J.AccountId
WHERE Journal.JournalNum = 1
AND DocumentTypeID IN (" + (int)DocType.Cheque + "," + (int)DocType.Deposit + @")
AND Journal.AccountId = " + acct);
				doc.AccountId = o.AsInt("AccountId");
				doc.AccountName = o.AsString("AccountName");
				doc.NameAddressId = 1;
				doc.Name = "";
			} else {
				checkDocType(doc.DocumentTypeId, DocType.Cheque, DocType.Deposit);
				foreach (Journal j in Database.Query<Journal>("SELECT * FROM Journal WHERE DocumentId = " + id)) {
					switch (j.JournalNum) {
						case 1:
							doc.DocumentAccountId = j.AccountId;
							doc.NameAddressId = j.NameAddressId;
							doc.Amount = j.Amount;
							break;
						case 2:
							doc.AccountId = j.AccountId;
							break;
						default:
							throw new CheckException("Document is not a balance adjustment");
					}
				}
				Utils.Check(acct == doc.DocumentAccountId, "Document is for a different account");
				doc.Name = Database.QueryOne("SELECT Name FROM NameAddress WHERE idNameAddress = " + doc.NameAddressId).AsString("Name");
				doc.AccountName = Database.QueryOne("SELECT AccountName FROM Account WHERE idAccount = " + doc.AccountId).AsString("AccountName");
				doc.ExistingBalance -= doc.Amount;
			}
			Record = new JObject().AddRange(
				"header", doc,
				"Accounts", SelectAccounts(),
				"Names", SelectOthers());
		}

		/// <summary>
		/// Save a BalanceAdjustment after editing. 
		/// Transaction amount is NewBalance - ExistingBalance
		/// </summary>
		public AjaxReturn BalanceAdjustmentSave(BalanceAdjustmentDocument json) {
			checkAccountIsAcctType(json.DocumentAccountId, AcctType.Investment);
			Utils.Check(json.AccountId > 0, "No account selected");
			// Pointless to post a new transaction that does nothing
			Utils.Check(json.idDocument > 0 || json.NewBalance != json.ExistingBalance, "Balance is unchanged");
			if (json.NameAddressId == 0)
				json.NameAddressId = Database.ForeignKey("NameAddress",
					"Type", "O",
					"Name", json.Name);
			else
				checkNameType(json.NameAddressId, "O");
			JObject old = getCompleteDocument(json.idDocument);
			json.Amount = json.NewBalance - json.ExistingBalance;
			json.DocumentTypeId = (int)(json.Amount < 0 ? DocType.Cheque : DocType.Deposit);
			Database.BeginTransaction();
			Database.Update(json);
			Journal j = new Journal();
			j.AccountId = json.DocumentAccountId;
			j.Outstanding = j.Amount = json.Amount;
			j.DocumentId = (int)json.idDocument;
			j.JournalNum = 1;
			j.Memo = json.DocumentMemo;
			j.NameAddressId = json.NameAddressId;
			Database.Update(j);
			j = new Journal();
			j.AccountId = json.AccountId;
			j.Outstanding = j.Amount = -json.Amount;
			j.DocumentId = (int)json.idDocument;
			j.JournalNum = 2;
			j.Memo = json.DocumentMemo;
			j.NameAddressId = json.NameAddressId;
			Database.Update(j);
			Line line = Database.Get<Line>((int)j.idJournal);
			line.idLine = j.idJournal;
			line.LineAmount = Math.Abs(json.Amount);
			Database.Update(line);
			JObject full = getCompleteDocument(json.idDocument);
			Database.AuditUpdate("Document", json.idDocument, old, full);
			Database.Commit();
			return new AjaxReturn() { message = "Balance adjusted", id = json.idDocument };
		}

		public void Securities() {
		}

		public object SecuritiesListing() {
			return Database.Query("SELECT * FROM Security ORDER BY SecurityName");
		}

		/// <summary>
		/// Security header and stock prices
		/// </summary>
		public void Security(int id) {
			Security record = Database.Get<Security>(id);
			if (record.Id != null)
				Title += " - " + record.SecurityName;
			Record = new JObject().AddRange(
				"header", record,
				"detail", Database.Query("SELECT *, 7 AS Unit FROM StockPrice WHERE SecurityId = " + id + " ORDER BY Date DESC"));
		}

		public AjaxReturn SecuritySave(SecurityInfo json) {
			Security existing = Database.Get(json.header);
			Database.BeginTransaction();
			Database.Update(json.header, true);
			if (existing.idSecurity > 0 && json.header.SecurityName != existing.SecurityName) {
				// Name has changed - change name of subaccounts
				foreach(Account a in Database.Query<Account>("SELECT * FROM Account WHERE AccountName LIKE "
					+ Database.Quote("%:" + existing.SecurityName))) {
					if(a.AccountName.EndsWith(":" + existing.SecurityName)) {
						a.AccountName = a.AccountName.Substring(0, a.AccountName.Length - existing.SecurityName.Length) + json.header.SecurityName;
						Database.Update(a);
					}
				}
			}
			// Replace old stock prices with new ones
			Database.Execute("DELETE FROM StockPrice WHERE SecurityId = " + json.header.idSecurity);
			foreach (StockPrice p in json.detail) {
				Database.Insert(p);
			}
			Database.Commit();
			return new AjaxReturn() { message = "Security updated" };
		}

		/// <summary>
		/// Sql to return the price of each stock as at date
		/// </summary>
		public static string LatestPrice(Database db, DateTime date) {
			return string.Format(@"(select StockPrice.* FROM StockPrice JOIN 
(select SecurityId AS Id, MAX(Date) AS MaxDate 
FROM StockPrice 
WHERE Date <= {0}
GROUP BY SecurityId) AS LatestPriceDate ON Id = SecurityId AND MaxDate = Date) AS LatestPrice", db.Quote(date));
		}

		/// <summary>
		/// Calculate the cost of all the securities in an account on a FIFO basis
		/// </summary>
		public decimal SecurityCost(int account) {
			decimal cost = 0;
			List<CostedStockTransaction> transactions = Database.Query<CostedStockTransaction>(@"SELECT StockTransaction.*
FROM StockTransaction 
JOIN Journal ON Journal.idJournal = idStockTransaction
JOIN Document ON idDocument = Journal.DocumentId
WHERE AccountId = " + account + @"
ORDER BY DocumentDate, idDocument").ToList();
			foreach (CostedStockTransaction t in transactions) {
				if (t.Quantity < 0) {
					// Sold - work out the cost of the sold items
					double q = -Math.Round(t.Quantity, 4);		// Quantity sold
					foreach (CostedStockTransaction b in transactions) {
						if (b.Quantity > 0 && b.SecurityId == t.SecurityId) {
							// Quantity bought
							double qb = Math.Round(b.Quantity, 4);
							// Amount sold, or quantity bought, whichever is the lesser
							double qs = Math.Min(qb, q);
							// Quantity left on purchase transaction
							b.Quantity = Math.Round(qb - qs, 4);
							q = Math.Round(q - qs, 4);
							cost -= (decimal)Math.Round(b.CostPer * qs, 4);
							if (q == 0)
								break;
						}
					}
				} else {
					cost += (decimal)Math.Round(t.CostPer * t.Quantity, 4);
				}
			}
			return Math.Round(cost, 2);
		}

		/// <summary>
		/// Sql to return the current value of each StockTransaction as at Date
		/// </summary>
		public static string SecurityValues(Database db, DateTime date) {
			return @"SELECT ParentAccountId, AccountId, SecuritiesByAccount.SecurityId AS SecurityId, Quantity, Price, SUM(ROUND(Quantity * Price, 2)) AS Value
FROM (SELECT DocumentDate, ParentAccountId, AccountId, SecurityId, SUM(Quantity) AS Quantity
FROM StockTransaction
JOIN Journal ON idJournal = idStockTransaction
JOIN Document ON idDocument = DocumentId
WHERE DocumentDate <= " + db.Quote(date) + @"
GROUP BY ParentAccountId, AccountId, SecurityId) AS SecuritiesByAccount
JOIN " + LatestPrice(db, date) + @"
ON LatestPrice.SecurityId = SecuritiesByAccount.SecurityId
GROUP BY AccountId, SecuritiesByAccount.SecurityId";
		}

		/// <summary>
		/// Sql to return the current value of each security account as at fate
		/// </summary>
		public static string AccountValue(Database db, DateTime date) {
			return @"SELECT ParentAccountId, SUM(Value) AS Value
FROM (" + SecurityValues(db, date) + @") AS SecurityValues
GROUP BY ParentAccountId";
		}

		public class InvestmentDetail : Account {
			public decimal? CashBalance;
			public decimal? CurrentBalance;
			public decimal? Value;
		}

		public class SecurityValue : JsonObject {
			public int? ParentAccountId;
			public int? AccountId;
			public int? SecurityId;
			public decimal Value;
		}

		public class SecurityValueWithName : JsonObject {
			public int? ParentAccountId;
			public int? AccountId;
			public int? SecurityId;
			public string SecurityName;
			public decimal Value;
		}

		public class CostedStockTransaction : StockTransaction {
			public decimal Cost;
		}

		public class InvestmentDocument : Extended_Document {
			public int? SecurityId;
			public string SecurityName = "";
			public double Quantity;
			public double Price;
			public int? FeeAccount;
			public decimal Fee;
			public string FeeMemo = "Fees";
		}

		public class BalanceAdjustmentDocument : Document {
			public int DocumentAccountId;
			public int AccountId;
			public string AccountName;
			public int? NameAddressId;
			public string Name;
			public decimal Amount;
			public decimal ExistingBalance;
			public decimal NewBalance;
		}

		public class SecurityInfo : JsonObject {
			public Security header;
			public List<StockPrice> detail;
		}
	}
}
